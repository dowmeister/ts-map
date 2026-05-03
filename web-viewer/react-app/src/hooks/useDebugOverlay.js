import { useEffect, useRef, useCallback } from 'react'
import maplibregl from 'maplibre-gl'
import { mapToGameCoords } from '../utils/coordinates'

const ROUTING_BASE = import.meta.env.VITE_ROUTING_SERVICE_URL || 'http://localhost:3001'
const DEBOUNCE_MS  = 350

// Programmatically create a 12×12 triangle arrow icon for MapLibre.
// Using SDF (Signed Distance Field) so icon-color paint works per-feature.
function createArrowImageData(size = 12) {
  const canvas = document.createElement('canvas')
  canvas.width  = size
  canvas.height = size
  const ctx = canvas.getContext('2d')
  ctx.fillStyle = '#ffffff'
  ctx.beginPath()
  ctx.moveTo(size / 2, 0)
  ctx.lineTo(size,     size)
  ctx.lineTo(0,        size)
  ctx.closePath()
  ctx.fill()
  return ctx.getImageData(0, 0, size, size)
}

export function useDebugOverlay(mapInstance, tileMapInfo, enabled) {
  const arrowLoadedRef  = useRef(false)
  const fetchTimerRef   = useRef(null)
  const enabledRef      = useRef(enabled)
  const popupRef        = useRef(null)

  useEffect(() => { enabledRef.current = enabled }, [enabled])

  // Load arrow icon once when map is available
  useEffect(() => {
    if (!mapInstance || arrowLoadedRef.current) return
    if (!mapInstance.isStyleLoaded()) {
      mapInstance.once('style.load', () => loadArrow(mapInstance))
    } else {
      loadArrow(mapInstance)
    }
  }, [mapInstance])

  function loadArrow(map) {
    if (arrowLoadedRef.current || map.hasImage('route-arrow')) {
      arrowLoadedRef.current = true
      return
    }
    const imageData = createArrowImageData(12)
    map.addImage('route-arrow', imageData, { sdf: true })
    arrowLoadedRef.current = true
  }

  // Re-load arrow after style reload (game switch)
  useEffect(() => {
    if (!mapInstance) return
    const onStyleLoad = () => {
      arrowLoadedRef.current = false
      loadArrow(mapInstance)
      if (enabledRef.current) fetchDebugData()
    }
    mapInstance.on('style.load', onStyleLoad)
    return () => mapInstance.off('style.load', onStyleLoad)
  }, [mapInstance])

  const fetchDebugData = useCallback(async () => {
    if (!mapInstance || !tileMapInfo || !enabledRef.current) return

    const zoom   = mapInstance.getZoom()
    const source = mapInstance.getSource('graph-debug')
    if (!source) return

    // Zoom gate on the client — no request below threshold
    if (zoom < 9) {
      source.setData({ type: 'FeatureCollection', features: [] })
      return
    }

    const bounds = mapInstance.getBounds()
    const [swX, swZ] = mapToGameCoords(bounds.getWest(),  bounds.getSouth(), tileMapInfo)
    const [neX, neZ] = mapToGameCoords(bounds.getEast(),  bounds.getNorth(), tileMapInfo)
    const minX = Math.min(swX, neX), maxX = Math.max(swX, neX)
    const minZ = Math.min(swZ, neZ), maxZ = Math.max(swZ, neZ)

    const game = tileMapInfo?.game || 'ets2'
    const url = `${ROUTING_BASE}/api/graph/debug?game=${game}&minX=${minX}&maxX=${maxX}&minZ=${minZ}&maxZ=${maxZ}`

    try {
      const res = await fetch(url)
      if (!res.ok) return
      const geojson = await res.json()
      source.setData(geojson)
    } catch {
      // Network errors: silently ignore (dev server may be stopped)
    }
  }, [mapInstance, tileMapInfo])

  // Debounced trigger on map move/zoom
  const debouncedFetch = useCallback(() => {
    clearTimeout(fetchTimerRef.current)
    fetchTimerRef.current = setTimeout(fetchDebugData, DEBOUNCE_MS)
  }, [fetchDebugData])

  // Attach / detach move listeners and click popups based on enabled state
  useEffect(() => {
    if (!mapInstance) return

    if (enabled) {
      mapInstance.on('moveend', debouncedFetch)
      mapInstance.on('zoomend', debouncedFetch)
      fetchDebugData()

      // Click popups
      mapInstance.on('click', 'graph-debug-edges', onEdgeClick)
      mapInstance.on('click', 'graph-debug-nodes', onNodeClick)
      mapInstance.on('mouseenter', 'graph-debug-edges', setCursorPointer)
      mapInstance.on('mouseleave', 'graph-debug-edges', resetCursor)
    } else {
      mapInstance.off('moveend', debouncedFetch)
      mapInstance.off('zoomend', debouncedFetch)
      mapInstance.off('click', 'graph-debug-edges', onEdgeClick)
      mapInstance.off('click', 'graph-debug-nodes', onNodeClick)
      mapInstance.off('mouseenter', 'graph-debug-edges', setCursorPointer)
      mapInstance.off('mouseleave', 'graph-debug-edges', resetCursor)

      const source = mapInstance.getSource('graph-debug')
      if (source) source.setData({ type: 'FeatureCollection', features: [] })
      if (popupRef.current) { popupRef.current.remove(); popupRef.current = null }
    }

    return () => {
      mapInstance.off('moveend', debouncedFetch)
      mapInstance.off('zoomend', debouncedFetch)
      mapInstance.off('click', 'graph-debug-edges', onEdgeClick)
      mapInstance.off('click', 'graph-debug-nodes', onNodeClick)
      mapInstance.off('mouseenter', 'graph-debug-edges', setCursorPointer)
      mapInstance.off('mouseleave', 'graph-debug-edges', resetCursor)
    }
  }, [enabled, mapInstance, debouncedFetch])

  function onEdgeClick(e) {
    const p = e.features[0].properties
    if (popupRef.current) popupRef.current.remove()
    popupRef.current = new maplibregl.Popup()
      .setLngLat(e.lngLat)
      .setHTML(`<strong>Edge</strong><br/>
        from: <code>${p.from}</code><br/>
        to: <code>${p.to}</code><br/>
        type: <b>${p.itemType}</b><br/>
        speed: ${p.speedClass}<br/>
        length: ${Math.round(p.length)} m<br/>
        weight: ${Math.round(p.weight)}`)
      .addTo(mapInstance)
  }

  function onNodeClick(e) {
    const p = e.features[0].properties
    if (popupRef.current) popupRef.current.remove()
    popupRef.current = new maplibregl.Popup()
      .setLngLat(e.lngLat)
      .setHTML(`<strong>Node</strong><br/>
        uid: <code>${p.uid}</code><br/>
        x: ${Number(p.x).toFixed(1)}<br/>
        z: ${Number(p.z).toFixed(1)}`)
      .addTo(mapInstance)
  }

  function setCursorPointer() { mapInstance.getCanvas().style.cursor = 'pointer' }
  function resetCursor()      { mapInstance.getCanvas().style.cursor = '' }
}

function showMessage(map, text) {
  // Brief toast-style console log — full UI toast can be added later
  console.info('[DebugOverlay]', text)
}
