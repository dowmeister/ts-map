import { useState, useEffect } from 'react'
import { useRoute } from '../hooks/useRoute'
import { useCompanies } from '../hooks/useCompanies'
import { mapToGameCoords, gameToMapCoords } from '../utils/coordinates'
import './RoutePlanner.css'

// ── Turn-by-turn formatting ──────────────────────────────────────────────────
const MANEUVER_ICON = {
  depart: '●',
  via: '◉',
  arrive: '⚑',
  ferry: '⛴',
  'ferry-exit': '⚓',
  exit: '⤴',
  merge: '⤚',
  'turn-left': '↰',
  'turn-right': '↱',
  'slight-left': '↖',
  'slight-right': '↗',
  'sharp-left': '⬅',
  'sharp-right': '➡',
  uturn: '↶',
  straight: '↑',
}

function maneuverIcon(m) {
  if (m.type === 'turn' || m.type === 'exit' || m.type === 'merge') {
    if (m.modifier && MANEUVER_ICON[m.modifier]) {
      if (m.modifier === 'left' || m.modifier === 'right') return MANEUVER_ICON[`turn-${m.modifier}`]
      return MANEUVER_ICON[m.modifier]
    }
  }
  return MANEUVER_ICON[m.type] || '•'
}

function sideLabel(modifier) {
  switch (modifier) {
    case 'slight-left':  return 'leggermente a sinistra'
    case 'slight-right': return 'leggermente a destra'
    case 'sharp-left':   return 'secca a sinistra'
    case 'sharp-right':  return 'secca a destra'
    case 'left':         return 'a sinistra'
    case 'right':        return 'a destra'
    case 'uturn':        return 'inverti il senso'
    default:             return ''
  }
}

function maneuverLabel(m) {
  switch (m.type) {
    case 'depart':     return 'Parti'
    case 'arrive':     return 'Arrivo a destinazione'
    case 'via':        return 'Tappa intermedia'
    case 'ferry':      return 'Imbarco sul traghetto'
    case 'ferry-exit': return 'Sbarca dal traghetto'
    case 'merge':      return 'Immettiti in autostrada'
    case 'exit':       return `Prendi l'uscita ${sideLabel(m.modifier)}`.trim()
    case 'turn':
      if (m.modifier === 'uturn') return 'Inverti il senso di marcia'
      if (m.modifier === 'slight-left' || m.modifier === 'slight-right') return `Tieni ${sideLabel(m.modifier)}`
      return `Svolta ${sideLabel(m.modifier)}`.trim()
    default:           return 'Prosegui'
  }
}

function formatDistance(meters) {
  if (!meters || meters < 0) return ''
  if (meters >= 1000) return `${(meters / 1000).toFixed(meters >= 10000 ? 0 : 1)} km`
  return `${Math.round(meters / 10) * 10} m`
}

function ManeuverList({ maneuvers, onSelect }) {
  if (!maneuvers || maneuvers.length === 0) return null
  return (
    <ol className="rp-steps">
      {maneuvers.map((m, i) => (
        <li
          key={i}
          className={`rp-step rp-step--${m.type}`}
          onClick={() => onSelect?.(m)}
          title="Vai alla manovra"
        >
          <span className="rp-step-icon">{maneuverIcon(m)}</span>
          <span className="rp-step-text">{maneuverLabel(m)}</span>
          {i > 0 && m.type !== 'depart' && (
            <span className="rp-step-dist">{formatDistance(m.distanceFromPrevM)}</span>
          )}
        </li>
      ))}
    </ol>
  )
}

