import { useCallback, useEffect, useState } from 'react'
import { mapToGameCoords } from '../utils/coordinates'
import './ContextMenu.css'

function ContextMenu({ mapInstance, tileMapInfo, routeActionsRef }) {
  const [menu, setMenu] = useState(null)
  const [copied, setCopied] = useState(false)

  // Close the menu
  const close = useCallback(() => {
    setMenu(null)
    setCopied(false)
  }, [])

  // Listen for right-click on the map
  useEffect(() => {
    if (!mapInstance) return

    const handleContextMenu = (e) => {
      setCopied(false)
      setMenu({
        x: e.point.x,
        y: e.point.y,
        lng: e.lngLat.lng,
        lat: e.lngLat.lat,
      })
    }

    mapInstance.on('contextmenu', handleContextMenu)
    // Close the menu when the map moves or is clicked
    mapInstance.on('movestart', close)
    mapInstance.on('click', close)

    return () => {
      mapInstance.off('contextmenu', handleContextMenu)
      mapInstance.off('movestart', close)
      mapInstance.off('click', close)
    }
  }, [mapInstance, close])

  // Close on outside click / escape
  useEffect(() => {
    if (!menu) return

    const onKeyDown = (e) => {
      if (e.key === 'Escape') close()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [menu, close])

  const handleCopyCoords = useCallback(async () => {
    if (!menu) return

    const [gameX, gameZ] = tileMapInfo
      ? mapToGameCoords(menu.lng, menu.lat, tileMapInfo)
      : [NaN, NaN]

    const text = [
      `In-game: X:${gameX.toFixed(1)}, Z:${gameZ.toFixed(1)}`,
      `Map: ${menu.lat.toFixed(6)}, ${menu.lng.toFixed(6)}`,
    ].join('\n')

    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(close, 900)
    } catch {
      // Fallback for browsers/contexts without clipboard API
      const textarea = document.createElement('textarea')
      textarea.value = text
      textarea.style.position = 'fixed'
      textarea.style.opacity = '0'
      document.body.appendChild(textarea)
      textarea.select()
      try {
        document.execCommand('copy')
        setCopied(true)
        setTimeout(close, 900)
      } catch {
        close()
      }
      document.body.removeChild(textarea)
    }
  }, [menu, tileMapInfo, close])

  const makeRoutePoint = useCallback(() => {
    if (!menu || !tileMapInfo) return null
    const [x, z] = mapToGameCoords(menu.lng, menu.lat, tileMapInfo)
    return { name: `${Math.round(x)}, ${Math.round(z)}`, x: Math.round(x), z: Math.round(z) }
  }, [menu, tileMapInfo])

  const handleSetRouteFrom = useCallback(() => {
    const pt = makeRoutePoint()
    if (pt) routeActionsRef?.current?.setFrom?.(pt)
    close()
  }, [makeRoutePoint, routeActionsRef, close])

  const handleSetRouteTo = useCallback(() => {
    const pt = makeRoutePoint()
    if (pt) routeActionsRef?.current?.setTo?.(pt)
    close()
  }, [makeRoutePoint, routeActionsRef, close])

  const handleAddVia = useCallback(() => {
    const pt = makeRoutePoint()
    if (pt) routeActionsRef?.current?.addVia?.(pt)
    close()
  }, [makeRoutePoint, routeActionsRef, close])

  if (!menu) return null

  return (
    <div className="ctx-menu-overlay" onClick={close} onContextMenu={(e) => e.preventDefault()}>
      <ul
        className="ctx-menu"
        style={{ left: `${menu.x}px`, top: `${menu.y}px` }}
        onClick={(e) => e.stopPropagation()}
      >
        {routeActionsRef && tileMapInfo && (
          <>
            <li className="ctx-menu__item" onClick={handleSetRouteFrom}>Set as start (A)</li>
            <li className="ctx-menu__item" onClick={handleSetRouteTo}>Set as destination (B)</li>
            <li className="ctx-menu__item" onClick={handleAddVia}>Add as via point</li>
            <li className="ctx-menu__separator" />
          </>
        )}
        <li className="ctx-menu__item" onClick={handleCopyCoords}>
          {copied ? 'Copied!' : 'Copy coordinates'}
        </li>
      </ul>
    </div>
  )
}

export default ContextMenu
