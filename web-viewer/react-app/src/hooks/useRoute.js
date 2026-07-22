import { useState, useCallback, useEffect, useRef } from 'react'
import { gameToMapCoords } from '../utils/coordinates'
import { buildRouteArrowFeatures, ensureRouteArrowImage } from '../utils/routeArrows'

const ROUTING_BASE = import.meta.env.VITE_ROUTING_SERVICE_URL || 'http://localhost:3001'

// stop shape: { point: {name, x, z} | null, type: 'via' | 'avoid' }

const VIA_COLORS = ['#E67E22', '#8E44AD', '#2980B9', '#16A085', '#D35400', '#1A5276', '#7D6608']

// Combine per-leg maneuver lists into a single route-wide list: keep `depart`
// only for the first leg and `arrive` only for the last, turn intermediate
// boundaries into `via` stops, and make distances cumulative across legs.
function mergeLegManeuvers(legs) {
  const out = []
  let offset = 0
  legs.forEach((leg, li) => {
    const list = leg.maneuvers || []
    const isFirst = li === 0
    const isLast = li === legs.length - 1
    const legTotal = list.length ? list[list.length - 1].distanceFromStartM : 0
    for (const m of list) {
      if (m.type === 'depart' && !isFirst) continue
      if (m.type === 'arrive' && !isLast) {
        out.push({ ...m, type: 'via', distanceFromStartM: offset + m.distanceFromStartM })
        continue
      }
      out.push({ ...m, distanceFromStartM: offset + m.distanceFromStartM })
    }
    offset += legTotal
  })
  for (let i = 1; i < out.length; i++) {
    out[i].distanceFromPrevM = Math.max(0, out[i].distanceFromStartM - out[i - 1].distanceFromStartM)
  }
  return out
}


function buildRouteSource(legs, waypoints, stops, tileMapInfo) {
  const features = []
  for (const leg of legs)
    features.push({ type: 'Feature', geometry: leg.geometry, properties: { featureType: 'route_line' } })

  features.push(...buildRouteArrowFeatures(legs, tileMapInfo))

  let viaIdx = 0
  waypoints.forEach((wp, i) => {
    const isStart = i === 0
    const isEnd   = i === waypoints.length - 1
    const [lon, lat] = gameToMapCoords(wp.x, wp.z, tileMapInfo)
    const label = isStart ? 'A' : isEnd ? 'B' : String(++viaIdx)
    const color = isStart ? '#00AA55' : isEnd ? '#FF4444' : VIA_COLORS[(viaIdx - 1) % VIA_COLORS.length]
    features.push({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [lon, lat] },
      properties: {
        markerType: isStart ? 'start' : isEnd ? 'end' : 'stop',
        label, color,
        name: wp.name,
        wpX: wp.x,
        wpZ: wp.z,
      },
    })
  })

  // Avoid stops — shown as X on map even though they're not route waypoints
  stops.filter(s => s.type === 'avoid' && s.point).forEach(s => {
    const [lon, lat] = gameToMapCoords(s.point.x, s.point.z, tileMapInfo)
    features.push({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [lon, lat] },
      properties: {
        markerType: 'avoid',
        label: 'X',
        color: '#882222',
        name: s.point.name,
        wpX: s.point.x,
        wpZ: s.point.z,
      },
    })
  })

  return { type: 'FeatureCollection', features }
}

