import type { GraphEdge, GraphNode, RouteResult, RouteOptions } from './types';

// Binary min-heap ordered by f-score.
// Implements push O(log n) and pop O(log n) with no external dependencies.
// Uses lazy deletion to handle re-insertions when a better path is found.
class BinaryMinHeap {
  private readonly _data: Array<{ uid: string; g: number; f: number }> = [];

  get size(): number { return this._data.length; }

  push(uid: string, g: number, f: number): void {
    this._data.push({ uid, g, f });
    this._siftUp(this._data.length - 1);
  }

  pop(): { uid: string; g: number; f: number } | undefined {
    if (this._data.length === 0) return undefined;
    const top = this._data[0];
    const last = this._data.pop()!;
    if (this._data.length > 0) {
      this._data[0] = last;
      this._siftDown(0);
    }
    return top;
  }

  private _siftUp(i: number): void {
    while (i > 0) {
      const parent = (i - 1) >> 1;
      if (this._data[parent].f <= this._data[i].f) break;
      [this._data[parent], this._data[i]] = [this._data[i], this._data[parent]];
      i = parent;
    }
  }

  private _siftDown(i: number): void {
    const n = this._data.length;
    for (;;) {
      let smallest = i;
      const l = 2 * i + 1, r = 2 * i + 2;
      if (l < n && this._data[l].f < this._data[smallest].f) smallest = l;
      if (r < n && this._data[r].f < this._data[smallest].f) smallest = r;
      if (smallest === i) break;
      [this._data[smallest], this._data[i]] = [this._data[i], this._data[smallest]];
      i = smallest;
    }
  }
}

// Plain A* with an admissible Euclidean heuristic explores a huge fraction of the
// (1M+ node) lane graph on continent-scale routes, because the straight-line distance
// badly under-estimates the real driving cost when the path has to wrap around seas and
// mountain ranges. We use a lightly weighted heuristic (weighted A* / ε-admissible) to
// focus the frontier toward the goal: the returned path can be at most HEURISTIC_WEIGHT×
// the optimal cost, but in practice stays within a couple of percent on this road network
// while cutting expansions by an order of magnitude on long routes.
const HEURISTIC_WEIGHT = 1.6;
const MAX_EXPANSIONS = 2_000_000;
const LANE_CHANGE_COST_METERS = 10;
const LOCAL_DETOUR_MULTIPLIER_IN_SHORTEST = 1.45;
// Penalty for the first step off a motorway/expressway onto a local-speed prefab
// (service area, slip road, etc.). Without this, the lane-based graph lets the
// A* shortcut through service areas that are geometrically shorter than the
// slight highway curve they bypass.
const HIGHWAY_EXIT_PENALTY_METERS = 500;
const FERRY_BOARDING_PENALTY_METERS = 5_000;
const FERRY_FALLBACK_PENALTY_METERS = 150_000;
const FERRY_FALLBACK_MULTIPLIER = 1.5;
const FERRY_TRANSFER_PENALTY = 1_000_000;

function hasOfficialFerryCost(edge: GraphEdge): boolean {
  return edge.itemType === 'ferry' &&
    ((edge.ferryTimeMinutes ?? 0) > 0 || (edge.ferryDistanceKm ?? 0) > 0);
}

function heuristic(a: GraphNode, b: GraphNode): number {
  const dx = a.x - b.x, dz = a.z - b.z;
  return Math.sqrt(dx * dx + dz * dz);
}

function shortestBaseCost(edge: GraphEdge): number {
  if (edge.itemType === 'ferry') {
    if (hasOfficialFerryCost(edge)) {
      return edge.length + FERRY_BOARDING_PENALTY_METERS;
    }
    return edge.length * FERRY_FALLBACK_MULTIPLIER + FERRY_FALLBACK_PENALTY_METERS;
  }

  if (edge.itemType === 'lane_change') {
    return edge.length + LANE_CHANGE_COST_METERS;
  }

  // "shortest" should still avoid silly service-area/local-road cuts when a
  // legal lane change on the main carriageway is enough. Keep this mild so
  // city routing and genuine exits still work.
  if (edge.speedClass === 'local_road' &&
      edge.itemType !== 'lane_link' &&
      edge.itemType !== 'company_approach' &&
      edge.itemType !== 'ferry_approach') {
    return edge.length * LOCAL_DETOUR_MULTIPLIER_IN_SHORTEST;
  }

  return edge.length;
}

