import type { GraphNode, MapBounds } from './types';
import type { RouteResult } from './types';

const EARTH_RADIUS_METERS = 6370997.0;
const LENGTH_OF_DEGREE = EARTH_RADIUS_METERS * Math.PI / 180.0;

interface TileMapInfo {
  x1: number;  // minX
  x2: number;  // maxX
  y1: number;  // minZ  (the JS file uses y for game Z axis)
  y2: number;  // maxZ
  projection?: MapBounds['projection'];
}

interface ProjectionValues {
  type: string;
  standardParallel1: number;
  standardParallel2: number;
  originLat: number;
  originLon: number;
  offsetX: number;
  offsetZ: number;
  factorZ: number;
  factorX: number;
  useEts2UkScale: boolean;
}

function toTileMapInfo(bounds: MapBounds): TileMapInfo {
  return {
    x1: bounds.minX,
    x2: bounds.maxX,
    y1: bounds.minZ,
    y2: bounds.maxZ,
    projection: bounds.projection,
  };
}

function projectionValues(tileMapInfo: TileMapInfo): ProjectionValues | null {
  const p = tileMapInfo.projection;
  if (!p) return null;

  const [originLat, originLon] = p.map_origin;
  const [offsetX, offsetZ] = p.map_offset;
  const [factorZ, factorX] = p.map_factor;

  return {
    type: p.type,
    standardParallel1: p.standard_parallel_1,
    standardParallel2: p.standard_parallel_2,
    originLat,
    originLon,
    offsetX,
    offsetZ,
    factorZ,
    factorX,
    useEts2UkScale: !!p.use_ets2_uk_scale,
  };
}

function tanHalf(phi: number): number {
  return Math.tan(Math.PI / 4 + phi / 2);
}

function lccParams(p: ProjectionValues): { n: number; f: number; rho0: number; lambda0: number } {
  const phi1 = p.standardParallel1 * Math.PI / 180;
  const phi2 = p.standardParallel2 * Math.PI / 180;
  const phi0 = p.originLat * Math.PI / 180;
  const lambda0 = p.originLon * Math.PI / 180;
  const n = Math.log(Math.cos(phi1) / Math.cos(phi2)) / Math.log(tanHalf(phi2) / tanHalf(phi1));
  const f = Math.cos(phi1) * Math.pow(tanHalf(phi1), n) / n;
  const rho0 = EARTH_RADIUS_METERS * f / Math.pow(tanHalf(phi0), n);
  return { n, f, rho0, lambda0 };
}

export function wgs84ToGame(lon: number, lat: number, bounds: MapBounds): [number, number] {
  return mapToGameCoords(lon, lat, toTileMapInfo(bounds));
}

export function ets2ToWgs84(
  gameX: number,
  gameZ: number,
  bounds: MapBounds,
): [number, number] {
  return gameToMapCoords(gameX, gameZ, toTileMapInfo(bounds));
}

