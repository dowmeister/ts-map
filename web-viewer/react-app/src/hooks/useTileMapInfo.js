import { useState, useEffect } from 'react'

export function useTileMapInfo(game) {
  const [tileMapInfo, setTileMapInfo] = useState(null)

  useEffect(() => {
    async function loadTileMapInfo() {
      try {
        const baseUrl = import.meta.env.VITE_MAP_DATA_URL || 'http://localhost:8888'
        const response = await fetch(`${baseUrl}/${game}/VectorTileMapInfo.json`)
        const data = await response.json()
        setTileMapInfo({ ...data, game })
      } catch (error) {
        console.error('Error loading VectorTileMapInfo:', error)
      }
    }

    loadTileMapInfo()
  }, [game])

  return tileMapInfo
}
