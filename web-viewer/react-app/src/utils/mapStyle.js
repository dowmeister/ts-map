export function createMapStyle(game) {
  return {
    version: 8,
    glyphs: 'https://demotiles.maplibre.org/font/{fontstack}/{range}.pbf',
    sources: {
      'map': {
        type: 'vector',
        tiles: [`${import.meta.env.VITE_VECTOR_TILES_BASE_URL || 'http://localhost:8080/data'}/${game}-vector/{z}/{x}/{y}.pbf`],
        minzoom: 4,
        maxzoom: 8
      },
      'trucks': {
        type: 'geojson',
        data: {
          type: 'FeatureCollection',
          features: []
        }
      }
    },
    layers: [
      {
        id: 'background',
        type: 'background',
        paint: {
          'background-color': '#1a1a1a'
        }
      },
      {
        id: 'prefab-flat',
        type: 'fill',
        source: 'map',
        'source-layer': 'prefab_flat',
        paint: {
          'fill-color': ['get', 'color'],
          'fill-opacity': 0.4
        }
      },
      {
        id: 'map-flat',
        type: 'fill',
        source: 'map',
        'source-layer': 'map_flat',
        paint: {
          'fill-color': ['get', 'color'],
          'fill-opacity': 0.4
        }
      },
      {
        id: 'roads',
        type: 'fill',
        source: 'map',
        'source-layer': 'roads',
        paint: {
          'fill-color': '#FFC84C',
          'fill-opacity': 1.0,
          'fill-antialias': true,
        },
      },
      {
        id: 'prefab_roads',
        type: 'fill',
        source: 'map',
        'source-layer': 'prefab_roads',
        paint: {
          'fill-color': '#FFC84C',
          'fill-opacity': 1.0,
          'fill-antialias': true,
        }
      },
      {
        id: 'ferries',
        type: 'line',
        source: 'map',
        'source-layer': 'ferries',
        paint: {
          'line-color': '#0099ff',
          'line-width': [
            'interpolate',
            ['linear'],
            ['zoom'],
            4, 1,
            6, 2,
            8, 3,
            10, 4
          ],
          'line-dasharray': [2, 2]
        }
      },
      {
        id: 'prefab-buildings',
        type: 'fill-extrusion',
        source: 'map',
        'source-layer': 'prefab_buildings',
        paint: {
          'fill-extrusion-color': ['get', 'color'],
          'fill-extrusion-height': 400,
          'fill-extrusion-base': 0,
          'fill-extrusion-opacity': 1,
        }
      },
      {
        id: 'map-buildings',
        type: 'fill-extrusion',
        source: 'map',
        'source-layer': 'map_buildings',
        paint: {
          'fill-extrusion-color': ['get', 'color'],
          'fill-extrusion-height': 400,
          'fill-extrusion-base': 0,
          'fill-extrusion-opacity': 1,
        }
      },
      {
        id: 'overlays',
        type: 'symbol',
        source: 'map',
        'source-layer': 'overlays',
        layout: {
          'icon-image': ['get', 'image'],
          'icon-size': [
            'interpolate',
            ['linear'],
            ['zoom'],
            4, 0.08,
            5, 0.10,
            6, 0.12,
            7, 0.15,
            8, 0.4,
            10, 0.75,
            11, 1
          ],
          'icon-allow-overlap': true,
          'icon-ignore-placement': true
        }
      },
      {
        id: 'city-labels',
        type: 'symbol',
        source: 'map',
        'source-layer': 'cities',
        minzoom: 5,
        layout: {
          'text-field': ['get', 'localized_name'],
          'text-size': [
            'interpolate',
            ['linear'],
            ['zoom'],
            5, 11,
            6, 13,
            8, 16,
            10, 20
          ],
          'text-anchor': 'top',
          'text-offset': [0, 0.8],
          'text-allow-overlap': true,
          'text-ignore-placement': false
        },
        paint: {
          'text-color': '#ffffff',
          'text-halo-color': '#000000',
          'text-halo-width': 2.5
        }
      },
      {
        id: 'trucks',
        type: 'circle',
        source: 'trucks',
        paint: {
          'circle-radius': 5,
          'circle-color': '#00ff00',
          'circle-stroke-width': 2,
          'circle-stroke-color': '#ffffff'
        },
        minzoom: 7
      }
    ]
  }
}
