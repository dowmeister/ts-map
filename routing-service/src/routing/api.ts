import * as fs from 'fs';
import * as path from 'path';
import { Router, Request, Response } from 'express';
import { loadGraph, loadMapBounds } from './loader';
import { setGraphState, getAvailableGames } from './state';
import { graphDebugHandler, loadEdgePaths, prefabPathsCache } from './debug';
import { MainComponentIndex } from './component';
import { SpatialIndex } from './spatial-index';
import { findNearestMainComponent, findNearestWithHeading } from './nearest';
import { findRoute } from './astar';
import { ets2ToWgs84, wgs84ToGame, routeToGeoJson } from './coordinates';
import type { GraphEdge, LoadedGraph } from './types';

// ── Per-game runtime state (routing-specific, not shared with debug) ──────────

interface GameRuntime {
  loadedGraph: LoadedGraph;
  componentIndex: MainComponentIndex;
  spatialIndex: SpatialIndex;
}

const runtimes: Record<string, GameRuntime> = {};
const companiesCache: Record<string, { name: string; id: string; x: number; z: number }[]> = {};

let _mapDataPath: string;

function getMapDataPath(): string {
  return _mapDataPath ?? (_mapDataPath = process.env['MAP_DATA_PATH'] ?? path.resolve(__dirname, '../../..', 'map_data'));
}

// ── Startup ───────────────────────────────────────────────────────────────────

function discoverGames(mapDataPath: string): string[] {
  try {
    return fs.readdirSync(mapDataPath).filter(dir => {
      try {
        return fs.statSync(path.join(mapDataPath, dir)).isDirectory() &&
               fs.existsSync(path.join(mapDataPath, dir, 'geojson', 'routing-graph.json'));
      } catch { return false; }
    });
  } catch {
    return ['ets2'];
  }
}

function loadGame(mapDataPath: string, game: string): void {
  const graphPath = path.join(mapDataPath, game, 'geojson', 'routing-graph.json');
  console.log(`[Routing:${game}] Loading bounds...`);
  const bounds = loadMapBounds(mapDataPath, game);
  console.log(`[Routing:${game}] Bounds X[${bounds.minX.toFixed(0)},${bounds.maxX.toFixed(0)}] Z[${bounds.minZ.toFixed(0)},${bounds.maxZ.toFixed(0)}]`);

  const t1 = Date.now();
  const loadedGraph = loadGraph(graphPath, bounds);
  console.log(`[Routing:${game}] Graph loaded in ${Date.now()-t1}ms — ${loadedGraph.nodeCount} nodes, ${loadedGraph.edgeCount} edges`);

  const t2 = Date.now();
  const componentIndex = new MainComponentIndex();
  componentIndex.build(loadedGraph.nodes, Object.values(loadedGraph.adjacency).flat());
  const mainPct = (componentIndex.mainSize / loadedGraph.nodeCount * 100).toFixed(2);
  console.log(`[Routing:${game}] Main component: ${componentIndex.mainSize} nodes (${mainPct}%) in ${Date.now()-t2}ms`);

  const t3 = Date.now();
  const spatialIndex = new SpatialIndex(loadedGraph.nodes, loadedGraph.bounds);
  console.log(`[Routing:${game}] Spatial index: ${spatialIndex.cellCount} cells in ${Date.now()-t3}ms`);

  const [parisLon, parisLat] = ets2ToWgs84(-22674, -16800, loadedGraph.bounds);
  console.log(`[Routing:${game}] WGS84 check (-22674,-16800): lon=${parisLon.toFixed(2)} lat=${parisLat.toFixed(2)}`);

  runtimes[game] = { loadedGraph, componentIndex, spatialIndex };
  setGraphState(game, { graph: loadedGraph, spatialIndex });
  console.log(`[Routing:${game}] Ready`);
}

