using System;
using System.Collections.Generic;
using System.Linq;
using TsMap.Helpers.Logger;
using TsMap.TsItem;

namespace TsMap.Routing
{
    public class RoutingGraphBuilder
    {
        private readonly TsMapper _mapper;
        private readonly RoutingGraph _graph = new RoutingGraph();
        // Road-only node UIDs — used by ProcessCompanies to avoid connecting to other isolated company nodes
        private readonly HashSet<ulong> _roadNodeUids = new HashSet<ulong>();

        // Weight multipliers per speed class
        private const float MultFreeway   = 1.0f;
        private const float MultLocalRoad = 1.6f;
        private const float GpsAvoidMult  = 50f;
        private const float FerryWeight   = 50000f;

        public RoutingGraphBuilder(TsMapper mapper)
        {
            _mapper = mapper;
        }

        public RoutingGraph Build()
        {
            ProcessRoads();
            ProcessPrefabs();
            ProcessFerries();
            ProcessCompanies();
            Deduplicate();
            PruneOrphans();

            Logger.Instance.Info(
                $"[Routing] Built graph: {_graph.Nodes.Count} nodes, {_graph.Edges.Count} edges");
            return _graph;
        }

        // ── Roads ────────────────────────────────────────────────────────────

        private void ProcessRoads()
        {
            foreach (var road in _mapper.Roads)
            {
                if (!road.Valid || road.Hidden || road.RoadLook == null) continue;

                var startNode = road.GetStartNode();
                var endNode   = road.GetEndNode();
                if (startNode == null || endNode == null) continue;
                if (IsZeroNode(startNode) || IsZeroNode(endNode)) continue;

                float len = Dist(startNode, endNode);
                if (len < 0.001f) continue;

                string sc   = RoadSpeedClass(road.RoadLook);
                float  mult = sc == "freeway" ? MultFreeway : MultLocalRoad;
                if (road.GpsAvoid) mult *= GpsAvoidMult;

                // Build Hermite spline waypoints for the debug overlay.
                // GeoJsonExporter computes these points locally without storing them on the road,
                // so HasPoints() is always false here — we compute them directly.
                float[][] fwdWp = null, bwdWp = null;
                {
                    const int N = 12;
                    float sx = startNode.X, sz = startNode.Z;
                    float ex = endNode.X,   ez = endNode.Z;
                    double r = Math.Sqrt((sx-ex)*(double)(sx-ex) + (sz-ez)*(double)(sz-ez));
                    double tanSx = Math.Cos(-(Math.PI*0.5-startNode.Rotation))*r;
                    double tanSz = Math.Sin(-(Math.PI*0.5-startNode.Rotation))*r;
                    double tanEx = Math.Cos(-(Math.PI*0.5-endNode.Rotation))*r;
                    double tanEz = Math.Sin(-(Math.PI*0.5-endNode.Rotation))*r;
                    fwdWp = new float[N][];
                    bwdWp = new float[N][];
                    for (int i = 0; i < N; i++)
                    {
                        float s = i / (float)(N - 1);
                        float wx = (float)TsRoadLook.Hermite(s, sx, ex, tanSx, tanEx);
                        float wz = (float)TsRoadLook.Hermite(s, sz, ez, tanSz, tanEz);
                        fwdWp[i] = new float[] { wx, wz };
                        bwdWp[N - 1 - i] = new float[] { wx, wz };
                    }
                }

                // Forward: StartNode → EndNode when right-hand lanes exist
                if (road.RoadLook.LanesRight.Count > 0)
                {
                    RegisterNode(startNode);
                    RegisterNode(endNode);
                    _roadNodeUids.Add(startNode.Uid);
                    _roadNodeUids.Add(endNode.Uid);
                    _graph.Edges.Add(new GraphEdge(
                        startNode.Uid, endNode.Uid,
                        len * mult, len, sc, "road", fwdWp));
                }

                // Backward: EndNode → StartNode when left-hand lanes exist
                if (road.RoadLook.LanesLeft.Count > 0)
                {
                    RegisterNode(startNode);
                    RegisterNode(endNode);
                    _roadNodeUids.Add(startNode.Uid);
                    _roadNodeUids.Add(endNode.Uid);
                    _graph.Edges.Add(new GraphEdge(
                        endNode.Uid, startNode.Uid,
                        len * mult, len, sc, "road", bwdWp));
                }
            }
        }

