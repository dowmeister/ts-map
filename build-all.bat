@echo off
echo ======================================
echo Full Build Pipeline: Build + Export + Tiles
echo ======================================

set GAME_PATH=C:\Program Files (x86)\Steam\steamapps\common\Euro Truck Simulator 2
set OUTPUT_DIR=C:\personal\ts-map\map_data\ets2

echo.
echo [1/3] Building projects...
echo ======================================
call build.bat
if %ERRORLEVEL% neq 0 (
    echo ERROR: Build failed
    exit /b 1
)

echo.
echo.
echo [2/3] Exporting GeoJSON from game data...
echo ======================================
.\TsMap.Cli\bin\Release\net8.0\TsMap.Cli.exe -g "%GAME_PATH%" -o "%OUTPUT_DIR%" -f geojson
if %ERRORLEVEL% neq 0 (
    echo ERROR: GeoJSON export failed
    exit /b 1
)

echo.
echo.
echo [3/3] Generating vector tiles...
echo ======================================
call generate-mbtiles.bat
if %ERRORLEVEL% neq 0 (
    echo ERROR: Tile generation failed
    exit /b 1
)

echo.
echo.
echo ======================================
echo COMPLETE! All steps finished successfully
echo ======================================
echo.
echo To view the map:
echo   1. Start tile server: npx -y tileserver-gl-light map_data\ets2\map.mbtiles --port 8080
echo   2. Open: http://localhost:5500/web-viewer/vector-viewer.html
echo.
pause