function WaypointDropdown({ cities, companies, value, onChange, placeholder }) {
  const val = value ? JSON.stringify({ x: value.x, z: value.z, name: value.name }) : ''

  const getCityName = (c) => c.LocalizedNames?.en_gb || c.Name
  const sortedCities = [...cities].sort((a, b) => getCityName(a).localeCompare(getCityName(b)))

  const byCity = {}
  companies.forEach(c => {
    const key = c.city || '—'
    if (!byCity[key]) byCity[key] = []
    byCity[key].push(c)
  })
  const sortedCityKeys = Object.keys(byCity).sort((a, b) => a.localeCompare(b))

  // If current value doesn't match any city or company (e.g. right-click coordinate),
  // add it as a standalone option so it remains visible in the dropdown
  const isKnown = !value || sortedCities.some(c => c.X === value.x && c.Y === value.z) ||
                  companies.some(c => c.x === value.x && c.z === value.z)

  return (
    <select
      className="rp-select"
      value={val}
      onChange={e => {
        if (!e.target.value) { onChange(null); return }
        onChange(JSON.parse(e.target.value))
      }}
    >
      <option value="">{placeholder}</option>
      {value && !isKnown && (
        <option value={val}>{value.name || `${value.x}, ${value.z}`}</option>
      )}
      {sortedCities.length > 0 && (
        <optgroup label="── Cities ──">
          {sortedCities.map((c, i) => (
            <option key={i} value={JSON.stringify({ x: c.X, z: c.Y, name: getCityName(c) })}>
              {getCityName(c)}
            </option>
          ))}
        </optgroup>
      )}
      {sortedCityKeys.map(cityKey => (
        <optgroup key={cityKey} label={cityKey}>
          {byCity[cityKey]
            .sort((a, b) => a.name.localeCompare(b.name))
            .map((c, i) => (
              <option key={i} value={JSON.stringify({ x: c.x, z: c.z, name: c.name })}>
                {c.name}
              </option>
            ))}
        </optgroup>
      ))}
    </select>
  )
}

