import { useRef, useEffect } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { createMapStyle } from '../utils/mapStyle'
import './MapViewer.css'

const _bgCache = {}
async function fetchMapInfo(game) {
  if (game in _bgCache) return _bgCache[game]
  try {
    console.log(`Fetching map info for game: ${game}`)
    const rawBase = import.meta.env.VITE_MAP_DATA_URL || 'http://localhost:8888'
    const baseUrl = rawBase.startsWith('http') ? rawBase : `${window.location.origin}${rawBase}`
    const r = await fetch(`${baseUrl}/${game}/map_background/map_info.json`)
    const json = await r.json()
    console.log('Received map info:', json)
    _bgCache[game] = r.ok ? json : null
  } catch {
    _bgCache[game] = null
  }
  return _bgCache[game]
}

function getMapInfoBounds(mapInfo) {
  const bbox = mapInfo?.texture_bbox_wgs84
  if (!bbox) return null
  return {
    west: bbox.west,
    south: bbox.south,
    east: bbox.east,
    north: bbox.north,
  }
}

function getDefaultView(mapInfo) {
  const bounds = getMapInfoBounds(mapInfo)
  if (!bounds) {
    return {
      center: [
        parseFloat(import.meta.env.VITE_MAP_DEFAULT_CENTER_LON || 0),
        parseFloat(import.meta.env.VITE_MAP_DEFAULT_CENTER_LAT || 0)
      ],
      zoom: parseFloat(import.meta.env.VITE_MAP_DEFAULT_ZOOM || 4)
    }
  }

  return {
    center: [
      (bounds.west + bounds.east) / 2,
      (bounds.south + bounds.north) / 2
    ],
    zoom: parseFloat(import.meta.env.VITE_MAP_DEFAULT_ZOOM || 4)
  }
}

function isInsideMapBounds(position, mapInfo) {
  if (!position) return false
  const bounds = getMapInfoBounds(mapInfo)
  if (!bounds) return true
  const [lon, lat] = position.center
  return lon >= bounds.west && lon <= bounds.east && lat >= bounds.south && lat <= bounds.north
}

function MapViewer({ game, initialPosition, onMapLoad, onZoomChange, onBoundsChange, onPositionChange, trucks }) {
  const mapContainer = useRef(null)
  const map = useRef(null)
  const callbacksRef = useRef({ onMapLoad, onZoomChange, onBoundsChange, onPositionChange })
  const prevGameRef = useRef(game)

  // Keep callbacks ref up to date
  useEffect(() => {
    callbacksRef.current = { onMapLoad, onZoomChange, onBoundsChange, onPositionChange }
  }, [onMapLoad, onZoomChange, onBoundsChange, onPositionChange])

  // Initialize map once
  useEffect(() => {
    if (map.current) return
    let cancelled = false

    fetchMapInfo(game).then(mapInfo => {
      console.log(`Initializing map for game: ${game}`, mapInfo);
      if (cancelled || map.current) return
      const defaultView = getDefaultView(mapInfo)
      const startPosition = isInsideMapBounds(initialPosition, mapInfo) ? initialPosition : null
      const startCenter = startPosition?.center ?? defaultView.center
      const startZoom = startPosition?.zoom ?? defaultView.zoom

      map.current = new maplibregl.Map({
        container: mapContainer.current,
        projection: 'equirectangular',
        style: createMapStyle(game, mapInfo),
        center: startCenter,
        zoom: startZoom,
        minZoom: 4,
        pitch: parseFloat(import.meta.env.VITE_MAP_DEFAULT_PITCH || 20),
        bearing: parseFloat(import.meta.env.VITE_MAP_DEFAULT_BEARING || 0),
        renderWorldCopies: false
      })

    map.current.addControl(new maplibregl.NavigationControl())
    map.current.dragRotate.enable()
    map.current.touchZoomRotate.enableRotation()

    const updateMapState = () => {
      if (callbacksRef.current.onZoomChange) {
        callbacksRef.current.onZoomChange(map.current.getZoom())
      }
      if (callbacksRef.current.onBoundsChange) {
        callbacksRef.current.onBoundsChange(map.current.getBounds())
      }
    }

    const updateHash = () => {
      if (callbacksRef.current.onPositionChange) {
        callbacksRef.current.onPositionChange(map.current.getZoom(), map.current.getCenter())
      }
    }

    map.current.on('load', () => {
      if (callbacksRef.current.onMapLoad) {
        callbacksRef.current.onMapLoad(map.current)
      }
      updateMapState()
    })

    map.current.on('move', updateMapState)
    map.current.on('zoom', updateMapState)
    map.current.on('moveend', updateHash)
    }) // end fetchMapInfo.then

    return () => {
      cancelled = true
      if (map.current) {
        map.current.remove()
        map.current = null
      }
    }
  }, [])

  // Update style when game changes and reset to default center
  useEffect(() => {
    if (prevGameRef.current === game) return
    prevGameRef.current = game
    if (!map.current) return
    fetchMapInfo(game).then(mapInfo => {
      console.log(`Updating map style for game: ${game}`, mapInfo)
      if (!map.current) return
      const defaultView = getDefaultView(mapInfo)
      if (map.current.isStyleLoaded()) {
        console.log('Style loaded, setting new style')
        map.current.setStyle(createMapStyle(game, mapInfo));
        console.log('Resetting view to default center and zoom')
        map.current.jumpTo({ center: defaultView.center, zoom: defaultView.zoom })
      } else {
        map.current.once('load', () => {
          console.log('Style loaded after game change, resetting view to default center and zoom')
          map.current.jumpTo({ center: defaultView.center, zoom: defaultView.zoom })
        })
      }
    })
  }, [game])

  // Update truck data
  useEffect(() => {
    if (!map.current || !trucks) return

    const source = map.current.getSource('trucks')
    if (source) {
      source.setData(trucks)
    }
  }, [trucks])

  return <div ref={mapContainer} className="map-container" />
}

export default MapViewer
