using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TsMap.Common;
using TsMap.Helpers.Logger;
using TsMap.TsItem;

namespace TsMap.Routing
{
    public class LaneGraphDebugExporter
    {
        private readonly TsMapper _mapper;
        private readonly Dictionary<string, LaneDebugNode> _nodes = new Dictionary<string, LaneDebugNode>();
        private readonly List<LaneDebugEdge> _edges = new List<LaneDebugEdge>();
        private readonly List<LaneEndpoint> _endpoints = new List<LaneEndpoint>();
        private readonly HashSet<string> _matchedEndpointIds = new HashSet<string>();
        private readonly Dictionary<LaneDebugEdge, EdgeSnapTargets> _pendingRoadSnaps = new Dictionary<LaneDebugEdge, EdgeSnapTargets>();

        public LaneGraphDebugExporter(TsMapper mapper)
        {
            _mapper = mapper;
        }

        public void Export(string filePath)
        {
            _nodes.Clear();
            _edges.Clear();
            _endpoints.Clear();
            _matchedEndpointIds.Clear();
            _pendingRoadSnaps.Clear();

            ProcessRoads();
            ProcessPrefabs();
            SnapRoadEndpointsToPrefabs();

            Logger.Instance.Info($"[LaneGraphDebug] Writing {filePath} ...");
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs))
            using (var jw = new JsonTextWriter(sw))
            {
                jw.Formatting = Formatting.None;
                jw.WriteStartObject();

                jw.WritePropertyName("meta");
                jw.WriteStartObject();
                jw.WritePropertyName("nodeCount"); jw.WriteValue(_nodes.Count);
                jw.WritePropertyName("edgeCount"); jw.WriteValue(_edges.Count);
                jw.WritePropertyName("generatedAt"); jw.WriteValue(DateTime.UtcNow.ToString("o"));
                jw.WriteEndObject();

                jw.WritePropertyName("nodes");
                jw.WriteStartArray();
                foreach (var node in _nodes.Values)
                {
                    jw.WriteStartObject();
                    jw.WritePropertyName("id"); jw.WriteValue(node.Id);
                    jw.WritePropertyName("x"); jw.WriteValue(Math.Round(node.X, 4));
                    jw.WritePropertyName("z"); jw.WriteValue(Math.Round(node.Z, 4));
                    jw.WritePropertyName("kind"); jw.WriteValue(node.Kind);
                    jw.WritePropertyName("sourceUid"); jw.WriteValue(node.SourceUid);
                    jw.WritePropertyName("lane"); jw.WriteValue(node.Lane);
                    jw.WritePropertyName("rawNodeUid"); jw.WriteValue(node.RawNodeUid);
                    jw.WritePropertyName("snapStatus"); jw.WriteValue(node.SnapStatus);
                    jw.WritePropertyName("snapDetail"); jw.WriteValue(node.SnapDetail);
                    jw.WriteEndObject();
                }
                jw.WriteEndArray();

                jw.WritePropertyName("edges");
                jw.WriteStartArray();
                foreach (var edge in _edges)
                {
                    jw.WriteStartObject();
                    jw.WritePropertyName("from"); jw.WriteValue(edge.From);
                    jw.WritePropertyName("to"); jw.WriteValue(edge.To);
                    jw.WritePropertyName("kind"); jw.WriteValue(edge.Kind);
                    jw.WritePropertyName("sourceUid"); jw.WriteValue(edge.SourceUid);
                    jw.WritePropertyName("lane"); jw.WriteValue(edge.Lane);
                    jw.WritePropertyName("path");
                    jw.WriteStartArray();
                    foreach (var pt in edge.Path)
                    {
                        jw.WriteStartArray();
                        jw.WriteValue(Math.Round(pt[0], 4));
                        jw.WriteValue(Math.Round(pt[1], 4));
                        jw.WriteEndArray();
                    }
                    jw.WriteEndArray();
                    jw.WriteEndObject();
                }
                jw.WriteEndArray();

                jw.WriteEndObject();
            }
            Logger.Instance.Info($"[LaneGraphDebug] Export complete: {_nodes.Count} nodes, {_edges.Count} edges");
        }

        public void ExportGeoJson(string filePath)
        {
            if (_nodes.Count == 0 && _edges.Count == 0)
            {
                _endpoints.Clear();
                _matchedEndpointIds.Clear();
                _pendingRoadSnaps.Clear();
                ProcessRoads();
                ProcessPrefabs();
                SnapRoadEndpointsToPrefabs();
            }

            var projection = MapProjection.ReadClimateProjectionSii();
            Logger.Instance.Info($"[LaneGraphDebug] Writing {filePath} ...");
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs))
            using (var jw = new JsonTextWriter(sw))
            {
                jw.Formatting = Formatting.None;
                jw.WriteStartObject();
                jw.WritePropertyName("type"); jw.WriteValue("FeatureCollection");
                jw.WritePropertyName("features");
                jw.WriteStartArray();

                foreach (var edge in _edges)
                {
                    jw.WriteStartObject();
                    jw.WritePropertyName("type"); jw.WriteValue("Feature");
                    jw.WritePropertyName("geometry");
                    jw.WriteStartObject();
                    jw.WritePropertyName("type"); jw.WriteValue("LineString");
                    jw.WritePropertyName("coordinates");
                    jw.WriteStartArray();
                    foreach (var pt in edge.Path)
                    {
                        var ll = MapProjection.GameToLatLng(pt[0], pt[1], projection);
                        jw.WriteStartArray();
                        jw.WriteValue(ll.lon);
                        jw.WriteValue(ll.lat);
                        jw.WriteEndArray();
                    }
                    jw.WriteEndArray();
                    jw.WriteEndObject();
                    WriteProperties(jw, "edge", edge.Kind, edge.SourceUid, edge.Lane, edge.From, edge.To);
                    jw.WriteEndObject();
                }

                foreach (var node in _nodes.Values)
                {
                    var ll = MapProjection.GameToLatLng(node.X, node.Z, projection);
                    jw.WriteStartObject();
                    jw.WritePropertyName("type"); jw.WriteValue("Feature");
                    jw.WritePropertyName("geometry");
                    jw.WriteStartObject();
                    jw.WritePropertyName("type"); jw.WriteValue("Point");
                    jw.WritePropertyName("coordinates");
                    jw.WriteStartArray();
                    jw.WriteValue(ll.lon);
                    jw.WriteValue(ll.lat);
                    jw.WriteEndArray();
                    jw.WriteEndObject();
                    WriteProperties(jw, "node", node.Kind, node.SourceUid, node.Lane, node.Id, null, node.RawNodeUid, node.SnapStatus, node.SnapDetail);
                    jw.WriteEndObject();
                }

                jw.WriteEndArray();
                jw.WriteEndObject();
            }
        }

        private static void WriteProperties(
            JsonTextWriter jw,
            string featureType,
            string kind,
            string sourceUid,
            string lane,
            string idOrFrom,
            string to,
            string rawNodeUid = null,
            string snapStatus = null,
            string snapDetail = null)
        {
            jw.WritePropertyName("properties");
            jw.WriteStartObject();
            jw.WritePropertyName("featureType"); jw.WriteValue(featureType);
            jw.WritePropertyName("kind"); jw.WriteValue(kind);
            jw.WritePropertyName("sourceUid"); jw.WriteValue(sourceUid);
            jw.WritePropertyName("lane"); jw.WriteValue(lane);
            if (featureType == "node")
            {
                jw.WritePropertyName("id"); jw.WriteValue(idOrFrom);
                jw.WritePropertyName("rawNodeUid"); jw.WriteValue(rawNodeUid);
                jw.WritePropertyName("snapStatus"); jw.WriteValue(snapStatus);
                jw.WritePropertyName("snapDetail"); jw.WriteValue(snapDetail);
            }
            else
            {
                jw.WritePropertyName("from"); jw.WriteValue(idOrFrom);
                jw.WritePropertyName("to"); jw.WriteValue(to);
            }
            jw.WriteEndObject();
        }

        private void ProcessRoads()
        {
            foreach (var road in _mapper.Roads)
            {
                if (!road.Valid || road.Hidden || road.RoadLook == null) continue;

                var startNode = road.GetStartNode();
                var endNode = road.GetEndNode();
                if (startNode == null || endNode == null) continue;
                if (IsZeroNode(startNode) || IsZeroNode(endNode)) continue;

                var center = BuildRoadCenterline(startNode, endNode);
                int leftCount = road.RoadLook.LanesLeft.Count;
                int rightCount = road.RoadLook.LanesRight.Count;

                for (int lane = 0; lane < rightCount; lane++)
                {
                    float offset = RoadLaneOffset(road.RoadLook, lane, rightSide: true);
                    var path = OffsetPolyline(center, offset);
                    AddRoadLane(road, "right", lane, path, forward: true, startNode.Uid, endNode.Uid);
                }

                for (int lane = 0; lane < leftCount; lane++)
                {
                    float offset = RoadLaneOffset(road.RoadLook, lane, rightSide: false);
                    var path = OffsetPolyline(center, offset);
                    Array.Reverse(path);
                    AddRoadLane(road, "left", lane, path, forward: false, endNode.Uid, startNode.Uid);
                }
            }
        }

        private void AddRoadLane(
            TsRoadItem road,
            string side,
            int laneIndex,
            float[][] path,
            bool forward,
            ulong pathStartRawNodeUid,
            ulong pathEndRawNodeUid)
        {
            if (path == null || path.Length < 2) return;

            string lane = side + ":" + laneIndex;
            string startId = "road:" + road.Uid.ToString("X") + ":" + lane + ":start";
            string endId = "road:" + road.Uid.ToString("X") + ":" + lane + ":end";
            string from = forward ? startId : endId;
            string to = forward ? endId : startId;

            AddNode(from, path[0][0], path[0][1], "road", road.Uid, lane);
            AddNode(to, path[path.Length - 1][0], path[path.Length - 1][1], "road", road.Uid, lane);
            var edge = new LaneDebugEdge
            {
                From = from,
                To = to,
                Kind = "road",
                SourceUid = road.Uid.ToString("X"),
                Lane = lane,
                Path = path,
            };
            _edges.Add(edge);
            AddEndpoint(edge, atStart: true, pathStartRawNodeUid);
            AddEndpoint(edge, atStart: false, pathEndRawNodeUid);
        }

        private void ProcessPrefabs()
        {
            foreach (var prefab in _mapper.Prefabs)
            {
                if (!prefab.Valid || prefab.Hidden) continue;

                var desc = prefab.Prefab;
                if (desc == null || !desc.ValidRoad ||
                    desc.NavCurves == null || desc.NavCurves.Count == 0 ||
                    desc.PrefabNodes == null || desc.PrefabNodes.Count < 2)
                {
                    continue;
                }

                int descN = desc.PrefabNodes.Count;
                var curveToOutputNode = new Dictionary<int, int>();
                for (int i = 0; i < descN; i++)
                {
                    var outputs = desc.PrefabNodes[i].OutputPoints;
                    if (outputs == null) continue;
                    foreach (var ci in outputs) curveToOutputNode[ci] = i;
                }

                if (!TryGetPrefabTransform(prefab, desc, out var transform)) continue;

                for (int fromPpdIdx = 0; fromPpdIdx < descN; fromPpdIdx++)
                {
                    var inputs = desc.PrefabNodes[fromPpdIdx].InputPoints;
                    if (inputs == null || inputs.Count == 0) continue;

                    foreach (var inputLaneIdx in inputs)
                    {
                        foreach (var curvePath in GetCurvePaths(desc, curveToOutputNode, inputLaneIdx))
                        {
                            if (curvePath.CurveIndices.Count == 0) continue;
                            var path = BuildNavCurveWaypoints(curvePath.CurveIndices, desc, transform);
                            if (path == null || path.Length < 2) continue;

                            var fromRawUid = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, (byte)fromPpdIdx, descN);
                            var toRawUid = GetGlobalNodeUid(prefab.Nodes, prefab.Origin, (byte)curvePath.EndNodeIndex, descN);
                            if (fromRawUid == 0 || toRawUid == 0) continue;

                            string lane = "input:" + inputLaneIdx + ":to:" + curvePath.EndNodeIndex;
                            string startId = PrefabPortId(prefab.Uid, fromRawUid, path[0][0], path[0][1]);
                            string endId = PrefabPortId(prefab.Uid, toRawUid, path[path.Length - 1][0], path[path.Length - 1][1]);
                            AddNode(startId, path[0][0], path[0][1], "prefab", prefab.Uid, lane);
                            AddNode(endId, path[path.Length - 1][0], path[path.Length - 1][1], "prefab", prefab.Uid, lane);
                            var edge = new LaneDebugEdge
                            {
                                From = startId,
                                To = endId,
                                Kind = "prefab",
                                SourceUid = prefab.Uid.ToString("X"),
                                Lane = lane,
                                Path = path,
                            };
                            _edges.Add(edge);
                            AddEndpoint(edge, atStart: true, fromRawUid);
                            AddEndpoint(edge, atStart: false, toRawUid);
                        }
                    }
                }
            }
        }

        private void AddEndpoint(LaneDebugEdge edge, bool atStart, ulong rawNodeUid)
        {
            if (edge.Path == null || edge.Path.Length < 2) return;
            int idx = atStart ? 0 : edge.Path.Length - 1;
            int nextIdx = atStart ? 1 : edge.Path.Length - 2;
            var pt = edge.Path[idx];
            var next = edge.Path[nextIdx];
            float dirX = atStart ? next[0] - pt[0] : pt[0] - next[0];
            float dirZ = atStart ? next[1] - pt[1] : pt[1] - next[1];
            float len = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (len < 0.001f) return;
            if (_nodes.TryGetValue(atStart ? edge.From : edge.To, out var node))
            {
                node.RawNodeUid = rawNodeUid.ToString("X");
            }
            _endpoints.Add(new LaneEndpoint
            {
                Edge = edge,
                AtStart = atStart,
                Id = atStart ? edge.From : edge.To,
                X = pt[0],
                Z = pt[1],
                DirX = dirX / len,
                DirZ = dirZ / len,
                RawNodeUid = rawNodeUid,
            });
        }

        private void SnapRoadEndpointsToPrefabs()
        {
            const float MaxSnapDistance = 12f;
            const float MaxAngleDeg = 35f;
            float minDot = (float)Math.Cos(MaxAngleDeg * Math.PI / 180.0);

            var roadGroups = new Dictionary<string, List<LaneEndpoint>>();
            var prefabGroups = new Dictionary<string, List<LaneEndpoint>>();
            var endpointGroupKeys = new Dictionary<string, string>();
            foreach (var endpoint in _endpoints)
            {
                if (endpoint.Edge.Kind == "road")
                {
                    string key = SnapGroupKey(endpoint.RawNodeUid, endpoint.AtStart);
                    endpointGroupKeys[endpoint.Id] = key;
                    if (!roadGroups.TryGetValue(key, out var list))
                    {
                        list = new List<LaneEndpoint>();
                        roadGroups[key] = list;
                    }
                    list.Add(endpoint);
                }
                else if (endpoint.Edge.Kind == "prefab")
                {
                    // Road start must meet prefab end; road end must meet prefab start.
                    string key = SnapGroupKey(endpoint.RawNodeUid, !endpoint.AtStart);
                    endpointGroupKeys[endpoint.Id] = key;
                    if (!prefabGroups.TryGetValue(key, out var list))
                    {
                        list = new List<LaneEndpoint>();
                        prefabGroups[key] = list;
                    }
                    list.Add(endpoint);
                }
            }

            int snapped = 0;
            int groupsWithMismatch = 0;
            var pairedGroupKeys = new HashSet<string>();
            foreach (var kv in roadGroups)
            {
                if (!prefabGroups.TryGetValue(kv.Key, out var prefabEndpoints)) continue;
                pairedGroupKeys.Add(kv.Key);
                if (CountUniqueEndpointIds(kv.Value) != CountUniqueEndpointIds(prefabEndpoints)) groupsWithMismatch++;
                snapped += SnapEndpointGroup(kv.Value, prefabEndpoints, MaxSnapDistance, minDot);
            }

            ApplyPendingRoadSnaps();
            var unmatched = MarkUnmatchedEndpoints(endpointGroupKeys, pairedGroupKeys, roadGroups, prefabGroups, MaxSnapDistance, minDot);
            Logger.Instance.Info($"[LaneGraphDebug] Matched {snapped} road lane endpoints to prefab endpoints");
            Logger.Instance.Info($"[LaneGraphDebug] Unmatched endpoints: road={unmatched.Road}, prefab={unmatched.Prefab}, count-mismatch groups={groupsWithMismatch}");
        }

        private int SnapEndpointGroup(
            List<LaneEndpoint> roadEndpoints,
            List<LaneEndpoint> prefabEndpoints,
            float maxSnapDistance,
            float minDot)
        {
            if (roadEndpoints.Count == 0 || prefabEndpoints.Count == 0) return 0;

            float avgX = 0f, avgZ = 0f;
            foreach (var endpoint in roadEndpoints)
            {
                avgX += endpoint.DirX;
                avgZ += endpoint.DirZ;
            }
            float avgLen = (float)Math.Sqrt(avgX * avgX + avgZ * avgZ);
            if (avgLen < 0.001f) return 0;
            avgX /= avgLen;
            avgZ /= avgLen;

            float lateralX = -avgZ;
            float lateralZ = avgX;
            roadEndpoints.Sort((a, b) => Lateral(a, lateralX, lateralZ).CompareTo(Lateral(b, lateralX, lateralZ)));
            prefabEndpoints.Sort((a, b) => Lateral(a, lateralX, lateralZ).CompareTo(Lateral(b, lateralX, lateralZ)));
            roadEndpoints = UniqueEndpointsById(roadEndpoints);
            prefabEndpoints = UniqueEndpointsById(prefabEndpoints);

            int snapped = 0;
            var matches = FindBestOrderedMatches(roadEndpoints, prefabEndpoints, maxSnapDistance, minDot);
            foreach (var match in matches)
            {
                var roadEndpoint = match.Road;
                var best = match.Prefab;

                QueueRoadEndpointSnap(roadEndpoint, best.X, best.Z);
                _matchedEndpointIds.Add(roadEndpoint.Id);
                _matchedEndpointIds.Add(best.Id);
                if (_nodes.TryGetValue(roadEndpoint.Id, out var node))
                {
                    node.X = best.X;
                    node.Z = best.Z;
                    node.RawNodeUid = roadEndpoint.RawNodeUid.ToString("X");
                    node.SnapStatus = match.Distance < 2f ? "matched" : "adjusted";
                }
                if (_nodes.TryGetValue(best.Id, out var prefabNode))
                {
                    prefabNode.RawNodeUid = best.RawNodeUid.ToString("X");
                    prefabNode.SnapStatus = "matched";
                }

                _edges.Add(new LaneDebugEdge
                {
                    From = roadEndpoint.Id,
                    To = best.Id,
                    Kind = match.Distance < 2f ? "snap_good" : "snap_adjusted",
                    SourceUid = roadEndpoint.Edge.SourceUid,
                    Lane = roadEndpoint.Edge.Lane + " -> " + best.Edge.Lane + $" ({match.Distance:0.0}m, dot {match.Dot:0.00})",
                    Path = new[] { new[] { roadEndpoint.X, roadEndpoint.Z }, new[] { best.X, best.Z } },
                });
                snapped++;
            }

            return snapped;
        }

        private void QueueRoadEndpointSnap(LaneEndpoint endpoint, float targetX, float targetZ)
        {
            if (!_pendingRoadSnaps.TryGetValue(endpoint.Edge, out var targets))
            {
                targets = new EdgeSnapTargets();
                _pendingRoadSnaps[endpoint.Edge] = targets;
            }

            if (endpoint.AtStart)
            {
                targets.HasStart = true;
                targets.StartX = targetX;
                targets.StartZ = targetZ;
            }
            else
            {
                targets.HasEnd = true;
                targets.EndX = targetX;
                targets.EndZ = targetZ;
            }
        }

        private void ApplyPendingRoadSnaps()
        {
            foreach (var kv in _pendingRoadSnaps)
            {
                var edge = kv.Key;
                var targets = kv.Value;
                if (edge.Path == null || edge.Path.Length == 0) continue;

                var path = edge.Path;
                int n = path.Length;
                float startDx = 0f, startDz = 0f, endDx = 0f, endDz = 0f;
                if (targets.HasStart)
                {
                    startDx = targets.StartX - path[0][0];
                    startDz = targets.StartZ - path[0][1];
                }
                if (targets.HasEnd)
                {
                    endDx = targets.EndX - path[n - 1][0];
                    endDz = targets.EndZ - path[n - 1][1];
                }

                for (int i = 0; i < n; i++)
                {
                    float t = n == 1 ? 0f : i / (float)(n - 1);
                    float dx = startDx * (1f - t) + endDx * t;
                    float dz = startDz * (1f - t) + endDz * t;
                    path[i][0] += dx;
                    path[i][1] += dz;
                }
            }
        }

        private static List<EndpointMatch> FindBestOrderedMatches(
            List<LaneEndpoint> roadEndpoints,
            List<LaneEndpoint> prefabEndpoints,
            float maxSnapDistance,
            float minDot)
        {
            var forward = FindBestOrderedMatchesOneWay(roadEndpoints, prefabEndpoints, maxSnapDistance, minDot);
            var reversedPrefabs = new List<LaneEndpoint>(prefabEndpoints);
            reversedPrefabs.Reverse();
            var reverse = FindBestOrderedMatchesOneWay(roadEndpoints, reversedPrefabs, maxSnapDistance, minDot);

            if (reverse.Count > forward.Count) return reverse;
            if (reverse.Count < forward.Count) return forward;
            return MatchCost(reverse) < MatchCost(forward) ? reverse : forward;
        }

        private static List<EndpointMatch> FindBestOrderedMatchesOneWay(
            List<LaneEndpoint> roadEndpoints,
            List<LaneEndpoint> prefabEndpoints,
            float maxSnapDistance,
            float minDot)
        {
            int rCount = roadEndpoints.Count;
            int pCount = prefabEndpoints.Count;
            var memo = new MatchState[rCount + 1, pCount + 1];

            MatchState Solve(int r, int p)
            {
                if (r >= rCount || p >= pCount) return new MatchState { Count = 0, Cost = 0f };
                var cached = memo[r, p];
                if (cached != null) return cached;

                var best = Solve(r + 1, p);
                best = best.WithSkipRoad();

                var skipPrefab = Solve(r, p + 1).WithSkipPrefab();
                if (IsBetterMatchState(skipPrefab, best)) best = skipPrefab;

                if (TryScoreMatch(roadEndpoints[r], prefabEndpoints[p], maxSnapDistance, minDot, out var dist, out var dot, out var score))
                {
                    var next = Solve(r + 1, p + 1);
                    var take = next.WithMatch(new EndpointMatch
                    {
                        Road = roadEndpoints[r],
                        Prefab = prefabEndpoints[p],
                        Distance = dist,
                        Dot = dot,
                        Cost = score,
                    });
                    if (IsBetterMatchState(take, best)) best = take;
                }

                memo[r, p] = best;
                return best;
            }

            return Solve(0, 0).Matches;
        }

        private static bool TryScoreMatch(
            LaneEndpoint roadEndpoint,
            LaneEndpoint prefabEndpoint,
            float maxSnapDistance,
            float minDot,
            out float dist,
            out float dot,
            out float score)
        {
            float dx = prefabEndpoint.X - roadEndpoint.X;
            float dz = prefabEndpoint.Z - roadEndpoint.Z;
            dist = (float)Math.Sqrt(dx * dx + dz * dz);
            dot = roadEndpoint.DirX * prefabEndpoint.DirX + roadEndpoint.DirZ * prefabEndpoint.DirZ;
            score = dist + (1f - dot) * 8f;
            return dist <= maxSnapDistance && dot >= minDot;
        }

        private static bool IsBetterMatchState(MatchState candidate, MatchState current)
        {
            if (candidate.Count != current.Count) return candidate.Count > current.Count;
            return candidate.Cost < current.Cost;
        }

        private static float MatchCost(List<EndpointMatch> matches)
        {
            float cost = 0f;
            foreach (var match in matches) cost += match.Cost;
            return cost;
        }

        private static List<LaneEndpoint> UniqueEndpointsById(List<LaneEndpoint> endpoints)
        {
            var result = new List<LaneEndpoint>();
            var seen = new HashSet<string>();
            foreach (var endpoint in endpoints)
            {
                if (!seen.Add(endpoint.Id)) continue;
                result.Add(endpoint);
            }
            return result;
        }

        private static int CountUniqueEndpointIds(List<LaneEndpoint> endpoints)
        {
            var seen = new HashSet<string>();
            foreach (var endpoint in endpoints) seen.Add(endpoint.Id);
            return seen.Count;
        }

        private (int Road, int Prefab) MarkUnmatchedEndpoints(
            Dictionary<string, string> endpointGroupKeys,
            HashSet<string> pairedGroupKeys,
            Dictionary<string, List<LaneEndpoint>> roadGroups,
            Dictionary<string, List<LaneEndpoint>> prefabGroups,
            float maxSnapDistance,
            float minDot)
        {
            int road = 0;
            int prefab = 0;
            foreach (var endpoint in _endpoints)
            {
                if (_matchedEndpointIds.Contains(endpoint.Id)) continue;
                if (!endpointGroupKeys.TryGetValue(endpoint.Id, out var groupKey)) continue;
                if (!pairedGroupKeys.Contains(groupKey)) continue;
                if (!_nodes.TryGetValue(endpoint.Id, out var node)) continue;

                node.RawNodeUid = endpoint.RawNodeUid.ToString("X");
                node.SnapStatus = "unmatched";
                node.SnapDetail = DescribeUnmatchedEndpoint(endpoint, groupKey, roadGroups, prefabGroups, maxSnapDistance, minDot);
                if (endpoint.Edge.Kind == "road")
                {
                    node.Kind = "road_unmatched";
                    road++;
                }
                else if (endpoint.Edge.Kind == "prefab")
                {
                    node.Kind = "prefab_extra";
                    prefab++;
                }
            }
            return (road, prefab);
        }

        private static string DescribeUnmatchedEndpoint(
            LaneEndpoint endpoint,
            string groupKey,
            Dictionary<string, List<LaneEndpoint>> roadGroups,
            Dictionary<string, List<LaneEndpoint>> prefabGroups,
            float maxSnapDistance,
            float minDot)
        {
            var sameGroup = endpoint.Edge.Kind == "road" ? roadGroups : prefabGroups;
            var oppositeGroup = endpoint.Edge.Kind == "road" ? prefabGroups : roadGroups;
            int sameCount = sameGroup.TryGetValue(groupKey, out var sameEndpoints) ? CountUniqueEndpointIds(sameEndpoints) : 0;
            int oppositeCount = oppositeGroup.TryGetValue(groupKey, out var oppositeEndpoints) ? CountUniqueEndpointIds(oppositeEndpoints) : 0;

            if (oppositeEndpoints == null || oppositeEndpoints.Count == 0)
                return $"no opposite endpoints; same={sameCount}, opposite=0";

            LaneEndpoint nearest = null;
            float nearestDist = float.MaxValue;
            float nearestDot = -1f;
            foreach (var candidate in UniqueEndpointsById(oppositeEndpoints))
            {
                float dx = candidate.X - endpoint.X;
                float dz = candidate.Z - endpoint.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                float dot = endpoint.DirX * candidate.DirX + endpoint.DirZ * candidate.DirZ;
                if (dist < nearestDist)
                {
                    nearest = candidate;
                    nearestDist = dist;
                    nearestDot = dot;
                }
            }

            string reason = "order/count mismatch";
            if (nearestDist > maxSnapDistance) reason = "too far";
            else if (nearestDot < minDot) reason = "bad angle";
            else if (sameCount != oppositeCount) reason = "count mismatch";

            string nearestId = nearest == null ? "" : nearest.Id;
            return $"{reason}; same={sameCount}, opposite={oppositeCount}, nearest={nearestDist:0.0}m, dot={nearestDot:0.00}, candidate={nearestId}";
        }

        private static string PrefabPortId(ulong prefabUid, ulong rawNodeUid, float x, float z)
        {
            return "prefab:" + prefabUid.ToString("X") +
                ":port:" + rawNodeUid.ToString("X") +
                ":" + QuantizePortCoord(x) +
                ":" + QuantizePortCoord(z);
        }

        private static int QuantizePortCoord(float value)
        {
            return (int)Math.Round(value * 10f);
        }

        private static float Lateral(LaneEndpoint endpoint, float lateralX, float lateralZ)
        {
            return endpoint.X * lateralX + endpoint.Z * lateralZ;
        }

        private static string SnapGroupKey(ulong rawNodeUid, bool roadAtStart)
        {
            return rawNodeUid.ToString("X") + ":" + (roadAtStart ? "roadStart" : "roadEnd");
        }

        private static IEnumerable<LaneEndpoint> NearbyPrefabEndpoints(
            Dictionary<string, List<LaneEndpoint>> grid,
            float x,
            float z,
            float cellSize)
        {
            int cx = (int)Math.Floor(x / cellSize);
            int cz = (int)Math.Floor(z / cellSize);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    string key = (cx + dx) + ":" + (cz + dz);
                    if (!grid.TryGetValue(key, out var list)) continue;
                    foreach (var endpoint in list) yield return endpoint;
                }
            }
        }

        private static string GridKey(float x, float z, float cellSize)
        {
            int cx = (int)Math.Floor(x / cellSize);
            int cz = (int)Math.Floor(z / cellSize);
            return cx + ":" + cz;
        }

        private static ulong GetGlobalNodeUid(
            List<ulong> nodes,
            int origin,
            byte descriptorNodeIndex,
            int descriptorNodeCount)
        {
            int idx = (descriptorNodeIndex - origin + descriptorNodeCount) % descriptorNodeCount;
            return idx < nodes.Count ? nodes[idx] : 0UL;
        }

        private void AddNode(string id, float x, float z, string kind, ulong sourceUid, string lane)
        {
            if (_nodes.ContainsKey(id)) return;
            _nodes[id] = new LaneDebugNode
            {
                Id = id,
                X = x,
                Z = z,
                Kind = kind,
                SourceUid = sourceUid.ToString("X"),
                Lane = lane,
                SnapStatus = "pending",
            };
        }

        private static float[][] BuildRoadCenterline(TsNode startNode, TsNode endNode)
        {
            const int N = 16;
            float sx = startNode.X, sz = startNode.Z;
            float ex = endNode.X, ez = endNode.Z;
            double r = Math.Sqrt((sx - ex) * (double)(sx - ex) + (sz - ez) * (double)(sz - ez));
            double tanSx = Math.Cos(-(Math.PI * 0.5 - startNode.Rotation)) * r;
            double tanSz = Math.Sin(-(Math.PI * 0.5 - startNode.Rotation)) * r;
            double tanEx = Math.Cos(-(Math.PI * 0.5 - endNode.Rotation)) * r;
            double tanEz = Math.Sin(-(Math.PI * 0.5 - endNode.Rotation)) * r;

            var pts = new float[N][];
            for (int i = 0; i < N; i++)
            {
                float s = i / (float)(N - 1);
                float wx = (float)TsRoadLook.Hermite(s, sx, ex, tanSx, tanEx);
                float wz = (float)TsRoadLook.Hermite(s, sz, ez, tanSz, tanEz);
                pts[i] = new[] { wx, wz };
            }
            return pts;
        }

        private static float RoadLaneOffset(TsRoadLook look, int laneIndex, bool rightSide)
        {
            int sameSideCount = rightSide ? look.LanesRight.Count : look.LanesLeft.Count;
            int otherSideCount = rightSide ? look.LanesLeft.Count : look.LanesRight.Count;

            if (sameSideCount > 0 && otherSideCount == 0)
                return (laneIndex - (sameSideCount - 1) / 2f) * Consts.LaneWidth;

            float offset = look.Offset / 2f + (laneIndex + 0.5f) * Consts.LaneWidth;
            return rightSide ? offset : -offset;
        }

        private static float[][] OffsetPolyline(float[][] center, float offset)
        {
            if (Math.Abs(offset) < 0.001f) return CopyPath(center);

            var result = new float[center.Length][];
            for (int i = 0; i < center.Length; i++)
            {
                int prevIdx = i == 0 ? 0 : i - 1;
                int nextIdx = i == center.Length - 1 ? center.Length - 1 : i + 1;
                float tx = center[nextIdx][0] - center[prevIdx][0];
                float tz = center[nextIdx][1] - center[prevIdx][1];
                float len = (float)Math.Sqrt(tx * tx + tz * tz);
                if (len < 0.001f)
                {
                    result[i] = new[] { center[i][0], center[i][1] };
                    continue;
                }

                float nx = -tz / len;
                float nz = tx / len;
                result[i] = new[] { center[i][0] + nx * offset, center[i][1] + nz * offset };
            }
            return result;
        }

        private static float[][] CopyPath(float[][] path)
        {
            var copy = new float[path.Length][];
            for (int i = 0; i < path.Length; i++) copy[i] = new[] { path[i][0], path[i][1] };
            return copy;
        }

        private bool TryGetPrefabTransform(TsPrefabItem prefab, TsPrefab desc, out PrefabTransform transform)
        {
            transform = null;
            var originWorldNode = _mapper.GetNodeByUid(prefab.Nodes[0]);
            if (originWorldNode == null || prefab.Origin >= desc.PrefabNodes.Count) return false;

            var originPpdNode = desc.PrefabNodes[prefab.Origin];
            float rot = (float)(originWorldNode.Rotation - Math.PI
                - Math.Atan2(originPpdNode.RotZ, originPpdNode.RotX)
                + Math.PI / 2);
            transform = new PrefabTransform
            {
                RotSin = (float)Math.Sin(rot),
                RotCos = (float)Math.Cos(rot),
                OriginPpdX = originPpdNode.X,
                OriginPpdZ = originPpdNode.Z,
                OriginWorldX = originWorldNode.X,
                OriginWorldZ = originWorldNode.Z,
            };
            return true;
        }

        private static float[][] BuildNavCurveWaypoints(List<int> curvePath, TsPrefab desc, PrefabTransform transform)
        {
            var points = new List<float[]>(curvePath.Count * 8);
            for (int pathIdx = 0; pathIdx < curvePath.Count; pathIdx++)
            {
                var curve = desc.NavCurves[curvePath[pathIdx]];
                var sampled = SampleNavCurve(curve, transform);
                int start = pathIdx == 0 ? 0 : 1;
                for (int i = start; i < sampled.Length; i++)
                    AddPointIfDistinct(points, sampled[i]);
            }
            if (points.Count <= 2) return points.ToArray();
            return DouglasPeucker(points.ToArray(), 0.1f);
        }

        private static List<CurvePath> GetCurvePaths(
            TsPrefab desc,
            Dictionary<int, int> endingCurveIndexToNodeIndex,
            int inputLaneIndex)
        {
            var seenIndices = new HashSet<int>();

            CurvePath Prefix(CurvePath path, int curveIndex)
            {
                var indices = new List<int>(path.CurveIndices);
                indices.Insert(0, curveIndex);
                return new CurvePath { EndNodeIndex = path.EndNodeIndex, CurveIndices = indices };
            }

            List<CurvePath> GetPaths(int curveIndex)
            {
                var paths = new List<CurvePath>();
                if (seenIndices.Contains(curveIndex)) return paths;
                seenIndices.Add(curveIndex);

                if (endingCurveIndexToNodeIndex.TryGetValue(curveIndex, out var nodeIndex))
                {
                    paths.Add(new CurvePath { EndNodeIndex = nodeIndex, CurveIndices = new List<int>() });
                    return paths;
                }

                if (curveIndex < 0 || curveIndex >= desc.NavCurves.Count) return paths;
                var curve = desc.NavCurves[curveIndex];
                if (curve.NextLines == null) return paths;
                foreach (var nextCurveIndex in curve.NextLines)
                    foreach (var path in GetPaths(nextCurveIndex))
                        paths.Add(Prefix(path, nextCurveIndex));
                return paths;
            }

            var result = GetPaths(inputLaneIndex);
            for (int i = 0; i < result.Count; i++)
                result[i] = Prefix(result[i], inputLaneIndex);
            return result;
        }

        private static float[][] SampleNavCurve(TsNavCurve curve, PrefabTransform transform)
        {
            float dx = curve.EndX - curve.StartX;
            float dz = curve.EndZ - curve.StartZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist < 0.001f)
            {
                return new[]
                {
                    PpdToWorld(curve.StartX, curve.StartZ, transform),
                    PpdToWorld(curve.EndX, curve.EndZ, transform),
                };
            }

            double startRot = PrefabCurveRotation(curve.StartRotW, curve.StartRotY);
            double endRot = PrefabCurveRotation(curve.EndRotW, curve.EndRotY);
            double delta = NormalizeRadians(startRot - endRot);
            double stepEstimate = Math.Floor(Math.Abs(Math.Tan(delta)) * 20.0) + 1.0;
            if (double.IsNaN(stepEstimate) || double.IsInfinity(stepEstimate)) stepEstimate = 8.0;
            int steps = (int)Math.Min(8.0, stepEstimate);
            if (steps < 1) steps = 1;

            float startTanX = (float)Math.Cos(startRot) * dist;
            float startTanZ = (float)Math.Sin(startRot) * dist;
            float endTanX = (float)Math.Cos(endRot) * dist;
            float endTanZ = (float)Math.Sin(endRot) * dist;

            var result = new float[steps + 1][];
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float h00 = 2f * t * t * t - 3f * t * t + 1f;
                float h10 = t * t * t - 2f * t * t + t;
                float h01 = -2f * t * t * t + 3f * t * t;
                float h11 = t * t * t - t * t;
                float x = h00 * curve.StartX + h10 * startTanX + h01 * curve.EndX + h11 * endTanX;
                float z = h00 * curve.StartZ + h10 * startTanZ + h01 * curve.EndZ + h11 * endTanZ;
                result[i] = PpdToWorld(x, z, transform);
            }
            return result;
        }

        private static float[] PpdToWorld(float px, float pz, PrefabTransform transform)
        {
            float dx = px - transform.OriginPpdX;
            float dz = pz - transform.OriginPpdZ;
            return new[]
            {
                dx * transform.RotCos - dz * transform.RotSin + transform.OriginWorldX,
                dx * transform.RotSin + dz * transform.RotCos + transform.OriginWorldZ,
            };
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

        private static void AddPointIfDistinct(List<float[]> points, float[] point)
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

        private static float[][] DouglasPeucker(float[][] pts, float epsilon)
        {
            if (pts.Length <= 2) return pts;
            float ax = pts[0][0], az = pts[0][1];
            float bx = pts[pts.Length - 1][0], bz = pts[pts.Length - 1][1];
            float abLen = (float)Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(bz - az) * (bz - az));
            float maxDist = 0f;
            int maxIdx = 0;
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
                if (dist > maxDist)
                {
                    maxDist = dist;
                    maxIdx = i;
                }
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

        private static bool IsZeroNode(TsNode node)
        {
            return Math.Abs(node.X) < 0.001f && Math.Abs(node.Z) < 0.001f;
        }

        private class LaneDebugNode
        {
            public string Id;
            public float X;
            public float Z;
            public string Kind;
            public string SourceUid;
            public string Lane;
            public string RawNodeUid;
            public string SnapStatus;
            public string SnapDetail;
        }

        private class LaneDebugEdge
        {
            public string From;
            public string To;
            public string Kind;
            public string SourceUid;
            public string Lane;
            public float[][] Path;
        }

        private class LaneEndpoint
        {
            public LaneDebugEdge Edge;
            public bool AtStart;
            public string Id;
            public float X;
            public float Z;
            public float DirX;
            public float DirZ;
            public ulong RawNodeUid;
        }

        private class EndpointMatch
        {
            public LaneEndpoint Road;
            public LaneEndpoint Prefab;
            public float Distance;
            public float Dot;
            public float Cost;
        }

        private class MatchState
        {
            public int Count;
            public float Cost;
            public List<EndpointMatch> Matches = new List<EndpointMatch>();

            public MatchState WithMatch(EndpointMatch match)
            {
                var matches = new List<EndpointMatch>(Matches.Count + 1);
                matches.Add(match);
                matches.AddRange(Matches);
                return new MatchState
                {
                    Count = Count + 1,
                    Cost = Cost + match.Cost,
                    Matches = matches,
                };
            }

            public MatchState WithSkipRoad()
            {
                return CloneWithPenalty(1000f);
            }

            public MatchState WithSkipPrefab()
            {
                return CloneWithPenalty(1000f);
            }

            private MatchState CloneWithPenalty(float penalty)
            {
                return new MatchState
                {
                    Count = Count,
                    Cost = Cost + penalty,
                    Matches = Matches,
                };
            }
        }

        private class EdgeSnapTargets
        {
            public bool HasStart;
            public float StartX;
            public float StartZ;
            public bool HasEnd;
            public float EndX;
            public float EndZ;
        }

        private class CurvePath
        {
            public int EndNodeIndex;
            public List<int> CurveIndices;
        }

        private class PrefabTransform
        {
            public float RotSin;
            public float RotCos;
            public float OriginPpdX;
            public float OriginPpdZ;
            public float OriginWorldX;
            public float OriginWorldZ;
        }
    }
}
