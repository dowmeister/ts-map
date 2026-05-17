import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { mapToGameCoords } from '../utils/coordinates'
import './PolygonTool.css'

const SOURCE_ID = 'polygon-tool-source'
const FILL_LAYER_ID = 'polygon-tool-fill'
const LINE_LAYER_ID = 'polygon-tool-line'
const POINT_LAYER_ID = 'polygon-tool-points'

function emptyFeatureCollection() {
  return {
    type: 'FeatureCollection',
    features: [],
  }
}

function buildGeoJson(points, closed) {
  if (!points.length) return emptyFeatureCollection()

  const coords = points.map((point) => [point.lon, point.lat])
  const lineCoords = closed && coords.length > 2 ? [...coords, coords[0]] : coords
  const features = [
    {
      type: 'Feature',
      properties: { kind: 'outline' },
      geometry: {
        type: 'LineString',
        coordinates: lineCoords,
      },
    },
    ...points.map((point, index) => ({
      type: 'Feature',
      properties: {
        kind: 'vertex',
        index: index + 1,
      },
      geometry: {
        type: 'Point',
        coordinates: [point.lon, point.lat],
      },
    })),
  ]

  if (closed && coords.length > 2) {
    features.unshift({
      type: 'Feature',
      properties: { kind: 'area' },
      geometry: {
        type: 'Polygon',
        coordinates: [[...coords, coords[0]]],
      },
    })
  }

  return {
    type: 'FeatureCollection',
    features,
  }
}

function addPolygonLayers(map) {
  if (!map || !map.isStyleLoaded()) return

  if (!map.getSource(SOURCE_ID)) {
    map.addSource(SOURCE_ID, {
      type: 'geojson',
      data: emptyFeatureCollection(),
    })
  }

  if (!map.getLayer(FILL_LAYER_ID)) {
    map.addLayer({
      id: FILL_LAYER_ID,
      type: 'fill',
      source: SOURCE_ID,
      filter: ['==', ['get', 'kind'], 'area'],
      paint: {
        'fill-color': '#00e676',
        'fill-opacity': 0.18,
      },
    })
  }

  if (!map.getLayer(LINE_LAYER_ID)) {
    map.addLayer({
      id: LINE_LAYER_ID,
      type: 'line',
      source: SOURCE_ID,
      filter: ['==', ['get', 'kind'], 'outline'],
      paint: {
        'line-color': '#00e676',
        'line-width': 3,
        'line-opacity': 0.95,
        'line-dasharray': [2, 1],
      },
    })
  }

  if (!map.getLayer(POINT_LAYER_ID)) {
    map.addLayer({
      id: POINT_LAYER_ID,
      type: 'circle',
      source: SOURCE_ID,
      filter: ['==', ['get', 'kind'], 'vertex'],
      paint: {
        'circle-radius': 5,
        'circle-color': '#00e676',
        'circle-stroke-color': '#ffffff',
        'circle-stroke-width': 2,
      },
    })
  }
}

function removePolygonLayers(map) {
  if (!map) return

  for (const layerId of [POINT_LAYER_ID, LINE_LAYER_ID, FILL_LAYER_ID]) {
    if (map.getLayer(layerId)) {
      map.removeLayer(layerId)
    }
  }

  if (map.getSource(SOURCE_ID)) {
    map.removeSource(SOURCE_ID)
  }
}

function formatNumber(value) {
  return Number(value.toFixed(1))
}

