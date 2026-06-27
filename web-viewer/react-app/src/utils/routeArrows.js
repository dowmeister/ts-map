import { mapToGameCoords } from './coordinates'

export const ROUTE_ARROW_IMAGE = 'route-arrow'
const ARROW_PIXEL_RATIO = 4

export function createArrowImageData(size = 18, pixelRatio = ARROW_PIXEL_RATIO) {
  const dim = Math.max(1, Math.round(size * pixelRatio))
  const canvas = document.createElement('canvas')
  canvas.width = dim
  canvas.height = dim
  const ctx = canvas.getContext('2d')
  ctx.clearRect(0, 0, dim, dim)

  // Solid arrowhead pointing up (north) with a concave tail, drawn at high
  // resolution so it converts to a crisp signed-distance field at any scale.
  // The dark icon-halo configured in the style provides the outline, giving it
  // the bold navigation "turn arrow" look.
  ctx.fillStyle = '#ffffff'
  ctx.beginPath()
  ctx.moveTo(dim * 0.5, dim * 0.05) // tip
  ctx.lineTo(dim * 0.95, dim * 0.9) // right wing
  ctx.lineTo(dim * 0.5, dim * 0.64) // back notch
  ctx.lineTo(dim * 0.05, dim * 0.9) // left wing
  ctx.closePath()
  ctx.fill()

  return ctx.getImageData(0, 0, dim, dim)
}

export function ensureRouteArrowImage(map, size = 18) {
  if (!map || map.hasImage(ROUTE_ARROW_IMAGE)) return
  map.addImage(ROUTE_ARROW_IMAGE, createArrowImageData(size, ARROW_PIXEL_RATIO), {
    sdf: true,
    pixelRatio: ARROW_PIXEL_RATIO,
  })
}

export function arrowOnLine(coords, ratio = 0.7) {
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
    bearing: mapBearingDegrees(c0, c1),
  }
}

export function buildRouteManeuverFeatures(legs, tileMapInfo, options = {}) {
  const {
    lookDistance = 80,
    lookDistances = [lookDistance, lookDistance * 2, lookDistance * 3.25],
    exitArm = 72,
    bodyLength = 46,
    minTurnDegrees = 24,
    maxTurnDegrees = 170,
    minSpacing = 180,
  } = options
  const features = []

  for (const [legIndex, leg] of legs.entries()) {
    const coords = leg.geometry?.coordinates || []
    const points = routePoints(coords, tileMapInfo)
    if (points.length < 5) continue

    let lastArrow = null
    for (let i = 1; i < points.length - 1; i++) {
      // Multi-scale detection: a sharp junction shows a big angle over a short
      // window, while a sweeping interchange ramp only reveals its heading
      // change over a wider window. Try increasingly wide windows and accept
      // the first one whose direction change qualifies.
      let turn = 0
      let detected = false
      for (const ld of lookDistances) {
        const prev = pointBefore(points, i, ld)
        const next = pointAfter(points, i, ld)
        if (!prev || !next) continue
        const t = signedTurnDegrees(prev.game, points[i].game, next.game)
        const absT = Math.abs(t)
        if (absT >= minTurnDegrees && absT <= maxTurnDegrees) {
          turn = t
          detected = true
          break
        }
      }
      if (!detected) continue
      if (lastArrow && gameDistance(lastArrow.game, points[i].game) < minSpacing) continue

      // Tip sits just past the apex; the body is a fixed-length segment ending
      // at the tip, so the tail stays short and close to the turn.
      const armEnd = pointAfter(points, i, exitArm) || points[Math.min(points.length - 1, i + 1)]
      const armStart = pointBefore(points, armEnd.index, bodyLength) || points[Math.max(0, i - 1)]
      const maneuverCoords = routeSlice(points, armStart.index, armEnd.index)
      if (maneuverCoords.length < 2) continue

      lastArrow = points[i]
      // Aim the arrowhead along the exit tangent (averaged over a short span so
      // it follows the road after the turn rather than the apex chord).
      const exitFrom = pointBefore(points, armEnd.index, 18) || points[Math.max(0, armEnd.index - 1)]
      features.push({
        type: 'Feature',
        geometry: { type: 'LineString', coordinates: maneuverCoords },
        properties: {
          featureType: 'route_turn_line',
          maneuver: turn > 0 ? 'left' : 'right',
          turn: Math.round(turn),
          legIndex,
        },
      })
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: armEnd.coord },
        properties: {
          featureType: 'route_maneuver',
          maneuver: turn > 0 ? 'left' : 'right',
          turn: Math.round(turn),
          bearing: mapBearingDegrees(exitFrom.coord, armEnd.coord),
          legIndex,
        },
      })
    }
  }

  return features
}

function routePoints(coords, tileMapInfo) {
  const points = []
  let last = null
  for (const coord of coords) {
    if (!Array.isArray(coord) || coord.length < 2) continue
    const game = mapToGameCoords(coord[0], coord[1], tileMapInfo)
    if (!Number.isFinite(game[0]) || !Number.isFinite(game[1])) continue
    if (last && gameDistance(last.game, game) < 1) continue
    const point = { coord, game, index: points.length }
    points.push(point)
    last = point
  }
  return points
}

function routeSlice(points, startIndex, endIndex) {
  const start = Math.max(0, Math.min(startIndex, endIndex))
  const end = Math.min(points.length - 1, Math.max(startIndex, endIndex))
  return points.slice(start, end + 1).map(point => point.coord)
}

function pointBefore(points, index, minDistance) {
  let dist = 0
  for (let i = index; i > 0; i--) {
    dist += gameDistance(points[i].game, points[i - 1].game)
    if (dist >= minDistance) return points[i - 1]
  }
  return null
}

function pointAfter(points, index, minDistance) {
  let dist = 0
  for (let i = index; i < points.length - 1; i++) {
    dist += gameDistance(points[i].game, points[i + 1].game)
    if (dist >= minDistance) return points[i + 1]
  }
  return null
}

function gameDistance(a, b) {
  return Math.hypot(b[0] - a[0], b[1] - a[1])
}

function signedTurnDegrees(prev, at, next) {
  const ax = at[0] - prev[0]
  const az = at[1] - prev[1]
  const bx = next[0] - at[0]
  const bz = next[1] - at[1]
  const aLen = Math.hypot(ax, az)
  const bLen = Math.hypot(bx, bz)
  if (aLen < 1 || bLen < 1) return 0
  const cross = ax * bz - az * bx
  const dot = ax * bx + az * bz
  return Math.atan2(cross, dot) * 180 / Math.PI
}

function mapBearingDegrees(a, b) {
  const dLon = b[0] - a[0]
  const dLat = b[1] - a[1]
  return (Math.atan2(dLon, dLat) * 180 / Math.PI + 360) % 360
}
