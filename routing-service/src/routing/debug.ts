import * as fs from 'fs';
import * as path from 'path';
import { Request, Response } from 'express';
import { getGraphState } from './state';
import { ets2ToWgs84, chaikin } from './coordinates';
import type { GraphEdge } from './types';

const MAP_DATA_PATH = process.env['MAP_DATA_PATH'] || '/data';

// Per-game lazy-loaded NavCurve/road waypoints: game → "from-to" → [[x,z],...]
export const prefabPathsCache: Record<string, Record<string, [number, number][]>> = {};
const prefabPathsLoaded: Record<string, boolean> = {};

export function loadEdgePaths(game: string): void {
  if (prefabPathsLoaded[game]) return;
  prefabPathsLoaded[game] = true;
  try {
    const filePath = path.join(MAP_DATA_PATH, game, 'geojson', 'routing-edge-paths.json');
    if (fs.existsSync(filePath)) {
      prefabPathsCache[game] = JSON.parse(fs.readFileSync(filePath, 'utf-8')) as Record<string, [number,number][]>;
    }
  } catch { /* file absent or malformed — falls back to straight lines */ }
}

export function graphDebugHandler(req: Request, res: Response): void {
  const game = (req.query['game'] as string) || 'ets2';
  const state = getGraphState(game);
  if (!state) {
    res.status(503).json({ error: `Routing engine not ready for game '${game}'` });
    return;
  }

  loadEdgePaths(game);

  const minX = parseFloat(req.query['minX'] as string);
  const maxX = parseFloat(req.query['maxX'] as string);
  const minZ = parseFloat(req.query['minZ'] as string);
  const maxZ = parseFloat(req.query['maxZ'] as string);

  if ([minX, maxX, minZ, maxZ].some(isNaN)) {
    res.status(400).json({ error: 'Required: minX, maxX, minZ, maxZ' });
    return;
  }

  const { graph, spatialIndex } = state;
  const { nodes, adjacency, bounds } = graph;

  const candidates = spatialIndex.findInBbox(minX, maxX, minZ, maxZ);
  const nodeSet = new Set<string>(candidates.map(c => c.uid));

  const edges: GraphEdge[] = [];
  for (const uid of nodeSet) {
    const outEdges = adjacency[uid];
    if (!outEdges) continue;
    for (const e of outEdges) edges.push(e);
  }

  const edgePaths = prefabPathsCache[game] ?? {};
  const features: object[] = [];

  // Node features
  for (const uid of nodeSet) {
    const n = nodes[uid];
    if (!n) continue;
    const [lon, lat] = ets2ToWgs84(n.x, n.z, bounds);
    features.push({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [lon, lat] },
      properties: { featureType: 'node', uid: n.uid, x: n.x, z: n.z },
    });
  }

  // Edge + arrowhead features
  for (const edge of edges) {
    const fromNode = nodes[edge.from];
    const toNode   = nodes[edge.to];
    if (!fromNode || !toNode) continue;

    const [fLon, fLat] = ets2ToWgs84(fromNode.x, fromNode.z, bounds);
    const [tLon, tLat] = ets2ToWgs84(toNode.x,   toNode.z,   bounds);

    const wp = edgePaths[`${edge.from}-${edge.to}`];
    let coords: [number, number][];
    if (wp && wp.length >= 2) {
      const raw = wp.map(([x, z]) => ets2ToWgs84(x, z, bounds) as [number, number]);
      coords = chaikin(raw);
    } else {
      coords = [[fLon, fLat], [tLon, tLat]];
    }

    features.push({
      type: 'Feature',
      geometry: { type: 'LineString', coordinates: coords },
      properties: {
        featureType: 'edge',
        from: edge.from, to: edge.to,
        weight: edge.weight, length: edge.length,
        speedClass: edge.speedClass, itemType: edge.itemType,
      },
    });

    // Arrowhead at 70% along the polyline
    let aLon: number, aLat: number, bearing: number;
    if (coords.length >= 2) {
      let totalLen = 0;
      const segLens: number[] = [];
      for (let i = 1; i < coords.length; i++) {
        const dl = Math.sqrt((coords[i][0]-coords[i-1][0])**2 + (coords[i][1]-coords[i-1][1])**2);
        segLens.push(dl);
        totalLen += dl;
      }
      const target = totalLen * 0.7;
      let accumulated = 0;
      let segIdx = 0;
      for (let i = 0; i < segLens.length; i++) {
        if (accumulated + segLens[i] >= target) { segIdx = i; break; }
        accumulated += segLens[i];
      }
      const t = segLens[segIdx] > 1e-10 ? (target - accumulated) / segLens[segIdx] : 0;
      const c0 = coords[segIdx], c1 = coords[segIdx + 1];
      const dLon = c1[0] - c0[0], dLat = c1[1] - c0[1];
      aLon = c0[0] + dLon * t;
      aLat = c0[1] + dLat * t;
      bearing = (Math.atan2(dLon, dLat) * 180 / Math.PI + 360) % 360;
    } else {
      aLon = fLon; aLat = fLat; bearing = 0;
    }

    features.push({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [aLon, aLat] },
      properties: {
        featureType: 'arrow',
        bearing,
        speedClass: edge.speedClass,
        itemType: edge.itemType,
      },
    });
  }

  res.json({ type: 'FeatureCollection', features });
}
