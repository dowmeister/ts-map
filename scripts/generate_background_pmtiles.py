#!/usr/bin/env python3
"""
Generate raster PMTiles for the ETS2/ATS map background.

Usage:
    python generate_background_pmtiles.py <game>
    python generate_background_pmtiles.py ets2

Requires:
    pip install Pillow
    pmtiles CLI in PATH  (https://github.com/protomaps/go-pmtiles/releases)
    OR: Docker (used as fallback)

Input:
    map_data/<game>/map_background/map_info.json
    map_data/<game>/map_background/map0.png ... map3.png  (from convert_map_background.bat)

Output:
    map_data/pmtiles/<game>-background.pmtiles
"""

from __future__ import annotations

import io
import argparse
import json
import math
import shutil
import sqlite3
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

TILE_SIZE = 256
MIN_ZOOM = 2
MAX_ZOOM = 7

# Quadrant layout (verified visually):
#   map0 = TL,  map2 = TR
#   map1 = BL,  map3 = BR
QUADRANT_LAYOUT = [
    ("map0", 0, 0),  # TL
    ("map2", 1, 0),  # TR
    ("map1", 0, 1),  # BL
    ("map3", 1, 1),  # BR
]


def main():
    parser = argparse.ArgumentParser(description="Generate raster PMTiles for the map background.")
    parser.add_argument("game")
    parser.add_argument("--min-zoom", type=int, default=MIN_ZOOM, help="Minimum raster tile zoom.")
    parser.add_argument("--max-zoom", type=int, default=MAX_ZOOM, help="Maximum raster tile zoom.")
    parser.add_argument(
        "--mode",
        choices=("projected", "flat"),
        default="projected",
        help="projected warps the background into the same WGS84 projection as roads; flat keeps the texture linear.",
    )
    args = parser.parse_args()

    game = args.game
    root = Path(__file__).parent.parent
    bg_dir = root / "map_data" / game / "map_background"
    pmtiles_dir = root / "map_data" / "pmtiles"
    pmtiles_dir.mkdir(parents=True, exist_ok=True)

    info_path = bg_dir / "map_info.json"
    if not info_path.exists():
        print(f"Error: {info_path} not found. Run TsMap.Cli export first.")
        sys.exit(1)

    with open(info_path) as f:
        info = json.load(f)

    with tempfile.TemporaryDirectory(prefix="tsmap_bg_") as tmp_str:
        tmp = Path(tmp_str)
        img = assemble_quadrants(bg_dir)
        game_bbox, (west, south, east, north) = compute_texture_bbox(info, img)
        print(f"Texture bbox WGS84: W={west:.4f} S={south:.4f} E={east:.4f} N={north:.4f}")
        mbtiles_path = tmp / f"{game}-background.mbtiles"
        cut_tiles(
            img,
            game_bbox,
            info["projection"],
            west,
            south,
            east,
            north,
            mbtiles_path,
            game,
            args.min_zoom,
            args.max_zoom,
            args.mode,
        )
        pmtiles_path = pmtiles_dir / f"{game}-background.pmtiles"
        mbtiles_to_pmtiles(mbtiles_path, pmtiles_path)

    print(f"\nDone: {pmtiles_path}")


def assemble_quadrants(bg_dir: Path) -> Image.Image:
    print("Assembling quadrants ...")
    sample = Image.open(bg_dir / "map0.png")
    w, h = sample.size
    sample.close()

    combined = Image.new("RGBA", (w * 2, h * 2))
    for name, col, row in QUADRANT_LAYOUT:
        p = bg_dir / f"{name}.png"
        if not p.exists():
            print(f"Error: {p} not found. Run convert_map_background.bat first.")
            sys.exit(1)
        img = Image.open(p).convert("RGBA")
        combined.paste(img, (col * w, row * h))
        img.close()

    print(f"  Assembled: {combined.size[0]}x{combined.size[1]}px")
    return combined


def compute_texture_bbox(
    info: dict,
    img: Image.Image,
) -> tuple[dict, tuple[float, float, float, float]]:
    """
    Fit the camera rectangle to the actual raster aspect ratio before projecting.

    ui_map_camera_min/max describe the game UI camera envelope; they are not
    guaranteed to match the texture's pixel aspect. ETS2's background texture is
    square, while the raw camera envelope is much taller than wide, which makes
    the raster look compressed against the vector network.
    """
    game_bbox = compute_game_bbox(info, img.width / img.height)
    west, south, east, north = compute_projected_bbox(game_bbox, info["projection"])
    print(
        "Texture bbox game: "
        f"X={game_bbox['x_min']:.0f}..{game_bbox['x_max']:.0f} "
        f"Z={game_bbox['z_min']:.0f}..{game_bbox['z_max']:.0f}"
    )
    return game_bbox, (west, south, east, north)


