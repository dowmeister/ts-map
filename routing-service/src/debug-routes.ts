import * as fs from 'fs';
import * as path from 'path';
import { loadGraph, loadMapBounds } from './routing/loader';
import { DirectedComponentIndex, MainComponentIndex } from './routing/component';
import { SpatialIndex } from './routing/spatial-index';
import { findNearestMainComponent } from './routing/nearest';
import { wgs84ToGame } from './routing/coordinates';
import type { LoadedGraph } from './routing/types';

type PointKind = 'city' | 'company';

interface DebugPoint {
  kind: PointKind;
  label: string;
  id: string;
  x: number;
  z: number;
  anchorType?: string;
  startUid?: string;
  goalUid?: string;
  snapError?: string;
}

interface Options {
  game: string;
  mapDataPath: string;
  mode: 'all' | 'cities' | 'companies';
  maxIssues: number;
  candidateRadius: number;
  candidateLimit: number;
  jsonOut?: string;
}

interface PairIssue {
  from: DebugPoint;
  to: DebugPoint;
  goalUid?: string;
  reason: string;
}

interface DatasetResult {
  name: string;
  pointCount: number;
  checkedPairs: number;
  disconnectedPairs: number;
  snapIssues: DebugPoint[];
  pairIssues: PairIssue[];
  elapsedMs: number;
}

interface Runtime {
  graph: LoadedGraph;
  componentIndex: MainComponentIndex;
  directedComponentIndex: DirectedComponentIndex;
  spatialIndex: SpatialIndex;
  outgoingNodes: Set<string>;
  incomingNodes: Set<string>;
}

interface ArrivalCandidate {
  uid: string;
  component: number;
  distanceSq: number;
}

function parseArgs(argv: string[]): Options {
  const options: Options = {
    game: 'ets2',
    mapDataPath: process.env['MAP_DATA_PATH'] ?? path.resolve(__dirname, '..', '..', 'map_data'),
    mode: 'all',
    maxIssues: 100,
    candidateRadius: 2,
    candidateLimit: 96,
  };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const next = argv[i + 1];

    if (arg === '--help' || arg === '-h') {
      printHelp();
      process.exit(0);
    } else if ((arg === '--game' || arg === '-g') && next) {
      options.game = next;
      i++;
    } else if (arg.startsWith('--game=')) {
      options.game = arg.slice('--game='.length);
    } else if (arg === '--map-data' && next) {
      options.mapDataPath = path.resolve(next);
      i++;
    } else if (arg.startsWith('--map-data=')) {
      options.mapDataPath = path.resolve(arg.slice('--map-data='.length));
    } else if (arg === '--mode' && next) {
      options.mode = parseMode(next);
      i++;
    } else if (arg.startsWith('--mode=')) {
      options.mode = parseMode(arg.slice('--mode='.length));
    } else if (arg === '--max-issues' && next) {
      options.maxIssues = parsePositiveInt(next, '--max-issues');
      i++;
    } else if (arg.startsWith('--max-issues=')) {
      options.maxIssues = parsePositiveInt(arg.slice('--max-issues='.length), '--max-issues');
    } else if (arg === '--candidate-radius' && next) {
      options.candidateRadius = parsePositiveInt(next, '--candidate-radius');
      i++;
    } else if (arg.startsWith('--candidate-radius=')) {
      options.candidateRadius = parsePositiveInt(arg.slice('--candidate-radius='.length), '--candidate-radius');
    } else if (arg === '--candidate-limit' && next) {
      options.candidateLimit = parsePositiveInt(next, '--candidate-limit');
      i++;
    } else if (arg.startsWith('--candidate-limit=')) {
      options.candidateLimit = parsePositiveInt(arg.slice('--candidate-limit='.length), '--candidate-limit');
    } else if (arg === '--json-out' && next) {
      options.jsonOut = path.resolve(next);
      i++;
    } else if (arg.startsWith('--json-out=')) {
      options.jsonOut = path.resolve(arg.slice('--json-out='.length));
    } else {
      throw new Error(`Unknown argument: ${arg}`);
    }
  }

  return options;
}

