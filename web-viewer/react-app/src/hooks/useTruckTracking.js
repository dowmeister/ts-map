import { useEffect, useRef, useState } from 'react'
import { gameToMapCoords, mapToGameCoords } from '../utils/coordinates'

const TRUCK_UPDATE_INTERVAL = 3000
const MIN_ZOOM_FOR_TRUCKS = 7

export function useTruckTracking(map, game, zoom, tileMapInfo) {
  const [trucks, setTrucks] = useState({
    type: 'FeatureCollection',
    features: []
  })
  const intervalRef = useRef(null)

  useEffect(() => {
    if (!map || !tileMapInfo) return

    const shouldTrack = zoom > MIN_ZOOM_FOR_TRUCKS

    if (shouldTrack && !intervalRef.current) {
      updateTrucks()
      intervalRef.current = setInterval(updateTrucks, TRUCK_UPDATE_INTERVAL)
    } else if (!shouldTrack && intervalRef.current) {
      clearInterval(intervalRef.current)
      intervalRef.current = null
      setTrucks({
        type: 'FeatureCollection',
        features: []
      })
    }

    async function updateTrucks() {
      try {
        const bounds = map.getBounds()
        const [x1, y1] = mapToGameCoords(bounds.getWest(), bounds.getSouth(), tileMapInfo)
        const [x2, y2] = mapToGameCoords(bounds.getEast(), bounds.getNorth(), tileMapInfo)

        let serverId
        if (game === 'ats') {
          serverId = 10
        } else if (game === 'promods') {
          serverId = 50
        } else {
          serverId = 2
        }

        const url = `https://tracker.ets2map.com/v3/area?x1=${Math.floor(x1)}&y1=${Math.floor(y1)}&x2=${Math.ceil(x2)}&y2=${Math.ceil(y2)}&server=${serverId}`

        const response = await fetch(url)
        if (!response.ok) return

        const data = await response.json()

        const features = data.Data.map(truck => {
          const [lon, lat] = gameToMapCoords(truck.X, truck.Y, tileMapInfo)

          return {
            type: 'Feature',
            geometry: {
              type: 'Point',
              coordinates: [lon, lat]
            },
            properties: {
              name: truck.Name,
              mpId: truck.MpId,
              playerId: truck.PlayerId,
              vtcId: truck.VtcId
            }
          }
        })

        setTrucks({
          type: 'FeatureCollection',
          features: features
        })
      } catch (error) {
        console.error('Error updating trucks:', error)
      }
    }

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current)
        intervalRef.current = null
      }
    }
  }, [map, game, zoom, tileMapInfo])

  return trucks
}
