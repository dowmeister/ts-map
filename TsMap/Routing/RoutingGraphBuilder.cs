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

        // Reference speed (fastest class) used to normalise weights so freeway mult = 1.0.
        // All multipliers are computed as MaxSpeedKph / speedKph so the A* heuristic
        // (raw Euclidean distance) stays admissible: min possible weight == length * 1.0.
        private const float MaxSpeedKph  = 130f;
        private const float GpsAvoidMult = 50f;
        private const float FerryWeight  = 50000f;

        private static float SpeedMult(string speedClass) =>
            MaxSpeedKph / GraphEdge.SpeedClassToKph(speedClass);

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
                float  mult = SpeedMult(sc);
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

                for (int fromPpdIdx = 0; fromPpdIdx < descN; fromPpdIdx++)
                {
                    var inputPts = desc.PrefabNodes[fromPpdIdx].InputPoints;
                    if (inputPts == null || inputPts.Count == 0) continue;

                    var fromUid = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, (byte)fromPpdIdx, descN);
                    if (fromUid == 0) continue;
                    var fromNode = _mapper.GetNodeByUid(fromUid);
                    if (fromNode == null || IsZeroNode(fromNode)) continue;

                    var candidateEdges = new List<PrefabLaneCandidate>();

                    foreach (var inputLaneIdx in inputPts)
                    {
                        foreach (var curvePath in GetCurvePaths(desc, curveToOutputNode, inputLaneIdx))
                        {
                            int toPpdIdx = curvePath.EndNodeIndex;
                            if (toPpdIdx == fromPpdIdx) continue;

                            var toUid = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, (byte)toPpdIdx, descN);
                            if (toUid == 0) continue;

                            var toNode = _mapper.GetNodeByUid(toUid);
                            if (toNode == null || IsZeroNode(toNode)) continue;

                            float[][] waypoints = null;
                            float len = Dist(fromNode, toNode);
                            if (hasTransform)
                            {
                                waypoints = BuildNavCurveWaypoints(
                                    curvePath.CurveIndices, desc, rotSin, rotCos,
                                    originPpdX, originPpdZ, originWX, originWZ);
                                len = waypoints != null && waypoints.Length >= 2
                                    ? PolylineLength(waypoints)
                                    : curvePath.Length;
                            }
                            if (len < 0.001f) continue;

                            candidateEdges.Add(new PrefabLaneCandidate
                            {
                                ToUid = toUid,
                                ToNode = toNode,
                                Length = len,
                                Waypoints = waypoints,
                            });
                        }
                    }

                    foreach (var candidate in PickShortestPerTarget(candidateEdges))
                    {
                        RegisterNode(fromNode);
                        RegisterNode(candidate.ToNode);
                        _graph.Edges.Add(new GraphEdge(
                            fromUid, candidate.ToUid,
                            candidate.Length * SpeedMult("local_road"), candidate.Length, "local_road", "prefab",
                            candidate.Waypoints));
                    }
                }

                // If a valid prefab descriptor has no reachable NavCurve branch,
                // keep it disconnected instead of inventing full-mesh links.
                // This mirrors truckermudgeon's laneInfo/connections behavior.
            }
        }

        private static float[][] BuildNavCurveWaypoints(
            System.Collections.Generic.List<int> curvePath, TsPrefab desc,
            float rotSin, float rotCos,
            float originPpdX, float originPpdZ, float originWX, float originWZ)
        {
            var points = new System.Collections.Generic.List<float[]>(curvePath.Count * 8);

            for (int pathIdx = 0; pathIdx < curvePath.Count; pathIdx++)
            {
                var curve = desc.NavCurves[curvePath[pathIdx]];
                var sampled = SampleNavCurve(curve, rotSin, rotCos, originPpdX, originPpdZ, originWX, originWZ);
                int start = pathIdx == 0 ? 0 : 1;
                for (int i = start; i < sampled.Length; i++)
                {
                    AddPointIfDistinct(points, sampled[i]);
                }
            }

            if (points.Count <= 2) return points.ToArray();

            return DouglasPeucker(points.ToArray(), 0.1f);
        }

        private class PrefabLaneCandidate
        {
            public ulong ToUid;
            public TsNode ToNode;
            public float Length;
            public float[][] Waypoints;
        }

        private class CurvePath
        {
            public int EndNodeIndex;
            public System.Collections.Generic.List<int> CurveIndices;
            public float Length;
        }

        private static System.Collections.Generic.List<CurvePath> GetCurvePaths(
            TsPrefab desc,
            System.Collections.Generic.Dictionary<int, int> endingCurveIndexToNodeIndex,
            int inputLaneIndex)
        {
            var seenIndices = new System.Collections.Generic.HashSet<int>();

            CurvePath Prefix(CurvePath path, int curveIndex)
            {
                var indices = new System.Collections.Generic.List<int>(path.CurveIndices);
                indices.Insert(0, curveIndex);
                return new CurvePath
                {
                    EndNodeIndex = path.EndNodeIndex,
                    CurveIndices = indices,
                    Length = path.Length,
                };
            }

            System.Collections.Generic.List<CurvePath> GetPaths(int curveIndex)
            {
                var paths = new System.Collections.Generic.List<CurvePath>();
                if (seenIndices.Contains(curveIndex)) return paths;
                seenIndices.Add(curveIndex);

                if (endingCurveIndexToNodeIndex.TryGetValue(curveIndex, out var nodeIndex))
                {
                    paths.Add(new CurvePath
                    {
                        EndNodeIndex = nodeIndex,
                        CurveIndices = new System.Collections.Generic.List<int>(),
                    });
                    return paths;
                }

                if (curveIndex < 0 || curveIndex >= desc.NavCurves.Count) return paths;
                var curve = desc.NavCurves[curveIndex];
                if (curve.NextLines != null)
                {
                    foreach (var nextCurveIndex in curve.NextLines)
                    {
                        foreach (var path in GetPaths(nextCurveIndex))
                        {
                            paths.Add(Prefix(path, nextCurveIndex));
                        }
                    }
                }

                return paths;
            }

            var result = GetPaths(inputLaneIndex);
            for (int i = 0; i < result.Count; i++)
            {
                var path = Prefix(result[i], inputLaneIndex);
                path.Length = CurvePathLength(desc, path.CurveIndices);
                result[i] = path;
            }
            return result;
        }

        private static float CurvePathLength(TsPrefab desc, System.Collections.Generic.List<int> curveIndices)
        {
            var len = 0f;
            foreach (var curveIndex in curveIndices)
            {
                if (curveIndex < 0 || curveIndex >= desc.NavCurves.Count) continue;
                var curve = desc.NavCurves[curveIndex];
                len += curve.Length > 0.001f ? curve.Length : LocalCurveChord(curve);
            }
            return len;
        }

        private static System.Collections.Generic.List<PrefabLaneCandidate> PickShortestPerTarget(
            System.Collections.Generic.List<PrefabLaneCandidate> candidates)
        {
            candidates.Sort((a, b) => a.Length.CompareTo(b.Length));
            var result = new System.Collections.Generic.List<PrefabLaneCandidate>();
            var seenTargets = new System.Collections.Generic.HashSet<ulong>();
            foreach (var candidate in candidates)
            {
                if (seenTargets.Add(candidate.ToUid))
                    result.Add(candidate);
            }
            return result;
        }

        private static float LocalCurveChord(TsNavCurve curve)
        {
            float dx = curve.EndX - curve.StartX;
            float dz = curve.EndZ - curve.StartZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float[][] SampleNavCurve(
            TsNavCurve curve,
            float rotSin, float rotCos,
            float originPpdX, float originPpdZ, float originWX, float originWZ)
        {
            float dx = curve.EndX - curve.StartX;
            float dz = curve.EndZ - curve.StartZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist < 0.001f)
            {
                return new[]
                {
                    PpdToWorld(curve.StartX, curve.StartZ, rotSin, rotCos, originPpdX, originPpdZ, originWX, originWZ),
                    PpdToWorld(curve.EndX, curve.EndZ, rotSin, rotCos, originPpdX, originPpdZ, originWX, originWZ),
                };
            }

            double startRot = PrefabCurveRotation(curve.StartRotW, curve.StartRotY);
            double endRot   = PrefabCurveRotation(curve.EndRotW,   curve.EndRotY);
            double delta    = NormalizeRadians(startRot - endRot);
            double stepEstimate = Math.Floor(Math.Abs(Math.Tan(delta)) * 20.0) + 1.0;
            if (double.IsNaN(stepEstimate) || double.IsInfinity(stepEstimate)) stepEstimate = 8.0;
            int steps = (int)Math.Min(8.0, stepEstimate);
            if (steps < 1) steps = 1;

            float startTanX = (float)Math.Cos(startRot) * dist;
            float startTanZ = (float)Math.Sin(startRot) * dist;
            float endTanX   = (float)Math.Cos(endRot) * dist;
            float endTanZ   = (float)Math.Sin(endRot) * dist;

            var result = new float[steps + 1][];
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float h00 =  2f * t * t * t - 3f * t * t + 1f;
                float h10 =        t * t * t - 2f * t * t + t;
                float h01 = -2f * t * t * t + 3f * t * t;
                float h11 =        t * t * t -       t * t;
                float x = h00 * curve.StartX + h10 * startTanX + h01 * curve.EndX + h11 * endTanX;
                float z = h00 * curve.StartZ + h10 * startTanZ + h01 * curve.EndZ + h11 * endTanZ;
                result[i] = PpdToWorld(x, z, rotSin, rotCos, originPpdX, originPpdZ, originWX, originWZ);
            }
            return result;
        }

        private static double PrefabCurveRotation(float qw, float qy)
        {
            return NormalizeRadians(Math.Atan2(-qy, qw) * 2.0 - Math.PI / 2.0);
        }

        private static double NormalizeRadians(double angle)
        {
            while (angle <= -Math.PI) angle += Math.PI * 2.0;
            while (angle > Math.PI) angle -= Math.PI * 2.0;
            return angle;
        }

        private static void AddPointIfDistinct(System.Collections.Generic.List<float[]> points, float[] point)
        {
            if (points.Count > 0)
            {
                var prev = points[points.Count - 1];
                float dx = prev[0] - point[0];
                float dz = prev[1] - point[1];
                if (dx * dx + dz * dz < 0.0001f) return;
            }
            points.Add(point);
        }

        private static float PolylineLength(float[][] pts)
        {
            if (pts == null || pts.Length < 2) return 0f;
            float len = 0f;
            for (int i = 1; i < pts.Length; i++)
            {
                float dx = pts[i][0] - pts[i - 1][0];
                float dz = pts[i][1] - pts[i - 1][1];
                len += (float)Math.Sqrt(dx * dx + dz * dz);
            }
            return len;
        }

        private static float[][] DouglasPeucker(float[][] pts, float epsilon)
        {
            if (pts.Length <= 2) return pts;
            float ax = pts[0][0], az = pts[0][1];
            float bx = pts[pts.Length - 1][0], bz = pts[pts.Length - 1][1];
            float abLen = (float)Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(bz - az) * (bz - az));
            float maxDist = 0f; int maxIdx = 0;
            for (int i = 1; i < pts.Length - 1; i++)
            {
                float dist;
                if (abLen < 1e-6f)
                {
                    float dx = pts[i][0] - ax, dz = pts[i][1] - az;
                    dist = (float)Math.Sqrt(dx * dx + dz * dz);
                }
                else
                {
                    dist = Math.Abs((bz - az) * pts[i][0] - (bx - ax) * pts[i][1] + bx * az - bz * ax) / abLen;
                }
                if (dist > maxDist) { maxDist = dist; maxIdx = i; }
            }
            if (maxDist <= epsilon) return new[] { pts[0], pts[pts.Length - 1] };

            var left = new float[maxIdx + 1][];
            Array.Copy(pts, 0, left, 0, maxIdx + 1);
            var right = new float[pts.Length - maxIdx][];
            Array.Copy(pts, maxIdx, right, 0, pts.Length - maxIdx);
            var l = DouglasPeucker(left, epsilon);
            var r = DouglasPeucker(right, epsilon);
            var result = new float[l.Length + r.Length - 1][];
            Array.Copy(l, result, l.Length);
            Array.Copy(r, 1, result, l.Length, r.Length - 1);
            return result;
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
                        len * SpeedMult("local_road"), len, "local_road", "prefab"));
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
                _graph.Edges.Add(new GraphEdge(nearest.Uid, kv.Value, len * SpeedMult("local_road"), len, "local_road", "ferry_approach"));
                _graph.Edges.Add(new GraphEdge(kv.Value, nearest.Uid, len * SpeedMult("local_road"), len, "local_road", "ferry_approach"));
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
                    _graph.Edges.Add(new GraphEdge(nearest.Uid, nodeUid, len * SpeedMult("local_road"), len, "local_road", "company_approach"));
                    _graph.Edges.Add(new GraphEdge(nodeUid, nearest.Uid, len * SpeedMult("local_road"), len, "local_road", "company_approach"));
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
            // Check all lane names for speed class keywords (matches truckermudgeon getLaneSpeedClass).
            // Priority order: motorway/freeway (130) > expressway (110) > divided (100) > slow (50) > local (90).
            foreach (var lane in look.LanesLeft)
            {
                var c = LaneSpeedClass(lane);
                if (c != null) return c;
            }
            foreach (var lane in look.LanesRight)
            {
                var c = LaneSpeedClass(lane);
                if (c != null) return c;
            }
            return "local_road";
        }

        private static string LaneSpeedClass(string lane)
        {
            if (lane == null) return null;
            if (lane.IndexOf("motorway",   StringComparison.OrdinalIgnoreCase) >= 0) return "motorway";
            if (lane.IndexOf("freeway",    StringComparison.OrdinalIgnoreCase) >= 0) return "freeway";
            if (lane.IndexOf("expressway", StringComparison.OrdinalIgnoreCase) >= 0) return "expressway";
            if (lane.IndexOf("divided",    StringComparison.OrdinalIgnoreCase) >= 0) return "divided";
            if (lane.IndexOf("slow_road",  StringComparison.OrdinalIgnoreCase) >= 0) return "slow_road";
            if (lane.IndexOf("slow road",  StringComparison.OrdinalIgnoreCase) >= 0) return "slow_road";
            return null;
        }
    }
}
