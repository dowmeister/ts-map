const _SZ = [
  "interpolate",
  ["linear"],
  ["zoom"],
  7,
  0.08,
  8,
  0.12,
  9,
  0.18,
  10,
  0.26,
  11,
  0.35,
  13,
  0.45,
];
const _SZ_LG = [
  "interpolate",
  ["linear"],
  ["zoom"],
  7,
  0.13,
  8,
  0.19,
  9,
  0.29,
  10,
  0.42,
  11,
  0.56,
  13,
  0.72,
];

function _ov(id, filter, size = _SZ) {
  return {
    id,
    type: "symbol",
    source: "map",
    "source-layer": "overlays",
    minzoom: 7,
    filter,
    layout: {
      "icon-image": ["get", "image"],
      "icon-size": size,
      "icon-allow-overlap": true,
      "icon-ignore-placement": true,
    },
  };
}

function _overlayLayers() {
  return [
    _ov(
      "overlays-companies",
      ["==", ["slice", ["get", "image"], 0, 8], "company:"],
      _SZ_LG,
    ),
    _ov("overlays-fuel", ["==", ["get", "image"], "service:map_gas_ico"]),
    _ov("overlays-garage", [
      "==",
      ["get", "image"],
      "service:map_garage_large_ico",
    ]),
    _ov("overlays-repair", ["==", ["get", "image"], "service:map_service_ico"]),
    _ov("overlays-recruitment", [
      "==",
      ["get", "image"],
      "service:map_recruitment_ico",
    ]),
    _ov("overlays-dealer", ["==", ["get", "image"], "service:map_dealer_ico"]),
    _ov("overlays-border", ["==", ["get", "image"], "road:road_border_ico"]),
    _ov("overlays-toll", ["==", ["get", "image"], "road:road_toll_ico"]),
    _ov("overlays-busstop", ["==", ["get", "image"], "misc:busstop_bus_stop"]),
    _ov("overlays-road", [
      "all",
      ["==", ["slice", ["get", "image"], 0, 5], "road:"],
      ["!=", ["get", "image"], "road:road_border_ico"],
      ["!=", ["get", "image"], "road:road_toll_ico"],
    ]),
    _ov("overlays-misc", [
      "all",
      ["!=", ["slice", ["get", "image"], 0, 8], "company:"],
      ["!=", ["slice", ["get", "image"], 0, 5], "road:"],
      ["!=", ["get", "image"], "service:map_gas_ico"],
      ["!=", ["get", "image"], "service:map_garage_large_ico"],
      ["!=", ["get", "image"], "service:map_service_ico"],
      ["!=", ["get", "image"], "service:map_recruitment_ico"],
      ["!=", ["get", "image"], "service:map_dealer_ico"],
      ["!=", ["get", "image"], "misc:busstop_bus_stop"],
    ]),
  ];
}

export const OVERLAY_LAYER_GROUPS = [
  { id: "overlays-companies", label: "Companies" },
  { id: "overlays-fuel", label: "Fuel Stations" },
  { id: "overlays-garage", label: "Garages" },
  { id: "overlays-repair", label: "Service Stations" },
  { id: "overlays-recruitment", label: "Recruitment" },
  { id: "overlays-dealer", label: "Truck Dealers" },
  { id: "overlays-border", label: "Border Crossings" },
  { id: "overlays-toll", label: "Tollgates" },
  { id: "overlays-busstop", label: "Bus Stops" },
  { id: "overlays-road", label: "Road Signs" },
  { id: "overlays-misc", label: "Other" },
  { id: "city-labels", label: "City Names" },
  { id: "footprints", label: "Building Footprints" },
  { id: "prefab-buildings", label: "Prefab Buildings" },
  { id: "map-buildings", label: "Map Buildings" },
  { id: "buildings", label: "All Buildings" },
  { id: "hidden-prefabs", label: "Hidden Roads (Prefab)" },
  { id: "hidden-roads", label: "Hidden Roads" },
];

