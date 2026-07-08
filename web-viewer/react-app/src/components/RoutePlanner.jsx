import { useState, useEffect, useMemo } from 'react'
import Select from 'react-select'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faArrowLeft,
  faArrowRight,
  faArrowUp,
  faArrowsLeftRight,
  faRotateLeft,
  faTurnDown,
  faTurnUp,
  faLocationDot,
  faFlagCheckered,
  faCircleDot,
  faShip,
  faAnchor,
  faRoad,
  faSignHanging,
} from '@fortawesome/free-solid-svg-icons'
import { useRoute } from '../hooks/useRoute'
import { useCompanies } from '../hooks/useCompanies'
import { mapToGameCoords, gameToMapCoords } from '../utils/coordinates'
import './RoutePlanner.css'

// ── Turn-by-turn formatting ──────────────────────────────────────────────────

// Maps modifier → { icon, transform } for turn maneuvers.
const TURN_ICON = {
  'slight-left':  { icon: faTurnUp,    flip: 'horizontal' },
  'slight-right': { icon: faTurnUp,    flip: undefined },
  'left':         { icon: faArrowLeft,  flip: undefined },
  'right':        { icon: faArrowRight, flip: undefined },
  'sharp-left':   { icon: faArrowLeft,  flip: undefined },
  'sharp-right':  { icon: faArrowRight, flip: undefined },
  'uturn':        { icon: faRotateLeft, flip: undefined },
}

function maneuverIcon(m) {
  if (m.type === 'turn') return TURN_ICON[m.modifier] ?? { icon: faArrowUp }
  if (m.type === 'exit')       return { icon: faSignHanging }
  if (m.type === 'merge')      return { icon: faRoad }
  if (m.type === 'ferry')      return { icon: faShip }
  if (m.type === 'ferry-exit') return { icon: faAnchor }
  if (m.type === 'depart')     return { icon: faLocationDot }
  if (m.type === 'arrive')     return { icon: faFlagCheckered }
  if (m.type === 'via')        return { icon: faCircleDot }
  return { icon: faArrowUp }
}

function maneuverLabel(m) {
  switch (m.type) {
    case 'depart':     return 'Depart'
    case 'arrive':     return 'Arrive at destination'
    case 'via':        return 'Via waypoint'
    case 'ferry':      return 'Board ferry'
    case 'ferry-exit': return 'Disembark ferry'
    case 'merge':      return 'Merge onto motorway'
    case 'exit': {
      const s = m.modifier?.includes('right') ? 'right' : m.modifier?.includes('left') ? 'left' : ''
      return s ? `Take exit on the ${s}` : 'Take exit'
    }
    case 'turn': {
      if (m.modifier === 'uturn') return 'Make a U-turn'
      const labels = {
        'slight-left':  'Bear left',
        'slight-right': 'Bear right',
        'left':         'Turn left',
        'right':        'Turn right',
        'sharp-left':   'Turn sharp left',
        'sharp-right':  'Turn sharp right',
      }
      return labels[m.modifier] ?? 'Continue'
    }
    default: return 'Continue'
  }
}

function formatDistance(meters) {
  if (!meters || meters < 0) return ''
  if (meters >= 1000) return `${(meters / 1000).toFixed(meters >= 10000 ? 0 : 1)} km`
  return `${Math.round(meters / 10) * 10} m`
}

// ── Lane diagram (for exit maneuvers) ────────────────────────────────────────
function LaneDiagram({ lanesApproach, side }) {
  return (
    <div className="mn-lanes">
      {Array.from({ length: lanesApproach }, (_, i) => {
        const isExit = side === 'right' ? i === lanesApproach - 1 : i === 0
        const icon = isExit ? (side === 'right' ? faArrowRight : faArrowLeft) : faArrowUp
        return (
          <div key={i} className={`mn-lane ${isExit ? 'mn-lane--exit' : ''}`}>
            <FontAwesomeIcon icon={icon} />
          </div>
        )
      })}
    </div>
  )
}

// ── Maneuver HUD card ─────────────────────────────────────────────────────────
function ManeuverCard({ maneuver: m, onClose }) {
  const ic = maneuverIcon(m)
  return (
    <div className="mn-card" role="dialog" aria-label="Maneuver detail">
      <button className="mn-card-close" onClick={onClose} aria-label="Close">✕</button>

      <div className="mn-card-dist">{formatDistance(m.distanceFromPrevM)}</div>

      <div className="mn-card-body">
        <span className={`mn-card-icon mn-card-icon--${m.type}`}>
          <FontAwesomeIcon icon={ic.icon} flip={ic.flip} />
        </span>
        <span className="mn-card-label">{maneuverLabel(m)}</span>
      </div>

      {m.type === 'exit' && m.lanesApproach > 0 && (
        <LaneDiagram lanesApproach={m.lanesApproach} side={m.side ?? 'right'} />
      )}
    </div>
  )
}

function ManeuverList({ maneuvers, selected, onSelect }) {
  if (!maneuvers || maneuvers.length === 0) return null
  return (
    <ol className="rp-steps">
      {maneuvers.map((m, i) => (
        <li
          key={i}
          className={`rp-step rp-step--${m.type}${selected === m ? ' rp-step--active' : ''}`}
          onClick={() => onSelect?.(m)}
          title="Go to maneuver"
        >
          <span className={`rp-step-icon rp-step-icon--${m.type}`}>
            <FontAwesomeIcon icon={maneuverIcon(m).icon} flip={maneuverIcon(m).flip} fixedWidth />
          </span>
          <span className="rp-step-text">{maneuverLabel(m)}</span>
          {i > 0 && m.type !== 'depart' && (
            <span className="rp-step-dist">{formatDistance(m.distanceFromPrevM)}</span>
          )}
        </li>
      ))}
    </ol>
  )
}

