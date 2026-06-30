import type { GraphEdge, GraphNode, MapBounds, RouteResult } from './types';
import { ets2ToWgs84 } from './coordinates';

export type ManeuverType = 'turn' | 'exit';

export type ManeuverModifier =
  | 'slight-left'
  | 'slight-right'
  | 'left'
  | 'right'
  | 'sharp-left'
  | 'sharp-right'
  | 'uturn';

export interface Maneuver {
  type: ManeuverType;
  modifier: ManeuverModifier;
  /** Which way the route turns at the junction. */
  side: 'left' | 'right';
  /** Game-space anchor (junction centre). */
  x: number;
  z: number;
  /** Map-space anchor for direct placement on the web map. */
  lon: number;
  lat: number;
  /** Compass bearings (deg, 0 = north, clockwise) approaching / leaving the junction. */
  bearingBefore: number;
  bearingAfter: number;
  /** Signed turn angle in degrees, positive = right. */
  turnAngle: number;
  /** Cumulative display distance from the route start, in metres. */
  distanceFromStartM: number;
  /** Display distance travelled since the previous maneuver, in metres. */
  distanceFromPrevM: number;
  /** Lane count on the approach road (only for 'exit'). */
  lanesApproach?: number;
  /** Lane count on the departure road (only for 'exit'). */
  lanesDepart?: number;
}

interface GamePoint {
  x: number;
  z: number;
  /** Index into the path edge list that produced this point. */
  edgeIndex: number;
}

// A junction is "turned through" when its heading changes by at least this much.
const TURN_THRESHOLD_DEG = 30;
// Game units of road sampled either side of a junction to measure its heading.
const TANGENT_DISTANCE = 25;
// Minimum road actually sampled either side; below this the heading is unreliable.
const MIN_TANGENT_DISTANCE = 6;
// Game units; collapse near-duplicate turns from adjacent prefab runs.
const DEDUP_DISTANCE = 40;

function edgeBetween(
  adjacency: Record<string, GraphEdge[]>,
  from: string,
  to: string,
): GraphEdge | undefined {
  const list = adjacency[from];
  if (!list) return undefined;
  let best: GraphEdge | undefined;
  for (const edge of list) {
    if (edge.to !== to) continue;
    if (!best || edge.weight < best.weight) best = edge;
  }
  return best;
}

function gameDistance(a: [number, number], b: [number, number]): number {
  return Math.hypot(b[0] - a[0], b[1] - a[1]);
}

/** Compass bearing from a→b in map (lon/lat) space, matching the web client. */
function mapBearing(a: [number, number], b: [number, number]): number {
  const dLon = b[0] - a[0];
  const dLat = b[1] - a[1];
  return (Math.atan2(dLon, dLat) * 180) / Math.PI;
}

/** Signed smallest angle out − in, normalised to (−180, 180]. Positive = right. */
function angleDiff(out: number, inb: number): number {
  return ((out - inb + 540) % 360) - 180;
}

function modifierFor(turn: number): ManeuverModifier {
  const a = Math.abs(turn);
  const right = turn >= 0;
  if (a > 150) return 'uturn';
  if (a < 45) return right ? 'slight-right' : 'slight-left';
  if (a > 110) return right ? 'sharp-right' : 'sharp-left';
  return right ? 'right' : 'left';
}

/**
 * Walk the dense point list outward from `start` until `distance` units are
 * covered (or the polyline ends). Returns the landing index and how far we
 * actually travelled, so callers can reject headings sampled over too little road.
 */
function walk(
  points: GamePoint[],
  start: number,
  step: 1 | -1,
  distance: number,
): { idx: number; traveled: number } {
  let i = start;
  let acc = 0;
  while (i + step >= 0 && i + step < points.length) {
    const a = points[i];
    const b = points[i + step];
    acc += Math.hypot(b.x - a.x, b.z - a.z);
    i += step;
    if (acc >= distance) break;
  }
  return { idx: i, traveled: acc };
}

/**
 * Derive turn-by-turn maneuvers from a routing result. For now this only emits
 * one maneuver kind: a *turn at an intersection*. In ETS2 every junction
 * (crossroads, T-junction, roundabout, interchange) is a `prefab` item, so the
 * route traverses one or more `prefab` edges to pass through it. A turn is a
 * prefab the route enters on one heading and leaves on a meaningfully different
 * one — classified left or right. Highway exits/merges, ferries and the
 * depart/arrive bookends were intentionally removed and will be re-added later.
 */
