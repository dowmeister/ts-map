import { useRef, useEffect } from 'react'
import maplibregl from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { createMapStyle } from '../utils/mapStyle'
import './MapViewer.css'

function MapViewer({ game, onMapLoad, onZoomChange, onBoundsChange, trucks }) {
  const mapContainer = useRef(null)
  const map = useRef(null)
  const callbacksRef = useRef({ onMapLoad, onZoomChange, onBoundsChange })

  // Keep callbacks ref up to date
  useEffect(() => {
    callbacksRef.current = { onMapLoad, onZoomChange, onBoundsChange }
  }, [onMapLoad, onZoomChange, onBoundsChange])

  // Initialize map once
  useEffect(() => {
    if (map.current) return

    map.current = new maplibregl.Map({
      container: mapContainer.current,
      projection: 'equirectangular',
      style: createMapStyle(game),
      center: [
        parseFloat(import.meta.env.VITE_MAP_DEFAULT_CENTER_LON || 0),
        parseFloat(import.meta.env.VITE_MAP_DEFAULT_CENTER_LAT || 0)
      ],
      zoom: parseFloat(import.meta.env.VITE_MAP_DEFAULT_ZOOM || 4),
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

    map.current.on('load', () => {
      if (callbacksRef.current.onMapLoad) {
        callbacksRef.current.onMapLoad(map.current)
      }
      updateMapState()
    })

    map.current.on('move', updateMapState)
    map.current.on('zoom', updateMapState)

    return () => {
      if (map.current) {
        map.current.remove()
        map.current = null
      }
    }
  }, [])

  // Update style when game changes
  useEffect(() => {
    if (map.current && map.current.isStyleLoaded()) {
      map.current.setStyle(createMapStyle(game))
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
