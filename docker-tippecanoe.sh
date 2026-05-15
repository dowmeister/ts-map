#!/bin/bash
# Docker wrapper for tippecanoe tile generation
# Usage: ./docker-tippecanoe.sh [ets2|ats]
#
# Generates TWO pmtiles files per game:
#   {game}.pmtiles          — main map layers (roads, cities, overlays, etc.) Z3–Z8
#   {game}-footprints.pmtiles — building footprints only, Z6–Z8
# Keeping footprints separate reduces the main file size and improves
# HTTP Range-request performance on CDN/R2 backends.

GAME=${1:-ets2}
MODE=${2:-all}
MAIN_MAXZOOM=${MAIN_MAXZOOM:-10}
FOOTPRINTS_MAXZOOM=${FOOTPRINTS_MAXZOOM:-6}

if [[ "$MODE" != "all" && "$MODE" != "pmtiles" ]]; then
    echo "ERROR: Invalid mode '$MODE'. Use 'all' or 'pmtiles'."
    echo "Usage: $0 [ets2|ats] [all|pmtiles]"
    exit 1
fi

echo "======================================="
echo "Generating Vector Tiles with Tippecanoe (Docker)"
echo "Game: $GAME | Mode: $MODE"
echo "Main maxzoom: z$MAIN_MAXZOOM | Footprints maxzoom: z$FOOTPRINTS_MAXZOOM"
echo "======================================="

if [[ "$MODE" == "all" ]]; then
    GEOJSON_DIR="map_data/$GAME/geojson"

    if [ ! -f "$GEOJSON_DIR/roads.geojson" ]; then
        echo "ERROR: roads.geojson not found in $GEOJSON_DIR"
        exit 1
    fi

    mkdir -p "map_data/${GAME}/mbtiles"

    # ── 1. Main map layers ────────────────────────────────────────────────────
    echo ""
    echo "Removing old main mbtiles file..."
    rm -f "map_data/${GAME}/mbtiles/tiles.mbtiles"

    echo ""
    echo "Running tippecanoe (main map layers)..."
    docker run --rm \
        -v "$(pwd)/map_data/$GAME:/data" \
        tsmap-tippecanoe \
        bash -c "tippecanoe \
        -o \"/data/mbtiles/tiles.mbtiles\" \
        -z${MAIN_MAXZOOM} -Z3 \
        --drop-densest-as-needed \
        --force \
        -L roads:/data/geojson/roads.geojson \
        -L prefab_roads:/data/geojson/prefab_roads.geojson \
        -L prefab_flat:/data/geojson/prefab_flat.geojson \
        -L prefab_buildings:/data/geojson/prefab_buildings.geojson \
        -L map_flat:/data/geojson/map_flat.geojson \
        -L map_buildings:/data/geojson/map_buildings.geojson \
        -L buildings:/data/geojson/buildings.geojson \
        -L ferries:/data/geojson/ferries.geojson \
        -L cities:/data/geojson/cities.geojson \
        -L countries:/data/geojson/countries.geojson \
        -L companies:/data/geojson/companies.geojson \
        -L overlays:/data/geojson/overlays.geojson"

    if [ $? -ne 0 ]; then
        echo "ERROR: Tippecanoe (main) failed"
        exit 1
    fi

    echo ""
    echo "======================================="
    echo "Main map tiles generated: map_data/${GAME}/mbtiles/tiles.mbtiles"
    echo "======================================="

    # ── 2. Footprints layer ───────────────────────────────────────────────────
    if [ -f "$GEOJSON_DIR/footprints.geojson" ]; then
        echo ""
        echo "Removing old footprints mbtiles file..."
        rm -f "map_data/${GAME}/mbtiles/tiles-footprints.mbtiles"

        echo ""
        echo "Running tippecanoe (footprints + hidden layers)..."
        docker run --rm \
            -v "$(pwd)/map_data/$GAME:/data" \
            tsmap-tippecanoe \
            bash -c "tippecanoe \
            -o \"/data/mbtiles/tiles-footprints.mbtiles\" \
            -z${FOOTPRINTS_MAXZOOM} -Z6 \
            --drop-densest-as-needed \
            --force \
            -L footprints:/data/geojson/footprints.geojson \
            -L hidden_roads:/data/geojson/hidden_roads.geojson \
            -L hidden_prefabs:/data/geojson/hidden_prefabs.geojson"

        if [ $? -ne 0 ]; then
            echo "ERROR: Tippecanoe (footprints) failed"
            exit 1
        fi

        echo ""
        echo "======================================="
        echo "Footprints tiles generated: map_data/${GAME}/mbtiles/tiles-footprints.mbtiles"
        echo "======================================="
    else
        echo ""
        echo "WARNING: footprints.geojson not found, skipping footprints tile generation."
    fi
fi

# ── Convert main tiles to PMTiles ─────────────────────────────────────────────
if [ ! -f "map_data/${GAME}/mbtiles/tiles.mbtiles" ]; then
    echo "ERROR: map_data/${GAME}/mbtiles/tiles.mbtiles not found. Run without 'pmtiles' mode first."
    exit 1
fi

echo ""
echo "Converting main tiles to PMTiles..."

mkdir -p "map_data/pmtiles"
rm -f "map_data/pmtiles/${GAME}.pmtiles"

_convert_pmtiles() {
    local src="$1"
    local dst="$2"
    if command -v pmtiles &>/dev/null; then
        pmtiles convert "$src" "$dst" \
            && echo "Output: $dst" \
            || { echo "ERROR: PMTiles conversion failed for $dst"; exit 1; }
    else
        echo "(pmtiles CLI not found, using Docker...)"
        local src_dir dst_dir src_file dst_file
        src_dir="$(dirname "$(realpath "$src")")"
        src_file="$(basename "$src")"
        dst_dir="$(dirname "$(realpath "$dst")")"
        dst_file="$(basename "$dst")"
        docker run --rm \
            -v "${src_dir}:/src_vol" \
            -v "${dst_dir}:/dst_vol" \
            ghcr.io/protomaps/go-pmtiles:latest \
            convert "/src_vol/${src_file}" "/dst_vol/${dst_file}" \
            && echo "Output: $dst" \
            || { echo "ERROR: PMTiles conversion failed for $dst"; exit 1; }
    fi
}

_convert_pmtiles \
    "map_data/${GAME}/mbtiles/tiles.mbtiles" \
    "map_data/pmtiles/${GAME}.pmtiles"

# ── Convert footprints tiles to PMTiles (if generated) ────────────────────────
if [ -f "map_data/${GAME}/mbtiles/tiles-footprints.mbtiles" ]; then
    echo ""
    echo "Converting footprints tiles to PMTiles..."
    rm -f "map_data/pmtiles/${GAME}-footprints.pmtiles"
    _convert_pmtiles \
        "map_data/${GAME}/mbtiles/tiles-footprints.mbtiles" \
        "map_data/pmtiles/${GAME}-footprints.pmtiles"
fi
