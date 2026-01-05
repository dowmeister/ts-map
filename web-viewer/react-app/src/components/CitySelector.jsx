import { gameToMapCoords } from '../utils/coordinates'
import './CitySelector.css'

function CitySelector({ cities, mapInstance, tileMapInfo }) {
  const handleChange = (e) => {
    if (!e.target.value || !mapInstance || !tileMapInfo) return
    
    const coords = JSON.parse(e.target.value)
    const [lon, lat] = gameToMapCoords(coords.x, coords.z, tileMapInfo)
    mapInstance.flyTo({ center: [lon, lat], zoom: 8 })
  }

  return (
    <div className="city-selector">
      <label htmlFor="city-select">Go to City:</label>
      <select id="city-select" onChange={handleChange} defaultValue="">
        <option value="">Select a city...</option>
        {cities.map((city, index) => (
          <option 
            key={index} 
            value={JSON.stringify({ x: city.X, z: city.Y })}
          >
            {city.Name}
          </option>
        ))}
      </select>
    </div>
  )
}

export default CitySelector