        // ── Prefabs ──────────────────────────────────────────────────────────

        private void ProcessPrefabs()
        {
            foreach (var prefab in _mapper.Prefabs)
            {
                if (!prefab.Valid || prefab.Hidden) continue;
                int n = prefab.Nodes.Count;
                if (n < 2) continue;

                var desc = prefab.Prefab;

                // Fall back to full-mesh for prefabs with no NavCurve descriptor
                if (desc == null || !desc.ValidRoad ||
                    desc.NavCurves == null || desc.NavCurves.Count == 0 ||
                    desc.PrefabNodes == null || desc.PrefabNodes.Count < 2)
                {
                    AddPrefabFullMesh(prefab);
                    continue;
                }

                int descN = desc.PrefabNodes.Count;

                // Map: NavCurve index → PPD node index (for output curves)
                var curveToOutputNode = new Dictionary<int, int>();
                for (int i = 0; i < descN; i++)
                {
                    var pts = desc.PrefabNodes[i].OutputPoints;
                    if (pts == null) continue;
                    foreach (var ci in pts) curveToOutputNode[ci] = i;
                }

                // Compute local→world transformation for this prefab instance
                // (same formula as TsPrefabItem.Update / RenderHelper.RotatePoint)
                var originWorldNode = _mapper.GetNodeByUid(prefab.Nodes[0]);
                bool hasTransform = originWorldNode != null && prefab.Origin < descN;
                float rotSin = 0, rotCos = 1, originPpdX = 0, originPpdZ = 0;
                float originWX = 0, originWZ = 0;
                if (hasTransform)
                {
                    var originPpdNode = desc.PrefabNodes[prefab.Origin];
                    float rot = (float)(originWorldNode.Rotation - Math.PI
                        - Math.Atan2(originPpdNode.RotZ, originPpdNode.RotX)
                        + Math.PI / 2);
                    rotSin    = (float)Math.Sin(rot);
                    rotCos    = (float)Math.Cos(rot);
                    originPpdX = originPpdNode.X;
                    originPpdZ = originPpdNode.Z;
                    originWX  = originWorldNode.X;
                    originWZ  = originWorldNode.Z;
                }

                int edgesAdded = 0;
                var nodesWithOutEdge = new HashSet<int>();

                for (int fromPpdIdx = 0; fromPpdIdx < descN; fromPpdIdx++)
                {
                    var inputPts = desc.PrefabNodes[fromPpdIdx].InputPoints;
                    if (inputPts == null || inputPts.Count == 0) continue;

                    var fromUid = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, (byte)fromPpdIdx, descN);
                    if (fromUid == 0) continue;
                    var fromNode = _mapper.GetNodeByUid(fromUid);
                    if (fromNode == null || IsZeroNode(fromNode)) continue;

                    // BFS with predecessor tracking to reconstruct waypoint paths
                    var visited     = new HashSet<int>();
                    var queue       = new Queue<int>();
                    var predecessor = new Dictionary<int, int>(); // curveIdx → prevCurveIdx (-1 = start)
                    var reached     = new HashSet<int>();

                    foreach (var ci in inputPts)
                        if (visited.Add(ci)) { predecessor[ci] = -1; queue.Enqueue(ci); }

                    while (queue.Count > 0)
                    {
                        int ci = queue.Dequeue();
                        if (ci < 0 || ci >= desc.NavCurves.Count) continue;

                        if (curveToOutputNode.TryGetValue(ci, out int toPpdIdx)
                            && toPpdIdx != fromPpdIdx
                            && reached.Add(toPpdIdx))
                        {
                            var toUid = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, (byte)toPpdIdx, descN);
                            if (toUid != 0)
                            {
                                var toNode = _mapper.GetNodeByUid(toUid);
                                if (toNode != null && !IsZeroNode(toNode))
                                {
                                    float len = Dist(fromNode, toNode);
                                    if (len >= 0.001f)
                                    {
                                        // Reconstruct curve path for waypoints
                                        float[][] waypoints = null;
                                        if (hasTransform)
                                        {
                                            var path = new System.Collections.Generic.List<int>();
                                            int cur = ci;
                                            while (cur != -1)
                                            {
                                                path.Add(cur);
                                                cur = predecessor.ContainsKey(cur) ? predecessor[cur] : -1;
                                            }
                                            path.Reverse();
                                            waypoints = BuildNavCurveWaypoints(
                                                path, desc, rotSin, rotCos,
                                                originPpdX, originPpdZ, originWX, originWZ,
                                                fromNode.X, fromNode.Z, toNode.X, toNode.Z);
                                        }

                                        RegisterNode(fromNode);
                                        RegisterNode(toNode);
                                        _graph.Edges.Add(new GraphEdge(
                                            fromUid, toUid,
                                            len * MultLocalRoad, len, "local_road", "prefab",
                                            waypoints));
                                        edgesAdded++;
                                        nodesWithOutEdge.Add(fromPpdIdx);
                                    }
                                }
                            }
                        }

                        var nc = desc.NavCurves[ci];
                        if (nc.NextLines != null)
                            foreach (var next in nc.NextLines)
                                if (next >= 0 && next < desc.NavCurves.Count && visited.Add(next))
                                {
                                    predecessor[next] = ci;
                                    queue.Enqueue(next);
                                }
                    }
                }

