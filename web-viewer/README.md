# TsMap Web Viewer

A simple web-based viewer for TsMap tile exports that displays the map and allows placing markers based on game coordinates.

## Setup

1. **Export tiles** from TsMap.Canvas:

   - Open TsMap.Canvas application
   - Go to `Map -> Generate Tile Map`
   - Configure zoom levels and export path
   - Export tiles (this creates `Tiles/` directory with PNG files and `TileMapInfo.json`)

2. **Place web viewer**:

   - Put the `web-viewer` folder next to the `Tiles` folder, like this:
     ```
     YourExportPath/
     ├── Tiles/
     │   ├── TileMapInfo.json
     │   ├── 0/
     │   ├── 1/
     │   └── ...
     └── web-viewer/
         ├── index.html
         ├── map.js
         └── README.md
     ```

3. **Run local web server**:

   ```powershell
   # Using Python (if installed)
   cd YourExportPath
   python -m http.server 8000

   # Or using Node.js http-server (if installed)
   npx http-server -p 8000

   # Or use VS Code Live Server extension
   ```

4. **Open in browser**:
   - Navigate to `http://localhost:8000/web-viewer/`

## Usage

### Display Truck Position

Use the control panel on the right side:

1. Enter truck X coordinate (game units)
2. Enter truck Z coordinate (game units)
3. Enter rotation angle (0-360 degrees)
4. Click "Update Position" to place/move the marker
5. Click "Center on Truck" to focus the map on the truck

### Get Coordinates

Click anywhere on the map to see the game coordinates at that location (displayed in the control panel).

### JavaScript API

The viewer exposes functions for programmatic control:

```javascript
// Add a custom marker
addMarker(x, z, {
  title: "My Location",
  icon: customIcon, // optional Leaflet icon
});

// Draw a route (array of [x, z] coordinates)
addRoute(
  [
    [1000, 2000],
    [1500, 2500],
    [2000, 3000],
  ],
  {
    color: "#00ff00",
    weight: 4,
  }
);

// Update truck position programmatically
document.getElementById("truckX").value = 5000;
document.getElementById("truckZ").value = -3000;
document.getElementById("truckRotation").value = 45;
updateTruckPosition();

// Center on truck
centerOnTruck();
```

## Integration with Game Data

To display real-time truck position from the game:

1. **Export position data** from your game integration (e.g., mod or telemetry plugin) to JSON:

   ```json
   {
     "x": 1234.56,
     "z": -5678.9,
     "rotation": 135.5,
     "speed": 80
   }
   ```

2. **Poll and update** in the web viewer:

   ```javascript
   async function updateFromGame() {
     const response = await fetch("truck_position.json");
     const data = await response.json();

     document.getElementById("truckX").value = data.x;
     document.getElementById("truckZ").value = data.z;
     document.getElementById("truckRotation").value = data.rotation;
     updateTruckPosition();
   }

   // Update every second
   setInterval(updateFromGame, 1000);
   ```

## Customization

### Custom Marker Styles

Edit the CSS in `index.html` to change marker appearance:

- `.truck-marker` - Simple circular marker
- `.truck-marker-with-rotation` - Arrow-shaped directional marker

### Load Additional Data

If you exported optional data (cities, companies, etc.), the viewer will automatically load and display:

- `CityList.json` - City markers
- `BusStops.json` - Bus stop markers
- Add custom logic in `loadPOIData()` function

## Coordinate System

The map uses **native game coordinates**:

- X axis: West (-) to East (+)
- Z axis: North (-) to South (+)
- Units: Game distance units (not meters or miles)
- No GPS coordinate conversion needed

## Browser Compatibility

Works in all modern browsers (Chrome, Firefox, Edge, Safari).
Requires JavaScript enabled.
