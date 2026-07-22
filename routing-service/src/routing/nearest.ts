import type { GraphEdge, GraphNode, MapBounds } from './types';
import type { SpatialIndex } from './spatial-index';

// ETS2 heading convention: 0° = north (−Z), 90° = east (+X)
function headingToDirection(headingDeg: number): { dirX: number; dirZ: number } {
  const rad = (headingDeg * Math.PI) / 180;
  return { dirX: Math.sin(rad), dirZ: -Math.cos(rad) };
}

export function findNearest(
  x: number,
  z: number,
  spatialIndex: SpatialIndex,
): string | undefined {
  return spatialIndex.findNearest(x, z)?.uid;
}

// Returns the nearest node in the main component.
// First tries the normal 3×3 snap; if that lands on an isolated node, expands
// to a radius-5 search filtered to main-component nodes.
// Returns undefined only if no main-component node is found within radius 5.
export function findNearestMainComponent(
  x: number,
  z: number,
  spatialIndex: SpatialIndex,
  isInMain: (uid: string) => boolean,
  isRoutable: (uid: string) => boolean = () => true,
): string | undefined {
  const nearest = spatialIndex.findNearest(x, z);
  if (nearest && isInMain(nearest.uid) && isRoutable(nearest.uid)) return nearest.uid;

  // Snap landed on isolated node — widen search
  const candidates = spatialIndex.findCandidates(x, z, 5)
    .filter(c => isInMain(c.uid) && isRoutable(c.uid));
  if (candidates.length === 0) return undefined;

  let bestUid: string | undefined;
  let bestDSq = Infinity;
  for (const c of candidates) {
    const dx = c.x - x, dz = c.z - z;
    const dSq = dx * dx + dz * dz;
    if (dSq < bestDSq) { bestDSq = dSq; bestUid = c.uid; }
  }
  return bestUid;
}

// True road-network node: has at least one 'road' or 'prefab' edge (i.e. is
// part of the actual drivable through-network, not merely a company/ferry
// driveway endpoint). Used to snap VIA waypoints so the route passes THROUGH
// the point instead of detouring into a spur and backtracking.
function isThroughRoadNode(uid: string, adjacency: Record<string, GraphEdge[]>): boolean {
  const edges = adjacency[uid];
  if (!edges) return false;
  return edges.some(e => e.itemType === 'road' || e.itemType === 'prefab');
}

// Like findNearestMainComponent, but restricted to through-road nodes so a
// VIA waypoint doesn't snap to a company/ferry approach spur (which would
// force the route to detour in and back out instead of passing through).
// Falls back to the unrestricted result if no through-road node is found
// within the search radius.
export function findNearestThroughRoad(
  x: number,
  z: number,
  spatialIndex: SpatialIndex,
  adjacency: Record<string, GraphEdge[]>,
  isInMain: (uid: string) => boolean,
  isRoutable: (uid: string) => boolean = () => true,
): string | undefined {
  const isThrough = (uid: string) => isRoutable(uid) && isInMain(uid) && isThroughRoadNode(uid, adjacency);

  for (let radius = 1; radius <= 8; radius++) {
    const candidates = spatialIndex.findCandidates(x, z, radius).filter(c => isThrough(c.uid));
    if (candidates.length === 0) continue;

    let bestUid: string | undefined;
    let bestDSq = Infinity;
    for (const c of candidates) {
      const dx = c.x - x, dz = c.z - z;
      const dSq = dx * dx + dz * dz;
      if (dSq < bestDSq) { bestDSq = dSq; bestUid = c.uid; }
    }
    return bestUid;
  }

  return undefined;
}

export function findNearestReachableMainComponent(
  x: number,
  z: number,
  spatialIndex: SpatialIndex,
  isInMain: (uid: string) => boolean,
  isRoutable: (uid: string) => boolean,
  isReachable: (uid: string) => boolean,
): string | undefined {
  const seen = new Set<string>();

  for (let radius = 1; radius <= 8; radius++) {
    const candidates = spatialIndex.findCandidates(x, z, radius)
      .filter(c => {
        if (seen.has(c.uid)) return false;
        seen.add(c.uid);
        return isInMain(c.uid) && isRoutable(c.uid) && isReachable(c.uid);
      });

    if (candidates.length === 0) continue;

    let bestUid: string | undefined;
    let bestDSq = Infinity;
    for (const c of candidates) {
      const dx = c.x - x, dz = c.z - z;
      const dSq = dx * dx + dz * dz;
      if (dSq < bestDSq) { bestDSq = dSq; bestUid = c.uid; }
    }
    return bestUid;
  }

  return undefined;
}

// Heading-aware snap for start position on dual carriageways.
// Scores candidates by both distance and alignment with truck heading direction.
export function findNearestWithHeading(
  x: number,
  z: number,
  headingDeg: number,
  spatialIndex: SpatialIndex,
  nodes: Record<string, GraphNode>,
  adjacency: Record<string, GraphEdge[]>,
  bounds: MapBounds,
): string | undefined {
  // Search 5×5 cell window (radius=2) to get candidates for heading scoring
  const candidates = spatialIndex.findCandidates(x, z, 2);
  if (candidates.length === 0) return undefined;

  const { dirX, dirZ } = headingToDirection(headingDeg);

  // scaleFactor normalises heading penalty to be comparable with distSq:
  // a 180° mismatch equals being one quarter of the map width away
  const mapWidth = bounds.maxX - bounds.minX;
  const scaleFactor = Math.pow(mapWidth / 4, 2);
  const w = 0.3;

  let bestUid: string | undefined;
  let bestScore = Infinity;

  for (const candidate of candidates) {
    const edges = adjacency[candidate.uid];
    if (!edges || edges.length === 0) continue;

    const candidateNode = nodes[candidate.uid];
    if (!candidateNode) continue;

    const dx = candidateNode.x - x;
    const dz = candidateNode.z - z;
    const distSq = dx * dx + dz * dz;

    // Find the best-aligned outgoing edge
    let bestDot = -1;
    for (const edge of edges) {
      const toNode = nodes[edge.to];
      if (!toNode) continue;
      const edgeDx = toNode.x - candidateNode.x;
      const edgeDz = toNode.z - candidateNode.z;
      const edgeLen = Math.sqrt(edgeDx * edgeDx + edgeDz * edgeDz);
      if (edgeLen < 0.001) continue;
      const dot = (edgeDx / edgeLen) * dirX + (edgeDz / edgeLen) * dirZ;
      if (dot > bestDot) bestDot = dot;
    }

    // Combined score: lower is better
    // distSq drives distance preference, (1 - bestDot) drives heading alignment
    const score = distSq * (1 - w) + (1 - bestDot) * w * scaleFactor;
    if (score < bestScore) { bestScore = score; bestUid = candidate.uid; }
  }

  return bestUid;
}
