// TsMap Web Viewer
let map;
let tileMapInfo;
let truckMarker = null;

// Initialize the map
async function initMap() {
  try {
    // Load TileMapInfo.json from the tiles directory
    const response = await fetch("../map_data/ets2/TileMapInfo.json");
    tileMapInfo = await response.json();

    console.log("TileMapInfo loaded:", tileMapInfo);

    // Calculate max pixel dimensions at max zoom level
    const tileSize = tileMapInfo.tileSize || 256;
    const tilesPerSide = Math.pow(2, tileMapInfo.maxZoom);
    const maxPixelWidth = tilesPerSide * tileSize;
    const maxPixelHeight = tilesPerSide * tileSize;

    // Store for coordinate conversions
    window.tileMapConfig = {
      x1: tileMapInfo.x1,
      x2: tileMapInfo.x2,
      y1: tileMapInfo.y1,
      y2: tileMapInfo.y2,
      xtot: tileMapInfo.x2 - tileMapInfo.x1,
      ytot: tileMapInfo.y2 - tileMapInfo.y1,
      maxPixelWidth: maxPixelWidth,
      maxPixelHeight: maxPixelHeight,
      maxZoom: tileMapInfo.maxZoom,
    };

    console.log("Max pixel dimensions:", maxPixelWidth, "x", maxPixelHeight);

    // Initialize Leaflet map with Simple CRS (pixel-based coordinates)
    map = L.map("map", {
      crs: L.CRS.Simple,
      minZoom: tileMapInfo.minZoom,
      maxZoom: tileMapInfo.maxZoom,
      attributionControl: false,
    });

    // Calculate bounds using unproject (similar to TruckyApp approach)
    const bounds = new L.LatLngBounds(
      map.unproject([0, maxPixelHeight], tileMapInfo.maxZoom),
      map.unproject([maxPixelWidth, 0], tileMapInfo.maxZoom)
    );

    // Add tile layer (supports both PNG and SVG)
    const tileFormat = tileMapInfo.format || "png";
    L.tileLayer(`../map_data/ets2/Tiles/{z}/{x}/{y}.${tileFormat}`, {
      bounds: bounds,
      noWrap: true,
      tileSize: tileSize,
      maxNativeZoom: tileMapInfo.maxZoom,
      minNativeZoom: tileMapInfo.minZoom,
    }).addTo(map);

    // Set max bounds to prevent panning outside tile area
    map.setMaxBounds(bounds);

    // Add click handler to show coordinates
    map.on("click", function (e) {
      // Convert Leaflet LatLng to game coordinates
      const [gameX, gameY] = mapToGameCoords(e.latlng);

      document.getElementById("clickX").textContent = gameX.toFixed(2);
      document.getElementById("clickZ").textContent = gameY.toFixed(2);

      // Optionally add a temporary marker
      L.circleMarker(e.latlng, {
        radius: 5,
        color: "#00ff00",
        fillColor: "#00ff00",
        fillOpacity: 0.5,
      })
        .addTo(map)
        .bindPopup(`X: ${gameX.toFixed(2)}<br>Y: ${gameY.toFixed(2)}`)
        .openPopup();
    });

    console.log("Map initialized successfully");

    // Set initial view to zoom 6 centered at game coordinates 0,0
    const initialCenter = gameToMapCoords(0, 0);
    map.setView(initialCenter, 6);

    // Add initial truck marker at center (0,0)
    document.getElementById("truckX").value = "0";
    document.getElementById("truckZ").value = "0";
    updateTruckPosition();
  } catch (error) {
    console.error("Error loading map:", error);
    alert(
      "Error loading TileMapInfo.json. Make sure tiles are exported to ../map_data/ets2/Tiles/ directory."
    );
  }
}

// Convert game coordinates to Leaflet LatLng (using unproject)
// Based on: https://github.com/dariowouters/ts-map/issues/16 and #42
// Only uses data from TileMapInfo.json
function gameToMapCoords(gameX, gameY) {
  const config = window.tileMapConfig;

  // Calculate relative position (0 to 1)
  const xrel = (gameX - config.x1) / config.xtot;
  const yrel = (gameY - config.y1) / config.ytot;

  // Convert to pixel coordinates
  const pixelX = xrel * config.maxPixelWidth;
  const pixelY = yrel * config.maxPixelHeight;

  // Use Leaflet's unproject to convert pixels to LatLng
  // unproject expects [x, y] in pixel space
  return map.unproject([pixelX, pixelY], config.maxZoom);
}

// Convert Leaflet LatLng back to game coordinates (using project)
function mapToGameCoords(latlng) {
  const config = window.tileMapConfig;

  // Use Leaflet's project to convert LatLng to pixels
  const point = map.project(latlng, config.maxZoom);
  const pixelX = point.x;
  const pixelY = point.y;

  // Convert from pixels to relative position
  const xrel = pixelX / config.maxPixelWidth;
  const yrel = pixelY / config.maxPixelHeight;

  // Convert to game coordinates
  const gameX = config.x1 + xrel * config.xtot;
  const gameY = config.y1 + yrel * config.ytot;

  return [gameX, gameY];
}