function parseMode(value: string): Options['mode'] {
  if (value === 'all' || value === 'cities' || value === 'companies') return value;
  throw new Error(`Invalid --mode '${value}'. Expected: all, cities, companies`);
}

function parsePositiveInt(value: string, name: string): number {
  const parsed = parseInt(value, 10);
  if (!Number.isFinite(parsed) || parsed < 0) {
    throw new Error(`${name} must be a positive integer or 0`);
  }
  return parsed;
}

function printHelp(): void {
  console.log(`Routing graph connectivity debugger

Usage:
  npm run debug:routes -- --game ets2
  npm run debug:routes -- --game ets2 --mode cities
  npm run debug:routes -- --game promods --max-issues 500 --json-out route-debug.json

Options:
  --game, -g       Game folder under map_data (default: ets2)
  --map-data       Path to map_data (default: MAP_DATA_PATH or ../map_data)
  --mode           all, cities, companies (default: all)
  --max-issues     Max disconnected pairs printed/stored per dataset (default: 100, 0 = all)
  --candidate-radius  Spatial-index radius for destination snap candidates (default: 2)
  --candidate-limit   Max destination candidates kept after de-duping components (default: 96, 0 = all)
  --json-out       Write a JSON report
`);
}

function resolveGraphPath(mapDataPath: string, game: string): string {
  const newGraphPath = path.join(mapDataPath, game, 'routing', 'routing-graph.json');
  const oldGraphPath = path.join(mapDataPath, game, 'geojson', 'routing-graph.json');
  if (fs.existsSync(newGraphPath)) return newGraphPath;
  if (fs.existsSync(oldGraphPath)) return oldGraphPath;
  throw new Error(`routing-graph.json not found for '${game}' in ${mapDataPath}`);
}

function createRuntime(mapDataPath: string, game: string): Runtime {
  const bounds = loadMapBounds(mapDataPath, game);
  const graph = loadGraph(resolveGraphPath(mapDataPath, game), bounds);

  const componentIndex = new MainComponentIndex();
  componentIndex.build(graph.nodes, graph.adjacency);

  const spatialIndex = new SpatialIndex(graph.nodes, graph.bounds);
  const outgoingNodes = new Set<string>();
  const incomingNodes = new Set<string>();

  for (const edges of Object.values(graph.adjacency)) {
    for (const edge of edges) {
      outgoingNodes.add(edge.from);
      incomingNodes.add(edge.to);
    }
  }

  const directedComponentIndex = new DirectedComponentIndex();
  directedComponentIndex.build(graph.nodes, graph.adjacency);

  return { graph, componentIndex, directedComponentIndex, spatialIndex, outgoingNodes, incomingNodes };
}

function loadCities(mapDataPath: string, game: string, graph: LoadedGraph): DebugPoint[] {
  const citiesPath = path.join(mapDataPath, game, 'geojson', 'cities.geojson');
  const raw = JSON.parse(fs.readFileSync(citiesPath, 'utf8')) as {
    features?: Array<{
      geometry?: { type?: string; coordinates?: [number, number] };
      properties?: { name?: string; localized_name?: string; token?: number | string };
    }>;
  };

  const cities = (raw.features ?? [])
    .filter(f => f.geometry?.type === 'Point' && f.geometry.coordinates && f.properties?.name)
    .map(f => {
      const [lon, lat] = f.geometry!.coordinates!;
      const [x, z] = wgs84ToGame(lon, lat, graph.bounds);
      return {
        kind: 'city' as const,
        label: f.properties!.localized_name ?? f.properties!.name!,
        id: String(f.properties!.token ?? f.properties!.name!),
        x,
        z,
        anchorType: 'city-label',
      };
    });

  const anchorsByCity = new Map<number, Array<{ x: number; z: number; type: string; priority: number; distanceSq: number }>>();
  const addAnchor = (x: number, z: number, type: string, priority: number): void => {
    let bestIndex = -1;
    let bestDistanceSq = Infinity;
    for (let i = 0; i < cities.length; i++) {
      const dx = cities[i].x - x;
      const dz = cities[i].z - z;
      const distanceSq = dx * dx + dz * dz;
      if (distanceSq < bestDistanceSq) {
        bestDistanceSq = distanceSq;
        bestIndex = i;
      }
    }
    if (bestIndex < 0) return;
    const list = anchorsByCity.get(bestIndex) ?? [];
    list.push({ x, z, type, priority, distanceSq: bestDistanceSq });
    anchorsByCity.set(bestIndex, list);
  };

  loadOverlayAnchors(mapDataPath, game, graph, addAnchor);
  loadCompanyAnchors(mapDataPath, game, graph, addAnchor);

  return cities.map((city, index) => {
    const anchors = anchorsByCity.get(index) ?? [];
    anchors.sort((a, b) => a.priority - b.priority || a.distanceSq - b.distanceSq);
    const anchor = anchors[0];
    if (!anchor) return city;

    return {
      ...city,
      x: anchor.x,
      z: anchor.z,
      anchorType: anchor.type,
      label: `${city.label} (${anchor.type})`,
    };
  });
}

