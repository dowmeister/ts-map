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
    // Window (game units) on each side of a vertex used to measure how hard the
    // route turns *there*. Short window => only genuine junction turns register,
    // gentle road/highway curvature stays near 0.
    senseWindow = 24,
    // A vertex is a maneuver when the local turn is at least this many degrees.
    minTurnDegrees = 32,
    // Reject near-reversals (loop/smoothing artefacts), not real maneuvers.
    maxTurnDegrees = 172,
    // Minimum arc-length gap between two kept maneuvers.
    minSpacing = 95,
    // Arrow body geometry around the decision point.
    backArm = 15,
    frontArm = 32,
  } = options
  const features = []

  for (const [legIndex, leg] of legs.entries()) {
    const coords = leg.geometry?.coordinates || []
    const points = routePoints(coords, tileMapInfo)
    if (points.length < 5) continue

    // Cumulative arc length (game units) for spacing/peak suppression.
    const arc = new Array(points.length)
    arc[0] = 0
    for (let i = 1; i < points.length; i++) {
      arc[i] = arc[i - 1] + gameDistance(points[i - 1].game, points[i].game)
    }

    // 1) Local signed turn angle at every interior vertex.
    const candidates = []
    for (let i = 1; i < points.length - 1; i++) {
      const prev = pointBefore(points, i, senseWindow)
      const next = pointAfter(points, i, senseWindow)
      if (!prev || !next) continue
      const turn = signedTurnDegrees(prev.game, points[i].game, next.game)
      const absTurn = Math.abs(turn)
      if (absTurn < minTurnDegrees || absTurn > maxTurnDegrees) continue
      candidates.push({ index: i, turn, absTurn })
    }
    if (!candidates.length) continue

    // 2) Greedy non-maximum suppression: keep the sharpest vertex of each turn,
    //    then reject anything within minSpacing of an already-kept maneuver. This
    //    collapses the cluster of vertices that make up one junction into a
    //    single, well-placed arrow.
    candidates.sort((a, b) => b.absTurn - a.absTurn)
    const kept = []
    for (const c of candidates) {
      if (kept.some(k => Math.abs(arc[k.index] - arc[c.index]) < minSpacing)) continue
      kept.push(c)
    }
    kept.sort((a, b) => a.index - b.index)

    // 3) Emit a short bent arrow centred on each decision point: a stubby tail
    //    leading into the turn and the head just past it, aimed along the exit.
    for (const { index: i, turn } of kept) {
      const armEnd = pointAfter(points, i, frontArm) || points[Math.min(points.length - 1, i + 1)]
      const armStart = pointBefore(points, i, backArm) || points[Math.max(0, i - 1)]
      const maneuverCoords = routeSlice(points, armStart.index, armEnd.index)
      if (maneuverCoords.length < 2) continue

      // Exit tangent: bearing over the last short span of the body.
      const exitFrom = pointBefore(points, armEnd.index, 16) || points[Math.max(0, armEnd.index - 1)]
      const side = turn > 0 ? 'left' : 'right'

      features.push({
        type: 'Feature',
        geometry: { type: 'LineString', coordinates: maneuverCoords },
        properties: {
          featureType: 'route_turn_line',
          maneuver: side,
          turn: Math.round(turn),
          legIndex,
        },
      })
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: armEnd.coord },
        properties: {
          featureType: 'route_maneuver',
          maneuver: side,
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
