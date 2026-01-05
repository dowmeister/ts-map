import { mapToGameCoords } from '../utils/coordinates'
import './CoordinateInfo.css'

function CoordinateInfo({ zoom, bounds, tileMapInfo }) {
  if (!bounds || !tileMapInfo) {
    return (
      <div className="coord-info">
        <div><span className="label">Zoom:</span> <span className="value">-</span></div>
        <div><span className="label">Loading...</span></div>
      </div>
    )
  }

  const [gameSW_X, gameSW_Z] = mapToGameCoords(bounds.getWest(), bounds.getSouth(), tileMapInfo)
  const [gameNE_X, gameNE_Z] = mapToGameCoords(bounds.getEast(), bounds.getNorth(), tileMapInfo)

  return (
    <div className="coord-info">
      <div>
        <span className="label">Zoom:</span> 
        <span className="value">{zoom.toFixed(2)}</span>
      </div>
      <div><span className="label">Map Bounds:</span></div>
      <div style={{ marginLeft: '10px' }}>
        <span className="value">
          SW: [{bounds.getWest().toFixed(4)}, {bounds.getSouth().toFixed(4)}] 
          NE: [{bounds.getEast().toFixed(4)}, {bounds.getNorth().toFixed(4)}]
        </span>
      </div>
      <div><span className="label">Game Coords:</span></div>
      <div style={{ marginLeft: '10px' }}>
        <span className="value">
          SW: [X:{gameSW_X.toFixed(0)}, Z:{gameSW_Z.toFixed(0)}] 
          NE: [X:{gameNE_X.toFixed(0)}, Z:{gameNE_Z.toFixed(0)}]
        </span>
      </div>
    </div>
  )
}

export default CoordinateInfo
