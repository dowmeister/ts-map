import * as fs from 'fs';
import * as path from 'path';
import { Request, Response } from 'express';
import { getGraphState } from './state';
import { ets2ToWgs84, chaikin } from './coordinates';
import type { GraphEdge } from './types';

const MAP_DATA_PATH = process.env['MAP_DATA_PATH'] || '/data';
const DEBUG_LOG = process.env['ROUTING_DEBUG_LOG'] !== 'false';

function logDebug(message: string): void {
  if (DEBUG_LOG) console.log(message);
}

// Per-game lazy-loaded NavCurve/road waypoints: game → "from-to" → [[x,z],...]
export const prefabPathsCache: Record<string, Record<string, [number, number][]>> = {};
const prefabPathsLoaded: Record<string, boolean> = {};
type LaneGraphDebugNode = {
  id: string;
  x: number;
  z: number;
  kind: string;
  sourceUid?: string;
  lane?: string;
  rawNodeUid?: string;
  snapStatus?: string;
  snapDetail?: string;
  inDegree?: number;
  outDegree?: number;
};
type LaneGraphDebugEdge = {
  from: string;
  to: string;
  kind: string;
  sourceUid?: string;
  lane?: string;
  path: [number, number][];
};
type LaneGraphDebugData = {
  nodes: LaneGraphDebugNode[];
  edges: LaneGraphDebugEdge[];
};
const laneGraphCache: Record<string, LaneGraphDebugData | null> = {};

export function loadEdgePaths(game: string): void {
  if (prefabPathsLoaded[game]) return;
  prefabPathsLoaded[game] = true;
  const t0 = Date.now();
  try {
    const filePath = path.join(MAP_DATA_PATH, game, 'geojson', 'routing-edge-paths.json');
    if (fs.existsSync(filePath)) {
      prefabPathsCache[game] = JSON.parse(fs.readFileSync(filePath, 'utf-8')) as Record<string, [number,number][]>;
      logDebug(`[Debug:${game}] Loaded edge paths: ${Object.keys(prefabPathsCache[game]).length} in ${Date.now() - t0}ms`);
    } else {
      logDebug(`[Debug:${game}] Edge paths file not found: ${filePath}`);
    }
  } catch (err) {
    logDebug(`[Debug:${game}] Failed to load edge paths: ${err instanceof Error ? err.message : String(err)}`);
  }
}

export function graphDebugHandler(req: Request, res: Response): void {
  const t0 = Date.now();
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

  logDebug(`[Debug:${game}] graph bbox X[${minX.toFixed(0)},${maxX.toFixed(0)}] Z[${minZ.toFixed(0)},${maxZ.toFixed(0)}] nodes=${nodeSet.size} edges=${edges.length} features=${features.length} ${Date.now() - t0}ms`);
  res.json({ type: 'FeatureCollection', features });
}

function loadLaneGraphDebug(game: string): LaneGraphDebugData | null {
  if (game in laneGraphCache) return laneGraphCache[game];
  const t0 = Date.now();
  try {
    const filePath = path.join(MAP_DATA_PATH, game, 'geojson', 'routing-lane-graph-debug.json');
    if (!fs.existsSync(filePath)) {
      laneGraphCache[game] = null;
      logDebug(`[LaneDebug:${game}] Lane graph file not found: ${filePath}`);
      return null;
    }
    laneGraphCache[game] = JSON.parse(fs.readFileSync(filePath, 'utf-8')) as LaneGraphDebugData;
    logDebug(`[LaneDebug:${game}] Loaded lane graph: ${laneGraphCache[game]?.nodes.length ?? 0} nodes, ${laneGraphCache[game]?.edges.length ?? 0} edges in ${Date.now() - t0}ms`);
    return laneGraphCache[game];
  } catch (err) {
    laneGraphCache[game] = null;
    logDebug(`[LaneDebug:${game}] Failed to load lane graph: ${err instanceof Error ? err.message : String(err)}`);
    return null;
  }
}

function pathTouchesBbox(pathPts: [number, number][], minX: number, maxX: number, minZ: number, maxZ: number): boolean {
  for (const [x, z] of pathPts) {
    if (x >= minX && x <= maxX && z >= minZ && z <= maxZ) return true;
  }
  return false;
}

function arrowOnLine(coords: [number, number][], ratio: number): { lon: number; lat: number; bearing: number } | null {
  if (coords.length < 2) return null;
  let totalLen = 0;
  const segLens: number[] = [];
  for (let i = 1; i < coords.length; i++) {
    const dl = Math.hypot(coords[i][0] - coords[i - 1][0], coords[i][1] - coords[i - 1][1]);
    segLens.push(dl);
    totalLen += dl;
  }
  if (totalLen < 1e-12) return null;

  const target = totalLen * ratio;
  let accumulated = 0;
  let segIdx = 0;
  for (let i = 0; i < segLens.length; i++) {
    if (accumulated + segLens[i] >= target) {
      segIdx = i;
      break;
    }
    accumulated += segLens[i];
  }

  const t = segLens[segIdx] > 1e-12 ? (target - accumulated) / segLens[segIdx] : 0;
  const c0 = coords[segIdx];
  const c1 = coords[segIdx + 1];
  const dLon = c1[0] - c0[0];
  const dLat = c1[1] - c0[1];
  return {
    lon: c0[0] + dLon * t,
    lat: c0[1] + dLat * t,
    bearing: (Math.atan2(dLon, dLat) * 180 / Math.PI + 360) % 360,
  };
}

