#!/bin/bash
# Docker wrapper for tippecanoe tile generation
# Usage: ./docker-tippecanoe.sh [ets2|ats]

GAME=${1:-ets2}
MODE=${2:-all}

if [[ "$MODE" != "all" && "$MODE" != "pmtiles" ]]; then
    echo "ERROR: Invalid mode '$MODE'. Use 'all' or 'pmtiles'."
    echo "Usage: $0 [ets2|ats] [all|pmtiles]"
    exit 1
fi

echo "======================================="
echo "Generating Vector Tiles with Tippecanoe (Docker)"
echo "Game: $GAME | Mode: $MODE"
echo "======================================="

if [[ "$MODE" == "all" ]]; then
    GEOJSON_DIR="map_data/$GAME/geojson"

    if [ ! -f "$GEOJSON_DIR/roads.geojson" ]; then
        echo "ERROR: roads.geojson not found in $GEOJSON_DIR"
        exit 1
    fi

    echo ""
    echo "Removing old mbtiles file..."
    mkdir -p "map_data/${GAME}/mbtiles"
    rm -f "map_data/${GAME}/mbtiles/tiles.mbtiles"

    echo ""
    echo "Running tippecanoe..."
    docker run --rm \
        -v "$(pwd)/map_data/$GAME:/data" \
        tsmap-tippecanoe \
        bash -c "tippecanoe \
        -o \"/data/mbtiles/tiles.mbtiles\" \
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
    echo "======================================="
    echo "Vector tiles generated successfully!"
    echo "Game: $GAME"
    echo "Output: map_data/${GAME}/mbtiles/tiles.mbtiles"
    echo "======================================="
fi

if [ ! -f "map_data/${GAME}/mbtiles/tiles.mbtiles" ]; then
    echo "ERROR: map_data/${GAME}/mbtiles/tiles.mbtiles not found. Run without 'pmtiles' mode first."
    exit 1
fi

echo ""
echo "Converting to PMTiles..."

mkdir -p "map_data/pmtiles"
rm -f "map_data/pmtiles/${GAME}.pmtiles"

if command -v pmtiles &>/dev/null; then
    pmtiles convert "map_data/${GAME}/mbtiles/tiles.mbtiles" "map_data/pmtiles/${GAME}.pmtiles" \
        && echo "Output: map_data/pmtiles/${GAME}.pmtiles" \
        || { echo "ERROR: PMTiles conversion failed"; exit 1; }
else
    echo "(pmtiles CLI not found, using Docker...)"
    docker run --rm \
        -v "$(pwd)/map_data/${GAME}/mbtiles:/mbtiles" \
        -v "$(pwd)/map_data/pmtiles:/pmtiles" \
        ghcr.io/protomaps/go-pmtiles:latest \
        convert "/mbtiles/tiles.mbtiles" "/pmtiles/${GAME}.pmtiles" \
        && echo "Output: map_data/pmtiles/${GAME}.pmtiles" \
        || { echo "ERROR: PMTiles conversion failed"; exit 1; }
fi
