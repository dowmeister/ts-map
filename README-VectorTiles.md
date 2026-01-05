# TsMap Vector Tiles Guide

This guide covers the complete workflow for generating and viewing vector maps for Euro Truck Simulator 2 (ETS2) and American Truck Simulator (ATS).

## Overview

The vector tile workflow consists of four main steps:

1. **Build the C# application** - Compile TsMap.Cli
2. **Generate GeoJSON files** - Export game data to GeoJSON format
3. **Generate vector tiles** - Convert GeoJSON to MBTiles using Tippecanoe
4. **Serve and view** - Start tile server and web viewer

## Prerequisites

### Required Software

- **.NET Framework 4.7.2** (for building TsMap)
- **Docker Desktop** (for Tippecanoe and servers)
- **Visual Studio 2019+** or **MSBuild** (for compiling)
- **Game files** for ETS2 and/or ATS

### Directory Structure

```
ts-map/
├── TsMap/                      # Core library
├── TsMap.Cli/                  # CLI tool for exporting
├── map_data/                   # Generated GeoJSON and overlays
│   ├── ets2/
│   │   ├── geojson/           # GeoJSON files
│   │   └── overlay_images/    # Company/service icons
│   └── ats/
│       ├── geojson/
│       └── overlay_images/
├── mbtiles/                    # Generated vector tiles
│   ├── ets2.mbtiles
│   └── ats.mbtiles
├── web-viewer/                 # HTML/JS viewer
└── docker-compose.yml          # Docker services
```

## Step 1: Build the Application

### Using Visual Studio

1. Open `TsMap.sln` in Visual Studio
2. Build the solution in **Release** configuration
3. The compiled CLI will be in `TsMap.Cli/bin/Release/TsMap.Cli.exe`

### Using MSBuild (Command Line)

```powershell
# From the project root
msbuild TsMap.sln /p:Configuration=Release
```

Or use the provided script:

```powershell
.\build.bat
```

## Step 2: Generate GeoJSON Files

The GeoJSON export reads game files and outputs vector data in GeoJSON format.

### Command Line Arguments

The CLI uses the following arguments:

- `-g` or `--game` - Path to game installation directory (required)
- `-o` or `--output` - Output directory for GeoJSON files (required)
- `-f` or `--format` - Export format: `geojson`, `json`, or `all` (default: all)

### Export GeoJSON

#### For ETS2:

```powershell
.\TsMap.Cli\bin\Release\TsMap.Cli.exe -g "C:\Program Files (x86)\Steam\steamapps\common\Euro Truck Simulator 2" -o "map_data\ets2" -f geojson
```

#### For ATS:

```powershell
.\TsMap.Cli\bin\Release\TsMap.Cli.exe -g "C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator" -o "map_data\ats" -f geojson
```

**Note:** Adjust the game paths to match your installation directory.

### Using the Batch Script

Edit `generate-geojson.bat` to set your game path, then run:

```powershell
.\generate-geojson.bat
```

Example script content:

```batch
.\TsMap.Cli\bin\Release\TsMap.Cli.exe -g "C:\Program Files (x86)\Steam\steamapps\common\Euro Truck Simulator 2" -o "map_data\ets2" -f geojson
```

### Generated Files

The export creates the following GeoJSON files:

- `roads.geojson` - Road network
- `prefab_roads.geojson` - Prefabricated road intersections
- `prefab_flat.geojson` - Flat prefab areas (parking lots, etc.)
- `prefab_buildings.geojson` - 3D buildings in prefabs
- `map_flat.geojson` - Flat map areas (grass, water, etc.)
- `map_buildings.geojson` - 3D map buildings
- `ferries.geojson` - Ferry connections
- `cities.geojson` - City locations and names
- `companies.geojson` - Company locations
- `overlays.geojson` - Map overlays (gas stations, truck stops, etc.)

Plus supporting JSON files:

- `TileMapInfo.json` - Map bounds and metadata
- `Countries.json` - Country definitions
- `Cities.json` - City data
- `overlay_images/` - Icon images for overlays

## Step 3: Generate Vector Tiles with Docker

Vector tiles are generated using Tippecanoe, which runs in a Docker container.

### Build the Tippecanoe Docker Image

First time only:

```powershell
docker build -t tsmap-tippecanoe -f Dockerfile.tippecanoe .
```

This builds the latest version of Tippecanoe from source.

### Generate MBTiles

#### Windows:

```powershell
.\docker-tippecanoe.bat ets2
.\docker-tippecanoe.bat ats
```

