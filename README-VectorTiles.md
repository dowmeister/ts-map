# TsMap Vector Tiles Guide

This guide covers the complete workflow for generating and viewing vector maps for Euro Truck Simulator 2 (ETS2) and American Truck Simulator (ATS).

## Overview

The pipeline consists of five main steps:

1. **Build the C# application** — Compile `TsMap.Cli`
2. **Export game data** — Run the CLI to produce GeoJSON, routing graph, and support files
3. **Generate vector tiles** — Run Tippecanoe (via Docker in WSL) to produce PMTiles
4. **(Optional) Generate background raster tiles** ⚠️ *very experimental* — Convert DDS map textures to a raster PMTiles background
5. **Run the viewer** — Start the React web app and (optionally) the routing service ⚠️ *very experimental*

## Prerequisites

### Required Software

| Tool | Version | Purpose |
|------|---------|---------|
| **.NET SDK** | 8.0+ | Build `TsMap.Cli` (targets `net472`) |
| **Docker Desktop** with WSL2 backend | Latest | Runs Tippecanoe for tile generation |
| **WSL2** (Ubuntu recommended) | — | Required for the `docker-tippecanoe.sh` bash script |
| **Node.js** | 18+ | React web viewer and sprite generation |
| **Python** | 3.10+ | Background raster PMTiles generation |
| **texconv.exe** | Latest | Convert DDS map textures to PNG |
| **Game files** | ETS2 and/or ATS | Source data |

### Python Packages

```bash
pip install Pillow
```

Also requires the **`pmtiles` CLI** (go-pmtiles) in `PATH`:
download from https://github.com/protomaps/go-pmtiles/releases and place it in your `PATH`.

### Node.js Packages

```bash
# For sprite generation (from project root)
npm install sharp
```

### texconv.exe (for map background)

Download `texconv.exe` from https://github.com/microsoft/DirectXTex/releases and either:
- Place it in `PATH`, or
- Drop it in the `scripts/` folder next to `convert_map_background.bat`

### Directory Structure

```
ts-map/
├── TsMap/                          # Core C# library
├── TsMap.Cli/                      # CLI exporter
├── routing-service/                # Node.js A* routing engine
├── web-viewer/react-app/           # React + MapLibre GL viewer
├── scripts/
│   ├── convert_map_background.bat  # DDS → PNG converter
│   └── generate_background_pmtiles.py  # PNG → raster PMTiles
├── map_data/
│   ├── ets2/
│   │   ├── geojson/                # Exported GeoJSON + routing graph
│   │   ├── mbtiles/                # Intermediate MBTiles
│   │   ├── overlay_images/         # Extracted icon PNGs
│   │   ├── sprites/                # Generated sprite sheets
│   │   └── map_background/         # DDS/PNG map textures
│   └── ats/  (same structure)
│   └── pmtiles/                    # Final PMTiles files (all games)
├── docker-compose.yml              # Routing + React app services
├── docker-tippecanoe.sh            # Tile generation script (WSL/Linux)
├── docker-tippecanoe.bat           # Tile generation script (Windows, basic)
├── generate-sprites.js             # Sprite sheet builder
└── upload-overlay-images.sh        # Rclone upload to Cloudflare R2
```

## Step 1: Build the Application

The CLI tool targets `.NET Framework 4.7.2` (`net472`). Use the `dotnet` CLI or Visual Studio.

### Using the build script (recommended)

```powershell
# From the project root (PowerShell or cmd)
.\build.bat
```

This builds `TsMap` (the core library) first, then `TsMap.Cli`.

### Manual build

```powershell
dotnet build TsMap\TsMap.csproj -c Release
dotnet build TsMap.Cli\TsMap.Cli.csproj -c Release
```

The compiled executable ends up at:
```
TsMap.Cli\bin\Release\net472\TsMap.Cli.exe
```

## Step 2: Export Game Data

The CLI reads the game archives and outputs GeoJSON vector data, a routing graph, and support files.

### Command-Line Options

| Option | Alias | Description | Required |
|--------|-------|-------------|----------|
| `--game` | `-g` | Path to game installation directory | **Yes** |
| `--output` | `-o` | Output directory | **Yes** |
| `--format` | `-f` | `geojson`, `json`, `routing` ⚠️ experimental, or `all` (default: `all`) | No |
| `--mods-dir` | `-m` | Directory containing `.scs` mod files | No |
| `--mods-list` | `-l` | JSON file listing which mods to load | No |
| `--validate` | | Run 8 routing-graph validation checks after export | No |
| `--mdb` | | Comma-separated map databases to export, e.g. `europe,usa` | No |

