import * as fs from 'fs';
import * as path from 'path';
import { Router, Request, Response } from 'express';
import { loadGraph, loadMapBounds, findRoutingGraph } from './loader';
import { setGraphState, getAvailableGames } from './state';
import { graphDebugHandler, laneGraphDebugHandler, laneGraphIssuesHandler, loadSelectedEdgePaths } from './debug';
import { DirectedComponentIndex, MainComponentIndex } from './component';
import { reconnectDirectionalTraps } from './reconnect';
import { SpatialIndex } from './spatial-index';
import { findNearestMainComponent, findNearestReachableMainComponent, findNearestWithHeading } from './nearest';
import { findRoute } from './astar';
import { ets2ToWgs84, wgs84ToGame, routeToGeoJson } from './coordinates';
import { buildManeuvers } from './maneuvers';
import type { GraphEdge, LoadedGraph } from './types';

// ── Per-game runtime state (routing-specific, not shared with debug) ──────────

interface GameRuntime {
  loadedGraph: LoadedGraph;
  componentIndex: MainComponentIndex;
  directedComponentIndex: DirectedComponentIndex;
  spatialIndex: SpatialIndex;
  outgoingNodes: Set<string>;
  incomingNodes: Set<string>;
  // Directed components from which the giant SCC is reachable. A start node must
  // live in one of these, otherwise it can route almost nowhere.
  escapeSet: Set<number>;
}

const runtimes: Record<string, GameRuntime> = {};
const companiesCache: Record<string, { name: string; id: string; x: number; z: number }[]> = {};
const ROUTING_DEBUG_LOG = process.env['ROUTING_DEBUG_LOG'] !== 'false';

let _mapDataPath: string;

function getMapDataPath(): string {
  return _mapDataPath ?? (_mapDataPath = process.env['MAP_DATA_PATH'] ?? path.resolve(__dirname, '../../..', 'map_data'));
}

function logRoute(message: string): void {
  if (ROUTING_DEBUG_LOG) console.log(message);
}

// ── Startup ───────────────────────────────────────────────────────────────────

function discoverGames(mapDataPath: string): string[] {
  try {
    return fs.readdirSync(mapDataPath).filter(dir => {
      try {
        return fs.statSync(path.join(mapDataPath, dir)).isDirectory() &&
               findRoutingGraph(mapDataPath, dir) !== null;
      } catch { return false; }
    });
  } catch {
    return ['ets2'];
  }
}