def compute_game_bbox(info: dict, image_aspect: float) -> dict:
    return info["texture_bbox_game"]


def fit_game_bbox_to_image_aspect(
    game_bbox: dict,
    image_aspect: float,
) -> dict:
    x_min = float(game_bbox["x_min"])
    z_min = float(game_bbox["z_min"])
    x_max = float(game_bbox["x_max"])
    z_max = float(game_bbox["z_max"])

    width = x_max - x_min
    height = z_max - z_min
    if width <= 0 or height <= 0 or image_aspect <= 0:
        return game_bbox

    bounds_aspect = width / height

    if bounds_aspect < image_aspect:
        width = height * image_aspect
    elif bounds_aspect > image_aspect:
        height = width / image_aspect

    center_x = (x_min + x_max) / 2
    center_z = (z_min + z_max) / 2

    return {
        "x_min": center_x - width / 2,
        "z_min": center_z - height / 2,
        "x_max": center_x + width / 2,
        "z_max": center_z + height / 2,
    }


EARTH_RADIUS_METERS = 6_370_997.0
LENGTH_OF_DEGREE = EARTH_RADIUS_METERS * math.pi / 180.0


def compute_projected_bbox(game_bbox: dict, projection: dict) -> tuple[float, float, float, float]:
    west = math.inf
    south = math.inf
    east = -math.inf
    north = -math.inf

    samples = 96
    for i in range(samples + 1):
        t = i / samples
        x = game_bbox["x_min"] + (game_bbox["x_max"] - game_bbox["x_min"]) * t
        z = game_bbox["z_min"] + (game_bbox["z_max"] - game_bbox["z_min"]) * t
        for lon, lat in (
            game_to_wgs84(x, game_bbox["z_min"], projection),
            game_to_wgs84(x, game_bbox["z_max"], projection),
            game_to_wgs84(game_bbox["x_min"], z, projection),
            game_to_wgs84(game_bbox["x_max"], z, projection),
        ):
            west = min(west, lon)
            south = min(south, lat)
            east = max(east, lon)
            north = max(north, lat)

    return west, south, east, north


def _projection_values(projection: dict):
    origin_lat, origin_lon = projection["map_origin"]
    offset_x, offset_z = projection["map_offset"]
    factor_z, factor_x = projection["map_factor"]
    return (
        float(origin_lat),
        float(origin_lon),
        float(offset_x),
        float(offset_z),
        float(factor_z),
        float(factor_x),
    )


def _lcc_params(projection: dict):
    origin_lat, origin_lon, *_ = _projection_values(projection)
    phi1 = math.radians(float(projection["standard_parallel_1"]))
    phi2 = math.radians(float(projection["standard_parallel_2"]))
    phi0 = math.radians(origin_lat)
    lambda0 = math.radians(origin_lon)

    def tan_half(phi: float) -> float:
        return math.tan(math.pi / 4 + phi / 2)

    n = math.log(math.cos(phi1) / math.cos(phi2)) / math.log(tan_half(phi2) / tan_half(phi1))
    f = math.cos(phi1) * tan_half(phi1) ** n / n
    rho0 = EARTH_RADIUS_METERS * f / tan_half(phi0) ** n
    return n, f, rho0, lambda0


def game_to_wgs84(game_x: float, game_z: float, projection: dict) -> tuple[float, float]:
    origin_lat, origin_lon, offset_x, offset_z, factor_z, factor_x = _projection_values(projection)

    if projection.get("type") != "lambert_conic":
        lon = origin_lon + (game_x - offset_z) * factor_x
        lat = origin_lat + (game_z - offset_x) * factor_z
        return lon, lat

    x = game_x - offset_x
    z = game_z - offset_z

    if projection.get("use_ets2_uk_scale"):
        uk_scale = 0.75
        calais_x = -31100.0
        calais_z = -5500.0
        if x * uk_scale < calais_x and z * uk_scale < calais_z:
            x = (x + calais_x / 2) * uk_scale
            z = (z + calais_z / 2) * uk_scale

    lcc_x = x * factor_x * LENGTH_OF_DEGREE
    lcc_y = z * factor_z * LENGTH_OF_DEGREE
    n, f, rho0, lambda0 = _lcc_params(projection)

    rho = math.sqrt(lcc_x * lcc_x + (rho0 - lcc_y) * (rho0 - lcc_y))
    if n < 0:
        rho = -rho
    theta = math.atan2(lcc_x, rho0 - lcc_y)
    lat = 2 * math.atan((EARTH_RADIUS_METERS * f / rho) ** (1 / n)) - math.pi / 2
    lon = lambda0 + theta / n
    return math.degrees(lon), math.degrees(lat)


