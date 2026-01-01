@echo off
REM Usage: generate-mbtiles.bat [ets2|ats]
REM Default: ets2

set GAME=%1
if "%GAME%"=="" set GAME=ets2

if not "%GAME%"=="ets2" if not "%GAME%"=="ats" (
    echo ERROR: Invalid game parameter. Use 'ets2' or 'ats'
    echo Usage: generate-mbtiles.bat [ets2^|ats]
    exit /b 1
)

echo ======================================
echo Generating Vector Tiles with Tippecanoe
echo Game: %GAME%
echo ======================================

set GEOJSON_DIR=map_data\%GAME%\geojson
set OUTPUT_FILE=map_data\%GAME%\map.mbtiles

echo.
echo Checking if GeoJSON files exist...
if not exist "%GEOJSON_DIR%\roads.geojson" (
    echo ERROR: roads.geojson not found
    exit /b 1
)
if not exist "%GEOJSON_DIR%\prefab_roads.geojson" (
    echo ERROR: prefab_roads.geojson not found
    exit /b 1
)

echo.
echo Removing old mbtiles file...
wsl bash -c "cd /mnt/c/Personal/ts-map/map_data/%GAME% && rm -f ../../mbtiles/%GAME%.mbtiles"

echo.
echo Running tippecanoe...
wsl bash -c "cd /mnt/c/Personal/ts-map/map_data/%GAME% && tippecanoe -o ../../mbtiles/%GAME%.mbtiles -z8 -Z0 --drop-densest-as-needed --force -L roads:geojson/roads.geojson -L prefab_roads:geojson/prefab_roads.geojson -L prefab_flat:geojson/prefab_flat.geojson -L prefab_buildings:geojson/prefab_buildings.geojson -L map_flat:geojson/map_flat.geojson -L map_buildings:geojson/map_buildings.geojson -L ferries:geojson/ferries.geojson -L cities:geojson/cities.geojson -L companies:geojson/companies.geojson -L overlays:geojson/overlays.geojson"
if %ERRORLEVEL% neq 0 (
    echo ERROR: Tippecanoe failed
    exit /b 1
)

echo.
echo ======================================
echo Vector tiles generated successfully!
echo Game: %GAME%
echo Output: %OUTPUT_FILE%
echo ======================================
echo.
echo To view the map, start the tile server:
echo   npx -y tileserver-gl-light map_data\%GAME%\map.mbtiles --port 8080
echo.
echo Then open: http://localhost:5500/web-viewer/vector-viewer.html?game=%GAME%