function loadGame(mapDataPath: string, game: string): void {
  const graphPath = findRoutingGraph(mapDataPath, game);
  if (!graphPath) throw new Error(`routing-graph.json not found for '${game}' in ${mapDataPath}`);
  console.log(`[Routing:${game}] Loading bounds...`);
  const bounds = loadMapBounds(mapDataPath, game);
  console.log(`[Routing:${game}] Bounds X[${bounds.minX.toFixed(0)},${bounds.maxX.toFixed(0)}] Z[${bounds.minZ.toFixed(0)},${bounds.maxZ.toFixed(0)}]`);

  const t1 = Date.now();
  const loadedGraph = loadGraph(graphPath, bounds);
  console.log(`[Routing:${game}] Graph loaded in ${Date.now()-t1}ms — ${loadedGraph.nodeCount} nodes, ${loadedGraph.edgeCount} edges`);

  const t2 = Date.now();
  const componentIndex = new MainComponentIndex();
  componentIndex.build(loadedGraph.nodes, loadedGraph.adjacency);
  const mainPct = (componentIndex.mainSize / loadedGraph.nodeCount * 100).toFixed(2);
  console.log(`[Routing:${game}] Main component: ${componentIndex.mainSize} nodes (${mainPct}%) in ${Date.now()-t2}ms`);

  // Repair directional traps (e.g. the far north of ETS2): add the missing
  // reverse junction connectors so trapped-but-connected regions can reach the
  // giant SCC. Must run before the directed component index is built.
  const t2a = Date.now();
  const repair = reconnectDirectionalTraps(loadedGraph, componentIndex);
  console.log(`[Routing:${game}] Trap repair: +${repair.reverseEdgesAdded} reverse connectors for ${repair.repairedNodesBefore} non-giant-SCC nodes in ${Date.now()-t2a}ms`);

  const t2b = Date.now();
  const directedComponentIndex = new DirectedComponentIndex();
  directedComponentIndex.build(loadedGraph.nodes, loadedGraph.adjacency);
  console.log(`[Routing:${game}] Directed components: ${directedComponentIndex.componentCount} (largest ${directedComponentIndex.largestComponentSize}) in ${Date.now()-t2b}ms`);

  // Components from which the giant SCC is reachable. Used to snap the start to a
  // node that can actually reach the rest of the network (symmetric to the
  // reachability-aware goal snap).
  const escapeSet = directedComponentIndex.componentsThatCanReach(directedComponentIndex.largestComponentId);
  console.log(`[Routing:${game}] Escape set: ${escapeSet.size} components can reach the giant SCC`);

  const t3 = Date.now();
  const spatialIndex = new SpatialIndex(loadedGraph.nodes, loadedGraph.bounds);
  console.log(`[Routing:${game}] Spatial index: ${spatialIndex.cellCount} cells in ${Date.now()-t3}ms`);

  const outgoingNodes = new Set<string>();
  const incomingNodes = new Set<string>();
  for (const edges of Object.values(loadedGraph.adjacency)) {
    for (const edge of edges) {
      outgoingNodes.add(edge.from);
      incomingNodes.add(edge.to);
    }
  }

  const [parisLon, parisLat] = ets2ToWgs84(-22674, -16800, loadedGraph.bounds);
  console.log(`[Routing:${game}] WGS84 check (-22674,-16800): lon=${parisLon.toFixed(2)} lat=${parisLat.toFixed(2)}`);

  runtimes[game] = { loadedGraph, componentIndex, directedComponentIndex, spatialIndex, outgoingNodes, incomingNodes, escapeSet };
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
router.get('/lane-graph/debug', laneGraphDebugHandler);
router.get('/lane-graph/issues', laneGraphIssuesHandler);

router.get('/route', async (req: Request, res: Response) => {
  const requestT0 = Date.now();
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
  const routeMode = (mode === 'fastest' ? 'fastest' : 'shortest') as 'fastest' | 'shortest';

  logRoute(`[Route:${game}] request mode=${routeMode} from=(${fx.toFixed(1)},${fz.toFixed(1)}) to=(${tx.toFixed(1)},${tz.toFixed(1)}) heading=${fromHeading ? heading.toFixed(1) : 'none'} avoidHighways=${avoidHighways === 'true'} avoidFerries=${avoidFerries === 'true'}`);

  if (isNaN(fx) || isNaN(fz) || isNaN(tx) || isNaN(tz)) {
    res.status(400).json({ error: 'Required: fromX, fromZ, toX, toZ (numeric)' });
    return;
  }

  const { loadedGraph, componentIndex, directedComponentIndex, spatialIndex, outgoingNodes, incomingNodes } = rt;
  const { minX, maxX, minZ, maxZ } = loadedGraph.bounds;
  const isInMain = (uid: string) => componentIndex.isInMainComponent(uid);
  const canArrive = (uid: string) => incomingNodes.has(uid);
  // A start node is only useful if it can reach the giant SCC; otherwise it gets
  // stranded in a tiny directed dead-end and almost no destination is routable.
  const canDepartAndEscape = (uid: string) => {
    if (!outgoingNodes.has(uid)) return false;
    const component = directedComponentIndex.componentOf(uid);
    return component != null && rt.escapeSet.has(component);
  };

  const marginX = (maxX - minX) * 0.1, marginZ = (maxZ - minZ) * 0.1;
  if (fx < minX - marginX || fx > maxX + marginX || fz < minZ - marginZ || fz > maxZ + marginZ) {
    logRoute(`[Route:${game}] rejected: start outside bounds`);
    res.status(404).json({ error: 'Start position outside map bounds' });
    return;
  }
  if (tx < minX - marginX || tx > maxX + marginX || tz < minZ - marginZ || tz > maxZ + marginZ) {
    logRoute(`[Route:${game}] rejected: destination outside bounds`);
    res.status(404).json({ error: 'Destination outside map bounds' });
    return;
  }

  const snapT0 = Date.now();
  const headingSnap = fromHeading
    ? findNearestWithHeading(fx, fz, heading, spatialIndex, loadedGraph.nodes, loadedGraph.adjacency, loadedGraph.bounds)
    : undefined;
  const startUid = headingSnap && isInMain(headingSnap) && canDepartAndEscape(headingSnap)
    ? headingSnap
    : findNearestMainComponent(fx, fz, spatialIndex, isInMain, canDepartAndEscape);
  const snapMs = Date.now() - snapT0;

  if (!startUid) {
    logRoute(`[Route:${game}] rejected: start not connected snapMs=${snapMs}`);
    res.status(404).json({ error: 'Start position not connected to main road network' });
    return;
  }

  const goalSnapT0 = Date.now();
  const startComponent = directedComponentIndex.componentOf(startUid);
  const goalUid = startComponent == null
    ? undefined
    : findNearestReachableMainComponent(
        tx,
        tz,
        spatialIndex,
        isInMain,
        canArrive,
        uid => directedComponentIndex.canReachComponent(startComponent, uid),
      );
  const goalSnapMs = Date.now() - goalSnapT0;
  if (!goalUid) {
    logRoute(`[Route:${game}] rejected: destination not connected start=${startUid} snapMs=${snapMs} goalSnapMs=${goalSnapMs}`);
    res.status(404).json({ error: 'Destination not connected to main road network' });
    return;
  }
  logRoute(`[Route:${game}] snapped start=${startUid}${headingSnap ? ` headingSnap=${headingSnap}` : ''} goal=${goalUid} snapMs=${snapMs}+${goalSnapMs}`);

  let blockedNodes: Set<string> | undefined;
  if (avoidPoints) {
    try {
      const pts: { x: number; z: number }[] = JSON.parse(avoidPoints as string);
      blockedNodes = new Set(
        pts.map(p => findNearestMainComponent(p.x, p.z, spatialIndex, isInMain)).filter((u): u is string => !!u)
      );
      logRoute(`[Route:${game}] avoidPoints=${pts.length} blockedNodes=${blockedNodes.size}`);
    } catch (err) {
      logRoute(`[Route:${game}] malformed avoidPoints ignored: ${err instanceof Error ? err.message : String(err)}`);
    }
  }

  const routeOptions = {
    mode:          routeMode,
    avoidHighways: avoidHighways === 'true',
    avoidFerries:  avoidFerries  === 'true',
    blockedNodes,
  };

  const t0 = Date.now();
  const result = findRoute(startUid, goalUid, loadedGraph.nodes, loadedGraph.adjacency, routeOptions);
  const routeMs = Date.now() - t0;

  if (!result) {
    logRoute(`[Route:${game}] no route start=${startUid} goal=${goalUid} routeMs=${routeMs} totalMs=${Date.now() - requestT0}`);
    res.status(404).json({ error: 'No route found between the given positions' });
    return;
  }

  // ETS2/ATS display distances: game coordinate units × scale factor = virtual GPS km.
  // NOTE (2026-07-08): empirically calibrated from a CONFIRMED-identical route
  // (Dortmund→Berlin, flat/highway, ETS2): raw=28984m, in-game=460km → scale≈15.87.
  // An earlier "uniform 1:19" assumption was tested against an UNCONFIRMED route
  // (Kassel) that implied ~22.95 — discarded as unreliable (route likely diverged
  // from the in-game path, or included under-sampled curvy/mountain geometry).
  // Land scale stays LOWER than the ferry scale because city/urban sections are
  // far less compressed than highways; ferry scale is still unverified (no
  // confirmed-route ferry sample yet) and kept at the prior working estimate.
  // TODO: gather more confirmed same-route samples (mountain, urban-heavy,
  // ferry) before trusting a single scale further.
  const LAND_SCALE:  Record<string, number> = { ets2: 15.87, ats: 18.0 };
  const FERRY_SCALE: Record<string, number> = { ets2: 19.0,  ats: 20.0 };
  const landScale  = LAND_SCALE[game]  ?? 15.87;
  const ferryScale = FERRY_SCALE[game] ?? 19.0;
  const landLengthKm  = Math.round(result.landLength  * landScale  / 1000);
  // Ferries: prefer the OFFICIAL distance from the game's own ferry connection
  // defs (same figure shown in the in-game ferry booking dialog) — no scale
  // guessing needed. Only fall back to the estimated scale for ferry edges that
  // don't carry official data (e.g. unresolved/edge-case connections).
  const ferryLengthKm = Math.round(
    result.officialFerryDistanceKm + (result.unofficialFerryLength * ferryScale / 1000)
  );
  const totalLengthKm = landLengthKm + ferryLengthKm;

  const edgeKeys: string[] = [];
  for (let i = 0; i < result.path.length - 1; i++) {
    edgeKeys.push(`${result.path[i]}-${result.path[i + 1]}`);
  }
  const edgePaths = await loadSelectedEdgePaths(game, edgeKeys);
  const geoT0 = Date.now();
  const geoJson = routeToGeoJson(result, loadedGraph.nodes, loadedGraph.bounds, edgePaths);
  const geoMs = Date.now() - geoT0;
  const maneuvers = buildManeuvers(
    result,
    loadedGraph.nodes,
    loadedGraph.adjacency,
    loadedGraph.bounds,
    edgePaths,
    landScale,
  );
  logRoute(`[Route:${game}] ok start=${startUid} goal=${goalUid} nodes=${result.path.length} length=${Math.round(result.totalLength)}m km=${totalLengthKm} routeMs=${routeMs} geoMs=${geoMs} totalMs=${Date.now() - requestT0}`);
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
    maneuvers,
  });
});