### Export everything (GeoJSON + routing + JSON)

```powershell
# ETS2
.\TsMap.Cli\bin\Release\net472\TsMap.Cli.exe `
  -g "E:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" `
  -o ".\map_data\ets2" `
  -f all

# ATS
.\TsMap.Cli\bin\Release\net472\TsMap.Cli.exe `
  -g "E:\SteamLibrary\steamapps\common\American Truck Simulator" `
  -o ".\map_data\ats" `
  -f all
```

### Export GeoJSON only (for tile generation)

```powershell
.\TsMap.Cli\bin\Release\net472\TsMap.Cli.exe -g "<game_path>" -o ".\map_data\ets2" -f geojson
```

### Export routing graph only ⚠️ experimental

```powershell
.\TsMap.Cli\bin\Release\net472\TsMap.Cli.exe -g "<game_path>" -o ".\map_data\ets2" -f routing
```

### With mods (ProMods, RusMap, etc.)

```powershell
.\TsMap.Cli\bin\Release\net472\TsMap.Cli.exe `
  -g "<game_path>" `
  -o ".\map_data\promods" `
  -m "<mods_folder>" `
  -l ".\mods\promods.json" `
  -f all
```

### Generated Files

**GeoJSON** (in `map_data/<game>/geojson/`):

| File | Description |
|------|-------------|
| `roads.geojson` | Main road network |
| `prefab_roads.geojson` | Prefab road geometry (intersections, junctions) |
| `prefab_flat.geojson` | Flat prefab areas (parking lots, etc.) |
| `prefab_buildings.geojson` | 3D buildings in prefabs |
| `map_flat.geojson` | Flat map areas (terrain, water) |
| `map_buildings.geojson` | 3D map buildings |
| `buildings.geojson` | Additional building geometry |
| `footprints.geojson` | Building footprints (separate tile layer) |
| `hidden_roads.geojson` | Secret/hidden roads (separate tile layer) |
| `hidden_prefabs.geojson` | Hidden prefabs (separate tile layer) |
| `ferries.geojson` | Ferry connections |
| `cities.geojson` | City locations and names |
| `countries.geojson` | Country boundaries |
| `companies.geojson` | Company locations |
| `overlays.geojson` | Map overlays (gas stations, truck stops, etc.) |
| `routing-graph.json` | A* routing graph (nodes + edges) |

**Support files** (in `map_data/<game>/`):

- `VectorTileMapInfo.json` — projection bounds and metadata
- `Cities.json`, `Countries.json`, `BusStops.json`, `CargoDefs.json`
- `overlay_images/` — icon PNGs extracted from game archives

## Step 3: Generate Vector Tiles with Docker (WSL)

Tippecanoe runs inside a Docker container. The main script is a **bash script** (`docker-tippecanoe.sh`) that must be run from **WSL** (or Linux/macOS). A basic Windows `.bat` wrapper exists but is less maintained.

### First Time: Build the Docker Image

```bash
# In WSL, from the project root
docker build -t tsmap-tippecanoe -f Dockerfile.tippecanoe .
```

This builds the latest Tippecanoe from source. Only needed once (or after updating the Dockerfile).

### Generate Tiles

```bash
# WSL — from the project root (e.g. /mnt/e/Progetti/ts-map)
./docker-tippecanoe.sh ets2
./docker-tippecanoe.sh ats
```

The script generates **two separate PMTiles files** per game:

| File | Layers | Zoom |
|------|--------|------|
| `map_data/<game>/mbtiles/tiles.mbtiles` → `map_data/pmtiles/<game>.pmtiles` | All main layers | Z3–Z10 |
| `map_data/<game>/mbtiles/tiles-footprints.mbtiles` → `map_data/pmtiles/<game>-footprints.pmtiles` | `footprints`, `hidden_roads`, `hidden_prefabs` | Z6–Z6 |

Keeping footprints separate keeps the main file smaller and improves HTTP Range-request performance.

### Override Zoom Levels

```bash
MAIN_MAXZOOM=11 FOOTPRINTS_MAXZOOM=8 ./docker-tippecanoe.sh ets2
```

Defaults: `MAIN_MAXZOOM=10`, `FOOTPRINTS_MAXZOOM=6`.

### Convert to PMTiles only (skip tile re-generation)

If MBTiles already exist and you only need to (re-)convert to PMTiles:

```bash
./docker-tippecanoe.sh ets2 pmtiles
```

### How MBTiles → PMTiles conversion works

The script uses the `go-pmtiles` CLI inside WSL to convert each `.mbtiles` to `.pmtiles` and moves the result to `map_data/pmtiles/`. The `pmtiles` CLI must be in `PATH` inside WSL:

```bash
# Install go-pmtiles in WSL
wget https://github.com/protomaps/go-pmtiles/releases/latest/download/go-pmtiles_Linux_x86_64.tar.gz
tar xf go-pmtiles_Linux_x86_64.tar.gz
sudo mv pmtiles /usr/local/bin/
```

## Step 4 (Optional): Generate Raster Background Tiles ⚠️ VERY EXPERIMENTAL

> **WARNING**: This feature is very experimental and does not work correctly. The background texture alignment with the vector layers is inaccurate, and the output may be distorted or misaligned. Use at your own risk.

The viewer can display the in-game map texture as a raster layer behind the vector tiles.

### 4a. Extract and Convert DDS Textures

Run the CLI with `-f geojson` (or `-f all`) first — it exports `map_background/map_info.json` and the `.dds` files.

Then convert DDS → PNG using `texconv`:

```batch
scripts\convert_map_background.bat ets2
scripts\convert_map_background.bat ats
```

This produces `map0.png`–`map3.png` (four quadrants) in `map_data/<game>/map_background/`.

### 4b. Generate the Raster PMTiles

```bash
# From the project root
python scripts/generate_background_pmtiles.py ets2
python scripts/generate_background_pmtiles.py ats
```

Options:

```
--min-zoom  INT    Minimum zoom level (default: 2)
--max-zoom  INT    Maximum zoom level (default: 7)
--mode      projected|flat
                   projected: warps the background into WGS84 (matches road projection)
                   flat: keeps the texture linear (faster, less accurate)
