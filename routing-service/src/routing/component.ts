import type { GraphEdge, GraphNode } from './types';

export class MainComponentIndex {
  private readonly _parent: Map<string, string> = new Map();
  private _mainRoot = '';
  private _mainSize = 0;
  private _mainNodes: Set<string> = new Set();

  build(nodes: Record<string, GraphNode>, adjacency: Record<string, GraphEdge[]>): void {
    for (const uid of Object.keys(nodes)) {
      this._parent.set(uid, uid);
    }

    // Iterate the adjacency map directly instead of a flattened copy of every
    // edge — on large graphs (e.g. ProMods) that transient array is hundreds of
    // MB and was a major contributor to startup memory pressure.
    for (const edges of Object.values(adjacency)) {
      for (const edge of edges) {
        this._union(edge.from, edge.to);
      }
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

export class DirectedComponentIndex {
  private readonly _componentByUid: Map<string, number> = new Map();
  private readonly _componentAdjacency: Map<number, number[]> = new Map();
  private readonly _reachabilityCache: Map<number, Set<number>> = new Map();
  private _componentCount = 0;
  private _largestComponentSize = 0;

  build(nodes: Record<string, GraphNode>, adjacency: Record<string, GraphEdge[]>): void {
    this._componentByUid.clear();
    this._componentAdjacency.clear();
    this._reachabilityCache.clear();
    this._componentCount = 0;
    this._largestComponentSize = 0;

    const uids = Object.keys(nodes);
    const visited = new Set<string>();
    const order: string[] = [];

    for (const uid of uids) {
      if (visited.has(uid)) continue;

      const stack: Array<{ uid: string; nextEdgeIndex: number }> = [{ uid, nextEdgeIndex: 0 }];
      visited.add(uid);

      while (stack.length > 0) {
        const frame = stack[stack.length - 1];
        const edges = adjacency[frame.uid] ?? [];

        if (frame.nextEdgeIndex < edges.length) {
          const to = edges[frame.nextEdgeIndex++].to;
          if (!visited.has(to)) {
            visited.add(to);
            stack.push({ uid: to, nextEdgeIndex: 0 });
          }
        } else {
          order.push(frame.uid);
          stack.pop();
        }
      }
    }

    const reverseAdjacency = new Map<string, string[]>();
    for (const edges of Object.values(adjacency)) {
      for (const edge of edges) {
        let incoming = reverseAdjacency.get(edge.to);
        if (!incoming) {
          incoming = [];
          reverseAdjacency.set(edge.to, incoming);
        }
        incoming.push(edge.from);
      }
    }

    const componentSizes: number[] = [];

    for (let i = order.length - 1; i >= 0; i--) {
      const root = order[i];
      if (this._componentByUid.has(root)) continue;

      const componentId = componentSizes.length;
      let size = 0;
      const stack = [root];
      this._componentByUid.set(root, componentId);

      while (stack.length > 0) {
        const uid = stack.pop()!;
        size++;

        for (const from of reverseAdjacency.get(uid) ?? []) {
          if (!this._componentByUid.has(from)) {
            this._componentByUid.set(from, componentId);
            stack.push(from);
          }
        }
      }

      componentSizes.push(size);
    }

    const adjacencySets = new Map<number, Set<number>>();
    for (const edges of Object.values(adjacency)) {
      for (const edge of edges) {
        const fromComponent = this._componentByUid.get(edge.from);
        const toComponent = this._componentByUid.get(edge.to);
        if (fromComponent == null || toComponent == null || fromComponent === toComponent) continue;

        let targets = adjacencySets.get(fromComponent);
        if (!targets) {
          targets = new Set<number>();
          adjacencySets.set(fromComponent, targets);
        }
        targets.add(toComponent);
      }
    }

    for (const [componentId, targets] of adjacencySets) {
      this._componentAdjacency.set(componentId, [...targets]);
    }

    this._componentCount = componentSizes.length;
    this._largestComponentSize = componentSizes.reduce((best, size) => Math.max(best, size), 0);
  }

  componentOf(uid: string): number | undefined {
    return this._componentByUid.get(uid);
  }

  canReach(fromUid: string, toUid: string): boolean {
    const fromComponent = this.componentOf(fromUid);
    const toComponent = this.componentOf(toUid);
    if (fromComponent == null || toComponent == null) return false;
    if (fromComponent === toComponent) return true;
    return this._reachableComponents(fromComponent).has(toComponent);
  }

  canReachComponent(fromComponent: number, toUid: string): boolean {
    const toComponent = this.componentOf(toUid);
    if (toComponent == null) return false;
    return this.canReachComponentId(fromComponent, toComponent);
  }

  canReachComponentId(fromComponent: number, toComponent: number): boolean {
    if (fromComponent === toComponent) return true;
    return this._reachableComponents(fromComponent).has(toComponent);
  }

  get componentCount(): number { return this._componentCount; }
  get largestComponentSize(): number { return this._largestComponentSize; }

  private _reachableComponents(startComponent: number): Set<number> {
    let reachable = this._reachabilityCache.get(startComponent);
    if (reachable) return reachable;

    reachable = new Set<number>([startComponent]);
    const queue = [startComponent];

    for (let i = 0; i < queue.length; i++) {
      const component = queue[i];
      for (const next of this._componentAdjacency.get(component) ?? []) {
        if (!reachable.has(next)) {
          reachable.add(next);
          queue.push(next);
        }
      }
    }

    this._reachabilityCache.set(startComponent, reachable);
    return reachable;
  }
}
