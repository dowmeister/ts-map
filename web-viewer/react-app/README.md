# TsMap Vector Viewer - React Application

A React-based vector tile viewer for Euro Truck Simulator 2 and American Truck Simulator maps.

## Features

- 🗺️ MapLibre GL-based vector tile rendering
- 🎮 Support for ETS2, ATS, and ProMods
- 🏙️ City search and navigation
- 🚛 Live TruckersMP player tracking (zoom > 7)
- 📍 Real-time coordinate display
- ⚛️ Modern React 18 with hooks
- ⚡ Fast development with Vite

## Prerequisites

- Node.js 16+ and npm
- Running tile server on `http://localhost:8080`
- Exported map data in `../map_data/`

## Installation

```bash
cd web-viewer/react-app
npm install
```

## Development

```bash
npm run dev
```

Opens at `http://localhost:3000`

## Build for Production

```bash
npm run build
```

Output in `dist/` directory.

## Project Structure

```
src/
├── components/           # React components
│   ├── MapViewer.jsx    # Main map component
│   ├── GameSwitcher.jsx # Game selector
│   ├── CitySelector.jsx # City dropdown
│   └── CoordinateInfo.jsx # Coords display
├── hooks/               # Custom React hooks
│   ├── useTileMapInfo.js
│   ├── useCities.js
│   └── useTruckTracking.js
├── utils/              # Utility functions
│   ├── mapStyle.js     # MapLibre style definition
│   └── coordinates.js  # Coordinate conversion
├── App.jsx            # Root component
└── main.jsx          # Entry point
```

## Key Technologies

- **React 18**: Component framework
- **Vite**: Build tool and dev server
- **MapLibre GL**: Vector map rendering
- **Hooks**: State management with `useState`, `useEffect`, `useRef`

## Usage

1. Start the tile server: `docker compose up -d`
2. Run development server: `npm run dev`
3. Open `http://localhost:3000?game=ets2`

Query parameters:
- `?game=ets2` - Euro Truck Simulator 2
- `?game=ats` - American Truck Simulator
- `?game=promods` - ProMods
