@echo off
REM Docker wrapper for tippecanoe tile generation (Windows)
REM Usage: docker-tippecanoe.bat [ets2|ats]

set GAME=%1
if "%GAME%"=="" set GAME=ets2

echo =======================================
echo Generating Vector Tiles with Tippecanoe (Docker)
echo Game: %GAME%
echo =======================================

set GEOJSON_DIR=map_data\%GAME%\geojson

if not exist "%GEOJSON_DIR%\roads.geojson" (
    echo ERROR: roads.geojson not found in %GEOJSON_DIR%
    exit /b 1
)

echo.
echo Building tippecanoe image if needed...
docker build -t tsmap-tippecanoe -f Dockerfile.tippecanoe .

echo.
echo Removing old mbtiles file...
if not exist "mbtiles" mkdir mbtiles
del /Q "mbtiles\%GAME%.mbtiles" 2>nul

echo.
echo Running tippecanoe...
docker run --rm -v "%CD%\map_data\%GAME%:/data" tsmap-tippecanoe bash -c "tippecanoe -o /data/%GAME%.mbtiles -z8 -Z3 --drop-densest-as-needed --force -L roads:/data/geojson/roads.geojson -L prefab_roads:/data/geojson/prefab_roads.geojson -L prefab_flat:/data/geojson/prefab_flat.geojson -L prefab_buildings:/data/geojson/prefab_buildings.geojson -L map_flat:/data/geojson/map_flat.geojson -L map_buildings:/data/geojson/map_buildings.geojson -L ferries:/data/geojson/ferries.geojson -L cities:/data/geojson/cities.geojson -L countries:/data/geojson/countries.geojson -L companies:/data/geojson/companies.geojson -L overlays:/data/geojson/overlays.geojson"

if %ERRORLEVEL% neq 0 (
    echo ERROR: Tippecanoe failed
    exit /b 1
)

echo.
echo Moving mbtiles file to output directory...
move /Y "map_data\%GAME%\%GAME%.mbtiles" "mbtiles\%GAME%.mbtiles"

if %ERRORLEVEL% neq 0 (
    echo ERROR: Tippecanoe failed
    exit /b 1
)

echo.
echo =======================================
echo Vector tiles generated successfully!
echo Game: %GAME%
echo Output: mbtiles\%GAME%.mbtiles
echo =======================================
echo.
echo To start the servers:
echo   docker-compose up -d
echo.
echo Then open: http://localhost:5500/vector-viewer.html?game=%GAME%