// Update truck position on map
function updateTruckPosition() {
  const x = parseFloat(document.getElementById("truckX").value);
  const z = parseFloat(document.getElementById("truckZ").value);
  const rotation = parseFloat(document.getElementById("truckRotation").value);

  if (isNaN(x) || isNaN(z)) {
    alert("Please enter valid coordinates");
    return;
  }

  // Remove old marker if exists
  if (truckMarker) {
    map.removeLayer(truckMarker);
  }

  // Create custom icon with rotation
  // The arrow points up (north) by default, rotate it based on truck heading
  const truckIcon = L.divIcon({
    className: "truck-marker-icon",
    html: `<div style="
            width: 0;
            height: 0;
            border-left: 10px solid transparent;
            border-right: 10px solid transparent;
            border-bottom: 24px solid #ff4444;
            transform: rotate(${rotation}deg);
            transform-origin: 10px 18px;
            filter: drop-shadow(0 2px 4px rgba(0,0,0,0.4));
            position: relative;
            left: -10px;
            top: -18px;
        "></div>`,
    iconSize: [20, 24],
    iconAnchor: [10, 12],
  });

  // Add new marker
  const latlng = gameToMapCoords(x, z);
  truckMarker = L.marker(latlng, { icon: truckIcon })
    .addTo(map)
    .bindPopup(
      `<b>Truck Position</b><br>X: ${x.toFixed(2)}<br>Y: ${z.toFixed(
        2
      )}<br>Rotation: ${rotation}°`
    );

  console.log(`Truck position updated: X=${x}, Y=${z}, Rotation=${rotation}°`);
}

// Center map on truck
function centerOnTruck() {
  if (truckMarker) {
    map.setView(truckMarker.getLatLng(), map.getZoom());
  } else {
    alert("No truck position set");
  }
}

// Add a marker at specific game coordinates (for external use)
function addMarker(x, y, options = {}) {
  const latlng = gameToMapCoords(x, y);
  const marker = L.marker(latlng, options).addTo(map);
  return marker;
}

// Add a route/path (array of [x, y] coordinates)
function addRoute(coordinates, options = {}) {
  const latlngs = coordinates.map((coord) =>
    gameToMapCoords(coord[0], coord[1])
  );
  const polyline = L.polyline(latlngs, {
    color: options.color || "#0066ff",
    weight: options.weight || 3,
    opacity: options.opacity || 0.7,
  }).addTo(map);
  return polyline;
}

// Load external data (cities, companies, etc.)
async function loadPOIData() {
  try {
    // Try to load Cities.json if it exists
    const cityResponse = await fetch("../map_data/ets2/Cities.json");
    const cities = await cityResponse.json();

    console.log("Cities loaded:", cities.length);

    // Add city markers
    cities.forEach((city) => {
      const latlng = gameToMapCoords(city.X, city.Y);
      L.marker(latlng, {
        icon: L.divIcon({
          className: "city-label",
          html: `<div style="
            color: #ffaa00;
            font-weight: bold;
            font-size: 14px;
            text-shadow: 1px 1px 2px black, -1px -1px 2px black;
            white-space: nowrap;
          ">${city.LocalizedNames?.en_us || city.Name}</div>`,
          iconSize: [0, 0],
        }),
      }).addTo(map);
    });
  } catch (error) {
    console.log("No city data available (this is optional)");
  }

  try {
    // Try to load Countries.json if it exists
    const countryResponse = await fetch("../map_data/ets2/Countries.json");
    const countries = await countryResponse.json();

    console.log("Countries loaded:", countries.length);

    // Add country name markers
    countries.forEach((country) => {
      const latlng = gameToMapCoords(country.X, country.Y);

      // Create country name label
      const countryIcon = L.divIcon({
        className: "country-label",
        html: `<div style="
          font-size: 16px;
          font-weight: bold;
          color: #ffffff;
          text-shadow: 2px 2px 4px rgba(0,0,0,0.8), -1px -1px 2px rgba(0,0,0,0.8);
          cursor: pointer;
          white-space: nowrap;
        ">${country.LocalizedNames?.en_us || country.Name}</div>`,
        iconSize: [0, 0],
      });

      L.marker(latlng, { icon: countryIcon })
        .addTo(map)
        .bindPopup(
          `<b>${country.Name}</b><br>Country Code: ${country.CountryCode}`
        );
    });
  } catch (error) {
    console.log("No country data available (this is optional)");
  }
}

// Convert country code to flag emoji
function getFlagEmoji(countryCode) {
  if (!countryCode || countryCode.length !== 2) return "🏳️";

  // Convert country code to flag emoji
  // Country codes are ISO 3166-1 alpha-2
  const codePoints = countryCode
    .toUpperCase()
    .split("")
    .map((char) => 127397 + char.charCodeAt());
  return String.fromCodePoint(...codePoints);
}

// Initialize map when page loads
document.addEventListener("DOMContentLoaded", function () {
  initMap().then(() => {
    loadPOIData();
  });
});

// Expose functions globally for console access
window.addMarker = addMarker;
window.addRoute = addRoute;
window.updateTruckPosition = updateTruckPosition;
window.centerOnTruck = centerOnTruck;
