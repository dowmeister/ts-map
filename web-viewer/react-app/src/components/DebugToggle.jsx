import './DebugToggle.css'

function DebugToggle({ enabled, onToggle }) {
  return (
    <div className="debug-toggle">
      <label className="debug-toggle__label">
        <input
          type="checkbox"
          checked={enabled}
          onChange={e => onToggle(e.target.checked)}
          className="debug-toggle__checkbox"
        />
        <span className="debug-toggle__text">Show road graph</span>
      </label>
    </div>
  )
}

export default DebugToggle
