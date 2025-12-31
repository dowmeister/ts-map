@echo off
echo ======================================
echo Building TsMap Projects
echo ======================================

echo.
echo Building TsMap library...
dotnet build TsMap\TsMap.csproj -c Release -v:m
if %ERRORLEVEL% neq 0 (
    echo ERROR: TsMap build failed
    exit /b 1
)

echo.
echo Building TsMap.Cli...
dotnet build TsMap.Cli\TsMap.Cli.csproj -c Release -v:m
if %ERRORLEVEL% neq 0 (
    echo ERROR: TsMap.Cli build failed
    exit /b 1
)

echo.
echo ======================================
echo Build completed successfully!
echo ======================================