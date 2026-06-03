import { useState, useCallback, useEffect, useRef } from 'react'
import { gameToMapCoords } from '../utils/coordinates'

const ROUTING_BASE = import.meta.env.VITE_ROUTING_SERVICE_URL || 'http://localhost:3001'

// stop shape: { point: {name, x, z} | null, type: 'via' | 'avoid' }

const VIA_COLORS = ['#E67E22', '#8E44AD', '#2980B9', '#16A085', '#D35400', '#1A5276', '#7D6608']

function createArrowImageData(size = 18) {
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const ctx = canvas.getContext('2d')
  ctx.fillStyle = '#ffffff'
  ctx.beginPath()
  ctx.moveTo(size / 2, 0)
  ctx.lineTo(size, size)
  ctx.lineTo(size / 2, size * 0.72)
  ctx.lineTo(0, size)
  ctx.closePath()
  ctx.fill()
  return ctx.getImageData(0, 0, size, size)
}

function ensureRouteArrowImage(map) {
  if (!map || map.hasImage('route-maneuver-arrow')) return
  map.addImage('route-maneuver-arrow', createArrowImageData(), { sdf: true })
}

function toMeters(coord, refLat) {
  const [lon, lat] = coord
  return {
    x: lon * 111320 * Math.cos(refLat * Math.PI / 180),
    y: lat * 110540,
  }
}

function distanceMeters(a, b) {
  const refLat = (a[1] + b[1]) * 0.5
  const am = toMeters(a, refLat)
  const bm = toMeters(b, refLat)
  return Math.hypot(bm.x - am.x, bm.y - am.y)
}

function bearingDegrees(a, b) {
  const refLat = (a[1] + b[1]) * 0.5
  const am = toMeters(a, refLat)
  const bm = toMeters(b, refLat)
  return (Math.atan2(bm.x - am.x, bm.y - am.y) * 180 / Math.PI + 360) % 360
}

function signedTurnDegrees(prev, at, next) {
  const refLat = at[1]
  const pm = toMeters(prev, refLat)
  const am = toMeters(at, refLat)
  const nm = toMeters(next, refLat)
  const ax = am.x - pm.x, ay = am.y - pm.y
  const bx = nm.x - am.x, by = nm.y - am.y
  const aLen = Math.hypot(ax, ay)
  const bLen = Math.hypot(bx, by)
  if (aLen < 1 || bLen < 1) return 0
  const cross = ax * by - ay * bx
  const dot = ax * bx + ay * by
  return Math.atan2(cross, dot) * 180 / Math.PI
}

function pointBefore(coords, index, minMeters) {
  let dist = 0
  for (let i = index; i > 0; i--) {
    dist += distanceMeters(coords[i], coords[i - 1])
    if (dist >= minMeters) return coords[i - 1]
  }
  return null
}

function pointAfter(coords, index, minMeters) {
  let dist = 0
  for (let i = index; i < coords.length - 1; i++) {
    dist += distanceMeters(coords[i], coords[i + 1])
    if (dist >= minMeters) return coords[i + 1]
  }
  return null
}

function buildManeuverFeatures(legs) {
  const features = []
  const LOOK_DISTANCE_M = 35
  const MIN_TURN_DEG = 42
  const MAX_TURN_DEG = 155
  const MIN_SPACING_M = 140

  for (const [legIndex, leg] of legs.entries()) {
    const coords = leg.geometry?.coordinates || []
    let lastArrowCoord = null

    for (let i = 2; i < coords.length - 2; i++) {
      const prev = pointBefore(coords, i, LOOK_DISTANCE_M)
      const next = pointAfter(coords, i, LOOK_DISTANCE_M)
      if (!prev || !next) continue

      const turn = signedTurnDegrees(prev, coords[i], next)
      const absTurn = Math.abs(turn)
      if (absTurn < MIN_TURN_DEG || absTurn > MAX_TURN_DEG) continue
      if (lastArrowCoord && distanceMeters(lastArrowCoord, coords[i]) < MIN_SPACING_M) continue

      lastArrowCoord = coords[i]
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: coords[i] },
        properties: {
          featureType: 'route_maneuver',
          maneuver: turn > 0 ? 'left' : 'right',
          turn: Math.round(turn),
          bearing: bearingDegrees(coords[i], next),
          legIndex,
        },
      })
    }
  }

  return features
}

function buildRouteSource(legs, waypoints, stops, tileMapInfo) {
  const features = []
  for (const leg of legs)
    features.push({ type: 'Feature', geometry: leg.geometry, properties: {} })

  features.push(...buildManeuverFeatures(legs))

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
        const url = `${ROUTING_BASE}/api/route?${baseParams}${avoidParam}&fromX=${a.x}&fromZ=${a.z}&toX=${b.x}&toZ=${b.z}`
        const res = await fetch(url)
        if (!res.ok) {
          const body = await res.json().catch(() => ({}))
          throw new Error(body.error || `Leg ${i + 1}: route not found (${res.status})`)
        }
        const data = await res.json()
        legs.push({ geometry: data.route.geometry, length: data.totalLength })
        totalLengthKm += data.totalLengthKm
        landLengthKm  += data.landLengthKm
        ferryLengthKm += data.ferryLengthKm
      }

      const geojson = buildRouteSource(legs, waypoints, stops, tileMapInfo)
      routeDataRef.current = geojson
      mapInstance.getSource('route')?.setData(geojson)
      setRouteInfo({ totalLengthKm, landLengthKm, ferryLengthKm, legs: legs.length })

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