```

**Requires:**
- `pip install Pillow`
- `pmtiles` CLI in `PATH` (see step 3 for install instructions)

**Output:** `map_data/pmtiles/<game>-background.pmtiles`

## Step 5: Generate Sprite Sheets

The viewer uses MapLibre sprite sheets for overlay icons (gas stations, companies, etc.). Sprites must be regenerated after any `overlay_images` change.

```bash
# From the project root (requires Node.js)
npm install sharp          # first time only
node generate-sprites.js ets2 ats promods
```

This reads PNGs from `map_data/<game>/overlay_images/` and writes sprite files to `map_data/<game>/sprites/`:

```
sprites/
├── sprite.png         # 1x sprite sheet
├── sprite.json        # 1x metadata
├── sprite@2x.png      # 2x sprite sheet
└── sprite@2x.json     # 2x metadata
```

> When regenerating sprites for production, bump `VITE_MAP_ASSET_VERSION` in the React app `.env` before rebuilding, so clients fetch updated files.

## Step 6: Run the Web Viewer

The viewer is a **React + MapLibre GL** application. It reads PMTiles directly (no tile server needed) via the `pmtiles` JS protocol.

### Local Development

```powershell
# From the project root
.\webviewer-dev.bat
```

Or manually:

```bash
cd web-viewer/react-app
npm install   # first time
npm run dev
```

`npm run dev` starts two processes concurrently:

| Process | Port | Description |
|---------|------|-------------|
| `http-server` | 8888 | Serves `map_data/` (PMTiles, sprites, JSON) |
| `vite` | 3000 | React dev server with HMR |

Open: http://localhost:3000?game=ets2

### Environment Variables (`.env`)

Edit `web-viewer/react-app/.env` to configure the viewer:

```env
VITE_MAP_DEFAULT_PITCH=20
VITE_MAP_DEFAULT_ZOOM=4
VITE_MAP_DEFAULT_BEARING=0
VITE_MAP_DEFAULT_CENTER_LON=0
VITE_MAP_DEFAULT_CENTER_LAT=0

# Base URL for PMTiles, sprites, JSON assets
# Dev: http-server on port 8888
# Production: https://your-cdn.example.com/map_data
VITE_MAP_DATA_URL=http://localhost:8888

