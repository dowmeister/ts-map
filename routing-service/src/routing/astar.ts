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

const MAX_EXPANSIONS = 500_000;

function heuristic(a: GraphNode, b: GraphNode): number {
  const dx = a.x - b.x, dz = a.z - b.z;
  return Math.sqrt(dx * dx + dz * dz);
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
  heap.push(startUid, 0, heuristic(startNode, goalNode));

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
      // shortest: use raw length so the router minimises metres driven, ignoring speed class
      const baseCost = (options?.mode === 'shortest') ? edge.length : edge.weight;
      let weightMult = (options?.avoidHighways && edge.speedClass === 'freeway') ? 100 : 1;

      // Ferry→ferry direction change penalty: if we just arrived by ferry and the
      // next edge is also a ferry going in a significantly different direction,
      // heavily penalise it.  This prevents the A* from "rebounding" across the sea
      // (port A → port B → port C where B→C reverses the A→B heading).
      if (prevEdge?.itemType === 'ferry' && edge.itemType === 'ferry' && currentNode) {
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

      // U-turn penalty: penalise edges that reverse the incoming direction.
      // Ferry and company_approach edges are exempt (legitimately reverse).
      // At the start node (prevEdge is null) we use the goal direction as a
      // virtual incoming vector so the first move is also penalised correctly.
      if (currentNode &&
          edge.itemType !== 'ferry' && edge.itemType !== 'ferry_approach' &&
          edge.itemType !== 'company_approach') {
        let inDx: number, inDz: number;
        if (prevEdge) {
          const prevFrom = nodes[prevEdge.from];
          if (!prevFrom) { inDx = 0; inDz = 0; }
          else { inDx = currentNode.x - prevFrom.x; inDz = currentNode.z - prevFrom.z; }
        } else {
          // Start node: virtual incoming = direction from goal toward start
          // (i.e. we "arrived" travelling toward the goal)
          inDx = goalNode.x - currentNode.x;
          inDz = goalNode.z - currentNode.z;
        }
        const outDx = toNode.x - currentNode.x, outDz = toNode.z - currentNode.z;
        const inLen  = Math.sqrt(inDx * inDx + inDz * inDz);
        const outLen = Math.sqrt(outDx * outDx + outDz * outDz);
        if (inLen > 0.001 && outLen > 0.001) {
          const dot = (inDx * outDx + inDz * outDz) / (inLen * outLen);
          if      (dot < -0.7) weightMult *= 200;  // >134° near-reversal
          else if (dot < -0.5) weightMult *= 50;   // >120°
          else if (dot <  0.0) weightMult *= 5;    // >90°  going backwards
        }
      }

      const tentativeG = g + baseCost * weightMult;
      if (tentativeG < (gScore.get(edge.to) ?? Infinity)) {
        gScore.set(edge.to, tentativeG);
        cameFrom.set(edge.to, uid);
        cameFromEdge.set(edge.to, edge);
        const f = tentativeG + heuristic(toNode, goalNode);
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
