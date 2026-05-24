using System;
using System.Collections.Generic;
using TsMap.Helpers.Logger;
using TsMap.TsItem;

namespace TsMap.Routing
{
    public class RoutingGraphBuilder
    {
        private readonly TsMapper _mapper;
        private const float MaxSpeedKph = 130f;
        private const float FerryWeight = 50000f;

        private static float SpeedMult(string speedClass) =>
            MaxSpeedKph / GraphEdge.SpeedClassToKph(speedClass);

        public RoutingGraphBuilder(TsMapper mapper)
        {
            _mapper = mapper;
        }

        public LaneRoutingGraph Build()
        {
            var snapshot = new LaneGraphDebugExporter(_mapper).BuildSnapshot();
            var graph = new LaneRoutingGraph();
            var edgeTypeCounts = new Dictionary<string, int>();
            int zeroLengthConnectors = 0;

            foreach (var node in snapshot.Nodes)
            {
                graph.Nodes[node.Id] = new LaneRoutingNode(node.Id, node.X, node.Z);
            }

            foreach (var edge in snapshot.Edges)
            {
                if (!IsRoutableEdge(edge)) continue;
                if (!graph.Nodes.ContainsKey(edge.From) || !graph.Nodes.ContainsKey(edge.To)) continue;

                float length = PolylineLength(edge.Path);
                if (length < 0.001f && !IsConnectorEdge(edge.Kind)) continue;

                string speedClass = string.IsNullOrEmpty(edge.SpeedClass) ? "local_road" : edge.SpeedClass;
                string itemType = LaneEdgeItemType(edge.Kind);
                if (length < 0.001f && IsConnectorEdge(edge.Kind)) zeroLengthConnectors++;
                edgeTypeCounts.TryGetValue(itemType, out var edgeTypeCount);
                edgeTypeCounts[itemType] = edgeTypeCount + 1;
                graph.Edges.Add(new LaneRoutingEdge(
                    edge.From,
                    edge.To,
                    length * SpeedMult(speedClass),
                    length,
                    speedClass,
                    itemType,
                    edge.Path));
            }

            AddRoadLaneChangeEdges(snapshot, graph, edgeTypeCounts);
            var routingNodeIndex = new RoutingNodeSpatialIndex(graph.Nodes.Values);
            var outgoingNodes = BuildOutgoingNodeSet(graph);
            var incomingNodes = BuildIncomingNodeSet(graph);
            AddFerryEdges(graph, routingNodeIndex, outgoingNodes, incomingNodes, edgeTypeCounts);
            AddCompanyApproachEdges(graph, routingNodeIndex, outgoingNodes, incomingNodes, edgeTypeCounts);
            Deduplicate(graph);
            PruneOrphans(graph);

            Logger.Instance.Info($"[LaneRouting] Built graph: {graph.Nodes.Count} nodes, {graph.Edges.Count} edges");
            foreach (var pair in edgeTypeCounts)
                Logger.Instance.Info($"[LaneRouting]   {pair.Key}: {pair.Value}");
            Logger.Instance.Info($"[LaneRouting]   zero-length connectors kept: {zeroLengthConnectors}");
            return graph;
        }

