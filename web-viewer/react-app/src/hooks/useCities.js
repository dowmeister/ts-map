import { useState, useEffect } from 'react'

export function useCities(game) {
  const [cities, setCities] = useState([])

  useEffect(() => {
    async function loadCities() {
      try {
        const baseUrl = import.meta.env.VITE_OVERLAY_IMAGES_BASE_URL || '/map_data'
        const response = await fetch(`${baseUrl}/${game}/Cities.json`)
        if (!response.ok) return
        
        const data = await response.json()
        const validCities = data.filter(city => 
          city && city.Name && city.X !== undefined && city.Y !== undefined
        )
        validCities.sort((a, b) => {
          const nameA = a.LocalizedNames?.en_gb || a.Name || ''
          const nameB = b.LocalizedNames?.en_gb || b.Name || ''
          return nameA.localeCompare(nameB)
        })
        setCities(validCities)
      } catch (error) {
        console.error('Error loading cities:', error)
      }
    }

    loadCities()
  }, [game])

  return cities
}