export function initializeRouting(): void {
  const mapDataPath = process.env['MAP_DATA_PATH'] ??
    path.resolve(__dirname, '../../..', 'map_data');
  console.log('[Routing] map_data path:', mapDataPath);

  const games = discoverGames(mapDataPath);
  console.log('[Routing] Discovered games:', games.join(', ') || '(none)');

  for (const game of games) {
    try {
      loadGame(mapDataPath, game);
    } catch (err) {
      console.error(`[Routing:${game}] Failed to load:`, err);
    }
  }

  console.log(`[Routing] All games ready: ${getAvailableGames().join(', ')}`);
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function resolveGame(req: Request): string {
  return (req.query['game'] as string) || 'ets2';
}

function getRuntimeOrError(res: Response, game: string): GameRuntime | null {
  const rt = runtimes[game];
  if (!rt) {
    const available = getAvailableGames();
    res.status(404).json({
      error: `Game '${game}' not loaded`,
      available,
    });
    return null;
  }
  return rt;
}

// ── Router ────────────────────────────────────────────────────────────────────

export const router = Router();

// List available games
router.get('/games', (_req: Request, res: Response) => {
  res.json({ games: getAvailableGames() });
});

router.get('/companies', (req: Request, res: Response) => {
  const game = resolveGame(req);
  const rt = getRuntimeOrError(res, game);
  if (!rt) return;

  if (companiesCache[game]) { res.json({ companies: companiesCache[game] }); return; }

  const filePath = path.join(getMapDataPath(), game, 'geojson', 'companies.geojson');
  try {
    const data = JSON.parse(fs.readFileSync(filePath, 'utf8'));
    const bounds = rt.loadedGraph.bounds;

    // Load city positions (game coords) for nearest-city lookup
    type CityEntry = { name: string; x: number; z: number };
    let cities: CityEntry[] = [];
    try {
      const raw = JSON.parse(fs.readFileSync(path.join(getMapDataPath(), game, 'Cities.json'), 'utf8'));
      cities = (raw as any[]).filter(c => c.Name && c.X != null && c.Y != null)
                             .map(c => ({ name: c.Name as string, x: c.X as number, z: c.Y as number }));
    } catch { /* cities are optional — fall back to no city label */ }

    const nearestCity = (cx: number, cz: number): string | undefined => {
      let best: string | undefined, bestD = Infinity;
      for (const c of cities) {
        const d = (c.x - cx) ** 2 + (c.z - cz) ** 2;
        if (d < bestD) { bestD = d; best = c.name; }
      }
      return best;
    };

    const companies = (data.features as any[])
      .filter(f => f.properties?.name)
      .map(f => {
        const [lon, lat] = f.geometry.coordinates;
        const [x, z] = wgs84ToGame(lon, lat, bounds);
        const rx = Math.round(x), rz = Math.round(z);
        const city = nearestCity(rx, rz);
        return { name: f.properties.name as string, id: f.properties.id as string, city, x: rx, z: rz };
      });
    companiesCache[game] = companies;
    res.json({ companies });
  } catch (err) {
    res.status(500).json({ error: 'Failed to load companies' });
  }
});

router.get('/graph/debug', graphDebugHandler);

router.get('/route', (req: Request, res: Response) => {
  const game = resolveGame(req);
  const rt = getRuntimeOrError(res, game);
  if (!rt) return;

  const { fromX, fromZ, toX, toZ, fromHeading, avoidHighways, avoidFerries, mode, avoidPoints } = req.query;
  if (!fromX || !fromZ || !toX || !toZ) {
    res.status(400).json({ error: 'Required: fromX, fromZ, toX, toZ' });
    return;
  }

  const fx = parseFloat(fromX as string);
  const fz = parseFloat(fromZ as string);
  const tx = parseFloat(toX as string);
  const tz = parseFloat(toZ as string);
  const heading = fromHeading ? parseFloat(fromHeading as string) : 0;

  if (isNaN(fx) || isNaN(fz) || isNaN(tx) || isNaN(tz)) {
    res.status(400).json({ error: 'Required: fromX, fromZ, toX, toZ (numeric)' });
    return;
  }

  const { loadedGraph, componentIndex, spatialIndex } = rt;
  const { minX, maxX, minZ, maxZ } = loadedGraph.bounds;
  const isInMain = (uid: string) => componentIndex.isInMainComponent(uid);

  const marginX = (maxX - minX) * 0.1, marginZ = (maxZ - minZ) * 0.1;
  if (fx < minX - marginX || fx > maxX + marginX || fz < minZ - marginZ || fz > maxZ + marginZ) {
    res.status(404).json({ error: 'Start position outside map bounds' });
    return;
  }
  if (tx < minX - marginX || tx > maxX + marginX || tz < minZ - marginZ || tz > maxZ + marginZ) {
    res.status(404).json({ error: 'Destination outside map bounds' });
    return;
  }

  const headingSnap = fromHeading
    ? findNearestWithHeading(fx, fz, heading, spatialIndex, loadedGraph.nodes, loadedGraph.adjacency, loadedGraph.bounds)
    : undefined;
  const startUid = headingSnap && isInMain(headingSnap)
    ? headingSnap
    : findNearestMainComponent(fx, fz, spatialIndex, isInMain);

  if (!startUid) {
    res.status(404).json({ error: 'Start position not connected to main road network' });
    return;
  }

  const goalUid = findNearestMainComponent(tx, tz, spatialIndex, isInMain);
  if (!goalUid) {
    res.status(404).json({ error: 'Destination not connected to main road network' });
    return;
  }

  let blockedNodes: Set<string> | undefined;
  if (avoidPoints) {
    try {
      const pts: { x: number; z: number }[] = JSON.parse(avoidPoints as string);
      blockedNodes = new Set(
        pts.map(p => findNearestMainComponent(p.x, p.z, spatialIndex, isInMain)).filter((u): u is string => !!u)
      );
    } catch { /* ignore malformed input */ }
  }

  const routeOptions = {
    mode:          (mode === 'fastest' ? 'fastest' : 'shortest') as 'fastest' | 'shortest',
    avoidHighways: avoidHighways === 'true',
    avoidFerries:  avoidFerries  === 'true',
    blockedNodes,
  };

  const t0 = Date.now();
  const result = findRoute(startUid, goalUid, loadedGraph.nodes, loadedGraph.adjacency, routeOptions);
  const routeMs = Date.now() - t0;

  if (!result) {
    res.status(404).json({ error: 'No route found between the given positions' });
    return;
  }

  // ETS2/ATS display distances: game coordinate units × scale factor = virtual GPS km.
  // Land scale (~15.6 for ETS2): average across highways + compressed city areas.
  // Ferry scale (~19 for ETS2): ports sit on open coastline at full highway compression,
  //   so the full 1:19 factor applies rather than the city-blended average.
  const LAND_SCALE:  Record<string, number> = { ets2: 15.6, ats: 18.0 };
  const FERRY_SCALE: Record<string, number> = { ets2: 19.0, ats: 20.0 };
  const landScale  = LAND_SCALE[game]  ?? 15.6;
  const ferryScale = FERRY_SCALE[game] ?? 19.0;
  const landLengthKm  = Math.round(result.landLength  * landScale  / 1000);
  const ferryLengthKm = Math.round(result.ferryLength * ferryScale / 1000);
  const totalLengthKm = landLengthKm + ferryLengthKm;

  loadEdgePaths(game);
  const edgePaths = prefabPathsCache[game] ?? {};
  const geoJson = routeToGeoJson(result, loadedGraph.nodes, loadedGraph.bounds, edgePaths);
  res.json({
    game,
    mode: routeOptions.mode,
    nodeCount: result.path.length,
    totalLength: Math.round(result.totalLength),
    totalLengthKm,
    landLengthKm,
    ferryLengthKm,
    totalWeight: result.totalWeight,
    startUid,
    goalUid,
    routeMs,
    route: geoJson,
  });
});
