export function gameToMapCoords(gameX, gameZ, tileMapInfo) {
  if (!tileMapInfo) return [0, 0]
  
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

  let lonRange, latRange
  if (aspectRatio > 1.0) {
    lonRange = maxExtent
    latRange = maxExtent / aspectRatio
  } else {
    latRange = maxExtent
    lonRange = maxExtent * aspectRatio
  }

  const lon = (normalizedX * lonRange) - (lonRange / 2.0)
  const lat = (latRange / 2.0) - (normalizedZ * latRange)

  return [lon, lat]
}

export function mapToGameCoords(lon, lat, tileMapInfo) {
  if (!tileMapInfo) return [0, 0]
  
  const minX = tileMapInfo.x1
  const maxX = tileMapInfo.x2
  const minZ = tileMapInfo.y1
  const maxZ = tileMapInfo.y2

  const width = maxX - minX
  const height = maxZ - minZ

  const maxExtent = 70.0
  const aspectRatio = width / height

  let lonRange, latRange
  if (aspectRatio > 1.0) {
    lonRange = maxExtent
    latRange = maxExtent / aspectRatio
  } else {
    latRange = maxExtent
    lonRange = maxExtent * aspectRatio
  }

  const normalizedX = (lon + (lonRange / 2.0)) / lonRange
  const normalizedZ = ((latRange / 2.0) - lat) / latRange

  const gameX = minX + normalizedX * width
  const gameZ = minZ + normalizedZ * height

  return [gameX, gameZ]
}
