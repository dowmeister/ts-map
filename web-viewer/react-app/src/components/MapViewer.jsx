import { useRef, useEffect } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { createMapStyle } from '../utils/mapStyle'
import './MapViewer.css'

// WGS84 lon/lat bounding box, precomputed server-side (edge-sampled through the
// real projection, not just linearly) and stored in VectorTileMapInfo.json's
// "bounds_wgs84" field — the single source of truth for map bounds.
function getMapInfoBounds(tileMapInfo) {
  const bbox = tileMapInfo?.bounds_wgs84
  if (!bbox) return null
  return {
    west: bbox.west,
    south: bbox.south,
    east: bbox.east,
    north: bbox.north,
  }
}

function getDefaultView(tileMapInfo) {
  const bounds = getMapInfoBounds(tileMapInfo)
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

function isInsideMapBounds(position, tileMapInfo) {
  if (!position) return false
  const bounds = getMapInfoBounds(tileMapInfo)
  if (!bounds) return true
  const [lon, lat] = position.center
  return lon >= bounds.west && lon <= bounds.east && lat >= bounds.south && lat <= bounds.north
}

function MapViewer({ game, tileMapInfo, initialPosition, onMapLoad, onZoomChange, onBoundsChange, onPositionChange, trucks }) {
  const mapContainer = useRef(null)
  const map = useRef(null)
  const callbacksRef = useRef({ onMapLoad, onZoomChange, onBoundsChange, onPositionChange })
  const prevGameRef = useRef(game)

  // Keep callbacks ref up to date
  useEffect(() => {
    callbacksRef.current = { onMapLoad, onZoomChange, onBoundsChange, onPositionChange }
  }, [onMapLoad, onZoomChange, onBoundsChange, onPositionChange])

  // Initialize map once, as soon as tileMapInfo for the current game is available
  useEffect(() => {
    if (map.current) return
    if (!tileMapInfo || tileMapInfo.game !== game) return

    const defaultView = getDefaultView(tileMapInfo)
    const startPosition = isInsideMapBounds(initialPosition, tileMapInfo) ? initialPosition : null
    const startCenter = startPosition?.center ?? defaultView.center
    const startZoom = startPosition?.zoom ?? defaultView.zoom

    map.current = new maplibregl.Map({
      container: mapContainer.current,
      projection: 'equirectangular',
      style: createMapStyle(game, tileMapInfo),
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
  }, [tileMapInfo])

  // Destroy the map only when MapViewer itself unmounts — NOT on every
  // tileMapInfo change (a new object is fetched on every game switch, and
  // re-running this as a per-dependency cleanup would tear down the map while
  // `mapInstance` in the parent still pointed at it, crashing other hooks
  // that call methods like getSource() on the now-destroyed instance).
  useEffect(() => {
    return () => {
      if (map.current) {
        map.current.remove()
        map.current = null
      }
      if (callbacksRef.current.onMapLoad) {
        callbacksRef.current.onMapLoad(null)
      }
    }
  }, [])

  // Update style when game changes and reset to default center
  useEffect(() => {
    if (prevGameRef.current === game) return
    if (!tileMapInfo || tileMapInfo.game !== game) return
    prevGameRef.current = game
    if (!map.current) return

    const defaultView = getDefaultView(tileMapInfo)
    if (map.current.isStyleLoaded()) {
      map.current.setStyle(createMapStyle(game, tileMapInfo))
      map.current.jumpTo({ center: defaultView.center, zoom: defaultView.zoom })
    } else {
      map.current.once('load', () => {
        map.current.jumpTo({ center: defaultView.center, zoom: defaultView.zoom })
      })
    }
  }, [game, tileMapInfo])

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