function loadOverlayAnchors(
  mapDataPath: string,
  game: string,
  graph: LoadedGraph,
  addAnchor: (x: number, z: number, type: string, priority: number) => void,
): void {
  const filePath = path.join(mapDataPath, game, 'geojson', 'overlays.geojson');
  if (!fs.existsSync(filePath)) return;

  const priorityByType: Record<string, number> = {
    Garage: 1,
    Service: 2,
    Fuel: 3,
    TruckDealer: 4,
    Hotel: 5,
    'Bus Stop': 6,
  };
  const raw = JSON.parse(fs.readFileSync(filePath, 'utf8')) as {
    features?: Array<{
      geometry?: { type?: string; coordinates?: [number, number] };
      properties?: { type_name?: string };
    }>;
  };

  for (const feature of raw.features ?? []) {
    const type = feature.properties?.type_name;
    const priority = type == null ? undefined : priorityByType[type];
    if (priority == null || feature.geometry?.type !== 'Point' || !feature.geometry.coordinates) continue;

    const [lon, lat] = feature.geometry.coordinates;
    const [x, z] = wgs84ToGame(lon, lat, graph.bounds);
    addAnchor(x, z, type!, priority);
  }
}

function loadCompanyAnchors(
  mapDataPath: string,
  game: string,
  graph: LoadedGraph,
  addAnchor: (x: number, z: number, type: string, priority: number) => void,
): void {
  const filePath = path.join(mapDataPath, game, 'geojson', 'companies.geojson');
  if (!fs.existsSync(filePath)) return;

  const raw = JSON.parse(fs.readFileSync(filePath, 'utf8')) as {
    features?: Array<{ geometry?: { type?: string; coordinates?: [number, number] } }>;
  };

  for (const feature of raw.features ?? []) {
    if (feature.geometry?.type !== 'Point' || !feature.geometry.coordinates) continue;
    const [lon, lat] = feature.geometry.coordinates;
    const [x, z] = wgs84ToGame(lon, lat, graph.bounds);
    addAnchor(x, z, 'Company', 7);
  }
}

function loadCompanies(mapDataPath: string, game: string, graph: LoadedGraph): DebugPoint[] {
  const filePath = path.join(mapDataPath, game, 'geojson', 'companies.geojson');
  const raw = JSON.parse(fs.readFileSync(filePath, 'utf8')) as {
    features?: Array<{
      geometry?: { type?: string; coordinates?: [number, number] };
      properties?: { name?: string; id?: string };
    }>;
  };

  return (raw.features ?? [])
    .filter(f => f.geometry?.type === 'Point' && f.geometry.coordinates && f.properties?.name)
    .map((f, index) => {
      const [lon, lat] = f.geometry!.coordinates!;
      const [x, z] = wgs84ToGame(lon, lat, graph.bounds);
      const id = f.properties?.id ?? `company-${index}`;
      return {
        kind: 'company',
        label: `${f.properties!.name} (${id}) #${index + 1}`,
        id,
        x,
        z,
      };
    });
}

