import { useState, useEffect } from 'react'
import MapViewer from './components/MapViewer'
import GameSwitcher from './components/GameSwitcher'
import CitySelector from './components/CitySelector'
import CoordinateInfo from './components/CoordinateInfo'
import { useTileMapInfo } from './hooks/useTileMapInfo'
import { useCities } from './hooks/useCities'
import { useTruckTracking } from './hooks/useTruckTracking'
import './App.css'

function App() {
  const [currentGame, setCurrentGame] = useState(() => {
    const urlParams = new URLSearchParams(window.location.search)
    return urlParams.get('game') || 'ets2'
  })
  const [mapInstance, setMapInstance] = useState(null)
  const [zoom, setZoom] = useState(4)
  const [bounds, setBounds] = useState(null)

  const tileMapInfo = useTileMapInfo(currentGame)
  const cities = useCities(currentGame)
  const trucks = useTruckTracking(mapInstance, currentGame, zoom, tileMapInfo)

  useEffect(() => {
    const url = new URL(window.location)
    url.searchParams.set('game', currentGame)
    window.history.replaceState({}, '', url)
  }, [currentGame])

  return (
    <div className="app">
      <GameSwitcher currentGame={currentGame} onGameChange={setCurrentGame} />
      <CitySelector 
        cities={cities} 
        mapInstance={mapInstance}
        tileMapInfo={tileMapInfo}
      />
      <CoordinateInfo 
        zoom={zoom} 
        bounds={bounds} 
        tileMapInfo={tileMapInfo}
      />
      <MapViewer 
        game={currentGame}
        onMapLoad={setMapInstance}
        onZoomChange={setZoom}
        onBoundsChange={setBounds}
        trucks={trucks}
      />
    </div>
  )
}

export default App
