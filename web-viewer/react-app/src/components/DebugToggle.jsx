import './DebugToggle.css'

function DebugToggle({ graphEnabled, issuesEnabled, onGraphToggle, onIssuesToggle }) {
  return (
    <div className="debug-toggle">
      <label className="debug-toggle__label">
        <input
          type="checkbox"
          checked={graphEnabled}
          onChange={e => onGraphToggle(e.target.checked)}
          className="debug-toggle__checkbox"
        />
        <span className="debug-toggle__text">Show road graph</span>
      </label>
      <label className="debug-toggle__label">
        <input
          type="checkbox"
          checked={issuesEnabled}
          onChange={e => onIssuesToggle(e.target.checked)}
          className="debug-toggle__checkbox"
        />
        <span className="debug-toggle__text">Show graph issues</span>
      </label>
    </div>
  )
}

export default DebugToggle