function PolygonTool({ mapInstance, tileMapInfo }) {
  const [open, setOpen] = useState(false)
  const [drawing, setDrawing] = useState(false)
  const [closed, setClosed] = useState(false)
  const [points, setPoints] = useState([])
  const [copied, setCopied] = useState(false)
  const pointsRef = useRef(points)
  const closedRef = useRef(closed)

  useEffect(() => {
    pointsRef.current = points
  }, [points])

  useEffect(() => {
    closedRef.current = closed
  }, [closed])

  const geoJson = useMemo(() => buildGeoJson(points, closed), [points, closed])

  const exportedPolygon = useMemo(() => ({
    name: 'left-hand-traffic-area',
    points: points.map((point) => ({
      x: formatNumber(point.x),
      z: formatNumber(point.z),
      lon: Number(point.lon.toFixed(6)),
      lat: Number(point.lat.toFixed(6)),
    })),
  }), [points])

  const updateSource = useCallback(() => {
    if (!mapInstance || !mapInstance.isStyleLoaded()) return
    addPolygonLayers(mapInstance)
    const source = mapInstance.getSource(SOURCE_ID)
    if (source) {
      source.setData(buildGeoJson(pointsRef.current, closedRef.current))
    }
  }, [mapInstance])

  useEffect(() => {
    if (!mapInstance) return

    const onStyleReady = () => updateSource()
    if (mapInstance.isStyleLoaded()) {
      updateSource()
    } else {
      mapInstance.once('load', onStyleReady)
    }
    mapInstance.on('styledata', onStyleReady)

    return () => {
      mapInstance.off('styledata', onStyleReady)
      removePolygonLayers(mapInstance)
    }
  }, [mapInstance, updateSource])

  useEffect(() => {
    if (!mapInstance) return
    updateSource()
  }, [mapInstance, geoJson, updateSource])

  useEffect(() => {
    if (!mapInstance || !drawing) return

    const onClick = (event) => {
      if (!tileMapInfo) return

      const lon = event.lngLat.lng
      const lat = event.lngLat.lat
      const [x, z] = mapToGameCoords(lon, lat, tileMapInfo)
      setPoints((current) => [
        ...current,
        { lon, lat, x, z },
      ])
      setClosed(false)
      setCopied(false)
      event.originalEvent?.stopPropagation()
    }

    mapInstance.getCanvas().style.cursor = 'crosshair'
    mapInstance.on('click', onClick)

    return () => {
      mapInstance.off('click', onClick)
      if (mapInstance.getCanvas()) {
        mapInstance.getCanvas().style.cursor = ''
      }
    }
  }, [mapInstance, drawing, tileMapInfo])

  const canClose = points.length >= 3
  const output = JSON.stringify(exportedPolygon, null, 2)

  const handleUndo = () => {
    setPoints((current) => current.slice(0, -1))
    setClosed(false)
    setCopied(false)
  }

  const handleClear = () => {
    setPoints([])
    setClosed(false)
    setCopied(false)
  }

  const handleCopy = async () => {
    await navigator.clipboard.writeText(output)
    setCopied(true)
  }

  return (
    <div className="pt">
      <button
        type="button"
        className={`pt__toggle ${open ? 'pt__toggle--open' : ''}`}
        onClick={() => setOpen((value) => !value)}
      >
        Polygon
      </button>

      {open && (
        <div className="pt__panel">
          <label className="pt__row">
            <input
              type="checkbox"
              checked={drawing}
              onChange={(event) => setDrawing(event.target.checked)}
              disabled={!mapInstance || !tileMapInfo}
            />
            <span>draw on click</span>
          </label>

          <div className="pt__actions">
            <button type="button" onClick={handleUndo} disabled={!points.length}>
              Undo
            </button>
            <button type="button" onClick={() => setClosed(true)} disabled={!canClose}>
              Close
            </button>
            <button type="button" onClick={handleClear} disabled={!points.length}>
              Clear
            </button>
          </div>

          <div className="pt__meta">
            {points.length} points {closed ? 'closed' : 'open'}
          </div>

          <textarea
            className="pt__output"
            value={output}
            readOnly
            spellCheck="false"
          />

          <button
            type="button"
            className="pt__copy"
            onClick={handleCopy}
            disabled={!points.length}
          >
            {copied ? 'Copied' : 'Copy JSON'}
          </button>
        </div>
      )}
    </div>
  )
}

export default PolygonTool