function snapPoints(points: DebugPoint[], rt: Runtime): void {
  const isInMain = (uid: string) => rt.componentIndex.isInMainComponent(uid);
  const canDepart = (uid: string) => rt.outgoingNodes.has(uid);
  const canArrive = (uid: string) => rt.incomingNodes.has(uid);

  for (const point of points) {
    point.startUid = findNearestMainComponent(point.x, point.z, rt.spatialIndex, isInMain, canDepart);
    point.goalUid = findNearestMainComponent(point.x, point.z, rt.spatialIndex, isInMain, canArrive);

    if (!point.startUid && !point.goalUid) {
      point.snapError = 'not connected as origin or destination';
    } else if (!point.startUid) {
      point.snapError = 'not connected as origin';
    } else if (!point.goalUid) {
      point.snapError = 'not connected as destination';
    }
  }
}

function buildArrivalCandidates(
  points: DebugPoint[],
  rt: Runtime,
  candidateRadius: number,
  candidateLimit: number,
): ArrivalCandidate[][] {
  const isInMain = (uid: string) => rt.componentIndex.isInMainComponent(uid);
  const canArrive = (uid: string) => rt.incomingNodes.has(uid);

  return points.map(point => {
    const seen = new Set<string>();
    const bestByComponent = new Map<number, ArrivalCandidate>();

    for (let radius = 1; radius <= candidateRadius; radius++) {
      for (const candidate of rt.spatialIndex.findCandidates(point.x, point.z, radius)) {
        if (seen.has(candidate.uid)) continue;
        seen.add(candidate.uid);

        if (!isInMain(candidate.uid) || !canArrive(candidate.uid)) continue;
        const component = rt.directedComponentIndex.componentOf(candidate.uid);
        if (component == null) continue;

        const dx = candidate.x - point.x;
        const dz = candidate.z - point.z;
        const arrival = { uid: candidate.uid, component, distanceSq: dx * dx + dz * dz };
        const current = bestByComponent.get(component);
        if (!current || arrival.distanceSq < current.distanceSq) {
          bestByComponent.set(component, arrival);
        }
      }
    }

    const candidates = [...bestByComponent.values()].sort((a, b) => a.distanceSq - b.distanceSq);
    return candidateLimit === 0 ? candidates : candidates.slice(0, candidateLimit);
  });
}

function analyzeDataset(name: string, points: DebugPoint[], rt: Runtime, options: Options): DatasetResult {
  const startedAt = Date.now();
  const snapIssues = points.filter(p => p.snapError);
  const routableOrigins = points.filter(p => p.startUid);
  const arrivalCandidates = buildArrivalCandidates(points, rt, options.candidateRadius, options.candidateLimit);
  const reachableGoalCache = new Map<string, ArrivalCandidate | undefined>();
  const pairIssues: PairIssue[] = [];
  let checkedPairs = 0;
  let disconnectedPairs = 0;

  for (const from of routableOrigins) {
    const startComponent = rt.directedComponentIndex.componentOf(from.startUid!);
    if (startComponent == null) continue;

    for (let toIndex = 0; toIndex < points.length; toIndex++) {
      const to = points[toIndex];
      if (from === to) continue;
      checkedPairs++;

      const cacheKey = `${startComponent}:${toIndex}`;
      let goal: ArrivalCandidate | undefined;
      if (reachableGoalCache.has(cacheKey)) {
        goal = reachableGoalCache.get(cacheKey);
      } else {
        goal = arrivalCandidates[toIndex].find(candidate =>
          rt.directedComponentIndex.canReachComponentId(startComponent, candidate.component),
        );
        reachableGoalCache.set(cacheKey, goal);
      }

      if (!goal) {
        disconnectedPairs++;
        if (options.maxIssues === 0 || pairIssues.length < options.maxIssues) {
          pairIssues.push({ from, to, reason: 'no reachable destination snap' });
        }
      }
    }
  }

  return {
    name,
    pointCount: points.length,
    checkedPairs,
    disconnectedPairs,
    snapIssues,
    pairIssues,
    elapsedMs: Date.now() - startedAt,
  };
}

