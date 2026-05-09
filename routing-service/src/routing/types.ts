export interface GraphNode {
  uid: string;
  x: number;
  z: number;
}

export interface GraphEdge {
  from: string;
  to: string;
  weight: number;
  length: number;
  speedClass: string;
  speedLimitKph?: number;
  itemType: string;
}

export interface RoutingGraph {
  meta: {
    nodeCount: number;
    edgeCount: number;
    generatedAt: string;
  };
  nodes: GraphNode[];
  edges: GraphEdge[];
}

export interface RouteResult {
  path: string[];
  totalWeight: number;
  totalLength: number;
  landLength: number;    // road + prefab + approach edges
  ferryLength: number;   // ferry edges only
}

export interface MapProjection {
  type: string;
  standard_parallel_1: number;
  standard_parallel_2: number;
  map_origin: [number, number];
  map_offset: [number, number];
  map_factor: [number, number];
  use_ets2_uk_scale?: boolean;
}

export interface MapBounds {
  minX: number;
  maxX: number;
  minZ: number;
  maxZ: number;
  projection?: MapProjection;
}

export interface LoadedGraph {
  nodes: Record<string, GraphNode>;
  adjacency: Record<string, GraphEdge[]>;
  bounds: MapBounds;
  nodeCount: number;
  edgeCount: number;
}

export type RouteMode = 'fastest' | 'shortest';

export interface RouteOptions {
  mode?: RouteMode;
  avoidHighways?: boolean;
  avoidFerries?: boolean;
  blockedNodes?: Set<string>;
}