                if (edgesAdded == 0)
                {
                    // NavCurves produced nothing at all — full-mesh for the whole prefab
                    AddPrefabFullMesh(prefab);
                }
                else
                {
                    // Per-node fallback: only for nodes that HAD InputPoints but BFS
                    // found no reachable output paths (broken/incomplete PPD data).
                    // Exit-only nodes (empty InputPoints) are intentionally skipped —
                    // they have no outgoing prefab edges by design and are reachable
                    // via road edges; adding fallback would create wrong-way routes.
                    for (int fromPpdIdx = 0; fromPpdIdx < descN; fromPpdIdx++)
                    {
                        if (nodesWithOutEdge.Contains(fromPpdIdx)) continue;
                        var fallbackInputPts = desc.PrefabNodes[fromPpdIdx].InputPoints;
                        if (fallbackInputPts == null || fallbackInputPts.Count == 0) continue;

                        var fromUid = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, (byte)fromPpdIdx, descN);
                        if (fromUid == 0) continue;
                        var fromNode = _mapper.GetNodeByUid(fromUid);
                        if (fromNode == null || IsZeroNode(fromNode)) continue;

                        for (int j = 0; j < descN; j++)
                        {
                            if (j == fromPpdIdx) continue;
                            var toUid = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, (byte)j, descN);
                            if (toUid == 0) continue;
                            var toNode = _mapper.GetNodeByUid(toUid);
                            if (toNode == null || IsZeroNode(toNode)) continue;
                            float len = Dist(fromNode, toNode);
                            if (len < 0.001f) continue;
                            RegisterNode(fromNode);
                            RegisterNode(toNode);
                            _graph.Edges.Add(new GraphEdge(
                                fromUid, toUid,
                                len * MultLocalRoad, len, "local_road", "prefab"));
                        }
                    }
                }
            }
        }

        private static float[][] BuildNavCurveWaypoints(
            System.Collections.Generic.List<int> curvePath, TsPrefab desc,
            float rotSin, float rotCos,
            float originPpdX, float originPpdZ, float originWX, float originWZ,
            float fromNodeX, float fromNodeZ, float toNodeX, float toNodeZ)
        {
            // Collect knot points (world-space start of first curve + end of every curve).
            // Pin first and last point to the exact road node world positions so prefab
            // waypoints connect seamlessly to road Hermite splines at junction boundaries.
            var knots = new System.Collections.Generic.List<float[]>(curvePath.Count + 1);
            knots.Add(new float[] { fromNodeX, fromNodeZ }); // pinned start
            for (int i = 0; i < curvePath.Count; i++)
            {
                var curve = desc.NavCurves[curvePath[i]];
                // skip first knot (already added as pinned) and last (will be pinned below)
                if (i < curvePath.Count - 1)
                    knots.Add(PpdToWorld(curve.EndX, curve.EndZ, rotSin, rotCos, originPpdX, originPpdZ, originWX, originWZ));
            }
            knots.Add(new float[] { toNodeX, toNodeZ }); // pinned end

            // For 2-point straight segments, return as-is
            if (knots.Count <= 2) return knots.ToArray();

            // Centripetal Catmull-Rom (alpha=0.5) via Barry-Goldman algorithm.
            // Unlike uniform (alpha=0), the centripetal variant is mathematically
            // guaranteed to never produce self-intersections or cusps — critical for
            // unevenly-spaced NavCurve knots at ramp entrances/exits.
            const int STEPS = 4;

            // Compute cumulative t parameter: t_{i+1} = t_i + sqrt(dist(P_i, P_{i+1}))
            var tVals = new float[knots.Count];
            tVals[0] = 0f;
            for (int k = 1; k < knots.Count; k++)
            {
                float dx = knots[k][0] - knots[k-1][0];
                float dz = knots[k][1] - knots[k-1][1];
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                tVals[k] = tVals[k - 1] + (float)Math.Sqrt(dist < 0.001f ? 0.001f : dist);
            }

            var result = new System.Collections.Generic.List<float[]>(knots.Count * STEPS);
            for (int i = 0; i < knots.Count - 1; i++)
            {
                int i0 = i > 0 ? i - 1 : 0;
                int i3 = i + 2 < knots.Count ? i + 2 : knots.Count - 1;
                float[] p0 = knots[i0], p1 = knots[i], p2 = knots[i + 1], p3 = knots[i3];
                float   t0 = tVals[i0], t1 = tVals[i], t2 = tVals[i + 1], t3 = tVals[i3];

                if (i == 0) result.Add(p1);
                for (int s = 1; s <= STEPS; s++)
                {
                    float t = t1 + (t2 - t1) * s / (float)STEPS;
                    // Non-uniform Catmull-Rom evaluation
                    float[] a1 = LerpT(p0, p1, t0, t1, t);
                    float[] a2 = LerpT(p1, p2, t1, t2, t);
                    float[] a3 = LerpT(p2, p3, t2, t3, t);
                    float[] b1 = LerpT(a1, a2, t0, t2, t);
                    float[] b2 = LerpT(a2, a3, t1, t3, t);
                    result.Add(LerpT(b1, b2, t1, t2, t));
                }
            }
            return result.ToArray();
        }

        // Replicate GeoJsonExporter.ExportFerries() Bezier sampling so routing waypoints
        // are pixel-identical to the ferry lines drawn on the map tiles.
        private static float[][] BuildFerryWaypoints(TsFerryConnection conn)
        {
            var pts = new System.Collections.Generic.List<float[]>();

            if (conn.Connections == null || conn.Connections.Count == 0)
            {
                pts.Add(new float[] { conn.StartPortLocation.X, conn.StartPortLocation.Y });
                pts.Add(new float[] { conn.EndPortLocation.X,   conn.EndPortLocation.Y   });
                return pts.ToArray();
            }

            // Build cubic Bezier control-point list (same algorithm as ExportFerries)
            var bp = new System.Collections.Generic.List<(float x, float z)>();

            // Start port → first connection node
            var startYaw = Math.Atan2(conn.Connections[0].Z - conn.StartPortLocation.Y,
                                      conn.Connections[0].X - conn.StartPortLocation.X);
            var bn = RenderHelper.GetBezierControlNodes(
                conn.StartPortLocation.X, conn.StartPortLocation.Y, startYaw,
                conn.Connections[0].X, conn.Connections[0].Z, conn.Connections[0].Rotation);
            bp.Add((conn.StartPortLocation.X,                              conn.StartPortLocation.Y));
            bp.Add((conn.StartPortLocation.X + bn.Item1.X,                 conn.StartPortLocation.Y + bn.Item1.Y));
            bp.Add((conn.Connections[0].X    - bn.Item2.X,                 conn.Connections[0].Z    - bn.Item2.Y));
            bp.Add((conn.Connections[0].X,                                  conn.Connections[0].Z));

            // Intermediate connection nodes
            for (int i = 0; i < conn.Connections.Count - 1; i++)
            {
                var fp = conn.Connections[i];
                var np = conn.Connections[i + 1];
                bn = RenderHelper.GetBezierControlNodes(fp.X, fp.Z, fp.Rotation, np.X, np.Z, np.Rotation);
                bp.Add((fp.X + bn.Item1.X, fp.Z + bn.Item1.Y));
                bp.Add((np.X - bn.Item2.X, np.Z - bn.Item2.Y));
                bp.Add((np.X,               np.Z));
            }

            // Last connection node → end port
            var last    = conn.Connections[conn.Connections.Count - 1];
            var endYaw  = Math.Atan2(conn.EndPortLocation.Y - last.Z,
                                     conn.EndPortLocation.X - last.X);
            bn = RenderHelper.GetBezierControlNodes(
                last.X, last.Z, last.Rotation,
                conn.EndPortLocation.X, conn.EndPortLocation.Y, endYaw);
            bp.Add((last.X                   + bn.Item1.X, last.Z                   + bn.Item1.Y));
            bp.Add((conn.EndPortLocation.X   - bn.Item2.X, conn.EndPortLocation.Y   - bn.Item2.Y));
            bp.Add((conn.EndPortLocation.X,                 conn.EndPortLocation.Y));

            // Sample each cubic Bezier segment — 10 steps (same as ExportFerries)
            const int STEPS = 10;
            for (int i = 0; i < bp.Count - 3; i += 3)
            {
                var p0 = bp[i]; var p1 = bp[i+1]; var p2 = bp[i+2]; var p3 = bp[i+3];
                int start = (i == 0) ? 0 : 1;   // skip joint duplicate after first segment
                for (int t = start; t <= STEPS; t++)
                {
                    float s  = t / (float)STEPS;
                    float s2 = s * s, s3 = s2 * s;
                    float r  = 1f - s, r2 = r * r, r3 = r2 * r;
                    pts.Add(new float[]
                    {
                        r3*p0.x + 3*r2*s*p1.x + 3*r*s2*p2.x + s3*p3.x,
                        r3*p0.z + 3*r2*s*p1.z + 3*r*s2*p2.z + s3*p3.z,
                    });
                }
            }

            return pts.ToArray();
        }

        private static float[] LerpT(float[] a, float[] b, float ta, float tb, float t)
        {
            float dt = tb - ta;
            if (dt < 1e-10f) return new float[] { a[0], a[1] };
            float f = (t - ta) / dt;
            return new float[] { a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f };
        }

        private static float[] PpdToWorld(float px, float pz,
            float rotSin, float rotCos,
            float originPpdX, float originPpdZ, float originWX, float originWZ)
        {
            float dx = px - originPpdX;
            float dz = pz - originPpdZ;
            return new float[]
            {
                dx * rotCos - dz * rotSin + originWX,
                dx * rotSin + dz * rotCos + originWZ,
            };
        }

        private void AddPrefabFullMesh(TsPrefabItem prefab)
        {
            int n = prefab.Nodes.Count;
            for (int i = 0; i < n; i++)
            {
                var fromNode = _mapper.GetNodeByUid(prefab.Nodes[i]);
                if (fromNode == null || IsZeroNode(fromNode)) continue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    var toNode = _mapper.GetNodeByUid(prefab.Nodes[j]);
                    if (toNode == null || IsZeroNode(toNode)) continue;
                    float len = Dist(fromNode, toNode);
                    if (len < 0.001f) continue;
                    RegisterNode(fromNode);
                    RegisterNode(toNode);
                    _graph.Edges.Add(new GraphEdge(
                        fromNode.Uid, toNode.Uid,
                        len * MultLocalRoad, len, "local_road", "prefab"));
                }
            }
        }

        // ── Ferries ──────────────────────────────────────────────────────────

        private void ProcessFerries()
        {
            // Map FerryPortId → road node UID via TsFerryItem.Nodes[0]
            var portToNodeUid = new Dictionary<ulong, ulong>();
            foreach (var ferry in _mapper.FerryConnections)
            {
                if (ferry.Nodes != null && ferry.Nodes.Count > 0)
                    portToNodeUid[ferry.FerryPortId] = ferry.Nodes[0];
            }

            // Ferry port icons sit inside the port area, often off the driveable road network.
            // Add a synthetic approach edge from the ferry port node to the nearest road/prefab
            // node already in the graph so the terminal is reachable from the road network.
            foreach (var kv in portToNodeUid)
            {
                var portNode = _mapper.GetNodeByUid(kv.Value);
                if (portNode == null || IsZeroNode(portNode)) continue;

                var nearest = FindNearestGraphNode(portNode.X, portNode.Z, kv.Value);
                if (nearest == null) continue;

                float dx = nearest.X - portNode.X, dz = nearest.Z - portNode.Z;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 0.001f) continue;

                RegisterNode(portNode);
                _graph.Edges.Add(new GraphEdge(nearest.Uid, kv.Value, len * MultLocalRoad, len, "local_road", "ferry_approach"));
                _graph.Edges.Add(new GraphEdge(kv.Value, nearest.Uid, len * MultLocalRoad, len, "local_road", "ferry_approach"));
            }

            // Canonical (min,max) pair prevents processing the same route twice
            var seen = new HashSet<(ulong, ulong)>();

            foreach (var ferry in _mapper.FerryConnections)
            {
                var connections = _mapper.LookupFerryConnection(ferry.FerryPortId);
                foreach (var conn in connections)
                {
                    if (!portToNodeUid.TryGetValue(conn.StartPortToken, out var startUid)) continue;
                    if (!portToNodeUid.TryGetValue(conn.EndPortToken,   out var endUid))   continue;
                    if (startUid == endUid) continue;

                    var key = startUid < endUid ? (startUid, endUid) : (endUid, startUid);
                    if (!seen.Add(key)) continue;

                    var startNode = _mapper.GetNodeByUid(startUid);
                    var endNode   = _mapper.GetNodeByUid(endUid);
                    if (startNode == null || endNode == null) continue;

                    // Distance from world-space port locations (PointF.Y = game Z axis)
                    float dx   = conn.EndPortLocation.X - conn.StartPortLocation.X;
                    float dz   = conn.EndPortLocation.Y - conn.StartPortLocation.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist < 1f) dist = FerryWeight; // fallback for unresolved ports

                    RegisterNode(startNode);
                    RegisterNode(endNode);

                    // Build the same Bezier-sampled waypoints used by GeoJsonExporter.ExportFerries()
                    // so the route display follows the ferry line exactly as drawn on the map.
                    var fwdWp = BuildFerryWaypoints(conn);
                    var bwdWp = System.Linq.Enumerable.Reverse(fwdWp).ToArray();
                    _graph.Edges.Add(new GraphEdge(startUid, endUid, FerryWeight, dist, "ferry", "ferry", fwdWp));
                    _graph.Edges.Add(new GraphEdge(endUid, startUid, FerryWeight, dist, "ferry", "ferry", bwdWp));
                }
            }
        }

        // ── Companies ─────────────────────────────────────────────────────────

        private void ProcessCompanies()
        {
            // Company depots have internal NavCurve edges (loading docks, parking)
            // but are often isolated from the road network because their entrance
            // prefab doesn't produce a road-facing edge. For each company node that
            // isn't already in the graph, OR for every company node, add a synthetic
            // approach edge to the nearest already-registered graph node — same
            // pattern as ferry_approach.
            foreach (var company in _mapper.Companies)
            {
                if (company.Hidden || company.Nodes == null) continue;

                foreach (var nodeUid in company.Nodes)
                {
                    var compNode = _mapper.GetNodeByUid(nodeUid);
                    if (compNode == null || IsZeroNode(compNode)) continue;

                    var nearest = FindNearestGraphNode(compNode.X, compNode.Z, nodeUid);
                    if (nearest == null) continue;

                    float dx = nearest.X - compNode.X, dz = nearest.Z - compNode.Z;
                    float len = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (len < 0.001f) continue;

                    RegisterNode(compNode);
                    // Bidirectional: trucks can enter and exit the company
                    _graph.Edges.Add(new GraphEdge(nearest.Uid, nodeUid, len * MultLocalRoad, len, "local_road", "company_approach"));
                    _graph.Edges.Add(new GraphEdge(nodeUid, nearest.Uid, len * MultLocalRoad, len, "local_road", "company_approach"));
                }
            }
        }

        // ── Post-processing ──────────────────────────────────────────────────

        private void Deduplicate()
        {
            // Keep only the lowest-weight edge per (from, to) pair (parallel NavCurve lanes)
            var best = new Dictionary<(ulong, ulong), GraphEdge>();
            foreach (var e in _graph.Edges)
            {
                var key = (e.From, e.To);
                if (!best.TryGetValue(key, out var existing) || e.Weight < existing.Weight)
                    best[key] = e;
            }
            _graph.Edges.Clear();
            _graph.Edges.AddRange(best.Values);
        }

        private void PruneOrphans()
        {
            var connected = new HashSet<ulong>();
            foreach (var e in _graph.Edges)
            {
                connected.Add(e.From);
                connected.Add(e.To);
            }
            var toRemove = new List<ulong>();
            foreach (var uid in _graph.Nodes.Keys)
                if (!connected.Contains(uid)) toRemove.Add(uid);
            foreach (var uid in toRemove)
                _graph.Nodes.Remove(uid);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Maps a PPD descriptor control-node index to its global road-node UID,
        /// applying the Origin rotation.
        ///
        /// The .base sector stores Nodes[] beginning at the origin:
        ///   Nodes[0]   ↔ PPD control node Origin
        ///   Nodes[1]   ↔ PPD control node (Origin+1) % N
        ///   Nodes[i]   ↔ PPD control node (Origin+i) % N
        ///
        /// Inverse (what we need here — PPD index k → Nodes index):
        ///   idx = (k - Origin + N) % N
        ///
        /// Note: the formula (k + Origin) % N has the sign reversed and
        /// gives wrong results for Origin != 0 with N > 2.
        /// </summary>
        private static ulong GetGlobalNodeUid(
            System.Collections.Generic.List<ulong> nodes,
            int origin,
            byte descriptorNodeIndex,
            int descriptorNodeCount)
        {
            int idx = (descriptorNodeIndex - origin + descriptorNodeCount) % descriptorNodeCount;
            return idx < nodes.Count ? nodes[idx] : 0UL;
        }

        private void RegisterNode(TsNode node)
        {
            if (!_graph.Nodes.ContainsKey(node.Uid))
                _graph.Nodes[node.Uid] = new GraphNode(node.Uid, node.X, node.Z);
        }

        private GraphNode FindNearestGraphNode(float x, float z, ulong excludeUid = 0)
        {
            GraphNode best = null;
            float bestDSq = float.MaxValue;
            foreach (var gn in _graph.Nodes.Values)
            {
                if (gn.Uid == excludeUid) continue;
                float dx = gn.X - x, dz = gn.Z - z;
                float dSq = dx * dx + dz * dz;
                if (dSq < bestDSq) { bestDSq = dSq; best = gn; }
            }
            return best;
        }

        // Like FindNearestGraphNode but restricted to road nodes — prevents company approach
        // edges from connecting to other isolated company/prefab nodes instead of the road network.
        private GraphNode FindNearestRoadNode(float x, float z, ulong excludeUid = 0)
        {
            GraphNode best = null;
            float bestDSq = float.MaxValue;
            foreach (var uid in _roadNodeUids)
            {
                if (uid == excludeUid) continue;
                if (!_graph.Nodes.TryGetValue(uid, out var gn)) continue;
                float dx = gn.X - x, dz = gn.Z - z;
                float dSq = dx * dx + dz * dz;
                if (dSq < bestDSq) { bestDSq = dSq; best = gn; }
            }
            return best;
        }

        private static bool IsZeroNode(TsNode node) =>
            Math.Abs(node.X) < 0.001f && Math.Abs(node.Z) < 0.001f;

        private static float Dist(TsNode a, TsNode b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static string RoadSpeedClass(TsRoadLook look)
        {
            // First lane containing "freeway" (case-insensitive) → "freeway", otherwise → "local_road"
            foreach (var lane in look.LanesLeft)
                if (lane != null &&
                    lane.IndexOf("freeway", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "freeway";
            foreach (var lane in look.LanesRight)
                if (lane != null &&
                    lane.IndexOf("freeway", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "freeway";
            return "local_road";
        }
    }
}