function legacyGameToMapCoords(
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

function legacyMapToGameCoords(lon: number, lat: number, tileMapInfo: TileMapInfo): [number, number] {
  const minX = tileMapInfo.x1;
  const maxX = tileMapInfo.x2;
  const minZ = tileMapInfo.y1;
  const maxZ = tileMapInfo.y2;
  const width = maxX - minX;
  const height = maxZ - minZ;
  const maxExtent = 70.0;
  const aspectRatio = width / height;
  const lonRange = aspectRatio > 1.0 ? maxExtent : maxExtent * aspectRatio;
  const latRange = aspectRatio > 1.0 ? maxExtent / aspectRatio : maxExtent;
  const normalizedX = (lon + lonRange / 2.0) / lonRange;
  const normalizedZ = (latRange / 2.0 - lat) / latRange;
  return [minX + normalizedX * width, minZ + normalizedZ * height];
}

// WGS84 projection — field names and formula match the web viewer's coordinates.js.
function gameToMapCoords(
  gameX: number,
  gameZ: number,
  tileMapInfo: TileMapInfo,
): [number, number] {
  const p = projectionValues(tileMapInfo);
  if (!p) return legacyGameToMapCoords(gameX, gameZ, tileMapInfo);

  if (p.type !== 'lambert_conic') {
    return [
      p.originLon + (gameX - p.offsetZ) * p.factorX,
      p.originLat + (gameZ - p.offsetX) * p.factorZ,
    ];
  }

  let x = gameX - p.offsetX;
  let z = gameZ - p.offsetZ;

  if (p.useEts2UkScale) {
    const ukScale = 0.75;
    const calaisX = -31100.0;
    const calaisZ = -5500.0;
    if (x * ukScale < calaisX && z * ukScale < calaisZ) {
      x = (x + calaisX / 2) * ukScale;
      z = (z + calaisZ / 2) * ukScale;
    }
  }

  const lccX = x * p.factorX * LENGTH_OF_DEGREE;
  const lccY = z * p.factorZ * LENGTH_OF_DEGREE;
  const { n, f, rho0, lambda0 } = lccParams(p);
  let rho = Math.sqrt(lccX * lccX + (rho0 - lccY) * (rho0 - lccY));
  if (n < 0) rho = -rho;
  const theta = Math.atan2(lccX, rho0 - lccY);
  const lat = 2 * Math.atan(Math.pow(EARTH_RADIUS_METERS * f / rho, 1 / n)) - Math.PI / 2;
  const lon = lambda0 + theta / n;
  return [lon * 180 / Math.PI, lat * 180 / Math.PI];
}

function mapToGameCoords(lon: number, lat: number, tileMapInfo: TileMapInfo): [number, number] {
  const p = projectionValues(tileMapInfo);
  if (!p) return legacyMapToGameCoords(lon, lat, tileMapInfo);

  if (p.type !== 'lambert_conic') {
    return [
      (lon - p.originLon) / p.factorX + p.offsetZ,
      (lat - p.originLat) / p.factorZ + p.offsetX,
    ];
  }

  const { n, f, rho0, lambda0 } = lccParams(p);
  const phi = lat * Math.PI / 180;
  const lambda = lon * Math.PI / 180;
  const rho = EARTH_RADIUS_METERS * f / Math.pow(tanHalf(phi), n);
  const theta = n * (lambda - lambda0);
  const lccX = rho * Math.sin(theta);
  const lccY = rho0 - rho * Math.cos(theta);

  let x = lccX / p.factorX / LENGTH_OF_DEGREE;
  let z = lccY / p.factorZ / LENGTH_OF_DEGREE;

  if (p.useEts2UkScale) {
    const ukScale = 0.75;
    const calaisX = -31100.0;
    const calaisZ = -5500.0;
    if (x < calaisX * (1 + ukScale / 2) && z < calaisZ * (1 + ukScale / 2)) {
      x = x / ukScale - calaisX / 2;
      z = z / ukScale - calaisZ / 2;
    }
  }

  return [x + p.offsetX, z + p.offsetZ];
}

// Chaikin curve subdivision — selective: only rounds corners where direction changes
// more than thresholdDeg degrees. Straight or near-straight segments are kept as-is,
// avoiding false bumps at road→prefab junctions on straight roads.
// Endpoints are preserved exactly.
export function chaikin(pts: [number, number][], thresholdDeg = 8): [number, number][] {
  if (pts.length < 3) return pts;
  const threshold = thresholdDeg * Math.PI / 180;
  const out: [number, number][] = [pts[0]];

  for (let i = 1; i < pts.length - 1; i++) {
    const [px, py] = pts[i - 1];
    const [cx, cy] = pts[i];
    const [nx, ny] = pts[i + 1];

    const dx1 = cx - px, dy1 = cy - py;
    const dx2 = nx - cx, dy2 = ny - cy;
    const len1 = Math.hypot(dx1, dy1);
    const len2 = Math.hypot(dx2, dy2);

    if (len1 < 1e-10 || len2 < 1e-10) {
      out.push([cx, cy]);
      continue;
    }

    const dot = (dx1 * dx2 + dy1 * dy2) / (len1 * len2);
    const angle = Math.acos(Math.max(-1, Math.min(1, dot)));

    if (angle > threshold) {
      // Real corner: replace with the two Chaikin cut points
      out.push([0.25 * px + 0.75 * cx, 0.25 * py + 0.75 * cy]);
      out.push([0.75 * cx + 0.25 * nx, 0.75 * cy + 0.25 * ny]);
    } else {
      out.push([cx, cy]);
    }
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
