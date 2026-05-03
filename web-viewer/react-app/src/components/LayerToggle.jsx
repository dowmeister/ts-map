import { useState, useRef } from 'react'
import { OVERLAY_LAYER_GROUPS } from '../utils/mapStyle'
import './LayerToggle.css'

function LayerToggle({ mapInstance }) {
  const [open, setOpen] = useState(false)
  const [visible, setVisible] = useState(() =>
    Object.fromEntries(OVERLAY_LAYER_GROUPS.map(g => [g.id, true]))
  )
  const allCheckedRef = useRef(null)

  const allOn  = Object.values(visible).every(Boolean)
  const allOff = Object.values(visible).every(v => !v)

  if (allCheckedRef.current) {
    allCheckedRef.current.indeterminate = !allOn && !allOff
  }

  function toggle(id, checked) {
    setVisible(prev => ({ ...prev, [id]: checked }))
    mapInstance?.setLayoutProperty(id, 'visibility', checked ? 'visible' : 'none')
  }

  function toggleAll(checked) {
    const next = Object.fromEntries(OVERLAY_LAYER_GROUPS.map(g => [g.id, checked]))
    setVisible(next)
    if (!mapInstance) return
    for (const g of OVERLAY_LAYER_GROUPS)
      mapInstance.setLayoutProperty(g.id, 'visibility', checked ? 'visible' : 'none')
  }

  return (
    <div className="lt">
      <button className="lt__header" onClick={() => setOpen(o => !o)}>
        <span>Layers</span>
        <span className="lt__arrow">{open ? '▲' : '▼'}</span>
      </button>
      {open && (
        <div className="lt__panel">
          <label className="lt__row lt__row--all">
            <input type="checkbox" ref={allCheckedRef} checked={allOn}
              onChange={e => toggleAll(e.target.checked)} />
            <span>All</span>
          </label>
          <div className="lt__divider" />
          {OVERLAY_LAYER_GROUPS.map(g => (
            <label key={g.id} className="lt__row">
              <input type="checkbox" checked={visible[g.id]}
                onChange={e => toggle(g.id, e.target.checked)} />
              <span>{g.label}</span>
            </label>
          ))}
        </div>
      )}
    </div>
  )
}

export default LayerToggle