def wgs84_to_game(lon: float, lat: float, projection: dict) -> tuple[float, float]:
    origin_lat, origin_lon, offset_x, offset_z, factor_z, factor_x = _projection_values(projection)

    if projection.get("type") != "lambert_conic":
        game_x = (lon - origin_lon) / factor_x + offset_z
        game_z = (lat - origin_lat) / factor_z + offset_x
        return game_x, game_z

    n, f, rho0, lambda0 = _lcc_params(projection)
    phi = math.radians(lat)
    lam = math.radians(lon)
    rho = EARTH_RADIUS_METERS * f / math.tan(math.pi / 4 + phi / 2) ** n
    theta = n * (lam - lambda0)
    lcc_x = rho * math.sin(theta)
    lcc_y = rho0 - rho * math.cos(theta)

    x = lcc_x / factor_x / LENGTH_OF_DEGREE
    z = lcc_y / factor_z / LENGTH_OF_DEGREE

    if projection.get("use_ets2_uk_scale"):
        uk_scale = 0.75
        calais_x = -31100.0
        calais_z = -5500.0
        if x < calais_x * (1 + uk_scale / 2) and z < calais_z * (1 + uk_scale / 2):
            x = x / uk_scale - calais_x / 2
            z = z / uk_scale - calais_z / 2

    return x + offset_x, z + offset_z


def _lat_to_ty(lat: float, z: int) -> int:
    """Convert latitude to XYZ tile Y (Y=0 at north)."""
    lat_r = math.radians(max(-85.051129, min(85.051129, lat)))
    return int((1 - math.log(math.tan(lat_r) + 1 / math.cos(lat_r)) / math.pi) / 2 * 2**z)


def _tile_bounds(tx: int, ty: int, z: int) -> tuple[float, float, float, float]:
    """Returns (west, south, east, north) in degrees for XYZ tile (tx, ty, z)."""
    n = 2**z
    t_west = tx / n * 360 - 180
    t_east = (tx + 1) / n * 360 - 180
    t_north = math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * ty / n))))
    t_south = math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * (ty + 1) / n))))
    return t_west, t_south, t_east, t_north


def _render_flat_tile(
    img: Image.Image,
    img_west: float, img_south: float, img_east: float, img_north: float,
    tx: int, ty: int, z: int,
) -> bytes | None:
    """
    Render one tile by sampling the source image linearly in lon/lat space.
    The viewer uses MapLibre's equirectangular projection, which keeps the
    game UI texture flat and squared instead of bending it into Web Mercator.
    """
    t_west, t_south, t_east, t_north = _tile_bounds(tx, ty, z)

    if t_east <= img_west or t_west >= img_east:
        return None
    if t_north <= img_south or t_south >= img_north:
        return None

    iw, ih = img.size
    n = 2**z

    tile = Image.new("RGBA", (TILE_SIZE, TILE_SIZE), (0, 0, 0, 0))
    for row in range(TILE_SIZE):
        merc_frac = (ty + (row + 0.5) / TILE_SIZE) / n
        lat = math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * merc_frac))))
        if lat < img_south or lat > img_north:
            continue

        src_y = int((img_north - lat) / (img_north - img_south) * ih)
        src_y = max(0, min(ih - 1, src_y))

        src_x0 = (t_west - img_west) / (img_east - img_west) * iw
        src_x1 = (t_east - img_west) / (img_east - img_west) * iw
        clamped_x0 = max(0.0, src_x0)
        clamped_x1 = min(float(iw), src_x1)
        if clamped_x1 <= clamped_x0:
            continue

        col0 = max(0, int((img_west - t_west) / (t_east - t_west) * TILE_SIZE))
        col1 = min(TILE_SIZE, math.ceil((img_east - t_west) / (t_east - t_west) * TILE_SIZE))
        if col0 >= col1:
            continue

        strip = img.crop((int(clamped_x0), src_y, math.ceil(clamped_x1), src_y + 1))
        strip = strip.resize((col1 - col0, 1), Image.LANCZOS)
        tile.paste(strip, (col0, row))

    if not tile.getbbox():
        return None

    buf = io.BytesIO()
    tile.save(buf, format="PNG", optimize=False)
    return buf.getvalue()


