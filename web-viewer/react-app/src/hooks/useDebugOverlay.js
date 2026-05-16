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

export function useDebugOverlay(mapInstance, tileMapInfo, graphEnabled, issuesEnabled) {
  const arrowLoadedRef  = useRef(false)
  const fetchTimerRef   = useRef(null)
  const graphEnabledRef = useRef(graphEnabled)
  const issuesEnabledRef = useRef(issuesEnabled)
  const popupRef        = useRef(null)
  const issuesLoadedRef = useRef(false)

  useEffect(() => { graphEnabledRef.current = graphEnabled }, [graphEnabled])
  useEffect(() => { issuesEnabledRef.current = issuesEnabled }, [issuesEnabled])
  useEffect(() => { issuesLoadedRef.current = false }, [tileMapInfo?.game])

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
      if (graphEnabledRef.current) fetchDebugData()
      if (issuesEnabledRef.current) fetchIssuesData()
    }
    mapInstance.on('style.load', onStyleLoad)
    return () => mapInstance.off('style.load', onStyleLoad)
  }, [mapInstance])

  const fetchDebugData = useCallback(async () => {
    if (!mapInstance || !tileMapInfo || !graphEnabledRef.current) return

    const zoom   = mapInstance.getZoom()
    const laneSource = mapInstance.getSource('lane-graph-debug')
    if (!laneSource) return

    // Zoom gate on the client — no request below threshold
    if (zoom < 9) {
      laneSource.setData({ type: 'FeatureCollection', features: [] })
      return
    }

    const bounds = mapInstance.getBounds()
    const [swX, swZ] = mapToGameCoords(bounds.getWest(),  bounds.getSouth(), tileMapInfo)
    const [neX, neZ] = mapToGameCoords(bounds.getEast(),  bounds.getNorth(), tileMapInfo)
    const minX = Math.min(swX, neX), maxX = Math.max(swX, neX)
    const minZ = Math.min(swZ, neZ), maxZ = Math.max(swZ, neZ)

    const game = tileMapInfo?.game || 'ets2'
    const laneUrl = `${ROUTING_BASE}/api/lane-graph/debug?game=${game}&minX=${minX}&maxX=${maxX}&minZ=${minZ}&maxZ=${maxZ}`

    try {
      const laneRes = await fetch(laneUrl)
      if (laneRes.ok) {
        const laneGeojson = await laneRes.json()
        laneSource.setData(withLaneArrows(laneGeojson))
      }
    } catch {
      // Network errors: silently ignore (dev server may be stopped)
    }
  }, [mapInstance, tileMapInfo])

  const fetchIssuesData = useCallback(async () => {
    if (!mapInstance || !tileMapInfo || !issuesEnabledRef.current || issuesLoadedRef.current) return
    const issuesSource = mapInstance.getSource('lane-graph-issues')
    if (!issuesSource) return

    const game = tileMapInfo?.game || 'ets2'
    const issuesUrl = `${ROUTING_BASE}/api/lane-graph/issues?game=${game}`
    try {
      const issuesRes = await fetch(issuesUrl)
      if (issuesRes.ok) {
        const issuesGeojson = await issuesRes.json()
        issuesSource.setData(issuesGeojson)
        issuesLoadedRef.current = true
      }
    } catch {
      // Network errors: silently ignore (dev server may be stopped)
    }
  }, [mapInstance, tileMapInfo])

  // Debounced trigger on map move/zoom
  const debouncedFetch = useCallback(() => {
    clearTimeout(fetchTimerRef.current)
    fetchTimerRef.current = setTimeout(fetchDebugData, DEBOUNCE_MS)
  }, [fetchDebugData])

  // Attach / detach lane graph listeners based on graph toggle
  useEffect(() => {
    if (!mapInstance) return

    if (graphEnabled) {
      mapInstance.on('moveend', debouncedFetch)
      mapInstance.on('zoomend', debouncedFetch)
      fetchDebugData()

      // Click popups
      mapInstance.on('click', 'lane-graph-debug-edges', onLaneEdgeClick)
      mapInstance.on('click', 'lane-graph-debug-nodes', onLaneNodeClick)
      mapInstance.on('mouseenter', 'lane-graph-debug-edges', setCursorPointer)
      mapInstance.on('mouseleave', 'lane-graph-debug-edges', resetCursor)
    } else {
      mapInstance.off('moveend', debouncedFetch)
      mapInstance.off('zoomend', debouncedFetch)
      mapInstance.off('click', 'lane-graph-debug-edges', onLaneEdgeClick)
      mapInstance.off('click', 'lane-graph-debug-nodes', onLaneNodeClick)
      mapInstance.off('mouseenter', 'lane-graph-debug-edges', setCursorPointer)
      mapInstance.off('mouseleave', 'lane-graph-debug-edges', resetCursor)

      const source = mapInstance.getSource('graph-debug')
      if (source) source.setData({ type: 'FeatureCollection', features: [] })
      const laneSource = mapInstance.getSource('lane-graph-debug')
      if (laneSource) laneSource.setData({ type: 'FeatureCollection', features: [] })
      if (popupRef.current) { popupRef.current.remove(); popupRef.current = null }
    }

    return () => {
      mapInstance.off('moveend', debouncedFetch)
      mapInstance.off('zoomend', debouncedFetch)
      mapInstance.off('click', 'lane-graph-debug-edges', onLaneEdgeClick)
      mapInstance.off('click', 'lane-graph-debug-nodes', onLaneNodeClick)
      mapInstance.off('mouseenter', 'lane-graph-debug-edges', setCursorPointer)
      mapInstance.off('mouseleave', 'lane-graph-debug-edges', resetCursor)
    }
  }, [graphEnabled, mapInstance, debouncedFetch])

  // Attach / detach global issue markers based on issues toggle
  useEffect(() => {
    if (!mapInstance) return

    if (issuesEnabled) {
      fetchIssuesData()
      mapInstance.on('click', 'lane-graph-issues', onLaneNodeClick)
      mapInstance.on('mouseenter', 'lane-graph-issues', setCursorPointer)
      mapInstance.on('mouseleave', 'lane-graph-issues', resetCursor)
    } else {
      mapInstance.off('click', 'lane-graph-issues', onLaneNodeClick)
      mapInstance.off('mouseenter', 'lane-graph-issues', setCursorPointer)
      mapInstance.off('mouseleave', 'lane-graph-issues', resetCursor)
      const issuesSource = mapInstance.getSource('lane-graph-issues')
      if (issuesSource) issuesSource.setData({ type: 'FeatureCollection', features: [] })
      issuesLoadedRef.current = false
    }

    return () => {
      mapInstance.off('click', 'lane-graph-issues', onLaneNodeClick)
      mapInstance.off('mouseenter', 'lane-graph-issues', setCursorPointer)
      mapInstance.off('mouseleave', 'lane-graph-issues', resetCursor)
    }
  }, [issuesEnabled, mapInstance, fetchIssuesData])

  function onLaneEdgeClick(e) {
    const p = e.features[0].properties
    if (popupRef.current) popupRef.current.remove()
    popupRef.current = new maplibregl.Popup()
      .setLngLat(e.lngLat)
      .setHTML(`<strong>Lane edge</strong><br/>
        from: <code>${p.from}</code><br/>
        to: <code>${p.to}</code><br/>
        kind: <b>${p.kind}</b><br/>
        lane: <code>${p.lane || ''}</code><br/>
        source: <code>${p.sourceUid || ''}</code>`)
      .addTo(mapInstance)
  }

  function onLaneNodeClick(e) {
    const p = e.features[0].properties
    if (popupRef.current) popupRef.current.remove()
    popupRef.current = new maplibregl.Popup()
      .setLngLat(e.lngLat)
      .setHTML(`<strong>Lane node</strong><br/>
        id: <code>${p.id}</code><br/>
        kind: <b>${p.kind}</b><br/>
        lane: <code>${p.lane || ''}</code><br/>
        snap: <code>${p.snapStatus || ''}</code><br/>
        detail: <code>${p.snapDetail || ''}</code><br/>
        raw node: <code>${p.rawNodeUid || ''}</code><br/>
        degree: <code>${p.inDegree ?? ''} in / ${p.outDegree ?? ''} out</code><br/>
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

function withLaneArrows(geojson) {
  if (!geojson?.features) return geojson

  const nonArrowFeatures = geojson.features.filter(f => f?.properties?.featureType !== 'arrow')
  const arrows = []

  for (const feature of nonArrowFeatures) {
    if (feature?.properties?.featureType !== 'edge') continue
    if (feature?.geometry?.type !== 'LineString') continue

    const arrow = arrowOnLine(feature.geometry.coordinates, 0.7)
    if (!arrow) continue

    arrows.push({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [arrow.lon, arrow.lat] },
      properties: {
        featureType: 'arrow',
        bearing: arrow.bearing,
        kind: feature.properties.kind,
        sourceUid: feature.properties.sourceUid,
        lane: feature.properties.lane,
      },
    })
  }

  return {
    ...geojson,
    features: [...nonArrowFeatures, ...arrows],
  }
}

function arrowOnLine(coords, ratio) {
  if (!coords || coords.length < 2) return null

  let totalLen = 0
  const segLens = []
  for (let i = 1; i < coords.length; i++) {
    const dl = Math.hypot(coords[i][0] - coords[i - 1][0], coords[i][1] - coords[i - 1][1])
    segLens.push(dl)
    totalLen += dl
  }
  if (totalLen < 1e-12) return null

  const target = totalLen * ratio
  let accumulated = 0
  let segIdx = 0
  for (let i = 0; i < segLens.length; i++) {
    if (accumulated + segLens[i] >= target) {
      segIdx = i
      break
    }
    accumulated += segLens[i]
  }

  const t = segLens[segIdx] > 1e-12 ? (target - accumulated) / segLens[segIdx] : 0
  const c0 = coords[segIdx]
  const c1 = coords[segIdx + 1]
  const dLon = c1[0] - c0[0]
  const dLat = c1[1] - c0[1]

  return {
    lon: c0[0] + dLon * t,
    lat: c0[1] + dLat * t,
    bearing: (Math.atan2(dLon, dLat) * 180 / Math.PI + 360) % 360,
  }
}
