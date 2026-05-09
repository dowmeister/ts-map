import * as fs from 'fs';
import * as path from 'path';
import type { GraphEdge, GraphNode, LoadedGraph, MapBounds, MapProjection, RoutingGraph } from './types';

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
  const raw = fs.readFileSync(path.resolve(infoPath), 'utf-8');
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
  const raw = fs.readFileSync(resolved, 'utf-8');
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
