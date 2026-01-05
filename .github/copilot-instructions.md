# TsMap - Truck Simulator Map Renderer

This project parses and renders Euro Truck Simulator 2 (ETS2) and American Truck Simulator (ATS) game map files.

## Architecture Overview

### Core Components

- **TsMap (Library)**: Core parsing and rendering logic (.NET Framework 4.7.2)
  - Reads proprietary SCS game archives (`.scs` files - hash/zip formats)
  - Parses binary sector files (`.base`, `.aux`) containing map data
  - Renders maps using GDI+ graphics
  - **Must be built first** - other projects reference the compiled DLL
- **TsMap.Canvas (WinForms App)**: Interactive map viewer with zoom, pan, DLC filtering, and tile export (.NET Framework 4.7.2)
- **TsMap.Cli (CLI Tool)**: Command-line GeoJSON/JSON exporter for vector tile workflows (.NET 8.0)

### Key Data Flow

1. **File System Layer** (`TsMap/FileSystem/`)

   - `UberFileSystem`: Singleton managing all archive files, provides unified file/directory lookup
   - `HashArchiveFile`/`ZipArchiveFile`: Parses SCS proprietary hash archives and standard zip mods
   - `UberFile`/`UberDirectory`: Virtual filesystem abstractions

2. **Parsing Layer** (`TsMapper.cs`, `TsSector.cs`)

   - `TsMapper`: Main orchestrator - loads archives, parses definition files, builds lookup tables
   - `TsSector`: Parses binary sector files (`.base`/`.aux`) into map items
   - Item parsing is **version-dependent** (825, 829, 846, 854, 895+) - each version has different binary layouts

3. **Data Model** (`TsItem/`)

   - Base class: `TsItem` (uid, position, nodes, DLC guard)
   - Specialized items: `TsRoadItem`, `TsPrefabItem`, `TsCityItem`, etc.
   - Items reference `TsNode` objects that define connection points

4. **Rendering** (`TsMapRenderer.cs`)
   - Takes parsed data and renders to GDI+ Graphics context
   - Supports zoom levels, DLC filtering, and selective layer rendering via `RenderFlags`
   - Renders: roads, prefabs, map areas, overlays, ferry lines, cities, companies, buildings, services
   - Companies drawn as rectangles, buildings as squares, services as circles

## Critical Patterns

### Binary Parsing with Version Handling

Items are parsed from raw byte streams using version-specific offsets. Example from [TsRoadItem.cs](TsMap/TsItem/TsRoadItem.cs):

```csharp
if (sector.Version < 829)
    TsRoadItem825(startOffset);
else if (sector.Version >= 829 && sector.Version < 846)
    TsRoadItem829(startOffset);
// ... different parsing methods for each version
```

Each method calculates `BlockSize` by tracking `fileOffset` through the binary structure.

### Unsafe Memory Access

The project uses `unsafe` code for efficient byte array reading. [MemoryHelper.cs](TsMap/Helpers/MemoryHelper.cs) provides fixed-pointer methods:

```csharp
internal static unsafe uint ReadUInt32(byte[] s, int pos)
{
    fixed (byte* numRef = &(s[pos]))
        return *(uint*)numRef;
}
```

**Note**: Both projects require `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in `.csproj` files.

### Token/Hash System

Game files use 64-bit hashes instead of strings. The `ScsToken` class (in `FileSystem/`) converts between strings and hashes using CityHash algorithm. Lookups are always by `ulong` hash, not string paths.

### Singleton Pattern

Core services use lazy singletons:

- `UberFileSystem.Instance`
- `SettingsManager.Current`
- `Logger.Instance`

### DLC Guards

Map items have a `DlcGuard` byte indicating required DLC. Lists defined in [Consts.cs](TsMap/Common/Consts.cs). Renderer filters items based on enabled DLC guards.

## Building and Running

### Multi-Framework Build Requirements

**TsMap Core**: .NET Framework 4.7.2 (builds DLL referenced by other projects)  
**TsMap.Canvas**: .NET Framework 4.7.2 (Windows Forms)  
**TsMap.Cli**: .NET Framework 4.7.2 (CLI tool, references TsMap DLL)  
**Dependencies**: DotNetZip, Newtonsoft.Json, System.CommandLine (via NuGet), libdeflate.dll (native)

### Build Order

Always build TsMap library first, as other projects reference `TsMap.dll`:

```powershell
# Option 1: Use build scripts
.\build.bat              # Builds TsMap library + TsMap.Cli
.\build-all.bat         # Full pipeline: build + export + tiles

# Option 2: Manual build
dotnet build TsMap\TsMap.csproj -c Release
dotnet build TsMap.Cli\TsMap.Cli.csproj -c Release

# Option 3: Visual Studio
msbuild TsMap.sln /p:Configuration=Release
```

### Running the CLI

Export map data for vector tile workflows:

```powershell
# Basic GeoJSON export
.\TsMap.Cli\bin\Release\TsMap.Cli.exe -g "E:\SteamLibrary\steamapps\common\Euro Truck Simulator 2" -o ".\map_data\ets2" -f geojson

# With mods (ProMods, RusMap, etc.)
.\TsMap.Cli\bin\Release\TsMap.Cli.exe -g "<game_path>" -o ".\map_data\promods" -m "<mods_folder>" -l ".\mods\promods.json" -f all
```

### Docker Vector Tile Workflow

For web-based vector map viewing (requires Docker):

```powershell
# 1. Export GeoJSON using CLI (see above)
# 2. Generate vector tiles
bash docker-tippecanoe.sh ets2

