import { DirectedComponentIndex, MainComponentIndex } from './component';
import type { GraphEdge, LoadedGraph } from './types';

export interface ReconnectResult {
  reverseEdgesAdded: number;
  repairedNodesBefore: number;
}

// Some regions of the generated graph are not strongly connected to the rest of
// the landmass: you can drive into them but not out (the far north of ETS2 around
// Bodø / the Lofoten), or out but not in. The cause is a graph-generation gap
// where chains of junction/road segments are emitted in one direction only, so a
// whole region becomes a one-way sink or source. There is no single chokepoint —
// the traps are nested and connect to the rest only through other trapped nodes.
//
// This is a pragmatic service-side repair that runs at load time (no 287MB graph
// regeneration). For every non-ferry edge incident to a node that is in the main
// undirected component but NOT in the giant SCC, we add the missing reverse edge.
// This is provably sufficient to collapse the entire main undirected component
// into a single SCC: such a node n has an undirected path to the giant SCC, and
// every edge along the non-giant portion of that path (plus the frontier edge to
// the giant SCC) is now bidirectional — so n both reaches and is reachable from
// the giant SCC. Ferries are already bidirectional and are never reversed.
export function reconnectDirectionalTraps(
  graph: LoadedGraph,
  mainIndex: MainComponentIndex,
): ReconnectResult {
  // Directed components on the current (un-repaired) graph.
  const directed = new DirectedComponentIndex();
  directed.build(graph.nodes, graph.adjacency);
  const giantComponent = directed.largestComponentId;

  // A node needs repair when it belongs to the main undirected component (so it
  // is physically connected to the rest of the network) yet is not part of the
  // giant SCC — i.e. it cannot reach the giant SCC, or cannot be reached from it.
  // Truly isolated tiny components are intentionally left untouched.
  const needsRepair = (uid: string): boolean =>
    mainIndex.isInMainComponent(uid) && directed.componentOf(uid) !== giantComponent;

  let repairedNodesBefore = 0;
  for (const uid of Object.keys(graph.nodes)) {
    if (needsRepair(uid)) repairedNodesBefore++;
  }

  // Collect reverse edges first; do not mutate adjacency while iterating it.
  const toAdd: GraphEdge[] = [];
  for (const edges of Object.values(graph.adjacency)) {
    for (const edge of edges) {
      if (edge.itemType === 'ferry') continue;
      if (!needsRepair(edge.from) && !needsRepair(edge.to)) continue;

      const existing = graph.adjacency[edge.to];
      if (existing && existing.some(e => e.to === edge.from)) continue;

      toAdd.push({
        from: edge.to,
        to: edge.from,
        weight: edge.weight,
        length: edge.length,
        speedClass: edge.speedClass,
        speedLimitKph: edge.speedLimitKph,
        itemType: edge.itemType,
      });
    }
  }

  for (const edge of toAdd) {
    let list = graph.adjacency[edge.from];
    if (!list) {
      list = [];
      graph.adjacency[edge.from] = list;
    }
    list.push(edge);
  }

  return { reverseEdgesAdded: toAdd.length, repairedNodesBefore };
}