# Routing service URL
VITE_ROUTING_SERVICE_URL=http://localhost:3001
```

### Build for Production

```bash
cd web-viewer/react-app
npm run build
```

Output in `web-viewer/react-app/dist/`. Deploy with Docker:

```powershell
docker compose up -d react-app
```

The `docker-compose.yml` also accepts build-time `ARG` overrides for all `VITE_*` variables:

```powershell
VITE_MAP_DATA_URL=https://cdn.example.com/map_data docker compose up -d react-app
```

## Step 7: Run the Routing Service ⚠️ VERY EXPERIMENTAL

> **WARNING**: The routing service is very experimental and does not work correctly. Calculated routes may be wrong, incomplete, or crash the service. It is not suitable for production use.

The routing service is a Node.js/Express server that performs A* pathfinding on the exported routing graph.

### Local Development

```bash
cd routing-service
npm install    # first time
npm run dev    # ts-node, no build required
```

Runs on port 3001 by default.

### Via Docker

```powershell
docker compose up -d routing
```

The service reads routing graphs from `map_data/<game>/geojson/routing-graph.json` (mounted as `/data`).

To override the port:

```powershell
ROUTING_PORT=3002 docker compose up -d routing
```

## Complete Workflow Example

### First Time Setup

```powershell
# Windows PowerShell — project root

# 1. Build the C# CLI
.\build.bat

# 2. Build the Tippecanoe Docker image (WSL/bash)
#    Run this in a WSL terminal:
#    docker build -t tsmap-tippecanoe -f Dockerfile.tippecanoe .

# 3. Export game data
.\TsMap.Cli\bin\Release\net472\TsMap.Cli.exe `
  -g "E:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" `
  -o ".\map_data\ets2" -f all

# 4. Generate vector tiles (WSL terminal)
#    cd /mnt/e/Progetti/ts-map
#    ./docker-tippecanoe.sh ets2

# 5. (Optional) Generate raster background
#    scripts\convert_map_background.bat ets2
#    python scripts\generate_background_pmtiles.py ets2

# 6. Generate sprites
npm install sharp
node generate-sprites.js ets2

# 7. Start the viewer
.\webviewer-dev.bat
start http://localhost:3000?game=ets2

# 8. (Optional) Start routing service
#    docker compose up -d routing
```

### Updating After Game Updates

```powershell
# 1. Rebuild if C# code changed
.\build.bat

# 2. Re-export game data
.\TsMap.Cli\bin\Release\net472\TsMap.Cli.exe -g "<path>" -o ".\map_data\ets2" -f all

# 3. Regenerate tiles (WSL)
#    ./docker-tippecanoe.sh ets2

# 4. Refresh browser (Ctrl+F5)
```

## Map Viewer Features

The viewer is a React 18 + MapLibre GL JS application served at http://localhost:3000.

### Controls

- **Mouse drag** — Pan
- **Mouse wheel** — Zoom
- **Ctrl + drag** — Rotate
- **Right-click drag** — Adjust pitch (3D angle)

### Features

- **Game switcher** — Dropdown to switch between ETS2, ATS, ProMods, etc.
- **City search** — Dropdown to fly to any city
- **Live player tracking** — TruckersMP positions at zoom > 7
- **Coordinate display** — Current zoom, lat/lng bounds, and in-game X/Z coordinates
- **Routing** — Click two points to calculate a route via the routing service

### Map Layers

- Flat terrain, roads, prefab roads, ferry lines (dashed blue)
- 3D extruded buildings
- Cities (pins + labels), companies (circles), overlays (sprite icons)
- Building footprints (separate PMTiles, visible at high zoom)
- Hidden/secret roads (separate PMTiles, toggle in viewer)

## Configuration

### Coordinate System

The map uses a custom equirectangular projection:

- Centered at (0, 0), ±35° latitude range
- Aspect ratio preserved from the game map's bounding box
- Minimises distortion near the equator for web rendering

See [TsMap/GeoJsonExporter.cs](TsMap/GeoJsonExporter.cs#L38) to adjust `maxExtent` if needed.

### Zoom Levels

| Zoom | Detail |
|------|--------|
| 0–3 | Entire map overview |
| 4–5 | Regional (map fits screen) |
| 6–7 | State/province level |
| 8–10 | City and road detail |

Adjust with `MAIN_MAXZOOM` / `FOOTPRINTS_MAXZOOM` env vars when running `docker-tippecanoe.sh`.

### PMTiles on Cloudflare R2

For production, serve PMTiles from a Cloudflare-cached custom domain (not raw R2 URLs). PMTiles depends on HTTP Range requests; edge caching makes panning much smoother after the first load.

Use `upload-overlay-images.sh` to push assets to R2 via `rclone`:

```bash
# Configure rclone (copy the example and fill in credentials)
cp rclone.conf.example rclone.conf
# Edit rclone.conf with your Cloudflare R2 credentials

