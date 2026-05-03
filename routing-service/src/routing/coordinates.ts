import type { GraphNode, MapBounds } from './types';
import type { RouteResult } from './types';

interface TileMapInfo {
  x1: number;  // minX
  x2: number;  // maxX
  y1: number;  // minZ  (the JS file uses y for game Z axis)
  y2: number;  // maxZ
}

export function wgs84ToGame(lon: number, lat: number, bounds: MapBounds): [number, number] {
  const { minX, maxX, minZ, maxZ } = bounds;
  const width = maxX - minX;
  const height = maxZ - minZ;
  const aspectRatio = width / height;
  const maxExtent = 70.0;
  let lonRange: number, latRange: number;
  if (aspectRatio > 1.0) { lonRange = maxExtent; latRange = maxExtent / aspectRatio; }
  else                   { latRange = maxExtent; lonRange = maxExtent * aspectRatio; }
  const normalizedX = (lon + lonRange / 2) / lonRange;
  const normalizedZ = (latRange / 2 - lat) / latRange;
  return [normalizedX * width + minX, normalizedZ * height + minZ];
}

export function ets2ToWgs84(
  gameX: number,
  gameZ: number,
  bounds: MapBounds,
): [number, number] {
  const tileMapInfo: TileMapInfo = {
    x1: bounds.minX,
    x2: bounds.maxX,
    y1: bounds.minZ,
    y2: bounds.maxZ,
  };
  return gameToMapCoords(gameX, gameZ, tileMapInfo);
}

// WGS84 projection — field names and formula match the web viewer's coordinates.js.
function gameToMapCoords(
  gameX: number,
  gameZ: number,
  tileMapInfo: TileMapInfo,
): [number, number] {
  const minX = tileMapInfo.x1;
  const maxX = tileMapInfo.x2;
  const minZ = tileMapInfo.y1;
  const maxZ = tileMapInfo.y2;

  const width = maxX - minX;
  const height = maxZ - minZ;

  const normalizedX = (gameX - minX) / width;
  const normalizedZ = (gameZ - minZ) / height;
  const maxExtent = 70.0;
  const aspectRatio = width / height;

  let lonRange: number, latRange: number;
  if (aspectRatio > 1.0) {
    lonRange = maxExtent;
    latRange = maxExtent / aspectRatio;
  } else {
    latRange = maxExtent;
    lonRange = maxExtent * aspectRatio;
  }

  const lon = normalizedX * lonRange - lonRange / 2.0;
  const lat = latRange / 2.0 - normalizedZ * latRange;

  return [lon, lat];
}

// Chaikin curve subdivision — 1 pass halves C2 discontinuities at knot junctions.
// Endpoints are preserved exactly.
export function chaikin(pts: [number, number][]): [number, number][] {
  if (pts.length < 3) return pts;
  const out: [number, number][] = [pts[0]];
  for (let i = 0; i < pts.length - 1; i++) {
    const [x0, y0] = pts[i];
    const [x1, y1] = pts[i + 1];
    out.push([0.75 * x0 + 0.25 * x1, 0.75 * y0 + 0.25 * y1]);
    out.push([0.25 * x0 + 0.75 * x1, 0.25 * y0 + 0.75 * y1]);
  }
  out.push(pts[pts.length - 1]);
  return out;
}



export function routeToGeoJson(
  result: RouteResult,
  nodes: Record<string, GraphNode>,
  bounds: MapBounds,
  edgePaths?: Record<string, [number, number][]>,
): GeoJsonFeature {
  // Road edges (wp.length === 12): use stored Hermite waypoints exactly — 12 dense
  //   points follow the road surface precisely, no artefacts.
  // Prefab edges (wp.length ≠ 12): use stored Catmull-Rom waypoints IF they pass
  //   reversal detection (smooth junctions, roundabouts, ramps).  If a reversal is
  //   detected (loop artefact from uniform Catmull-Rom), fall back to straight line.
  //   After re-exporting with centripetal C# the reversal check becomes a no-op.
  // 1-pass Chaikin smooths C2 kinks at road–road and road–prefab junctions.
  const coordinates: [number, number][] = [];

  for (let i = 0; i < result.path.length - 1; i++) {
    const fromUid = result.path[i];
    const toUid   = result.path[i + 1];
    const wp      = edgePaths?.[`${fromUid}-${toUid}`];

    const fromNode = nodes[fromUid];
    const toNode   = nodes[toUid];
    if (!fromNode || !toNode) continue;

    const startPt = ets2ToWgs84(fromNode.x, fromNode.z, bounds);
    const endPt   = ets2ToWgs84(toNode.x,   toNode.z,   bounds);

    if (wp && wp.length >= 2) {
      const wgsPts = wp.map(([x, z]) => ets2ToWgs84(x, z, bounds));

      if (i === 0) {
        // First edge: include all waypoints (wp[0]..wp[last])
        for (const pt of wgsPts) coordinates.push(pt);
      } else {
        // Skip wp[0] only if near-duplicate of the previous endpoint
        // (road/prefab pinned endpoints match; ferry StartPortLocation may differ)
        const prev = coordinates[coordinates.length - 1];
        const d = Math.hypot(wgsPts[0][0] - prev[0], wgsPts[0][1] - prev[1]);
        for (let j = d < 1e-5 ? 1 : 0; j < wgsPts.length; j++) coordinates.push(wgsPts[j]);
      }
      // wp[last] is already included — no separate endPt needed
    } else {
      // No waypoints: straight line between node positions
      if (i === 0) coordinates.push(startPt);
      coordinates.push(endPt);
    }
  }

  return {
    type: 'Feature',
    geometry: { type: 'LineString', coordinates: chaikin(coordinates) },
    properties: {},
  };
}

interface GeoJsonFeature {
  type: 'Feature';
  geometry: {
    type: 'LineString';
    coordinates: [number, number][];
  };
  properties: Record<string, never>;
}