export function laneGraphDebugHandler(req: Request, res: Response): void {
  const t0 = Date.now();
  const game = (req.query['game'] as string) || 'ets2';
  const state = getGraphState(game);
  if (!state) {
    res.status(503).json({ error: `Routing engine not ready for game '${game}'` });
    return;
  }

  const minX = parseFloat(req.query['minX'] as string);
  const maxX = parseFloat(req.query['maxX'] as string);
  const minZ = parseFloat(req.query['minZ'] as string);
  const maxZ = parseFloat(req.query['maxZ'] as string);

  if ([minX, maxX, minZ, maxZ].some(isNaN)) {
    res.status(400).json({ error: 'Required: minX, maxX, minZ, maxZ' });
    return;
  }

  const data = loadLaneGraphDebug(game);
  if (!data) {
    logDebug(`[LaneDebug:${game}] empty response, lane graph unavailable`);
    res.json({ type: 'FeatureCollection', features: [] });
    return;
  }

  const bounds = state.graph.bounds;
  const features: object[] = [];
  let visibleEdges = 0;
  let visibleNodes = 0;

  for (const edge of data.edges) {
    if (!edge.path || edge.path.length < 2) continue;
    if (!pathTouchesBbox(edge.path, minX, maxX, minZ, maxZ)) continue;
    visibleEdges++;

    const coords = edge.path.map(([x, z]) => ets2ToWgs84(x, z, bounds));
    features.push({
      type: 'Feature',
      geometry: { type: 'LineString', coordinates: coords },
      properties: {
        featureType: 'edge',
        from: edge.from,
        to: edge.to,
        kind: edge.kind,
        sourceUid: edge.sourceUid,
        lane: edge.lane,
      },
    });

    const arrow = arrowOnLine(coords, 0.7);
    if (arrow) {
      features.push({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [arrow.lon, arrow.lat] },
        properties: {
          featureType: 'arrow',
          bearing: arrow.bearing,
          kind: edge.kind,
          sourceUid: edge.sourceUid,
          lane: edge.lane,
        },
      });
    }
  }

  for (const node of data.nodes) {
    if (node.x < minX || node.x > maxX || node.z < minZ || node.z > maxZ) continue;
    visibleNodes++;
    const [lon, lat] = ets2ToWgs84(node.x, node.z, bounds);
    features.push({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [lon, lat] },
      properties: {
        featureType: 'node',
        id: node.id,
        x: node.x,
        z: node.z,
        kind: node.kind,
        sourceUid: node.sourceUid,
        lane: node.lane,
        rawNodeUid: node.rawNodeUid,
        snapStatus: node.snapStatus,
        snapDetail: node.snapDetail,
        inDegree: node.inDegree,
        outDegree: node.outDegree,
      },
    });
  }

  logDebug(`[LaneDebug:${game}] bbox X[${minX.toFixed(0)},${maxX.toFixed(0)}] Z[${minZ.toFixed(0)},${maxZ.toFixed(0)}] nodes=${visibleNodes}/${data.nodes.length} edges=${visibleEdges}/${data.edges.length} features=${features.length} ${Date.now() - t0}ms`);
  res.json({ type: 'FeatureCollection', features });
}

export function laneGraphIssuesHandler(req: Request, res: Response): void {
  const t0 = Date.now();
  const game = (req.query['game'] as string) || 'ets2';
  const state = getGraphState(game);
  if (!state) {
    res.status(503).json({ error: `Routing engine not ready for game '${game}'` });
    return;
  }

  const data = loadLaneGraphDebug(game);
  if (!data) {
    res.json({ type: 'FeatureCollection', features: [] });
    return;
  }

  const issueKinds = new Set(['road_unmatched', 'prefab_extra']);
  const bounds = state.graph.bounds;
  const features: object[] = [];

  for (const node of data.nodes) {
    if (!issueKinds.has(node.kind)) continue;
    const [lon, lat] = ets2ToWgs84(node.x, node.z, bounds);
    features.push({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [lon, lat] },
      properties: {
        featureType: 'issue',
        id: node.id,
        x: node.x,
        z: node.z,
        kind: node.kind,
        sourceUid: node.sourceUid,
        lane: node.lane,
        rawNodeUid: node.rawNodeUid,
        snapStatus: node.snapStatus,
        snapDetail: node.snapDetail,
        inDegree: node.inDegree,
        outDegree: node.outDegree,
      },
    });
  }

  logDebug(`[LaneIssues:${game}] issues=${features.length} ${Date.now() - t0}ms`);
  res.json({ type: 'FeatureCollection', features });
}
