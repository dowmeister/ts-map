import * as fs from 'fs';
import * as path from 'path';
import * as zlib from 'zlib';
import type { GraphEdge, GraphNode, LoadedGraph, MapBounds, MapProjection, RoutingGraph } from './types';

// Read a JSON file that may be stored gzipped. If the exact path does not
// exist, a sibling `<path>.gz` is tried. Gzipped files are decompressed in
// memory — no temp files are written. Used so the production image can bake
// the large routing graphs compressed (~770MB raw → ~80-100MB on disk).
function readJsonText(filePath: string): string {
  let actual = filePath;
  if (!fs.existsSync(actual)) {
    if (fs.existsSync(filePath + '.gz')) actual = filePath + '.gz';
    else throw new Error(`File not found: ${filePath} (and no .gz sibling)`);
  }
  const buf = fs.readFileSync(actual);
  return actual.endsWith('.gz') ? zlib.gunzipSync(buf).toString('utf-8') : buf.toString('utf-8');
}

// Locate a game's routing graph, preferring the `routing/` layout over the
// legacy `geojson/` one, and transparently accepting gzipped (`.gz`) files.
// Returns the path to pass to loadGraph, or null if none exists.
export function findRoutingGraph(mapDataPath: string, game: string): string | null {
  const candidates = [
    path.join(mapDataPath, game, 'routing', 'routing-graph.json'),
    path.join(mapDataPath, game, 'geojson', 'routing-graph.json'),
  ];
  for (const c of candidates) {
    if (fs.existsSync(c)) return c;
    if (fs.existsSync(c + '.gz')) return c + '.gz';
  }
  return null;
}

interface VectorTileMapInfo {
  x1: number;  // minX
  x2: number;  // maxX
  y1: number;  // minZ  (game Z axis)
  y2: number;  // maxZ
  projection?: MapProjection;
}

// Read VectorTileMapInfo.json — the authoritative map bounds used by the web viewer.
// These bounds are used for WGS84 projection and spatial index sizing.
export function loadMapBounds(mapDataPath: string, game: string): MapBounds {
  const infoPath = path.join(mapDataPath, game, 'VectorTileMapInfo.json');
  const raw = readJsonText(path.resolve(infoPath));
  const info: VectorTileMapInfo = JSON.parse(raw) as VectorTileMapInfo;
  return {
    minX: info.x1,
    maxX: info.x2,
    minZ: info.y1,
    maxZ: info.y2,
    projection: info.projection,
  };
}

export function loadGraph(graphPath: string, bounds: MapBounds): LoadedGraph {
  const resolved = path.resolve(graphPath);
  const raw = readJsonText(resolved);
  const graph: RoutingGraph = JSON.parse(raw) as RoutingGraph;

  const nodes: Record<string, GraphNode> = {};
  for (const node of graph.nodes) {
    nodes[node.uid] = node;
  }

  const adjacency: Record<string, GraphEdge[]> = {};
  for (const edge of graph.edges) {
    if (!adjacency[edge.from]) adjacency[edge.from] = [];
    adjacency[edge.from].push(edge);
  }

  return {
    nodes,
    adjacency,
    bounds,
    nodeCount: graph.meta.nodeCount,
    edgeCount: graph.meta.edgeCount,
  };
}