export function findRoute(
  startUid: string,
  goalUid: string,
  nodes: Record<string, GraphNode>,
  adjacency: Record<string, GraphEdge[]>,
  options?: RouteOptions,
): RouteResult | null {
  const startNode = nodes[startUid];
  const goalNode = nodes[goalUid];
  if (!startNode || !goalNode) return null;

  const heap = new BinaryMinHeap();
  const gScore = new Map<string, number>();
  // cameFrom[uid] = the uid of the node we came from
  const cameFrom = new Map<string, string>();
  // cameFromEdge[uid] = the edge used to reach uid (for weight/length accumulation)
  const cameFromEdge = new Map<string, GraphEdge>();

  gScore.set(startUid, 0);
  heap.push(startUid, 0, HEURISTIC_WEIGHT * heuristic(startNode, goalNode));

  let expansions = 0;

  while (heap.size > 0) {
    const entry = heap.pop()!;
    const { uid, g } = entry;

    // Lazy deletion: skip stale heap entries
    const knownG = gScore.get(uid);
    if (knownG !== undefined && g > knownG) continue;

    if (uid === goalUid) {
      return reconstructPath(startUid, goalUid, cameFrom, cameFromEdge);
    }

    if (++expansions > MAX_EXPANSIONS) {
      console.warn(`[A*] Expansion cap hit after ${MAX_EXPANSIONS} iterations`);
      return null;
    }

    const edges = adjacency[uid];
    if (!edges) continue;

    // Incoming direction at current node (for U-turn detection)
    const prevEdge = cameFromEdge.get(uid);
    const currentNode = nodes[uid];

    for (const edge of edges) {
      const toNode = nodes[edge.to];
      if (!toNode) continue;

      if (options?.blockedNodes?.has(edge.to)) continue;

      // Apply routing options
      if (options?.avoidFerries &&
          (edge.itemType === 'ferry' || edge.itemType === 'ferry_approach')) continue;

      // fastest (default): use pre-weighted cost (freeways preferred via lower multiplier)
      // shortest: mostly use raw length, but keep lane changes cheap enough to
      // prevent pointless service-area detours from winning over staying on a
      // through lane.
      const baseCost = (options?.mode === 'shortest') ? shortestBaseCost(edge) : edge.weight;
      let weightMult = (options?.avoidHighways && edge.speedClass === 'freeway') ? 100 : 1;
      let extraCost = 0;
      if (edge.itemType === 'ferry') {
        if (hasOfficialFerryCost(edge)) {
          extraCost += FERRY_BOARDING_PENALTY_METERS;
        } else {
          extraCost += FERRY_FALLBACK_PENALTY_METERS;
          if (options?.mode !== 'shortest') weightMult *= FERRY_FALLBACK_MULTIPLIER;
        }
      }

      // Highway-exit penalty: entering a local-speed prefab from a motorway/expressway.
      // In the lane-based graph, service area prefabs snap to highway lane endpoints at
      // different positions along the road, creating a bypass path that can be
      // geometrically shorter than the (slightly curved) highway segment it skips.
      // A fixed penalty makes such detours unattractive without blocking legitimate
      // exits when the destination is genuinely off the highway.
      if (prevEdge &&
          (prevEdge.speedClass === 'motorway' || prevEdge.speedClass === 'expressway' || prevEdge.speedClass === 'divided') &&
          edge.itemType === 'prefab' &&
          edge.speedClass === 'local_road') {
        extraCost += HIGHWAY_EXIT_PENALTY_METERS;
      }

      // Ferry→ferry direction change penalty: if we just arrived by ferry and the
      // next edge is also a ferry going in a significantly different direction,
      // heavily penalise it.  This prevents the A* from "rebounding" across the sea
      // (port A → port B → port C where B→C reverses the A→B heading).
      if (prevEdge?.itemType === 'ferry' && edge.itemType === 'ferry' && currentNode) {
        extraCost += FERRY_TRANSFER_PENALTY;

        const prevPortNode = nodes[prevEdge.from];
        if (prevPortNode) {
          const inDx = currentNode.x - prevPortNode.x, inDz = currentNode.z - prevPortNode.z;
          const outDx = toNode.x - currentNode.x,      outDz = toNode.z - currentNode.z;
          const inLen  = Math.sqrt(inDx * inDx  + inDz * inDz);
          const outLen = Math.sqrt(outDx * outDx + outDz * outDz);
          if (inLen > 0.001 && outLen > 0.001) {
            const dot = (inDx * outDx + inDz * outDz) / (inLen * outLen);
            if (dot < 0) weightMult *= 100;   // ferry direction reversal
          }
        }
      }

      const tentativeG = g + baseCost * weightMult + extraCost;
      if (tentativeG < (gScore.get(edge.to) ?? Infinity)) {
        gScore.set(edge.to, tentativeG);
        cameFrom.set(edge.to, uid);
        cameFromEdge.set(edge.to, edge);
        const f = tentativeG + HEURISTIC_WEIGHT * heuristic(toNode, goalNode);
        heap.push(edge.to, tentativeG, f);
      }
    }
  }

  return null;
}

function reconstructPath(
  startUid: string,
  goalUid: string,
  cameFrom: Map<string, string>,
  cameFromEdge: Map<string, GraphEdge>,
): RouteResult {
  const path: string[] = [];
  let totalWeight = 0;
  let totalLength = 0;
  let landLength = 0;
  let ferryLength = 0;
  let curr = goalUid;

  while (curr !== startUid) {
    path.push(curr);
    const edge = cameFromEdge.get(curr);
    if (edge) {
      totalWeight += edge.weight;
      totalLength += edge.length;
      if (edge.itemType === 'ferry') ferryLength += edge.length;
      else                           landLength  += edge.length;
    }
    curr = cameFrom.get(curr)!;
  }
  path.push(startUid);
  path.reverse();

  return { path, totalWeight, totalLength, landLength, ferryLength };
}
