import { useState, useEffect } from 'react'

export function useTileMapInfo(game) {
  const [tileMapInfo, setTileMapInfo] = useState(null)

  useEffect(() => {
    async function loadTileMapInfo() {
      try {
        const baseUrl = import.meta.env.VITE_OVERLAY_IMAGES_BASE_URL || '/map_data'
        const response = await fetch(`${baseUrl}/${game}/VectorTileMapInfo.json`)
        const data = await response.json()
        setTileMapInfo(data)
      } catch (error) {
        console.error('Error loading VectorTileMapInfo:', error)
      }
    }

    loadTileMapInfo()
  }, [game])

  return tileMapInfo
}
