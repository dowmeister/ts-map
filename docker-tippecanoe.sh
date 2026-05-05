#!/bin/bash
# Docker wrapper for tippecanoe tile generation
# Usage: ./docker-tippecanoe.sh [ets2|ats]

GAME=${1:-ets2}

echo "======================================="
echo "Generating Vector Tiles with Tippecanoe (Docker)"
echo "Game: $GAME"
echo "======================================="

GEOJSON_DIR="map_data/$GAME/geojson"

if [ ! -f "$GEOJSON_DIR/roads.geojson" ]; then
    echo "ERROR: roads.geojson not found in $GEOJSON_DIR"
    exit 1
fi

echo ""
echo "Removing old mbtiles file..."
mkdir -p mbtiles
rm -f "mbtiles/${GAME}.mbtiles"

echo ""
echo "Running tippecanoe..."
docker run --rm \
    -v "$(pwd)/map_data/$GAME:/data" \
    tsmap-tippecanoe \
    bash -c "tippecanoe \
    -o \"/data/${GAME}.mbtiles\" \
    -z8 -Z3 \
    --drop-densest-as-needed \
    --force \
    -L roads:/data/geojson/roads.geojson \
    -L prefab_roads:/data/geojson/prefab_roads.geojson \
    -L prefab_flat:/data/geojson/prefab_flat.geojson \
    -L prefab_buildings:/data/geojson/prefab_buildings.geojson \
    -L map_flat:/data/geojson/map_flat.geojson \
    -L map_buildings:/data/geojson/map_buildings.geojson \
    -L buildings:/data/geojson/buildings.geojson \
    -L footprints:/data/geojson/footprints.geojson \
    -L hidden_roads:/data/geojson/hidden_roads.geojson \
    -L hidden_prefabs:/data/geojson/hidden_prefabs.geojson \
    -L ferries:/data/geojson/ferries.geojson \
    -L cities:/data/geojson/cities.geojson \
    -L companies:/data/geojson/companies.geojson \
    -L overlays:/data/geojson/overlays.geojson"

if [ $? -ne 0 ]; then
    echo "ERROR: Tippecanoe failed"
    exit 1
fi

echo ""
echo "Moving mbtiles file to output directory..."
mv "map_data/$GAME/${GAME}.mbtiles" "mbtiles/${GAME}.mbtiles"

echo ""
echo "======================================="
echo "Vector tiles generated successfully!"
echo "Game: $GAME"
echo "Output: mbtiles/${GAME}.mbtiles"
echo "======================================="

echo ""
echo "Converting to PMTiles..."

# Detect Python — override with PYTHON_CMD env var if needed
PYTHON_CMD="${PYTHON_CMD:-}"
if [ -z "$PYTHON_CMD" ]; then
    if command -v python3 &>/dev/null; then
        PYTHON_CMD="python3"
    elif command -v python &>/dev/null; then
        PYTHON_CMD="python"
    fi
fi

if [ -z "$PYTHON_CMD" ]; then
    echo "WARNING: Python not found, skipping PMTiles conversion."
    echo "  Install Python + 'pip install pmtiles', or set PYTHON_CMD=/path/to/python"
else
    mkdir -p pmtiles
    rm -f "pmtiles/${GAME}.pmtiles"

    "$PYTHON_CMD" -c "
from pmtiles.convert import mbtiles_to_pmtiles
mbtiles_to_pmtiles('mbtiles/${GAME}.mbtiles', 'pmtiles/${GAME}.pmtiles', None)
" && echo "Output: pmtiles/${GAME}.pmtiles" \
    || echo "WARNING: PMTiles conversion failed. Is 'pmtiles' installed? Run: pip install pmtiles"
fi