export function createMapStyle(game) {
  const rawBase = import.meta.env.VITE_OVERLAY_IMAGES_BASE_URL || "/map_data";
  const baseUrl = rawBase.startsWith("http")
    ? rawBase
    : `${window.location.origin}${rawBase}`;
  // Cache busting version - automatically set at build time
  const spriteVersion = Date.now().toString();

  return {
    version: 8,
    glyphs: "https://fonts.openmaptiles.org/{fontstack}/{range}.pbf",
    sprite: [
      {
        id: "company",
        url: `${baseUrl}/${game}/sprites/sprite-company?v=${spriteVersion}`,
      },
      {
        id: "service",
        url: `${baseUrl}/${game}/sprites/sprite-service?v=${spriteVersion}`,
      },
      {
        id: "road",
        url: `${baseUrl}/${game}/sprites/sprite-road?v=${spriteVersion}`,
      },
      {
        id: "misc",
        url: `${baseUrl}/${game}/sprites/sprite-misc?v=${spriteVersion}`,
      },
    ],
    sources: {
      map: {
        type: "vector",
        url: `pmtiles://${import.meta.env.VITE_VECTOR_TILES_BASE_URL || "http://localhost:8888"}/${game}.pmtiles`,
      },
      "footprints-source": {
        type: "vector",
        url: `pmtiles://${import.meta.env.VITE_VECTOR_TILES_BASE_URL || "http://localhost:8888"}/${game}-footprints.pmtiles`,
      },
      trucks: {
        type: "geojson",
        data: {
          type: "FeatureCollection",
          features: [],
        },
      },
      "graph-debug": {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      },
      route: {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      },
    },
    layers: [
      {
        id: "background",
        type: "background",
        paint: {
          "background-color": "#1A2733",
        },
      },
      {
        id: "footprints",
        type: "fill-extrusion",
        source: "footprints-source",
        "source-layer": "footprints",
        minzoom: 6,
        paint: {
          "fill-extrusion-color": "#1e2a35",
          // Game height units (meters) need ~20x scale to match other building layers in the viewer
          "fill-extrusion-height": [
            "*",
            20,
            ["coalesce", ["get", "height"], 8],
          ],
          "fill-extrusion-base": 0,
          "fill-extrusion-opacity": [
            "interpolate",
            ["linear"],
            ["zoom"],
            6,
            0.25,
            7,
            0.35,
            8,
            0.45,
          ],
          "fill-extrusion-vertical-gradient": false,
        },
      },
      {
        id: "hidden-prefabs",
        type: "line",
        source: "footprints-source",
        "source-layer": "hidden_prefabs",
        minzoom: 6,
        paint: {
          "line-color": "#4a5e70",
          "line-width": [
            "interpolate",
            ["linear"],
            ["zoom"],
            6, 0.4,
            10, 1.2,
            14, 2.5,
          ],
          "line-opacity": ["interpolate", ["linear"], ["zoom"], 6, 0.5, 8, 0.8],
        },
      },
      {
        id: "hidden-roads",
        type: "line",
        source: "footprints-source",
        "source-layer": "hidden_roads",
        minzoom: 6,
        paint: {
          "line-color": "#4a5e70",
          "line-width": [
            "interpolate",
            ["linear"],
            ["zoom"],
            6, 0.4,
            10, 1.2,
            14, 2.5,
          ],
          "line-opacity": ["interpolate", ["linear"], ["zoom"], 6, 0.5, 8, 0.8],
        },
      },
      {
        id: "prefab-flat",
        type: "fill",
        source: "map",
        "source-layer": "prefab_flat",
        paint: {
          "fill-color": ["get", "color"],
          "fill-opacity": 0.4,
        },
      },
      {
        id: "map-flat",
        type: "fill",
        source: "map",
        "source-layer": "map_flat",
        paint: {
          "fill-color": ["get", "color"],
          "fill-opacity": 0.4,
        },
      },
      {
        id: "roads",
        type: "fill",
        source: "map",
        "source-layer": "roads",
        paint: {
          "fill-color": [
            "match",
            ["get", "road_class"],
            "highway",
            "#D4A017",
            /* normal/local */ "#8A9BAD",
          ],
          "fill-opacity": 1.0,
          "fill-antialias": true,
        },
      },
      {
        id: "prefab_roads",
        type: "fill",
        source: "map",
        "source-layer": "prefab_roads",
        paint: {
          "fill-color": [
            "match",
            ["get", "road_class"],
            "highway",
            "#D4A017",
            /* normal/local */ "#8A9BAD",
          ],
          "fill-opacity": 1.0,
          "fill-antialias": true,
        },
      },
      {
        id: "ferries",
        type: "line",
        source: "map",
        "source-layer": "ferries",
        paint: {
          "line-color": "#4D7EA8",
          "line-width": [
            "interpolate",
            ["linear"],
            ["zoom"],
            4,
            1,
            6,
            2,
            8,
            3,
            10,
            4,
          ],
          "line-dasharray": [1, 2],
        },
      },
      {
        id: "prefab-buildings",
        type: "fill-extrusion",
        source: "map",
        "source-layer": "prefab_buildings",
        paint: {
          "fill-extrusion-color": "#2A3645",
          "fill-extrusion-height": ["-", ["coalesce", ["get", "height"], 400], ["coalesce", ["get", "elevation"], 0]],
          "fill-extrusion-base": 0,
          "fill-extrusion-opacity": 0.55,
        },
      },
      {
        id: "map-buildings",
        type: "fill-extrusion",
        source: "map",
        "source-layer": "map_buildings",
        paint: {
          "fill-extrusion-color": "#2E3E50",
          "fill-extrusion-height": ["-", ["coalesce", ["get", "height"], 400], ["coalesce", ["get", "elevation"], 0]],
          "fill-extrusion-base": 0,
          "fill-extrusion-opacity": 0.55,
          "fill-extrusion-vertical-gradient": false,
        },
      },
      {
        id: "buildings",
        type: "fill-extrusion",
        source: "map",
        "source-layer": "buildings",
        paint: {
          "fill-extrusion-color": "#354755",
          "fill-extrusion-height": ["coalesce", ["get", "height"], 500],
          "fill-extrusion-base": ["coalesce", ["get", "elevation"], 0],
          "fill-extrusion-opacity": 0.55,
        },
      },
      // "buildings" layer (TsBuildingItem procedural segments) intentionally omitted —
      // they appear as thin diagonal strips that are not useful for visualization.

      ..._overlayLayers(),
      {
        id: "city-labels",
        type: "symbol",
        source: "map",
        "source-layer": "cities",
        minzoom: 5,
        layout: {
          "text-field": ["get", "localized_name"],
          "text-font": ["Klokantech Noto Sans Regular"],
          "text-size": [
            "interpolate",
            ["linear"],
            ["zoom"],
            5,
            11,
            6,
            13,
            8,
            16,
            10,
            20,
          ],
          "text-anchor": "top",
          "text-offset": [0, 0.8],
          "text-allow-overlap": true,
          "text-ignore-placement": false,
        },
        paint: {
          "text-color": "#C0A882",
          "text-halo-color": "#1A2733",
          "text-halo-width": 2.5,
        },
      },
      // ── Route display ─────────────────────────────────────────────────────
      {
        id: "route-line",
        type: "line",
        source: "route",
        filter: ["==", "$type", "LineString"],
        layout: {
          "line-cap": "round",
          "line-join": "round",
        },
        paint: {
          "line-color": "#00D4FF",
          "line-width": [
            "interpolate",
            ["linear"],
            ["zoom"],
            4,
            2,
            8,
            3.5,
            12,
            5,
          ],
          "line-opacity": 0.92,
        },
      },
      {
        id: "route-markers",
        type: "circle",
        source: "route",
        filter: ["==", "$type", "Point"],
        paint: {
          "circle-radius": ["interpolate", ["linear"], ["zoom"], 4, 8, 10, 13],
          "circle-color": ["coalesce", ["get", "color"], "#FF9900"],
          "circle-stroke-color": "#ffffff",
          "circle-stroke-width": 2,
          "circle-opacity": 1,
        },
      },
      {
        id: "route-marker-labels",
        type: "symbol",
        source: "route",
        filter: ["==", "$type", "Point"],
        layout: {
          "text-field": ["get", "label"],
          "text-font": ["Klokantech Noto Sans Regular"],
          "text-size": 10,
          "text-allow-overlap": true,
          "text-ignore-placement": true,
          "text-anchor": "center",
        },
        paint: {
          "text-color": "#ffffff",
          "text-halo-color": "rgba(0,0,0,0)",
          "text-halo-width": 0,
        },
      },
      // ── Graph debug overlay (toggle via DebugToggle control) ──────────────
      {
        id: "graph-debug-edges",
        type: "line",
        source: "graph-debug",
        filter: ["==", ["get", "featureType"], "edge"],
        paint: {
          "line-color": [
            "match",
            ["get", "itemType"],
            "road",
            "#4A90D9",
            "prefab",
            "#FF2222",
            "ferry",
            "#9B59B6",
            "ferry_approach",
            "#27AE60",
            "#999999",
          ],
          "line-width": [
            "interpolate",
            ["linear"],
            ["zoom"],
            9,
            ["case", ["==", ["get", "speedClass"], "freeway"], 1.5, 1.2],
            13,
            ["case", ["==", ["get", "speedClass"], "freeway"], 2.5, 1.5],
          ],
          "line-opacity": 0.85,
        },
      },
      {
        id: "graph-debug-arrows",
        type: "symbol",
        source: "graph-debug",
        filter: ["==", ["get", "featureType"], "arrow"],
        layout: {
          "icon-image": "route-arrow",
          "icon-rotate": ["get", "bearing"],
          "icon-rotation-alignment": "map",
          "icon-allow-overlap": true,
          "icon-size": 0.8,
        },
        paint: {
          "icon-color": [
            "match",
            ["get", "itemType"],
            "road",
            "#4A90D9",
            "prefab",
            "#FF2222",
            "ferry",
            "#9B59B6",
            "ferry_approach",
            "#27AE60",
            "#999999",
          ],
          "icon-opacity": 0.9,
        },
      },
      {
        id: "graph-debug-nodes",
        type: "circle",
        source: "graph-debug",
        filter: ["==", ["get", "featureType"], "node"],
        paint: {
          "circle-radius": ["interpolate", ["linear"], ["zoom"], 9, 1.5, 12, 4],
          "circle-color": "#FFFFFF",
          "circle-stroke-color": "#333333",
          "circle-stroke-width": 1,
          "circle-opacity": 0.9,
        },
      },
      // ── Speed camera markers ───────────────────────────────────────────────
      {
        id: "speed-cameras",
        type: "circle",
        source: "map",
        "source-layer": "speed_cameras",
        minzoom: 7,
        paint: {
          "circle-radius": ["interpolate", ["linear"], ["zoom"], 7, 4, 10, 7],
          "circle-color": "#FF3300",
          "circle-stroke-width": 1.5,
          "circle-stroke-color": "#ffffff",
          "circle-opacity": 0.9,
        },
      },
      // ── Live truck markers ─────────────────────────────────────────────────
      {
        id: "trucks",
        type: "circle",
        source: "trucks",
        paint: {
          "circle-radius": 5,
          "circle-color": "#00ff00",
          "circle-stroke-width": 2,
          "circle-stroke-color": "#ffffff",
        },
        minzoom: 7,
      },
    ],
  };
}
