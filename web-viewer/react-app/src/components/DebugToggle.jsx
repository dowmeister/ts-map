import './DebugToggle.css'

function DebugToggle({
  graphEnabled,
  issuesEnabled,
  softIssuesEnabled,
  onGraphToggle,
  onIssuesToggle,
  onSoftIssuesToggle,
}) {
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
      <label className="debug-toggle__label debug-toggle__label--sub">
        <input
          type="checkbox"
          checked={softIssuesEnabled}
          onChange={e => onSoftIssuesToggle(e.target.checked)}
          disabled={!issuesEnabled}
          className="debug-toggle__checkbox"
        />
        <span className="debug-toggle__text">Show soft issues</span>
      </label>
    </div>
  )
}

export default DebugToggle
