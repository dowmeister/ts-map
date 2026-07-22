// Grid-based spatial index: zero external dependencies, O(1) cell lookup, O(k) candidates.
// For 194,187 nodes over ETS2's ~91k×98k world, cellSize ≈ 2625 gives ~150
// nodes/cell — the target specified in Phase 4.
import type { GraphEdge, GraphNode, MapBounds } from './types';

interface CellItem {
  uid: string;
  x: number;
  z: number;
}

export class SpatialIndex {
  private readonly _cells: Map<string, CellItem[]> = new Map();
  private readonly _cellSize: number;
  readonly cellCount: number;
  readonly avgNodesPerCell: number;

  constructor(nodes: Record<string, GraphNode>, bounds: MapBounds) {
    const nodeCount = Object.keys(nodes).length;
    const w = bounds.maxX - bounds.minX;
    const h = bounds.maxZ - bounds.minZ;

    this._cellSize = Math.round(Math.sqrt((w * h) / (nodeCount / 150)));

    for (const node of Object.values(nodes)) {
      const key = this._key(node.x, node.z);
      let cell = this._cells.get(key);
      if (!cell) { cell = []; this._cells.set(key, cell); }
      cell.push({ uid: node.uid, x: node.x, z: node.z });
    }

    this.cellCount = this._cells.size;
    this.avgNodesPerCell = nodeCount / this.cellCount;
  }

  get cellSize(): number { return this._cellSize; }

  // Returns the nearest node in a 3×3 cell window, expanding to 5×5 if empty.
  findNearest(x: number, z: number): CellItem | undefined {
    let candidates = this._candidatesInRadius(x, z, 1);
    if (candidates.length === 0) candidates = this._candidatesInRadius(x, z, 2);
    return this._nearest(x, z, candidates);
  }

  // Returns all nodes in a (2*radius+1)×(2*radius+1) cell window.
  findCandidates(x: number, z: number, radius: number): CellItem[] {
    return this._candidatesInRadius(x, z, radius);
  }

  // Returns all nodes whose cell overlaps [minX..maxX] × [minZ..maxZ].
  // Used by the debug endpoint to avoid linear scan of all nodes.
  findInBbox(minX: number, maxX: number, minZ: number, maxZ: number): CellItem[] {
    const cxMin = Math.floor(minX / this._cellSize);
    const cxMax = Math.floor(maxX / this._cellSize);
    const czMin = Math.floor(minZ / this._cellSize);
    const czMax = Math.floor(maxZ / this._cellSize);
    const result: CellItem[] = [];
    for (let cx = cxMin; cx <= cxMax; cx++) {
      for (let cz = czMin; cz <= czMax; cz++) {
        const cell = this._cells.get(`${cx}:${cz}`);
        if (cell) result.push(...cell);
      }
    }
    return result;
  }

  private _candidatesInRadius(x: number, z: number, radius: number): CellItem[] {
    const cx = Math.floor(x / this._cellSize);
    const cz = Math.floor(z / this._cellSize);
    const result: CellItem[] = [];
    for (let dx = -radius; dx <= radius; dx++) {
      for (let dz = -radius; dz <= radius; dz++) {
        const key = `${cx + dx}:${cz + dz}`;
        const cell = this._cells.get(key);
        if (cell) result.push(...cell);
      }
    }
    return result;
  }

  private _nearest(x: number, z: number, candidates: CellItem[]): CellItem | undefined {
    let best: CellItem | undefined;
    let bestDSq = Infinity;
    for (const c of candidates) {
      const dx = c.x - x, dz = c.z - z;
      const dSq = dx * dx + dz * dz;
      if (dSq < bestDSq) { bestDSq = dSq; best = c; }
    }
    return best;
  }

  private _key(x: number, z: number): string {
    return `${Math.floor(x / this._cellSize)}:${Math.floor(z / this._cellSize)}`;
  }
}