function RoutePlanner({ mapInstance, tileMapInfo, cities }) {
  const [open, setOpen] = useState(false)
  const game = tileMapInfo?.game || 'ets2'
  const companies = useCompanies(game)

  const {
    from, setFrom,
    to, setTo,
    stops, addStop, addStopWithPoint, removeStop, updateStopPoint, updateStopType, addWaypoint,
    options, setOptions,
    routeInfo, loading, error,
    calculateRoute, clearRoute,
  } = useRoute(mapInstance, tileMapInfo)

  const flyTo = (wp) => {
    if (!wp || !tileMapInfo || !mapInstance) return
    const [lon, lat] = gameToMapCoords(wp.x, wp.z, tileMapInfo)
    mapInstance.flyTo({ center: [lon, lat], zoom: Math.max(mapInstance.getZoom(), 9), duration: 800 })
  }

  // Map click handlers — active when panel is open
  useEffect(() => {
    if (!mapInstance || !tileMapInfo || !open) return

    const handleCityClick = (e) => {
      const f = e.features?.[0]
      if (!f) return
      const [lon, lat] = [e.lngLat.lng, e.lngLat.lat]
      const [x, z] = mapToGameCoords(lon, lat, tileMapInfo)
      addWaypoint({ name: f.properties.localized_name || f.properties.name, x: Math.round(x), z: Math.round(z) })
    }

    const handleCompanyClick = (e) => {
      const f = e.features?.[0]
      if (!f) return
      const [lon, lat] = [e.lngLat.lng, e.lngLat.lat]
      const [x, z] = mapToGameCoords(lon, lat, tileMapInfo)
      // image = "company:company_polar_fish" → strip prefix to get id
      const id = (f.properties.image || '').replace(/^company:company_/, '')
      const match = companies.find(c => c.id === id)
      const name = match ? (match.city ? `${match.name} (${match.city})` : match.name) : id
      addWaypoint({ name, x: Math.round(x), z: Math.round(z) })
    }

    const setCursor = () => { mapInstance.getCanvas().style.cursor = 'crosshair' }
    const resetCursor = () => { mapInstance.getCanvas().style.cursor = '' }

    mapInstance.on('click', 'city-labels', handleCityClick)
    mapInstance.on('click', 'overlays-companies', handleCompanyClick)
    mapInstance.on('mouseenter', 'city-labels', setCursor)
    mapInstance.on('mouseleave', 'city-labels', resetCursor)
    mapInstance.on('mouseenter', 'overlays-companies', setCursor)
    mapInstance.on('mouseleave', 'overlays-companies', resetCursor)

    return () => {
      mapInstance.off('click', 'city-labels', handleCityClick)
      mapInstance.off('click', 'overlays-companies', handleCompanyClick)
      mapInstance.off('mouseenter', 'city-labels', setCursor)
      mapInstance.off('mouseleave', 'city-labels', resetCursor)
      mapInstance.off('mouseenter', 'overlays-companies', setCursor)
      mapInstance.off('mouseleave', 'overlays-companies', resetCursor)
    }
  }, [mapInstance, tileMapInfo, open, addWaypoint, companies])

  // Right-click anywhere on map → add via waypoint
  useEffect(() => {
    if (!mapInstance || !tileMapInfo || !open) return
    const handleContextMenu = (e) => {
      e.originalEvent?.preventDefault()
      const [x, z] = mapToGameCoords(e.lngLat.lng, e.lngLat.lat, tileMapInfo)
      addStopWithPoint({ name: `${Math.round(x)}, ${Math.round(z)}`, x: Math.round(x), z: Math.round(z) })
    }
    mapInstance.on('contextmenu', handleContextMenu)
    return () => mapInstance.off('contextmenu', handleContextMenu)
  }, [mapInstance, tileMapInfo, open, addStopWithPoint])

  // Left-click on route marker (circle or label) → remove it
  useEffect(() => {
    if (!mapInstance || !open) return
    const handleMarkerClick = (e) => {
      // Query both circle and label layers at the click point
      const features = mapInstance.queryRenderedFeatures(e.point, {
        layers: ['route-markers', 'route-marker-labels'],
      })
      const f = features[0]
      if (!f) return
      e.stopPropagation?.()
      const { markerType, wpX, wpZ } = f.properties
      if (markerType === 'start') { setFrom(null); return }
      if (markerType === 'end')   { setTo(null);   return }
      const idx = stops.findIndex(s => s.point?.x === wpX && s.point?.z === wpZ)
      if (idx >= 0) removeStop(idx)
    }
    const setPointer   = () => { mapInstance.getCanvas().style.cursor = 'pointer' }
    const resetPointer = () => { mapInstance.getCanvas().style.cursor = '' }
    mapInstance.on('click',      handleMarkerClick)
    mapInstance.on('mouseenter', 'route-markers',       setPointer)
    mapInstance.on('mouseleave', 'route-markers',       resetPointer)
    mapInstance.on('mouseenter', 'route-marker-labels', setPointer)
    mapInstance.on('mouseleave', 'route-marker-labels', resetPointer)
    return () => {
      mapInstance.off('click',      handleMarkerClick)
      mapInstance.off('mouseenter', 'route-markers',       setPointer)
      mapInstance.off('mouseleave', 'route-markers',       resetPointer)
      mapInstance.off('mouseenter', 'route-marker-labels', setPointer)
      mapInstance.off('mouseleave', 'route-marker-labels', resetPointer)
    }
  }, [mapInstance, open, stops, setFrom, setTo, removeStop])

  const toggleOption = (key) => setOptions(prev => ({ ...prev, [key]: !prev[key] }))
  const setMode = (m) => setOptions(prev => ({ ...prev, mode: m }))

  return (
    <div className="rp-container">
      <button
        className={`rp-toggle ${open ? 'rp-toggle--open' : ''}`}
        onClick={() => setOpen(v => !v)}
      >
        Route
      </button>

      {open && (
        <div className="rp-panel">
          {/* From */}
          <div className="rp-row">
            <span className="rp-label rp-label--from">A</span>
            <WaypointDropdown cities={cities} companies={companies}
              value={from} onChange={setFrom} placeholder="From..." />
            <button className="rp-btn-locate" onClick={() => flyTo(from)} disabled={!from} title="Go to">◎</button>
          </div>

          {/* Intermediate stops */}
          {stops.map((stop, idx) => (
            <div key={idx} className="rp-row">
              <span className={`rp-label ${stop.type === 'avoid' ? 'rp-label--avoid' : 'rp-label--stop'}`}>
                {stop.type === 'avoid' ? '✕' : idx + 1}
              </span>
              <WaypointDropdown cities={cities} companies={companies}
                value={stop.point} onChange={pt => updateStopPoint(idx, pt)}
                placeholder={stop.type === 'avoid' ? 'Avoid...' : 'Via...'} />
              <button className="rp-btn-locate" onClick={() => flyTo(stop.point)} disabled={!stop.point} title="Go to">◎</button>
              <button
                className={`rp-btn-type ${stop.type === 'avoid' ? 'rp-btn-type--avoid' : 'rp-btn-type--via'}`}
                onClick={() => updateStopType(idx, stop.type === 'via' ? 'avoid' : 'via')}
                title={stop.type === 'via' ? 'Switch to avoid' : 'Switch to via'}
              >
                {stop.type === 'via' ? 'Via' : 'Avoid'}
              </button>
              <button className="rp-btn-remove" onClick={() => removeStop(idx)} title="Remove">✕</button>
            </div>
          ))}

          {/* To */}
          <div className="rp-row">
            <span className="rp-label rp-label--to">B</span>
            <WaypointDropdown cities={cities} companies={companies}
              value={to} onChange={setTo} placeholder="To..." />
            <button className="rp-btn-locate" onClick={() => flyTo(to)} disabled={!to} title="Go to">◎</button>
          </div>

          <button className="rp-btn-add-stop" onClick={addStop}>+ Add stop</button>
          <div className="rp-hint">Right-click on map to add waypoint</div>

          <div className="rp-mode-toggle">
            <button className={`rp-mode-btn ${options.mode === 'fastest' ? 'rp-mode-btn--active' : ''}`}
              onClick={() => setMode('fastest')}>⚡ Fastest</button>
            <button className={`rp-mode-btn ${options.mode === 'shortest' ? 'rp-mode-btn--active' : ''}`}
              onClick={() => setMode('shortest')}>📏 Shortest</button>
          </div>

          <div className="rp-options">
            <label className="rp-option">
              <input type="checkbox" checked={options.avoidHighways} onChange={() => toggleOption('avoidHighways')} />
              Avoid highways
            </label>
            <label className="rp-option">
              <input type="checkbox" checked={options.avoidFerries} onChange={() => toggleOption('avoidFerries')} />
              Avoid ferries
            </label>
          </div>

          <div className="rp-actions">
            <button className="rp-btn-go" onClick={calculateRoute} disabled={!from || !to || loading}>
              {loading ? 'Calculating...' : 'Calculate route'}
            </button>
            <button className="rp-btn-clear" onClick={clearRoute}>Clear</button>
          </div>

          {error && <div className="rp-error">{error}</div>}

          {routeInfo && !error && (
            <div className="rp-result">
              {routeInfo.legs > 1 && <div className="rp-result-legs">{routeInfo.legs} legs</div>}
              <div className="rp-result-total">
                {routeInfo.totalLengthKm.toLocaleString()} km
                <span className="rp-result-note"> in-game</span>
              </div>
              {routeInfo.ferryLengthKm > 0 && (
                <div className="rp-result-breakdown">
                  <span className="rp-result-land">🛣 {routeInfo.landLengthKm.toLocaleString()} km</span>
                  <span className="rp-result-sep"> · </span>
                  <span className="rp-result-ferry">⛴ {routeInfo.ferryLengthKm.toLocaleString()} km</span>
                </div>
              )}
              <ManeuverList maneuvers={routeInfo.maneuvers} onSelect={flyTo} />
            </div>
          )}
        </div>
      )}
    </div>
  )
}

export default RoutePlanner