export function useRoute(mapInstance, tileMapInfo) {
  const [from, setFrom]   = useState(null)
  const [to, setTo]       = useState(null)
  // each stop: { point: {name,x,z}|null, type: 'via'|'avoid' }
  const [stops, setStops] = useState([])
  const [options, setOptions] = useState({ mode: 'shortest', avoidHighways: false, avoidFerries: false })
  const [routeInfo, setRouteInfo] = useState(null)
  const [loading, setLoading]    = useState(false)
  const [error, setError]        = useState(null)
  const routeDataRef = useRef(null)

  useEffect(() => {
    if (!mapInstance) return
    const onStyleLoad = () => {
      ensureRouteArrowImage(mapInstance)
      const source = mapInstance.getSource('route')
      if (source && routeDataRef.current) source.setData(routeDataRef.current)
    }
    if (mapInstance.isStyleLoaded()) ensureRouteArrowImage(mapInstance)
    mapInstance.on('style.load', onStyleLoad)
    return () => mapInstance.off('style.load', onStyleLoad)
  }, [mapInstance])

  // Add a point to the next empty slot: from → to → via stop
  const addWaypoint = useCallback((point) => {
    if (!from)      { setFrom(point); return }
    if (!to)        { setTo(point);   return }
    setStops(prev => [...prev, { point, type: 'via' }])
  }, [from, to])

  const addStop = useCallback(() =>
    setStops(prev => [...prev, { point: null, type: 'via' }]), [])

  const addStopWithPoint = useCallback((point) =>
    setStops(prev => [...prev, { point, type: 'via' }]), [])

  const removeStop = useCallback((idx) =>
    setStops(prev => prev.filter((_, i) => i !== idx)), [])

  const updateStopPoint = useCallback((idx, point) =>
    setStops(prev => prev.map((s, i) => i === idx ? { ...s, point } : s)), [])

  const updateStopType = useCallback((idx, type) =>
    setStops(prev => prev.map((s, i) => i === idx ? { ...s, type } : s)), [])

  const clearRoute = useCallback(() => {
    setFrom(null); setTo(null); setStops([])
    setRouteInfo(null); setError(null)
    routeDataRef.current = null
    mapInstance?.getSource('route')?.setData({ type: 'FeatureCollection', features: [] })
  }, [mapInstance])

  const calculateRoute = useCallback(async () => {
    if (!mapInstance || !tileMapInfo || !from || !to) return

    const viaStops  = stops.filter(s => s.type === 'via'   && s.point).map(s => s.point)
    const avoidPts  = stops.filter(s => s.type === 'avoid' && s.point).map(s => ({ x: s.point.x, z: s.point.z }))
    const waypoints = [from, ...viaStops, to]

    setLoading(true); setError(null)

    const game = tileMapInfo.game || 'ets2'
    const baseParams = `game=${game}&mode=${options.mode}&avoidHighways=${options.avoidHighways}&avoidFerries=${options.avoidFerries}`
    const avoidParam = avoidPts.length ? `&avoidPoints=${encodeURIComponent(JSON.stringify(avoidPts))}` : ''

    try {
      const legs = []
      let totalLengthKm = 0, landLengthKm = 0, ferryLengthKm = 0

      for (let i = 0; i < waypoints.length - 1; i++) {
        const a = waypoints[i], b = waypoints[i + 1]
        // A via waypoint is an intermediate stop shared between two legs (not the
        // route's true start/end) — snap it to a real through-road node so the
        // route passes through it instead of detouring into a spur.
        const fromIsVia = i > 0
        const toIsVia = i < waypoints.length - 2
        const url = `${ROUTING_BASE}/api/route?${baseParams}${avoidParam}&fromX=${a.x}&fromZ=${a.z}&toX=${b.x}&toZ=${b.z}&fromIsVia=${fromIsVia}&toIsVia=${toIsVia}`
        const res = await fetch(url)
        if (!res.ok) {
          const body = await res.json().catch(() => ({}))
          throw new Error(body.error || `Leg ${i + 1}: route not found (${res.status})`)
        }
        const data = await res.json()
        legs.push({ geometry: data.route.geometry, length: data.totalLength, maneuvers: data.maneuvers || [] })
        totalLengthKm += data.totalLengthKm
        landLengthKm  += data.landLengthKm
        ferryLengthKm += data.ferryLengthKm
      }

      const geojson = buildRouteSource(legs, waypoints, stops, tileMapInfo)
      routeDataRef.current = geojson
      ensureRouteArrowImage(mapInstance)
      mapInstance.getSource('route')?.setData(geojson)
      setRouteInfo({ totalLengthKm, landLengthKm, ferryLengthKm, legs: legs.length, maneuvers: mergeLegManeuvers(legs) })

      // Fit map to route bounds
      let minLon = Infinity, maxLon = -Infinity, minLat = Infinity, maxLat = -Infinity
      for (const leg of legs) {
        for (const [lon, lat] of leg.geometry.coordinates) {
          if (lon < minLon) minLon = lon
          if (lon > maxLon) maxLon = lon
          if (lat < minLat) minLat = lat
          if (lat > maxLat) maxLat = lat
        }
      }
      if (isFinite(minLon)) {
        mapInstance.fitBounds([[minLon, minLat], [maxLon, maxLat]], { padding: 80, duration: 1000 })
      }
      setLoading(false)
    } catch (err) {
      setError(err.message || 'Routing failed')
      setLoading(false)
    }
  }, [mapInstance, tileMapInfo, from, to, stops, options])

  // Keep a fresh ref to avoid stale closure in the avoid-watcher below
  const latestCalcRef = useRef(calculateRoute)
  latestCalcRef.current = calculateRoute

  const avoidKey = stops
    .filter(s => s.type === 'avoid' && s.point)
    .map(s => `${s.point.x},${s.point.z}`)
    .join('|')
  const prevAvoidKeyRef = useRef(null)

  useEffect(() => {
    if (prevAvoidKeyRef.current === null) { prevAvoidKeyRef.current = avoidKey; return }
    if (avoidKey === prevAvoidKeyRef.current) return
    prevAvoidKeyRef.current = avoidKey
    if (from && to) latestCalcRef.current()
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [avoidKey])

  return {
    from, setFrom,
    to, setTo,
    stops, addStop, addStopWithPoint, removeStop, updateStopPoint, updateStopType, addWaypoint,
    options, setOptions,
    routeInfo, loading, error,
    calculateRoute, clearRoute,
  }
}
