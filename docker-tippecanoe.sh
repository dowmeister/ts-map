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
echo "To start the servers:"
echo "  docker-compose up -d"
echo ""
echo "Then open: http://localhost:5500/vector-viewer.html?game=$GAME"
