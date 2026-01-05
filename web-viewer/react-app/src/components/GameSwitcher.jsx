import './GameSwitcher.css'

function GameSwitcher({ currentGame, onGameChange }) {
  return (
    <div className="game-switcher">
      <label htmlFor="game-select">Game:</label>
      <select 
        id="game-select" 
        value={currentGame} 
        onChange={(e) => onGameChange(e.target.value)}
      >
        <option value="ets2">Euro Truck Simulator 2</option>
        <option value="ats">American Truck Simulator</option>
        <option value="promods">Promods</option>
      </select>
    </div>
  )
}

export default GameSwitcher