#### Linux/Mac/WSL:

```bash
chmod +x docker-tippecanoe.sh  # First time only
./docker-tippecanoe.sh ets2
./docker-tippecanoe.sh ats
```

### What Happens

1. Creates `mbtiles/` directory if needed
2. Runs Tippecanoe in Docker container
3. Processes all GeoJSON layers into a single MBTiles file
4. Outputs to `mbtiles/{game}.mbtiles`

### Tippecanoe Parameters

The script uses these settings:

- `-z8` - Maximum zoom level 8
- `-Z0` - Minimum zoom level 0
- `--drop-densest-as-needed` - Simplify dense areas to keep tile sizes manageable
- `--force` - Overwrite existing output

### Manual Docker Command

If you need to customize the generation:

```bash
docker run --rm \
  -v "$(pwd)/map_data/ets2:/data" \
  tsmap-tippecanoe \
  bash -c "tippecanoe -o /data/ets2.mbtiles -z8 -Z0 --force \
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
```

Then move the file:

```bash
mv map_data/ets2/ets2.mbtiles mbtiles/ets2.mbtiles
```

## Step 4: Serve and View

### Start the Docker Services

```powershell
docker-compose up -d
```

This starts two services:

- **Tile Server** on `http://localhost:8080` - Serves MBTiles files
- **Web Server** on `http://localhost:5500` - Serves the HTML viewer

### View the Maps

Open your browser:

- **ETS2**: http://localhost:5500/vector-viewer.html?game=ets2
- **ATS**: http://localhost:5500/vector-viewer.html?game=ats

### Stop the Services

```powershell
docker-compose down
```

### View Logs

```powershell
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f tileserver
docker-compose logs -f webserver
```

## Complete Workflow Example

### First Time Setup

```powershell
# 1. Build the application
.\build.bat

# 2. Build Docker images
docker build -t tsmap-tippecanoe -f Dockerfile.tippecanoe .

# 3. Edit generate-geojson.bat with your game paths, then export
.\generate-geojson.bat

# 4. Generate vector tiles
.\docker-tippecanoe.bat ets2
.\docker-tippecanoe.bat ats

# 5. Start servers
docker-compose up -d

# 6. Open browser
start http://localhost:5500/vector-viewer.html?game=ets2
```

### Updating After Game Updates

When the game updates and you need to regenerate maps:

```powershell
# 1. Rebuild if code changed
.\build.bat

# 2. Re-export GeoJSON (edit script first to set game path)
.\generate-geojson.bat

# 3. Regenerate tiles
.\docker-tippecanoe.bat ets2

# 4. Refresh browser (Ctrl+F5)
```

No need to restart Docker services - the tile server automatically picks up the new `.mbtiles` files.

## Map Viewer Features

### Controls

- **Mouse drag** - Pan the map
- **Mouse wheel** - Zoom in/out
- **Ctrl + drag** - Rotate the map
- **Right-click drag** - Adjust pitch (3D angle)

### Navigation Widget

Top-right corner controls:

- **+/-** buttons - Zoom
- **Compass** - Reset rotation
- **Pitch slider** - Adjust 3D view angle

### Game Switcher

Top-left dropdown to switch between ETS2 and ATS maps.

### Coordinate Info

Bottom-left panel shows:

- Current zoom level
- Map bounds (lat/lng)
- Game coordinates (X/Z)

### Map Layers

The viewer displays:

- **Flat areas** - Terrain (grass, water, parking lots)
- **Roads** - Main road network in orange
- **Prefab roads** - Intersections and junctions
- **Ferry lines** - Dashed blue lines
- **3D buildings** - Extruded buildings with height
- **Cities** - Red pins with white text labels
- **Companies** - Orange circles
- **Overlays** - Service icons (gas stations, etc.)

## Configuration

### Coordinate System

The map uses a custom coordinate projection optimized for minimal distortion:

- Centered at (0, 0)
- ±35° latitude range
- Aspect ratio preserved based on game map dimensions
- Equirectangular projection for flat display