# Sync everything for ets2
./upload-overlay-images.sh all ets2

# Sync only PMTiles
./upload-overlay-images.sh pmtiles

# Sync only sprites
./upload-overlay-images.sh sprites ets2 ats
```

The script sets:
- `Cache-Control: public, max-age=3600, must-revalidate` for PMTiles (re-fetchable after 1 h)
- `Cache-Control: public, max-age=31536000, immutable` for sprites (versioned by `VITE_MAP_ASSET_VERSION`)
- `Content-Type: application/vnd.pmtiles` for `.pmtiles` files

When regenerating sprites for production, bump `VITE_MAP_ASSET_VERSION` in `web-viewer/react-app/.env` before rebuilding.

### Docker Service Ports

Override any port via environment variables before running `docker compose`:

```powershell
$env:ROUTING_PORT=3002
$env:REACT_APP_PORT=3001
docker compose up -d
```

## Troubleshooting

### Build fails: `.NET Framework 4.7.2 not found`

The CLI targets `net472`. Install the [.NET Framework 4.7.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net472).

### Export error: `Game directory not found`

- Verify the `-g` path points to the folder containing `Euro Truck Simulator 2.exe` (or `American Truck Simulator.exe`)
- Use quotes around paths with spaces

### Export produces no data / empty GeoJSON

- Confirm the game is fully installed (not just the launcher)
- Check that `base.scs` exists in the game directory
- Try running without mods first

### Tippecanoe fails in WSL

- Ensure Docker Desktop has WSL integration enabled: **Docker Desktop → Settings → Resources → WSL Integration**
- Confirm the `tsmap-tippecanoe` image exists: `docker images tsmap-tippecanoe`
- Check the GEOJSON path passed to Docker is correct (must be under the WSL-accessible mount)

### Viewer is blank / map not loading

- Open browser console (F12) and check for errors
- Confirm `map_data/<game>/pmtiles/<game>.pmtiles` exists
- Ensure `VITE_MAP_DATA_URL` in `.env` points to the correct server
- Verify `http://localhost:8888` (http-server) is running when in dev mode

### Sprites missing / wrong icons

- Re-run `node generate-sprites.js <game>`
- Confirm `map_data/<game>/overlay_images/` contains PNG files
- Check browser console for 404s on sprite requests

### Routing service not responding

- Confirm `routing-graph.json` was exported (`-f routing` or `-f all`)
- Check the service logs: `docker compose logs routing`
- Verify `VITE_ROUTING_SERVICE_URL` in `.env` matches the running service port

## Performance Notes

### PMTiles File Sizes (approximate)

| Game | Main tiles | Footprints |
|------|-----------|------------|
| ETS2 (vanilla) | ~150–300 MB | ~50–100 MB |
| ATS (vanilla) | ~100–200 MB | ~30–80 MB |
| ETS2 + ProMods | ~400–700 MB | ~150–300 MB |

### Generation Times (modern machine)

| Step | Time |
|------|------|
| C# export (GeoJSON + routing) | 1–3 min per game |
| Tippecanoe (main tiles) | 2–5 min per game |
| Tippecanoe (footprints) | 30 s–2 min |
| Background raster PMTiles | 1–3 min |

### Browser Performance

- Use Chrome, Firefox, or Edge (WebGL required)
- Ensure hardware acceleration is enabled
- 4 GB+ RAM recommended for ProMods-scale maps

## Resources

- **MapLibre GL JS**: https://maplibre.org/maplibre-gl-js-docs/
- **Tippecanoe**: https://github.com/felt/tippecanoe
- **PMTiles spec**: https://github.com/protomaps/PMTiles
- **go-pmtiles CLI**: https://github.com/protomaps/go-pmtiles/releases
- **DirectXTex (texconv)**: https://github.com/microsoft/DirectXTex/releases
- **GeoJSON spec**: https://geojson.org/

## Support

1. Check this README and troubleshooting section
2. Review the main [README.md](README.md)
3. Check Docker logs: `docker compose logs`
4. Open an issue on GitHub