        private void AddFerryEdges(
            LaneRoutingGraph graph,
            RoutingNodeSpatialIndex routingNodeIndex,
            HashSet<string> outgoingNodes,
            HashSet<string> incomingNodes,
            Dictionary<string, int> edgeTypeCounts)
        {
            var portToNodeUid = new Dictionary<ulong, ulong>();
            foreach (var ferry in _mapper.FerryConnections)
            {
                if (ferry.Nodes != null && ferry.Nodes.Count > 0)
                    portToNodeUid[ferry.FerryPortId] = ferry.Nodes[0];
            }

            foreach (var kv in portToNodeUid)
            {
                var portNode = _mapper.GetNodeByUid(kv.Value);
                if (portNode == null || IsZeroNode(portNode)) continue;

                string portUid = FerryPortNodeUid(kv.Value);
                graph.Nodes[portUid] = new LaneRoutingNode(portUid, portNode.X, portNode.Z);

                var exitNode = routingNodeIndex.FindNearest(portNode.X, portNode.Z, n => outgoingNodes.Contains(n.Uid));
                if (exitNode != null)
                {
                    AddApproachEdge(graph, portUid, exitNode.Uid, "ferry_approach");
                    Increment(edgeTypeCounts, "ferry_approach");
                }

                var entryNode = routingNodeIndex.FindNearest(portNode.X, portNode.Z, n => incomingNodes.Contains(n.Uid));
                if (entryNode != null)
                {
                    AddApproachEdge(graph, entryNode.Uid, portUid, "ferry_approach");
                    Increment(edgeTypeCounts, "ferry_approach");
                }
            }

            var seen = new HashSet<string>();
            foreach (var ferry in _mapper.FerryConnections)
            {
                var connections = _mapper.LookupFerryConnection(ferry.FerryPortId);
                foreach (var conn in connections)
                {
                    if (!portToNodeUid.TryGetValue(conn.StartPortToken, out var startRawUid)) continue;
                    if (!portToNodeUid.TryGetValue(conn.EndPortToken, out var endRawUid)) continue;
                    if (startRawUid == endRawUid) continue;

                    string startUid = FerryPortNodeUid(startRawUid);
                    string endUid = FerryPortNodeUid(endRawUid);
                    if (!graph.Nodes.ContainsKey(startUid) || !graph.Nodes.ContainsKey(endUid)) continue;

                    string key = string.CompareOrdinal(startUid, endUid) < 0
                        ? startUid + "\n" + endUid
                        : endUid + "\n" + startUid;
                    if (!seen.Add(key)) continue;

                    float dx = conn.EndPortLocation.X - conn.StartPortLocation.X;
                    float dz = conn.EndPortLocation.Y - conn.StartPortLocation.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist < 1f) dist = FerryWeight;

                    var fwdWp = BuildFerryWaypoints(conn);
                    var bwdWp = ReversePath(fwdWp);
                    graph.Edges.Add(new LaneRoutingEdge(startUid, endUid, FerryWeight, dist, "ferry", "ferry", fwdWp));
                    graph.Edges.Add(new LaneRoutingEdge(endUid, startUid, FerryWeight, dist, "ferry", "ferry", bwdWp));
                    Increment(edgeTypeCounts, "ferry", 2);
                }
            }
        }

        private void AddCompanyApproachEdges(
            LaneRoutingGraph graph,
            RoutingNodeSpatialIndex routingNodeIndex,
            HashSet<string> outgoingNodes,
            HashSet<string> incomingNodes,
            Dictionary<string, int> edgeTypeCounts)
        {
            foreach (var company in _mapper.Companies)
            {
                if (company.Hidden || company.Nodes == null) continue;

                foreach (var nodeUid in company.Nodes)
                {
                    var companyNode = _mapper.GetNodeByUid(nodeUid);
                    if (companyNode == null || IsZeroNode(companyNode)) continue;

                    string companyUid = CompanyNodeUid(nodeUid);
                    graph.Nodes[companyUid] = new LaneRoutingNode(companyUid, companyNode.X, companyNode.Z);

                    var exitNode = routingNodeIndex.FindNearest(companyNode.X, companyNode.Z, n => outgoingNodes.Contains(n.Uid));
                    if (exitNode != null)
                    {
                        AddApproachEdge(graph, companyUid, exitNode.Uid, "company_approach");
                        Increment(edgeTypeCounts, "company_approach");
                    }

                    var entryNode = routingNodeIndex.FindNearest(companyNode.X, companyNode.Z, n => incomingNodes.Contains(n.Uid));
                    if (entryNode != null)
                    {
                        AddApproachEdge(graph, entryNode.Uid, companyUid, "company_approach");
                        Increment(edgeTypeCounts, "company_approach");
                    }
                }
            }
        }

        private static HashSet<string> BuildOutgoingNodeSet(LaneRoutingGraph graph)
        {
            var result = new HashSet<string>();
            foreach (var edge in graph.Edges)
                result.Add(edge.From);
            return result;
        }

        private static HashSet<string> BuildIncomingNodeSet(LaneRoutingGraph graph)
        {
            var result = new HashSet<string>();
            foreach (var edge in graph.Edges)
                result.Add(edge.To);
            return result;
        }