See [TsMap/GeoJsonExporter.cs](TsMap/GeoJsonExporter.cs#L38) to adjust `maxExtent` if needed.

### Zoom Levels

- **Zoom 0-3**: Overview of entire map
- **Zoom 4-5**: Regional view, map fits screen
- **Zoom 6-7**: State/province level
- **Zoom 8**: City detail level

Adjust in `docker-tippecanoe` scripts by changing `-z8 -Z0`.

### Tile Server Port

To change the tile server port, edit `docker-compose.yml`:

```yaml
tileserver:
  ports:
    - "8081:8080" # Change 8081 to any free port
```

Then update the viewer URL in [vector-viewer.html](web-viewer/vector-viewer.html):

```javascript
tiles: [`http://localhost:8081/data/${currentGame}-vector/{z}/{x}/{y}.pbf`];
```

### Web Server Port

To change the web viewer port:

```yaml
webserver:
  ports:
    - "5501:80" # Change 5501 to any free port
```

## Troubleshooting

### GeoJSON Export Issues

**Error: Game directory does not exist**

- Check the path in your command or `generate-geojson.bat` script
- Ensure game is installed at the specified location
- Use quotes around paths with spaces

**Error: Required option '--game' is missing**

- The `-g` argument is required and must point to the game directory
- Example: `-g "C:\Program Files (x86)\Steam\steamapps\common\Euro Truck Simulator 2"`

**Missing data in export**

- Make sure you have all DLCs installed (or they won't be exported)
- Check that game files are not corrupted
- Verify output directory has write permissions

### Docker Issues

**Error: Docker daemon not running**

- Start Docker Desktop
- Wait for the whale icon to be solid (not animated)

**Error: Port already in use**

- Change ports in `docker-compose.yml` as described above
- Or stop the conflicting service

**Tippecanoe failed: unable to open database file**

- Ensure `mbtiles/` directory exists
- Check Docker has permission to write to project directory
- On Windows: Docker Desktop → Settings → Resources → File Sharing

### Viewer Issues

**Map not loading / blank screen**

- Check browser console (F12) for errors
- Verify tile server is running: `docker-compose ps`
- Check tiles exist: `dir mbtiles` (Windows) or `ls mbtiles` (Linux)
- Test tile server directly: http://localhost:8080

**Tiles not found (404 errors)**

- Ensure MBTiles file names match: `ets2.mbtiles`, `ats.mbtiles`
- Check tile server logs: `docker-compose logs tileserver`

**Stretched or distorted map**

- Rebuild the application after any coordinate system changes
- Re-export GeoJSON with updated code
- Regenerate MBTiles from new GeoJSON

**Missing city labels or overlays**

- Check that `cities.geojson` and `overlays.geojson` were generated
- Verify overlay images are in `map_data/{game}/overlay_images/`
- Check browser console for font errors

## Advanced Topics

### Custom Styling

Edit [vector-viewer.html](web-viewer/vector-viewer.html) to customize:

- Road colors and opacity
- Building heights and colors
- City pin appearance
- Text sizes and fonts

### Exporting Specific Regions

TsMap.Cli supports filtering by region:

```powershell
TsMap.Cli.exe -g ets2 -o map_data\ets2 --bounds "-50000,50000,-50000,50000"
```

### Performance Optimization

For faster tile serving:

- Reduce max zoom level (e.g., `-z6` instead of `-z8`)
- Use `--drop-densest-as-needed` more aggressively
- Simplify geometries with `--simplification=10`

### Using Different Tile Servers

Instead of tileserver-gl-light, you can use:

- **tileserver-gl** (full version with more features)
- **Martin** (Rust-based, very fast)
- **Tegola** (Go-based)

Update `docker-compose.yml` with your preferred server.

## Performance Notes

### File Sizes

Typical MBTiles sizes:

- **ETS2** (full map): ~200-500 MB
- **ATS** (full map): ~150-400 MB

Size depends on:

- Number of DLCs enabled
- Zoom level range (more zoom = larger files)
- Simplification settings

### Generation Times

On a modern machine:

- **GeoJSON export**: 1-3 minutes per game
- **Tippecanoe processing**: 2-5 minutes per game
- Total workflow: ~10-15 minutes per game

### Browser Performance

For smooth rendering:

- Use modern browsers (Chrome, Firefox, Edge)
- GPU acceleration enabled
- Sufficient RAM (4GB+ recommended)

## Resources

- **MapLibre GL JS**: https://maplibre.org/maplibre-gl-js-docs/
- **Tippecanoe**: https://github.com/felt/tippecanoe
- **MBTiles Spec**: https://github.com/mapbox/mbtiles-spec
- **GeoJSON Spec**: https://geojson.org/

## Support

For issues or questions:

1. Check this README and troubleshooting section
2. Review the main [README.md](README.md)
3. Check Docker logs: `docker-compose logs`
4. Open an issue on GitHub

---

**Happy mapping!** 🗺️🚛
