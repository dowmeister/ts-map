import type { GraphEdge, GraphNode } from './types';

export class MainComponentIndex {
  private readonly _parent: Map<string, string> = new Map();
  private _mainRoot = '';
  private _mainSize = 0;
  private _mainNodes: Set<string> = new Set();

  build(nodes: Record<string, GraphNode>, edges: GraphEdge[]): void {
    for (const uid of Object.keys(nodes)) {
      this._parent.set(uid, uid);
    }

    for (const edge of edges) {
      this._union(edge.from, edge.to);
    }

    // Count component sizes
    const counts = new Map<string, number>();
    for (const uid of Object.keys(nodes)) {
      const root = this._find(uid);
      counts.set(root, (counts.get(root) ?? 0) + 1);
    }

    // Find largest component
    let maxSize = 0;
    for (const [root, size] of counts) {
      if (size > maxSize) {
        maxSize = size;
        this._mainRoot = root;
      }
    }
    this._mainSize = maxSize;

    // Collect all UIDs in the main component
    for (const uid of Object.keys(nodes)) {
      if (this._find(uid) === this._mainRoot) {
        this._mainNodes.add(uid);
      }
    }
  }

  isInMainComponent(uid: string): boolean {
    return this._mainNodes.has(uid);
  }

  get mainSize(): number { return this._mainSize; }
  get mainNodeCount(): number { return this._mainNodes.size; }

  private _find(x: string): string {
    while (this._parent.get(x) !== x) {
      // Path halving
      const grandparent = this._parent.get(this._parent.get(x)!)!;
      this._parent.set(x, grandparent);
      x = grandparent;
    }
    return x;
  }

  private _union(a: string, b: string): void {
    const ra = this._find(a);
    const rb = this._find(b);
    if (ra !== rb) this._parent.set(ra, rb);
  }
}