        private static void AddApproachEdge(LaneRoutingGraph graph, string fromUid, string toUid, string itemType)
        {
            if (!graph.Nodes.TryGetValue(fromUid, out var from)) return;
            if (!graph.Nodes.TryGetValue(toUid, out var to)) return;

            float dx = to.X - from.X;
            float dz = to.Z - from.Z;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.001f) return;

            var path = new[] { new[] { from.X, from.Z }, new[] { to.X, to.Z } };
            float weight = len * SpeedMult("local_road");
            graph.Edges.Add(new LaneRoutingEdge(fromUid, toUid, weight, len, "local_road", itemType, path));
        }

        private static string CompanyNodeUid(ulong rawUid) => "company:" + rawUid.ToString("X");
        private static string FerryPortNodeUid(ulong rawUid) => "ferry_port:" + rawUid.ToString("X");

        private static bool IsZeroNode(TsNode node) =>
            Math.Abs(node.X) < 0.001f && Math.Abs(node.Z) < 0.001f;

        private static void AddRoadLaneChangeEdges(
            LaneGraphDebugExporter.LaneGraphSnapshot snapshot,
            LaneRoutingGraph graph,
            Dictionary<string, int> edgeTypeCounts)
        {
            const float LaneChangePenaltyMeters = 45f;
            var groups = new Dictionary<string, List<LaneGraphDebugExporter.SnapshotEdge>>();

            foreach (var edge in snapshot.Edges)
            {
                if (edge.Kind != "road") continue;
                if (!TryParseRoadLane(edge.Lane, out var side, out _)) continue;
                bool forward = edge.From.EndsWith(":start", StringComparison.Ordinal) &&
                               edge.To.EndsWith(":end", StringComparison.Ordinal);
                string key = edge.SourceUid + ":" + side + ":" + (forward ? "forward" : "backward");
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<LaneGraphDebugExporter.SnapshotEdge>();
                    groups[key] = list;
                }
                list.Add(edge);
            }

            foreach (var list in groups.Values)
            {
                list.Sort((a, b) =>
                {
                    TryParseRoadLane(a.Lane, out _, out var laneA);
                    TryParseRoadLane(b.Lane, out _, out var laneB);
                    return laneA.CompareTo(laneB);
                });

                for (int i = 0; i < list.Count - 1; i++)
                {
                    if (!TryParseRoadLane(list[i].Lane, out _, out var laneA)) continue;
                    if (!TryParseRoadLane(list[i + 1].Lane, out _, out var laneB)) continue;
                    if (laneB != laneA + 1) continue;

                    AddLaneChangePair(graph, list[i].From, list[i + 1].From, LaneChangePenaltyMeters);
                    AddLaneChangePair(graph, list[i].To, list[i + 1].To, LaneChangePenaltyMeters);
                    Increment(edgeTypeCounts, "lane_change", 4);
                }
            }
        }

        private static void AddLaneChangePair(LaneRoutingGraph graph, string aUid, string bUid, float penaltyMeters)
        {
            if (!graph.Nodes.TryGetValue(aUid, out var a)) return;
            if (!graph.Nodes.TryGetValue(bUid, out var b)) return;

            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.001f) return;

            float weight = (length + penaltyMeters) * SpeedMult("local_road");
            var pathAB = new[] { new[] { a.X, a.Z }, new[] { b.X, b.Z } };
            var pathBA = new[] { new[] { b.X, b.Z }, new[] { a.X, a.Z } };
            graph.Edges.Add(new LaneRoutingEdge(aUid, bUid, weight, length, "local_road", "lane_change", pathAB));
            graph.Edges.Add(new LaneRoutingEdge(bUid, aUid, weight, length, "local_road", "lane_change", pathBA));
        }

        private static void Increment(Dictionary<string, int> counts, string key, int amount = 1)
        {
            counts.TryGetValue(key, out var count);
            counts[key] = count + amount;
        }

        private static bool TryParseRoadLane(string lane, out string side, out int index)
        {
            side = "";
            index = 0;
            if (string.IsNullOrEmpty(lane)) return false;
            int colon = lane.IndexOf(':');
            if (colon <= 0 || colon + 1 >= lane.Length) return false;
            side = lane.Substring(0, colon);
            return (side == "left" || side == "right") &&
                   int.TryParse(lane.Substring(colon + 1), out index);
        }

        private static float[][] BuildFerryWaypoints(TsFerryConnection conn)
        {
            var pts = new List<float[]>();

            if (conn.Connections == null || conn.Connections.Count == 0)
            {
                pts.Add(new[] { conn.StartPortLocation.X, conn.StartPortLocation.Y });
                pts.Add(new[] { conn.EndPortLocation.X, conn.EndPortLocation.Y });
                return pts.ToArray();
            }

            var bp = new List<Tuple<float, float>>();
            var startYaw = Math.Atan2(conn.Connections[0].Z - conn.StartPortLocation.Y,
                                      conn.Connections[0].X - conn.StartPortLocation.X);
            var bn = RenderHelper.GetBezierControlNodes(
                conn.StartPortLocation.X, conn.StartPortLocation.Y, startYaw,
                conn.Connections[0].X, conn.Connections[0].Z, conn.Connections[0].Rotation);
            bp.Add(Tuple.Create(conn.StartPortLocation.X, conn.StartPortLocation.Y));
            bp.Add(Tuple.Create(conn.StartPortLocation.X + bn.Item1.X, conn.StartPortLocation.Y + bn.Item1.Y));
            bp.Add(Tuple.Create(conn.Connections[0].X - bn.Item2.X, conn.Connections[0].Z - bn.Item2.Y));
            bp.Add(Tuple.Create(conn.Connections[0].X, conn.Connections[0].Z));

            for (int i = 0; i < conn.Connections.Count - 1; i++)
            {
                var fp = conn.Connections[i];
                var np = conn.Connections[i + 1];
                bn = RenderHelper.GetBezierControlNodes(fp.X, fp.Z, fp.Rotation, np.X, np.Z, np.Rotation);
                bp.Add(Tuple.Create(fp.X + bn.Item1.X, fp.Z + bn.Item1.Y));
                bp.Add(Tuple.Create(np.X - bn.Item2.X, np.Z - bn.Item2.Y));
                bp.Add(Tuple.Create(np.X, np.Z));
            }

            var last = conn.Connections[conn.Connections.Count - 1];
            var endYaw = Math.Atan2(conn.EndPortLocation.Y - last.Z,
                                    conn.EndPortLocation.X - last.X);
            bn = RenderHelper.GetBezierControlNodes(
                last.X, last.Z, last.Rotation,
                conn.EndPortLocation.X, conn.EndPortLocation.Y, endYaw);
            bp.Add(Tuple.Create(last.X + bn.Item1.X, last.Z + bn.Item1.Y));
            bp.Add(Tuple.Create(conn.EndPortLocation.X - bn.Item2.X, conn.EndPortLocation.Y - bn.Item2.Y));
            bp.Add(Tuple.Create(conn.EndPortLocation.X, conn.EndPortLocation.Y));

            const int Steps = 10;
            for (int i = 0; i < bp.Count - 3; i += 3)
            {
                var p0 = bp[i];
                var p1 = bp[i + 1];
                var p2 = bp[i + 2];
                var p3 = bp[i + 3];
                int start = i == 0 ? 0 : 1;
                for (int t = start; t <= Steps; t++)
                {
                    float s = t / (float)Steps;
                    float s2 = s * s;
                    float s3 = s2 * s;
                    float r = 1f - s;
                    float r2 = r * r;
                    float r3 = r2 * r;
                    pts.Add(new[]
                    {
                        r3 * p0.Item1 + 3 * r2 * s * p1.Item1 + 3 * r * s2 * p2.Item1 + s3 * p3.Item1,
                        r3 * p0.Item2 + 3 * r2 * s * p1.Item2 + 3 * r * s2 * p2.Item2 + s3 * p3.Item2,
                    });
                }
            }

            return pts.ToArray();
        }

        private static float[][] ReversePath(float[][] path)
        {
            if (path == null) return null;
            var result = new float[path.Length][];
            for (int i = 0; i < path.Length; i++)
            {
                var pt = path[path.Length - 1 - i];
                result[i] = new[] { pt[0], pt[1] };
            }
            return result;
        }

        private class RoutingNodeSpatialIndex
        {
            private const float CellSize = 1000f;
            private readonly Dictionary<Tuple<int, int>, List<LaneRoutingNode>> _cells =
                new Dictionary<Tuple<int, int>, List<LaneRoutingNode>>();

            public RoutingNodeSpatialIndex(IEnumerable<LaneRoutingNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node.Uid.StartsWith("company:", StringComparison.Ordinal)) continue;
                    if (node.Uid.StartsWith("ferry_port:", StringComparison.Ordinal)) continue;

                    var key = CellKey(node.X, node.Z);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<LaneRoutingNode>();
                        _cells[key] = list;
                    }
                    list.Add(node);
                }
            }

            public LaneRoutingNode FindNearest(float x, float z, Func<LaneRoutingNode, bool> predicate = null)
            {
                var center = CellKey(x, z);
                LaneRoutingNode best = null;
                float bestDSq = float.MaxValue;

                for (int radius = 0; radius <= 8; radius++)
                {
                    bool visitedAny = false;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            if (Math.Abs(dx) != radius && Math.Abs(dz) != radius) continue;
                            var key = Tuple.Create(center.Item1 + dx, center.Item2 + dz);
                            if (!_cells.TryGetValue(key, out var list)) continue;
                            visitedAny = true;
                            foreach (var node in list)
                            {
                                if (predicate != null && !predicate(node)) continue;
                                float ndx = node.X - x;
                                float ndz = node.Z - z;
                                float dSq = ndx * ndx + ndz * ndz;
                                if (dSq < bestDSq)
                                {
                                    bestDSq = dSq;
                                    best = node;
                                }
                            }
                        }
                    }

                    if (best != null && (!visitedAny || radius * CellSize > Math.Sqrt(bestDSq) + CellSize))
                        return best;
                }

                return best;
            }

            private static Tuple<int, int> CellKey(float x, float z)
            {
                return Tuple.Create(
                    (int)Math.Floor(x / CellSize),
                    (int)Math.Floor(z / CellSize));
            }
        }

        private static bool IsRoutableEdge(LaneGraphDebugExporter.SnapshotEdge edge)
        {
            switch (edge.Kind)
            {
                case "road":
                case "prefab":
                case "snap_good":
                case "snap_adjusted":
                case "prefab_link_good":
                case "prefab_link_adjusted":
                case "road_link_good":
                case "road_link_adjusted":
                    return true;
                default:
                    return false;
            }
        }

        private static string LaneEdgeItemType(string kind)
        {
            if (kind == "road") return "road";
            if (kind == "prefab") return "prefab";
            if (kind == "snap_good" || kind == "snap_adjusted") return "lane_link";
            if (kind == "road_link_good" || kind == "road_link_adjusted") return "road_link";
            if (kind == "prefab_link_good" || kind == "prefab_link_adjusted") return "prefab_link";
            return "lane";
        }

        private static bool IsConnectorEdge(string kind)
        {
            return kind == "snap_good"
                || kind == "snap_adjusted"
                || kind == "road_link_good"
                || kind == "road_link_adjusted"
                || kind == "prefab_link_good"
                || kind == "prefab_link_adjusted";
        }

        private static float PolylineLength(float[][] path)
        {
            if (path == null || path.Length < 2) return 0f;
            float length = 0f;
            for (int i = 1; i < path.Length; i++)
            {
                float dx = path[i][0] - path[i - 1][0];
                float dz = path[i][1] - path[i - 1][1];
                length += (float)Math.Sqrt(dx * dx + dz * dz);
            }
            return length;
        }

        private static void Deduplicate(LaneRoutingGraph graph)
        {
            var best = new Dictionary<string, LaneRoutingEdge>();
            foreach (var edge in graph.Edges)
            {
                string key = edge.From + "\n" + edge.To;
                if (!best.TryGetValue(key, out var existing) || edge.Weight < existing.Weight)
                    best[key] = edge;
            }
            graph.Edges.Clear();
            graph.Edges.AddRange(best.Values);
        }

        private static void PruneOrphans(LaneRoutingGraph graph)
        {
            var connected = new HashSet<string>();
            foreach (var edge in graph.Edges)
            {
                connected.Add(edge.From);
                connected.Add(edge.To);
            }

            var remove = new List<string>();
            foreach (var uid in graph.Nodes.Keys)
                if (!connected.Contains(uid)) remove.Add(uid);
            foreach (var uid in remove)
                graph.Nodes.Remove(uid);
        }
    }
}
