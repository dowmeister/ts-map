# TsMap.Cli - Command-Line Map Data Exporter

A .NET Framework 4.7.2 command-line tool for exporting Euro Truck Simulator 2 and American Truck Simulator map data.

## Features

- 🚀 Fast command-line operation
- 📊 Export GeoJSON files (for vector tiles)
- 📄 Export JSON data files (cities, countries)
- 🎯 No GUI required
- ⚡ Async/await for efficient processing

## Prerequisites

- .NET Framework 4.7.2 or later (included in Windows 10 1803+)
- Windows
- ETS2 or ATS game installed

## Building

From the root `ts-map` directory:

```powershell
# Build TsMap library first
dotnet build TsMap\TsMap.csproj -c Release

# Build CLI
dotnet build TsMap.Cli\TsMap.Cli.csproj -c Release

# Or use the build script
.\build.bat
```

## Usage

### Basic Usage

```powershell
# Export GeoJSON only
.\TsMap.Cli\bin\Release\TsMap.Cli.exe --game "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" --output .\map_data\ets2 --format geojson

# Export JSON data only
.\TsMap.Cli\bin\Release\TsMap.Cli.exe --game "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" --output .\map_data\ets2 --format json

# Export everything
.\TsMap.Cli\bin\Release\TsMap.Cli.exe --game "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" --output .\map_data\ets2 --format all
```

### After Building

```powershell
# Run the compiled executable
.\TsMap.Cli\bin\Release\TsMap.Cli.exe --game "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" -o .\map_data\ets2
```

## Command-Line Options

| Option        | Alias | Description                                                 | Required |
| ------------- | ----- | ----------------------------------------------------------- | -------- |
| `--game`      | `-g`  | Path to ETS2/ATS game directory                             | Yes      |
| `--output`    | `-o`  | Output directory for exported data                          | Yes      |
| `--format`    | `-f`  | Export format: `geojson`, `json`, or `all` (default: `all`) | No       |
| `--mods-dir`  | `-m`  | Path to directory containing mod files                      | No       |
| `--mods-list` | `-l`  | Path to JSON file listing mods to load                      | No       |

## Mod Support

The CLI supports loading game mods (such as ProMods, RusMap, etc.) to export modded map data.

### Setting Up Mods

1. **Create a mods configuration file** (e.g., `mods.json`):

```json
{
  "mods": [
    "promods-def-v268.scs",
    "promods-map-v268.scs",
    "promods-assets-v268.scs",
    "promods-media-v268.scs"
  ]
}
```

See [mods.example.json](mods.example.json) for a complete example.

2. **Place your mod files** in a directory (e.g., `C:\mods\`):

```
C:\mods\
  promods-def-v268.scs
  promods-map-v268.scs
  promods-model1-v268.scs
  my-custom-mod.zip
```

3. **Run the CLI with mod parameters**:

```powershell
TsMap.Cli.exe -g "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" -o .\map_data\ets2_promods -m "C:\mods" -l .\mods.json
```

### Mod Configuration Format

The JSON configuration file is a simple array of mod filenames:

- Each string is the filename of a mod file that must exist in the mods directory
- Mods are loaded in the order listed (important for load priority)
- To disable a mod, simply remove it from the list or comment it out

**Important:** Mods are loaded in the order specified in the JSON file. The game loads mods with highest priority last, so list your mods in load order (base mods first, overrides last).

### Examples with Mods

#### ProMods Export

```powershell
# Create mods config
@"
{
  \"mods\": [
    \"promods-def-v268.scs\",
    \"promods-map-v268.scs\",
    \"promods-assets-v268.scs\",
    \"promods-media-v268.scs\"
  ]
}
"@ | Out-File -Encoding UTF8 promods.json

# Export with ProMods
TsMap.Cli.exe -g "C:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" -o .\map_data\ets2_promods -m "C:\mods\promods" -l .\promods.json
```

#### Multiple Map Mods

```powershell
# Combine ProMods and RusMap
@"
{
  \"mods\": [
    \"promods-def-v268.scs\",
    \"promods-map-v268.scs\",
    \"rusmap-def-v242.scs\",
    \"rusmap-map-v242.scs\"
  ]
}
"@ | Out-File -Encoding UTF8 combined-mods.json

TsMap.Cli.exe -g "C:\Steam\Euro Truck Simulator 2" -o .\map_data\ets2_combined -m "C:\mods" -l .\combined-mods.json
```

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
