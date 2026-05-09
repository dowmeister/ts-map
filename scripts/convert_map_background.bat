@echo off
setlocal

if "%~1"=="" (
    echo Usage: convert_map_background.bat ^<game^>
    echo Example: convert_map_background.bat ets2
    exit /b 1
)

set GAME=%~1
set DIR=%~dp0..\map_data\%GAME%\map_background

if not exist "%DIR%" (
    echo Error: directory not found: %DIR%
    echo Run TsMap.Cli export first.
    exit /b 1
)

where texconv >nul 2>&1
if errorlevel 1 (
    if exist "%~dp0texconv.exe" (
        set TEXCONV=%~dp0texconv.exe
    ) else (
        echo Error: texconv.exe not found in PATH or script directory.
        echo Download from https://github.com/microsoft/DirectXTex/releases
        exit /b 1
    )
) else (
    set TEXCONV=texconv
)

echo Converting DDS files in %DIR% ...
for %%F in (map map0 map1 map2 map3) do (
    if exist "%DIR%\%%F.dds" (
        echo   %%F.dds ...
        "%TEXCONV%" -ft png -y -o "%DIR%" "%DIR%\%%F.dds"
    )
)

echo Done. PNG files are in %DIR%
endlocal