# 3. Start tile server and web viewer
docker compose up -d

# 4. View at http://localhost:5500/vector-viewer.html?game=ets2
```

Run the Canvas app: `TsMap.Canvas/bin/Release/TsMap.Canvas.exe`

## Common Tasks

### Adding a New Item Type

1. Add enum value to `TsItemType` in [TsTypes.cs](TsMap/TsTypes.cs)
2. Create new class in `TsItem/` inheriting from `TsItem`
3. Add parsing case in [TsSector.cs](TsSector.cs#L50-L150) `Parse()` method
4. Add rendering logic in [TsMapRenderer.cs](TsMap/TsMapRenderer.cs) if visual
5. Add to appropriate collection in `TsMapper` if needed

### Supporting New Game Versions

When game updates change sector file format:

1. Document new binary structure in `docs/structures/` using 010 Editor templates
2. Add new version check in item constructors (e.g., `TsRoadItem895()`)
3. Update `BlockSize` calculation for the new layout
4. Test with both new and old saves to ensure backward compatibility

### File Lookup Pattern

Always use `UberFileSystem` for file access:

```csharp
var file = UberFileSystem.Instance.GetFile("def/city/city_name.sii");
var data = file.Entry.Read();
```

Never use `System.IO.File` directly for game data files.

### Mod Loading System

The CLI supports loading game mods via JSON configuration. Example `mods.json`:

```json
{
  "mods": [
    "promods-def-v268.scs",
    "promods-map-v268.scs"
  ]
}
```

Mods are loaded in order and mounted into `UberFileSystem` as archive overlays. Files in later mods override earlier ones. The `Mod` class has a `Load` flag to enable/disable individual mods without removing from the list.

## GeoJSON Export Pipeline

The CLI tool exports game data to GeoJSON for vector tile generation. See [TsMap.Cli/Program.cs](TsMap.Cli/Program.cs) and [TsMap/GeoJsonExporter.cs](TsMap/GeoJsonExporter.cs).

### Coordinate Conversion

Game coordinates (X/Z plane, units in meters) are converted to WGS84 lat/lng:

```csharp
private (double lon, double lat) GameToLatLng(float gameX, float gameZ)
{
    var normalizedX = (gameX - _mapper.minX) / (_mapper.maxX - _mapper.minX);
    var normalizedZ = (gameZ - _mapper.minZ) / (_mapper.maxZ - _mapper.minZ);
    // Maps to ±35° range, preserving aspect ratio
}
```

This ensures minimal distortion near the equator for web map viewing.

### Export Formats

- **GeoJSON**: Roads, prefabs (roads/flat/buildings), cities, companies, ferry connections, map areas
- **JSON**: Cities.json, CompanyDefs.json, BusStops.json, CargoDefs.json
- **Images**: Company/service overlay icons extracted from game files

## Tile Map Export System

The Canvas app can export maps as slippy map tiles (similar to OpenStreetMap) for web viewing. See [live example](https://dariowouters.github.io/ts-tile-map-example/).

### Export Process

1. **Zoom Level Calculation**: Uses powers of 2 (0 = 1 tile, 1 = 4 tiles, 2 = 16 tiles, etc.)

   ```csharp
   for (int z = startZoomLevel; z <= endZoomLevel; z++)
       _totalTileCount += (uint)Math.Pow(4, z); // Total tiles for all zoom levels
   ```

2. **Map Centering** ([TsMapCanvas.cs#L127](TsMap.Canvas/TsMapCanvas.cs#L127)):

   - `ZoomOutAndCenterMap()` calculates scale to fit entire map within tile grid
   - Adds configurable padding (`MapPadding` setting, default 500 units)
   - Uses mapper's bounds (`minX`, `maxX`, `minZ`, `maxZ`)

3. **Tile Generation** ([TsMapCanvas.cs#L107](TsMap.Canvas/TsMapCanvas.cs#L107)):

   - Each tile rendered to `TileSize × TileSize` bitmap (default 256×256)
   - Directory structure: `{exportPath}/Tiles/{z}/{x}/{y}.png`
   - Follows [slippy map tile naming](https://wiki.openstreetmap.org/wiki/Slippy_map_tilenames)

4. **Export Data** (`ExportFlags` enum in [TsTypes.cs](TsMap/TsTypes.cs)):
   - `TileMapInfo.json`: Contains coordinate bounds and zoom levels
   - `CityList.json`: City coordinates and names
   - `OverlayList.json` + PNG exports: Map overlay images
   - `BusStops.json`, `CargoDefs.json`: Game data exports

### Key Settings

Stored in `SettingsManager.Current.Settings.TileGenerator`:

- `TileSize`: Pixel dimensions per tile (default 256)
- `MapPadding`: Extra space around map edges in game units (default 500)
- `StartZoomLevel`/`EndZoomLevel`: Zoom range to export
- `RenderFlags`: Which map elements to include (roads, prefabs, cities, etc.)

## Known Gotchas

- **Coordinate system**: Game uses X/Z plane (Y is vertical), renderer uses X/Y
- **Item visibility**: Check `item.Hidden` flag and `item.Valid` before rendering
- **Node resolution**: Items store node UIDs; call `GetStartNode()`/`GetEndNode()` to resolve via mapper
- **Prefab origins**: Prefab items have local node coordinates; must transform to world space
- **Secret roads**: Marked with `IsSecret` flag; toggle visibility via `RenderFlags.SecretRoads`
- **Tile export memory**: Large zoom levels (>4) generate exponentially more tiles; monitor disk space