// react-select styles matching the dark HUD theme
const selectStyles = {
  container: (base) => ({ ...base, flex: 1, minWidth: 0 }),
  control: (base, state) => ({
    ...base,
    minHeight: 26,
    background: 'rgba(40, 40, 40, 0.9)',
    borderColor: state.isFocused ? '#00D4FF' : '#555',
    boxShadow: 'none',
    cursor: 'text',
    '&:hover': { borderColor: state.isFocused ? '#00D4FF' : '#888' },
  }),
  valueContainer: (base) => ({ ...base, padding: '0 6px' }),
  input: (base) => ({ ...base, margin: 0, padding: 0, color: '#fff', fontSize: 12 }),
  placeholder: (base) => ({ ...base, color: '#888', fontSize: 12 }),
  singleValue: (base) => ({ ...base, color: '#ddd', fontSize: 12 }),
  indicatorSeparator: () => ({ display: 'none' }),
  dropdownIndicator: (base) => ({ ...base, padding: 4, color: '#666' }),
  clearIndicator: (base) => ({ ...base, padding: 4, color: '#666' }),
  menu: (base) => ({
    ...base,
    background: '#1a1a1a',
    border: '1px solid #444',
    zIndex: 2000,
    fontSize: 12,
  }),
  menuList: (base) => ({ ...base, maxHeight: 260 }),
  groupHeading: (base) => ({
    ...base,
    color: '#00D4FF',
    fontFamily: 'monospace',
    fontSize: 10,
    letterSpacing: '0.05em',
    textTransform: 'none',
  }),
  option: (base, state) => ({
    ...base,
    background: state.isFocused ? 'rgba(0, 212, 255, 0.15)' : 'transparent',
    color: state.isSelected ? '#00D4FF' : '#ddd',
    cursor: 'pointer',
    padding: '5px 10px',
  }),
  noOptionsMessage: (base) => ({ ...base, color: '#888', fontSize: 12 }),
}

function WaypointDropdown({ cities, companies, value, onChange, placeholder }) {
  const getCityName = (c) => c.LocalizedNames?.en_gb || c.Name

  const options = useMemo(() => {
    const cityOptions = [...cities]
      .sort((a, b) => getCityName(a).localeCompare(getCityName(b)))
      .map(c => ({ value: `city:${c.X}:${c.Y}`, label: getCityName(c), x: c.X, z: c.Y, name: getCityName(c) }))

    const byCity = {}
    companies.forEach(c => {
      const key = c.city || '—'
      if (!byCity[key]) byCity[key] = []
      byCity[key].push(c)
    })
    const companyGroups = Object.keys(byCity).sort((a, b) => a.localeCompare(b)).map(cityKey => ({
      label: cityKey,
      options: byCity[cityKey]
        .sort((a, b) => a.name.localeCompare(b.name))
        .map(c => ({ value: `company:${c.x}:${c.z}`, label: c.name, x: c.x, z: c.z, name: c.name })),
    }))

    return [{ label: '── Cities ──', options: cityOptions }, ...companyGroups]
  }, [cities, companies])

  const isKnown = !value || cities.some(c => c.X === value.x && c.Y === value.z) ||
                  companies.some(c => c.x === value.x && c.z === value.z)

  const selectedOption = value
    ? { value: `sel:${value.x}:${value.z}`, label: value.name || `${value.x}, ${value.z}`, x: value.x, z: value.z, name: value.name }
    : null

  const extraOptions = value && !isKnown
    ? [{ label: placeholder, options: [selectedOption] }, ...options]
    : options

  return (
    <Select
      className="rp-select"
      classNamePrefix="rp-select"
      styles={selectStyles}
      options={extraOptions}
      value={selectedOption}
      onChange={(opt) => onChange(opt ? { x: opt.x, z: opt.z, name: opt.name } : null)}
      placeholder={placeholder}
      isClearable
      isSearchable
      noOptionsMessage={() => 'No matches'}
    />
  )
}

function RoutePlanner({ mapInstance, tileMapInfo, cities, routeActionsRef }) {
  const [open, setOpen] = useState(false)
  const [selectedManeuver, setSelectedManeuver] = useState(null)
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

  const handleManeuverSelect = (m) => {
    flyTo(m)
    setSelectedManeuver(prev => prev === m ? null : m)
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

  // Expose route actions to siblings (ContextMenu) via the shared ref
  if (routeActionsRef) {
    routeActionsRef.current = {
      setFrom:  (pt) => { setFrom(pt);            setOpen(true) },
      setTo:    (pt) => { setTo(pt);              setOpen(true) },
      addVia:   (pt) => { addStopWithPoint(pt);   setOpen(true) },
    }
  }

  const toggleOption = (key) => setOptions(prev => ({ ...prev, [key]: !prev[key] }))
  const setMode = (m) => setOptions(prev => ({ ...prev, mode: m }))

  return (
    <>
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
          <div className="rp-hint">Right-click on map to set start, end, or via point</div>

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
              <ManeuverList
                maneuvers={routeInfo.maneuvers}
                selected={selectedManeuver}
                onSelect={handleManeuverSelect}
              />
            </div>
          )}
        </div>
      )}
      </div>

      {selectedManeuver && (
        <ManeuverCard
          maneuver={selectedManeuver}
          onClose={() => setSelectedManeuver(null)}
        />
      )}
    </>
  )
}

export default RoutePlanner
