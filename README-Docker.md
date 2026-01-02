# TsMap Docker Setup

This Docker setup provides containerized services for the TsMap vector map viewer.

## Services

1. **Tippecanoe** - Vector tile generation (on-demand)
2. **Tile Server** - Serves mbtiles files via HTTP
3. **Web Server** - Serves the HTML viewer

## Quick Start

### 1. Generate Vector Tiles (Docker)

For ETS2:

```bash
bash docker-tippecanoe.sh ets2
```

For ATS:

```bash
bash docker-tippecanoe.sh ats
```

Or on Windows with Git Bash or WSL:

```bash
./docker-tippecanoe.sh ets2
```

### 2. Start the Servers

```bash
docker-compose up -d
```

This starts:

- **Tile Server** on `http://localhost:8080`
- **Web Viewer** on `http://localhost:5500`

### 3. Open the Viewer

- ETS2: http://localhost:5500/vector-viewer.html?game=ets2
- ATS: http://localhost:5500/vector-viewer.html?game=ats

## Directory Structure

```
ts-map/
├── docker-compose.yml       # Docker services configuration
├── docker-tippecanoe.sh     # Script to generate tiles with Docker
├── mbtiles/                 # Generated mbtiles (mounted in tileserver)
│   ├── ets2.mbtiles
│   └── ats.mbtiles
├── map_data/                # GeoJSON and overlay images
│   ├── ets2/
│   └── ats/
└── web-viewer/              # HTML viewer (mounted in webserver)
    └── vector-viewer.html
```

## Commands

### Start all services

```bash
docker-compose up -d
```

### Stop all services

```bash
docker-compose down
```

### View logs

```bash
docker-compose logs -f tileserver
docker-compose logs -f webserver
```

### Restart a service

```bash
docker-compose restart tileserver
```

### Generate tiles without the script

```bash
docker run --rm \
  -v "$(pwd)/map_data/ets2:/data" \
  -v "$(pwd)/mbtiles:/output" \
  klokantech/tippecanoe:latest \
  tippecanoe -o /output/ets2.mbtiles -z8 -Z0 --force \
  -L roads:/data/geojson/roads.geojson \
  # ... add other layers
```

## Notes

- The tippecanoe service uses a `profiles: tools` flag, so it doesn't start with `docker-compose up`. Use the script instead.
- Tile server automatically serves all `.mbtiles` files in the `mbtiles/` directory
- Web server serves static files from `web-viewer/` and has read-only access to `map_data/`
- The viewer accesses tiles via: `http://localhost:8080/data/{game}-vector/{z}/{x}/{y}.pbf`

## Troubleshooting

### Tiles not found

Make sure the mbtiles files are in the `mbtiles/` directory and named correctly:

- `ets2.mbtiles`
- `ats.mbtiles`

### Port conflicts

If ports 8080 or 5500 are already in use, edit `docker-compose.yml` and change the port mappings:

```yaml
ports:
  - "8081:8080" # Change 8081 to any free port
```

### Permission issues on Linux/Mac

Make sure the script is executable:

```bash
chmod +x docker-tippecanoe.sh
```
