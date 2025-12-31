# TsMap.Cli - Command-Line Map Data Exporter

A .NET 6 command-line tool for exporting Euro Truck Simulator 2 and American Truck Simulator map data.

## Features

- 🚀 Fast command-line operation
- 📊 Export GeoJSON files (for vector tiles)
- 📄 Export JSON data files (cities, countries)
- 🎯 No GUI required
- ⚡ Modern .NET 6 with async/await

## Prerequisites

- .NET 6 SDK or later
- Windows (references TsMap.dll which is .NET Framework)
- ETS2 or ATS game installed

## Building

From the root `ts-map` directory:

```powershell
# Build TsMap library first
msbuild TsMap\TsMap.csproj /p:Configuration=Release

# Build CLI
dotnet build TsMap.Cli\TsMap.Cli.csproj -c Release
```

## Usage

### Basic Usage

```powershell
# Export GeoJSON only
dotnet run --project TsMap.Cli -- --game "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" --output .\map_data\ets2 --format geojson

# Export JSON data only
dotnet run --project TsMap.Cli -- --game "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" --output .\map_data\ets2 --format json

# Export everything
dotnet run --project TsMap.Cli -- --game "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" --output .\map_data\ets2 --format all
```

### After Building

```powershell
# Run the compiled executable
.\TsMap.Cli\bin\Release\net6.0\TsMap.Cli.exe --game "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" -o .\map_data\ets2

# Or publish as single file
dotnet publish TsMap.Cli -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true

# Then run
.\TsMap.Cli\bin\Release\net6.0\win-x64\publish\TsMap.Cli.exe -g "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" -o .\map_data\ets2
```

## Command-Line Options

| Option     | Alias | Description                                                 | Required |
| ---------- | ----- | ----------------------------------------------------------- | -------- |
| `--game`   | `-g`  | Path to ETS2/ATS game directory                             | Yes      |
| `--output` | `-o`  | Output directory for exported data                          | Yes      |
| `--format` | `-f`  | Export format: `geojson`, `json`, or `all` (default: `all`) | No       |

## Output Structure

### GeoJSON Files (for vector tiles)

```
output/
  geojson/
    roads.geojson          - Simple road segments
    prefab_roads.geojson   - Roads inside intersections/junctions
    prefabs.geojson        - Prefab boundaries (polygons)
    cities.geojson         - City locations
    companies.geojson      - Company locations
    ferries.geojson        - Ferry connections
```

### JSON Files (for web viewer)

```
output/
  Cities.json       - City data with localization
  Countries.json    - Country data
```

## Examples

### American Truck Simulator

```powershell
TsMap.Cli.exe -g "C:\SteamLibrary\steamapps\common\American Truck Simulator" -o .\map_data\ats
```

### Export Only GeoJSON (for tippecanoe)

```powershell
TsMap.Cli.exe -g "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" -o .\map_data\ets2 -f geojson
```

### Use in Scripts

```powershell
# PowerShell script to export and generate vector tiles
$gameDir = "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2"
$outputDir = ".\map_data\ets2"

# Export GeoJSON
.\TsMap.Cli.exe -g $gameDir -o $outputDir -f geojson

# Generate vector tiles
.\generate-vector-tiles.ps1 -OutputPath $outputDir
```

## Performance

Typical export times (ETS2 full map):

- Map parsing: 10-30 seconds
- GeoJSON export: 30-60 seconds
- JSON export: 1-2 seconds
- **Total: ~60-90 seconds**

## Troubleshooting

### "Could not load file or assembly 'TsMap'"

Make sure you build TsMap.dll first:

```powershell
msbuild TsMap\TsMap.csproj /p:Configuration=Release
```

### "Game directory not found"

Verify the game path. Common locations:

- Steam: `C:\Program Files (x86)\Steam\steamapps\common\Euro Truck Simulator 2`
- Steam (custom): Check Steam Library folders
- Non-Steam: Check installation directory

### Large file sizes

This is normal! The full ETS2 map generates:

- `roads.geojson`: 300-500 MB
- `prefab_roads.geojson`: 200-400 MB
- Other files: 50-100 MB

These will be compressed significantly when converted to vector tiles.

## Next Steps

After exporting GeoJSON:

1. **Generate vector tiles:**

   ```powershell
   .\generate-vector-tiles.ps1 -OutputPath .\map_data\ets2
   ```

2. **View in web browser:**
   - Update web viewer to use MapLibre GL JS
   - Load vector tiles instead of PNG tiles

## Development

Built with:

- .NET 6
- System.CommandLine for argument parsing
- TsMap library for game file parsing
- Newtonsoft.Json for serialization

## License

Same as TsMap project (see root LICENSE file)
