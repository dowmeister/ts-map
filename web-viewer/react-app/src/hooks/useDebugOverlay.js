import { useEffect, useRef, useCallback } from 'react'
import maplibregl from 'maplibre-gl'
import { mapToGameCoords } from '../utils/coordinates'
import { arrowOnLine, ensureRouteArrowImage } from '../utils/routeArrows'

const ROUTING_BASE = import.meta.env.VITE_ROUTING_SERVICE_URL || 'http://localhost:3001'
const DEBOUNCE_MS  = 350

export function useDebugOverlay(mapInstance, tileMapInfo, graphEnabled, issuesEnabled, softIssuesEnabled = false) {
  const arrowLoadedRef  = useRef(false)
  const fetchTimerRef   = useRef(null)
  const graphEnabledRef = useRef(graphEnabled)
  const issuesEnabledRef = useRef(issuesEnabled)
  const softIssuesEnabledRef = useRef(softIssuesEnabled)
  const popupRef        = useRef(null)
  const issuesLoadedRef = useRef(false)
  const fetchSeqRef     = useRef(0)
  const lastBboxKeyRef  = useRef(null)

  useEffect(() => { graphEnabledRef.current = graphEnabled }, [graphEnabled])
  useEffect(() => { issuesEnabledRef.current = issuesEnabled }, [issuesEnabled])
  useEffect(() => { softIssuesEnabledRef.current = softIssuesEnabled }, [softIssuesEnabled])
  useEffect(() => { issuesLoadedRef.current = false }, [tileMapInfo?.game, softIssuesEnabled])

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
    ensureRouteArrowImage(map, 18)
    arrowLoadedRef.current = true
  }

  // Re-load arrow after style reload (game switch)
  useEffect(() => {
    if (!mapInstance) return
    const onStyleLoad = () => {
      arrowLoadedRef.current = false
      loadArrow(mapInstance)
      lastBboxKeyRef.current = null
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
      lastBboxKeyRef.current = null
      return
    }

    const bounds = mapInstance.getBounds()
    const [swX, swZ] = mapToGameCoords(bounds.getWest(),  bounds.getSouth(), tileMapInfo)
    const [neX, neZ] = mapToGameCoords(bounds.getEast(),  bounds.getNorth(), tileMapInfo)
    const minX = Math.min(swX, neX), maxX = Math.max(swX, neX)
    const minZ = Math.min(swZ, neZ), maxZ = Math.max(swZ, neZ)

    // Skip refetch when the viewport hasn't meaningfully changed (avoids redundant
    // 25 MB chunk work on the server for sub-tile pans / repeated moveend events).
    const bboxKey = `${Math.round(minX)},${Math.round(maxX)},${Math.round(minZ)},${Math.round(maxZ)}`
    if (bboxKey === lastBboxKeyRef.current) return

    const game = tileMapInfo?.game || 'ets2'
    const laneUrl = `${ROUTING_BASE}/api/lane-graph/debug?game=${game}&minX=${minX}&maxX=${maxX}&minZ=${minZ}&maxZ=${maxZ}&arrows=false`
    const syntheticUrl = `${ROUTING_BASE}/api/graph/debug?game=${game}&minX=${minX}&maxX=${maxX}&minZ=${minZ}&maxZ=${maxZ}&synthetic=true`

    // Monotonic request token: a newer request supersedes older ones. We never
    // abort an in-flight request (that would throw away data the server already
    // produced) — instead we let it finish and only apply the result if it is
    // still the most recent request. This guarantees the final viewport always
    // renders, while stale out-of-order responses are dropped.
    const seq = ++fetchSeqRef.current

    try {
      const [laneRes, syntheticRes] = await Promise.all([fetch(laneUrl), fetch(syntheticUrl)])
      if (seq !== fetchSeqRef.current || !graphEnabledRef.current) return
      if (laneRes.ok) {
        const laneGeojson = await laneRes.json()
        let features = laneGeojson.features || []
        if (syntheticRes.ok) {
          const syntheticGeojson = await syntheticRes.json()
          features = [...features, ...asLaneDebugFeatures(syntheticGeojson)]
        }
        if (seq !== fetchSeqRef.current || !graphEnabledRef.current) return
        laneSource.setData(withLaneArrows({ ...laneGeojson, features }))
        lastBboxKeyRef.current = bboxKey
      }
    } catch {
      // Network errors (dev server stopped): ignore silently.
    }
  }, [mapInstance, tileMapInfo])

  const fetchIssuesData = useCallback(async () => {
    if (!mapInstance || !tileMapInfo || !issuesEnabledRef.current || issuesLoadedRef.current) return
    const issuesSource = mapInstance.getSource('lane-graph-issues')
    if (!issuesSource) return

    const game = tileMapInfo?.game || 'ets2'
    const issuesUrl = `${ROUTING_BASE}/api/lane-graph/issues?game=${game}&soft=${softIssuesEnabledRef.current ? 'true' : 'false'}`
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

      // Invalidate any in-flight request so its (late) response won't repaint.
      fetchSeqRef.current++
      lastBboxKeyRef.current = null
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
    issuesLoadedRef.current = false
    if (issuesEnabled) fetchIssuesData()
  }, [softIssuesEnabled, issuesEnabled, mapInstance, fetchIssuesData])

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
        <b>speedClass: <span style="color:#f90">${p.speedClass || '?'}</span></b><br/>
        ${p.laneName ? `laneName: <code>${p.laneName}</code><br/>` : ''}
        ${p.speedLimitKph != null ? `speedLimit: <code>${p.speedLimitKph} km/h</code><br/>` : ''}
        ${p.lanes != null ? `lanes: <code>${p.lanes}</code><br/>` : ''}
        ${p.weight != null ? `weight: <code>${Number(p.weight).toFixed(1)}</code> / len: <code>${Number(p.length).toFixed(1)}</code><br/>` : ''}
        lane: <code>${p.lane || ''}</code><br/>
        source: <code>${p.sourceUid || ''}</code><br/>
        direction: <code>${p.direction || ''}</code><br/>
        traffic: <code>${p.trafficSide || ''}</code><br/>
        mid: <code>${Number(p.midX ?? 0).toFixed(1)}, ${Number(p.midZ ?? 0).toFixed(1)}</code><br/>
        path raw: <code>${p.pathStartRawNodeUid || ''} -> ${p.pathEndRawNodeUid || ''}</code>`)
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
        severity: <code>${p.issueSeverity || ''}</code><br/>
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

function asLaneDebugFeatures(geojson) {
  if (!geojson?.features) return []
  return geojson.features
    .filter(f => f?.properties?.featureType === 'edge')
    .map(f => ({
      ...f,
      properties: {
        ...f.properties,
        kind: f.properties.itemType || f.properties.kind,
        lane: f.properties.itemType || f.properties.lane || '',
      },
    }))
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
