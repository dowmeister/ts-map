const EARTH_RADIUS_METERS = 6370997.0
const LENGTH_OF_DEGREE = EARTH_RADIUS_METERS * Math.PI / 180.0

function projectionValues(tileMapInfo) {
  const p = tileMapInfo?.projection
  if (!p) return null

  const [originLat, originLon] = p.map_origin
  const [offsetX, offsetZ] = p.map_offset
  const [factorZ, factorX] = p.map_factor

  return {
    type: p.type,
    standardParallel1: p.standard_parallel_1,
    standardParallel2: p.standard_parallel_2,
    originLat,
    originLon,
    offsetX,
    offsetZ,
    factorZ,
    factorX,
    useEts2UkScale: !!p.use_ets2_uk_scale,
  }
}

function tanHalf(phi) {
  return Math.tan(Math.PI / 4 + phi / 2)
}

function lccParams(p) {
  const phi1 = p.standardParallel1 * Math.PI / 180
  const phi2 = p.standardParallel2 * Math.PI / 180
  const phi0 = p.originLat * Math.PI / 180
  const lambda0 = p.originLon * Math.PI / 180
  const n = Math.log(Math.cos(phi1) / Math.cos(phi2)) / Math.log(tanHalf(phi2) / tanHalf(phi1))
  const f = Math.cos(phi1) * Math.pow(tanHalf(phi1), n) / n
  const rho0 = EARTH_RADIUS_METERS * f / Math.pow(tanHalf(phi0), n)
  return { n, f, rho0, lambda0 }
}

function legacyGameToMapCoords(gameX, gameZ, tileMapInfo) {
  const minX = tileMapInfo.x1
  const maxX = tileMapInfo.x2
  const minZ = tileMapInfo.y1
  const maxZ = tileMapInfo.y2
  const width = maxX - minX
  const height = maxZ - minZ
  const normalizedX = (gameX - minX) / width
  const normalizedZ = (gameZ - minZ) / height
  const maxExtent = 70.0
  const aspectRatio = width / height
  const lonRange = aspectRatio > 1.0 ? maxExtent : maxExtent * aspectRatio
  const latRange = aspectRatio > 1.0 ? maxExtent / aspectRatio : maxExtent
  return [
    normalizedX * lonRange - lonRange / 2.0,
    latRange / 2.0 - normalizedZ * latRange,
  ]
}

function legacyMapToGameCoords(lon, lat, tileMapInfo) {
  const minX = tileMapInfo.x1
  const maxX = tileMapInfo.x2
  const minZ = tileMapInfo.y1
  const maxZ = tileMapInfo.y2
  const width = maxX - minX
  const height = maxZ - minZ
  const maxExtent = 70.0
  const aspectRatio = width / height
  const lonRange = aspectRatio > 1.0 ? maxExtent : maxExtent * aspectRatio
  const latRange = aspectRatio > 1.0 ? maxExtent / aspectRatio : maxExtent
  const normalizedX = (lon + lonRange / 2.0) / lonRange
  const normalizedZ = (latRange / 2.0 - lat) / latRange
  return [minX + normalizedX * width, minZ + normalizedZ * height]
}

export function gameToMapCoords(gameX, gameZ, tileMapInfo) {
  if (!tileMapInfo) return [0, 0]

  const p = projectionValues(tileMapInfo)
  if (!p) return legacyGameToMapCoords(gameX, gameZ, tileMapInfo)

  if (p.type !== 'lambert_conic') {
    return [
      p.originLon + (gameX - p.offsetZ) * p.factorX,
      p.originLat + (gameZ - p.offsetX) * p.factorZ,
    ]
  }

  let x = gameX - p.offsetX
  let z = gameZ - p.offsetZ

  if (p.useEts2UkScale) {
    const ukScale = 0.75
    const calaisX = -31100.0
    const calaisZ = -5500.0
    if (x * ukScale < calaisX && z * ukScale < calaisZ) {
      x = (x + calaisX / 2) * ukScale
      z = (z + calaisZ / 2) * ukScale
    }
  }

  const lccX = x * p.factorX * LENGTH_OF_DEGREE
  const lccY = z * p.factorZ * LENGTH_OF_DEGREE
  const { n, f, rho0, lambda0 } = lccParams(p)
  let rho = Math.sqrt(lccX * lccX + (rho0 - lccY) * (rho0 - lccY))
  if (n < 0) rho = -rho
  const theta = Math.atan2(lccX, rho0 - lccY)
  const lat = 2 * Math.atan(Math.pow(EARTH_RADIUS_METERS * f / rho, 1 / n)) - Math.PI / 2
  const lon = lambda0 + theta / n
  return [lon * 180 / Math.PI, lat * 180 / Math.PI]
}

export function mapToGameCoords(lon, lat, tileMapInfo) {
  if (!tileMapInfo) return [0, 0]

  const p = projectionValues(tileMapInfo)
  if (!p) return legacyMapToGameCoords(lon, lat, tileMapInfo)

  if (p.type !== 'lambert_conic') {
    return [
      (lon - p.originLon) / p.factorX + p.offsetZ,
      (lat - p.originLat) / p.factorZ + p.offsetX,
    ]
  }

  const { n, f, rho0, lambda0 } = lccParams(p)
  const phi = lat * Math.PI / 180
  const lambda = lon * Math.PI / 180
  const rho = EARTH_RADIUS_METERS * f / Math.pow(tanHalf(phi), n)
  const theta = n * (lambda - lambda0)
  const lccX = rho * Math.sin(theta)
  const lccY = rho0 - rho * Math.cos(theta)

  let x = lccX / p.factorX / LENGTH_OF_DEGREE
  let z = lccY / p.factorZ / LENGTH_OF_DEGREE

  if (p.useEts2UkScale) {
    const ukScale = 0.75
    const calaisX = -31100.0
    const calaisZ = -5500.0
    if (x < calaisX * (1 + ukScale / 2) && z < calaisZ * (1 + ukScale / 2)) {
      x = x / ukScale - calaisX / 2
      z = z / ukScale - calaisZ / 2
    }
  }

  return [x + p.offsetX, z + p.offsetZ]
}