def _render_projected_tile(
    img: Image.Image,
    game_bbox: dict,
    projection: dict,
    img_west: float, img_south: float, img_east: float, img_north: float,
    tx: int, ty: int, z: int,
) -> bytes | None:
    """
    Render one tile by inverse-projecting each tile pixel to game coordinates,
    then sampling the original UI texture in game space.
    """
    t_west, t_south, t_east, t_north = _tile_bounds(tx, ty, z)

    if t_east <= img_west or t_west >= img_east:
        return None
    if t_north <= img_south or t_south >= img_north:
        return None

    iw, ih = img.size
    n = 2**z
    src = img.load()
    tile = Image.new("RGBA", (TILE_SIZE, TILE_SIZE), (0, 0, 0, 0))
    dst = tile.load()
    wrote = False

    x_min = float(game_bbox["x_min"])
    x_max = float(game_bbox["x_max"])
    z_min = float(game_bbox["z_min"])
    z_max = float(game_bbox["z_max"])
    game_w = x_max - x_min
    game_h = z_max - z_min

    for row in range(TILE_SIZE):
        merc_frac = (ty + (row + 0.5) / TILE_SIZE) / n
        lat = math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * merc_frac))))
        if lat < img_south or lat > img_north:
            continue

        for col in range(TILE_SIZE):
            lon = (tx + (col + 0.5) / TILE_SIZE) / n * 360.0 - 180.0
            if lon < img_west or lon > img_east:
                continue

            game_x, game_z = wgs84_to_game(lon, lat, projection)
            if game_x < x_min or game_x > x_max or game_z < z_min or game_z > z_max:
                continue

            src_x = int((game_x - x_min) / game_w * iw)
            src_y = int((game_z - z_min) / game_h * ih)
            if src_x < 0 or src_x >= iw or src_y < 0 or src_y >= ih:
                continue

            dst[col, row] = src[src_x, src_y]
            wrote = True

    if not wrote:
        return None

    buf = io.BytesIO()
    tile.save(buf, format="PNG", optimize=False)
    return buf.getvalue()


def cut_tiles(
    img: Image.Image,
    game_bbox: dict,
    projection: dict,
    west: float, south: float, east: float, north: float,
    mbtiles_path: Path,
    game: str,
    min_zoom: int,
    max_zoom: int,
    mode: str,
):
    if min_zoom < 0 or max_zoom < min_zoom:
        raise ValueError("--max-zoom must be greater than or equal to --min-zoom")

    print(f"Cutting {mode} background tiles (z{min_zoom}-z{max_zoom}) ...")

    conn = sqlite3.connect(mbtiles_path)
    c = conn.cursor()
    c.execute("CREATE TABLE metadata (name TEXT, value TEXT)")
    c.execute(
        "CREATE TABLE tiles "
        "(zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB)"
    )
    c.execute("CREATE UNIQUE INDEX tile_index ON tiles (zoom_level, tile_column, tile_row)")

    for key, val in [
        ("name", game),
        ("format", "png"),
        ("version", "1.3"),
        ("type", "overlay"),
        ("bounds", f"{west},{south},{east},{north}"),
        ("minzoom", str(min_zoom)),
        ("maxzoom", str(max_zoom)),
    ]:
        c.execute("INSERT INTO metadata VALUES (?, ?)", (key, val))

    total = 0
    for z in range(min_zoom, max_zoom + 1):
        tx_min = int((west + 180) / 360 * 2**z)
        tx_max = int((east + 180) / 360 * 2**z)
        ty_min = _lat_to_ty(north, z)  # north → smaller ty (Y=0 at top)
        ty_max = _lat_to_ty(south, z)  # south → larger ty

        count = 0
        for tx in range(tx_min, tx_max + 1):
            for ty in range(ty_min, ty_max + 1):
                if mode == "flat":
                    data = _render_flat_tile(img, west, south, east, north, tx, ty, z)
                else:
                    data = _render_projected_tile(
                        img,
                        game_bbox,
                        projection,
                        west,
                        south,
                        east,
                        north,
                        tx,
                        ty,
                        z,
                    )
                if data is None:
                    continue
                # MBTiles spec uses TMS Y (Y=0 at south); flip from XYZ
                tms_y = 2**z - 1 - ty
                c.execute(
                    "INSERT OR REPLACE INTO tiles VALUES (?, ?, ?, ?)",
                    (z, tx, tms_y, data),
                )
                count += 1

        conn.commit()
        total += count
        print(f"  z={z}: {count} tiles")

    conn.close()
    print(f"  Total: {total} tiles")


def mbtiles_to_pmtiles(mbtiles: Path, out: Path):
    cli = shutil.which("pmtiles") or shutil.which("go-pmtiles")
    if cli:
        print(f"Converting to PMTiles ({Path(cli).name}) ...")
        _run([cli, "convert", str(mbtiles), str(out)])
        return

    print("pmtiles CLI not found — trying Docker ...")
    src_dir = str(mbtiles.parent.resolve())
    dst_dir = str(out.parent.resolve())
    _run([
        "docker", "run", "--rm",
        "-v", f"{src_dir}:/src_vol",
        "-v", f"{dst_dir}:/dst_vol",
        "ghcr.io/protomaps/go-pmtiles:latest",
        "convert", f"/src_vol/{mbtiles.name}", f"/dst_vol/{out.name}",
    ])


def _run(cmd: list):
    result = subprocess.run([str(c) for c in cmd])
    if result.returncode != 0:
        print(f"Error running: {' '.join(str(c) for c in cmd)}")
        sys.exit(result.returncode)


if __name__ == "__main__":
    main()
