import { useState, useEffect, useCallback, useRef } from 'react'
import MapViewer from './components/MapViewer'
import GameSwitcher from './components/GameSwitcher'
import CitySelector from './components/CitySelector'
import CoordinateInfo from './components/CoordinateInfo'
import DebugToggle from './components/DebugToggle'
import LayerToggle from './components/LayerToggle'
import PolygonTool from './components/PolygonTool'
import RoutePlanner from './components/RoutePlanner'
import ContextMenu from './components/ContextMenu'
import { useTileMapInfo } from './hooks/useTileMapInfo'
import { useCities } from './hooks/useCities'
import { useDebugOverlay } from './hooks/useDebugOverlay'
import './App.css'

const VALID_GAMES = ['ets2', 'ats', 'promods', 'gu']

function getGameFromPath() {
  const parts = window.location.pathname.split('/').filter(Boolean)
  return VALID_GAMES.includes(parts[0]) ? parts[0] : 'ets2'
}

// Hash format: #zoom/lat/lon  (same convention as OpenStreetMap)
function getPositionFromHash() {
  const hash = window.location.hash.slice(1)
  const parts = hash.split('/')
  if (parts.length === 3) {
    const zoom = parseFloat(parts[0])
    const lat = parseFloat(parts[1])
    const lon = parseFloat(parts[2])
    if (!isNaN(zoom) && !isNaN(lat) && !isNaN(lon)) {
      return { zoom, center: [lon, lat] }
    }
  }
  return null
}

function buildUrl(game, hash) {
  return '/' + game + (hash || '')
}

function App() {
  const [currentGame, setCurrentGame] = useState(getGameFromPath)
  const [initialPosition] = useState(getPositionFromHash)
  const [mapInstance, setMapInstance] = useState(null)
  const [zoom, setZoom] = useState(4)
  const [bounds, setBounds] = useState(null)
  const [debugEnabled, setDebugEnabled] = useState(false)
  const [debugIssuesEnabled, setDebugIssuesEnabled] = useState(false)
  const [debugSoftIssuesEnabled, setDebugSoftIssuesEnabled] = useState(false)

  const routeActionsRef = useRef({})

  const tileMapInfo = useTileMapInfo(currentGame)
  const cities = useCities(currentGame)

  useDebugOverlay(mapInstance, tileMapInfo, debugEnabled, debugIssuesEnabled, debugSoftIssuesEnabled)

  // Sync game to path, clear hash so position resets
  const handleGameChange = useCallback((newGame) => {
    setCurrentGame(newGame)
    window.history.pushState({}, '', buildUrl(newGame, ''))
  }, [])

  // Update hash on map moveend
  const handlePositionChange = useCallback((newZoom, center) => {
    const hash = `#${newZoom.toFixed(2)}/${center.lat.toFixed(5)}/${center.lng.toFixed(5)}`
    window.history.replaceState({}, '', buildUrl(currentGame, hash))
  }, [currentGame])

  // Handle browser back/forward
  useEffect(() => {
    const onPopState = () => setCurrentGame(getGameFromPath())
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  // On first load, if path has no valid game segment, redirect to /ets2
  useEffect(() => {
    const parts = window.location.pathname.split('/').filter(Boolean)
    if (!VALID_GAMES.includes(parts[0])) {
      window.history.replaceState({}, '', buildUrl('ets2', window.location.hash))
    }
  }, [])

  return (
    <div className="app">
      <GameSwitcher currentGame={currentGame} onGameChange={handleGameChange} />
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
      <LayerToggle mapInstance={mapInstance} />
      <DebugToggle
        graphEnabled={debugEnabled}
        issuesEnabled={debugIssuesEnabled}
        softIssuesEnabled={debugSoftIssuesEnabled}
        onGraphToggle={setDebugEnabled}
        onIssuesToggle={setDebugIssuesEnabled}
        onSoftIssuesToggle={setDebugSoftIssuesEnabled}
      />
      <PolygonTool
        mapInstance={mapInstance}
        tileMapInfo={tileMapInfo}
      />
      <RoutePlanner
        mapInstance={mapInstance}
        tileMapInfo={tileMapInfo}
        cities={cities}
        routeActionsRef={routeActionsRef}
      />
      <ContextMenu
        mapInstance={mapInstance}
        tileMapInfo={tileMapInfo}
        routeActionsRef={routeActionsRef}
      />
      <MapViewer
        game={currentGame}
        initialPosition={initialPosition}
        onMapLoad={setMapInstance}
        onZoomChange={setZoom}
        onBoundsChange={setBounds}
        onPositionChange={handlePositionChange}
      />
    </div>
  )
}

export default App