export function buildManeuvers(
  result: RouteResult,
  nodes: Record<string, GraphNode>,
  adjacency: Record<string, GraphEdge[]>,
  bounds: MapBounds,
  edgePaths: Record<string, [number, number][]> | undefined,
  displayScale: number,
): Maneuver[] {
  const path = result.path;
  if (path.length < 2) return [];

  // 1) Build a dense game-space polyline, tagging each point with the index of
  //    the path edge that produced it (so we can tell roads from prefabs later).
  const points: GamePoint[] = [];
  const edges: (GraphEdge | undefined)[] = [];

  for (let i = 0; i < path.length - 1; i++) {
    const fromUid = path[i];
    const toUid = path[i + 1];
    edges.push(edgeBetween(adjacency, fromUid, toUid));

    const fromNode = nodes[fromUid];
    const toNode = nodes[toUid];
    if (!fromNode || !toNode) continue;

    const wp = edgePaths?.[`${fromUid}-${toUid}`];
    const raw: [number, number][] =
      wp && wp.length >= 2 ? wp : [[fromNode.x, fromNode.z], [toNode.x, toNode.z]];

    for (let j = 0; j < raw.length; j++) {
      const [x, z] = raw[j];
      const last = points[points.length - 1];
      // Skip the first waypoint when it duplicates the previous edge's endpoint.
      if (j === 0 && last && Math.hypot(last.x - x, last.z - z) < 1e-6) continue;
      points.push({ x, z, edgeIndex: i });
    }
  }
  if (points.length < 2) return [];

  // 2) Cumulative arc length (game units) at every polyline point.
  const arc = new Array<number>(points.length);
  arc[0] = 0;
  for (let i = 1; i < points.length; i++) {
    arc[i] = arc[i - 1] + Math.hypot(points[i].x - points[i - 1].x, points[i].z - points[i - 1].z);
  }

  const toLL = (p: { x: number; z: number }): [number, number] => ets2ToWgs84(p.x, p.z, bounds);

  // Detect turns at intersections (prefab traversals). Walk the dense polyline,
  // group consecutive `prefab` points into junction runs, and compare the road
  // heading approaching the junction with the heading leaving it.
  const isPrefabPoint = (idx: number): boolean =>
    edges[points[idx].edgeIndex]?.itemType === 'prefab';

  const raw: Maneuver[] = [];

  let p = 0;
  while (p < points.length) {
    if (!isPrefabPoint(p)) {
      p++;
      continue;
    }
    // Maximal run of consecutive prefab points = one junction traversal.
    const s = p;
    while (p + 1 < points.length && isPrefabPoint(p + 1)) p++;
    const e = p;
    p++;

    // Sample the approach road (before the prefab) and the departure road (after).
    const back = walk(points, s, -1, TANGENT_DISTANCE);
    const fwd = walk(points, e, 1, TANGENT_DISTANCE);
    if (back.traveled < MIN_TANGENT_DISTANCE || fwd.traveled < MIN_TANGENT_DISTANCE) continue;

    // Approach/depart edges are the road edges that walk() landed on — not the
    // prefab edges at s/e, which have no lanes field.
    const approachEdge = edges[points[back.idx].edgeIndex];
    const departEdge   = edges[points[fwd.idx].edgeIndex];
    const lanesApproach = approachEdge?.lanes ?? 0;
    const lanesDepart   = departEdge?.lanes ?? 0;

    const entryLL = toLL(points[s]);
    const exitLL = toLL(points[e]);
    const bearingBefore = mapBearing(toLL(points[back.idx]), entryLL);
    const bearingAfter = mapBearing(exitLL, toLL(points[fwd.idx]));
    const turn = angleDiff(bearingAfter, bearingBefore);

    const midIdx = (s + e) >> 1;
    const mid = points[midIdx];
    const midLL = toLL(mid);
    const base = {
      x: mid.x,
      z: mid.z,
      lon: midLL[0],
      lat: midLL[1],
      bearingBefore: (bearingBefore + 360) % 360,
      bearingAfter: (bearingAfter + 360) % 360,
      turnAngle: Math.round(turn),
      distanceFromStartM: Math.round(arc[midIdx] * displayScale),
      distanceFromPrevM: 0,
    };

    // Highway exit: lanes drop to a single-lane ramp AND we were on a highway.
    // A 4→3 or 3→2 change is just a motorway narrowing, not an exit; true exit ramps have 1 lane.
    const isHighway = (e: GraphEdge | undefined) =>
      !!e && (e.speedClass === 'motorway' || e.speedClass === 'expressway');
    const isExitRamp = lanesApproach >= 2 && lanesDepart === 1 && isHighway(approachEdge);
    if (isExitRamp) {
      raw.push({
        ...base,
        type: 'exit',
        modifier: modifierFor(turn),
        side: turn >= 0 ? 'right' : 'left',
        lanesApproach,
        lanesDepart,
      });
      continue;
    }

    // Regular turn: angle above threshold but no lane reduction exit.
    if (Math.abs(turn) < TURN_THRESHOLD_DEG) continue;

    raw.push({
      type: 'turn',
      modifier: modifierFor(turn),
      side: turn >= 0 ? 'right' : 'left',
      ...base,
    });
  }

  // Collapse turns whose anchors almost coincide (adjacent prefab runs in a
  // complex junction) — keep the sharper one, or 'exit' over 'turn'.
  const list: Maneuver[] = [];
  for (const m of raw) {
    const prev = list[list.length - 1];
    if (prev && gameDistance([prev.x, prev.z], [m.x, m.z]) < DEDUP_DISTANCE) {
      const mBetter =
        (m.type === 'exit' && prev.type !== 'exit') ||
        (m.type === prev.type && Math.abs(m.turnAngle) > Math.abs(prev.turnAngle));
      if (mBetter) list[list.length - 1] = m;
      continue;
    }
    list.push(m);
  }

  let prevDist = 0;
  for (const m of list) {
    m.distanceFromPrevM = Math.max(0, m.distanceFromStartM - prevDist);
    prevDist = m.distanceFromStartM;
  }

  return list;
}
