import { useRef, useEffect } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { createMapStyle } from '../utils/mapStyle'
import './MapViewer.css'

function MapViewer({ game, initialPosition, onMapLoad, onZoomChange, onBoundsChange, onPositionChange, trucks }) {
  const mapContainer = useRef(null)
  const map = useRef(null)
  const callbacksRef = useRef({ onMapLoad, onZoomChange, onBoundsChange, onPositionChange })
  const isFirstGame = useRef(true)

  // Keep callbacks ref up to date
  useEffect(() => {
    callbacksRef.current = { onMapLoad, onZoomChange, onBoundsChange, onPositionChange }
  }, [onMapLoad, onZoomChange, onBoundsChange, onPositionChange])

  // Initialize map once
  useEffect(() => {
    if (map.current) return

    const startCenter = initialPosition?.center ?? [
      parseFloat(import.meta.env.VITE_MAP_DEFAULT_CENTER_LON || 0),
      parseFloat(import.meta.env.VITE_MAP_DEFAULT_CENTER_LAT || 0)
    ]
    const startZoom = initialPosition?.zoom ?? parseFloat(import.meta.env.VITE_MAP_DEFAULT_ZOOM || 4)

    map.current = new maplibregl.Map({
      container: mapContainer.current,
      projection: 'equirectangular',
      style: createMapStyle(game),
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

    return () => {
      if (map.current) {
        map.current.remove()
        map.current = null
      }
    }
  }, [])

  // Update style when game changes and reset to default center
  useEffect(() => {
    if (isFirstGame.current) {
      isFirstGame.current = false
      return
    }
    if (!map.current) return
    const defaultCenter = [
      parseFloat(import.meta.env.VITE_MAP_DEFAULT_CENTER_LON || 0),
      parseFloat(import.meta.env.VITE_MAP_DEFAULT_CENTER_LAT || 0)
    ]
    const defaultZoom = parseFloat(import.meta.env.VITE_MAP_DEFAULT_ZOOM || 4)
    if (map.current.isStyleLoaded()) {
      map.current.setStyle(createMapStyle(game))
      map.current.jumpTo({ center: defaultCenter, zoom: defaultZoom })
    } else {
      map.current.once('load', () => {
        map.current.jumpTo({ center: defaultCenter, zoom: defaultZoom })
      })
    }
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