function printResult(result: DatasetResult): void {
  const ok = result.disconnectedPairs === 0 && result.snapIssues.length === 0;
  console.log('');
  console.log(`[${result.name}] ${ok ? 'OK' : 'DISCONNECTIONS FOUND'}`);
  console.log(`  Points: ${result.pointCount}`);
  console.log(`  Checked ordered pairs: ${result.checkedPairs}`);
  console.log(`  Snap issues: ${result.snapIssues.length}`);
  console.log(`  Disconnected pairs: ${result.disconnectedPairs}`);
  console.log(`  Time: ${result.elapsedMs}ms`);

  for (const point of result.snapIssues) {
    console.log(`  SNAP: ${point.label} [${point.id}] anchor=${point.anchorType ?? 'n/a'} at (${point.x.toFixed(1)}, ${point.z.toFixed(1)}) - ${point.snapError}`);
  }

  for (const issue of result.pairIssues) {
    console.log(
      `  ROUTE: ${issue.from.label} [${issue.from.startUid}] -> ` +
      `${issue.to.label} [${issue.goalUid ?? issue.to.goalUid ?? 'no-goal'}] - ${issue.reason}`,
    );
  }

  if (result.disconnectedPairs > result.pairIssues.length) {
    console.log(`  ... ${result.disconnectedPairs - result.pairIssues.length} more disconnected pairs hidden by --max-issues`);
  }
}

async function main(): Promise<void> {
  const options = parseArgs(process.argv.slice(2));
  const startedAt = Date.now();

  console.log(`[RoutingDebug] game=${options.game}`);
  console.log(`[RoutingDebug] map_data=${options.mapDataPath}`);

  const rt = createRuntime(options.mapDataPath, options.game);
  const mainPct = rt.graph.nodeCount > 0 ? (rt.componentIndex.mainSize / rt.graph.nodeCount * 100).toFixed(2) : '0.00';
  console.log(`[RoutingDebug] graph nodes=${rt.graph.nodeCount} edges=${rt.graph.edgeCount}`);
  console.log(`[RoutingDebug] main component=${rt.componentIndex.mainSize} (${mainPct}%)`);
  console.log(
    `[RoutingDebug] directed components=${rt.directedComponentIndex.componentCount} ` +
    `largest=${rt.directedComponentIndex.largestComponentSize}`,
  );
  console.log(`[RoutingDebug] destination candidates radius=${options.candidateRadius} limit=${options.candidateLimit || 'all'}`);

  const results: DatasetResult[] = [];

  if (options.mode === 'all' || options.mode === 'cities') {
    const cities = loadCities(options.mapDataPath, options.game, rt.graph);
    snapPoints(cities, rt);
    results.push(analyzeDataset('city-city', cities, rt, options));
  }

  if (options.mode === 'all' || options.mode === 'companies') {
    const companies = loadCompanies(options.mapDataPath, options.game, rt.graph);
    snapPoints(companies, rt);
    results.push(analyzeDataset('company-company', companies, rt, options));
  }

  for (const result of results) printResult(result);

  const report = {
    game: options.game,
    mapDataPath: options.mapDataPath,
    graph: {
      nodeCount: rt.graph.nodeCount,
      edgeCount: rt.graph.edgeCount,
      mainComponentSize: rt.componentIndex.mainSize,
      directedComponentCount: rt.directedComponentIndex.componentCount,
      largestDirectedComponentSize: rt.directedComponentIndex.largestComponentSize,
    },
    results,
    elapsedMs: Date.now() - startedAt,
  };

  if (options.jsonOut) {
    fs.writeFileSync(options.jsonOut, JSON.stringify(report, null, 2));
    console.log('');
    console.log(`[RoutingDebug] JSON report written to ${options.jsonOut}`);
  }

  const hasIssues = results.some(r => r.disconnectedPairs > 0 || r.snapIssues.length > 0);
  process.exitCode = hasIssues ? 2 : 0;
}

main().catch(err => {
  console.error(`[RoutingDebug] ${err instanceof Error ? err.message : String(err)}`);
  process.exitCode = 1;
});
