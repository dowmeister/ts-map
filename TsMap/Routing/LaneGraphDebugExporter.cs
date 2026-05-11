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

        public LaneGraphDebugExporter(TsMapper mapper)
        {
            _mapper = mapper;
        }

        public void Export(string filePath)
        {
            _nodes.Clear();
            _edges.Clear();

            ProcessRoads();
            ProcessPrefabs();

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
                ProcessRoads();
                ProcessPrefabs();
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
                    WriteProperties(jw, "node", node.Kind, node.SourceUid, node.Lane, node.Id, null);
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
            string to)
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
                    AddRoadLane(road, "right", lane, path, forward: true);
                }

                for (int lane = 0; lane < leftCount; lane++)
                {
                    float offset = RoadLaneOffset(road.RoadLook, lane, rightSide: false);
                    var path = OffsetPolyline(center, offset);
                    Array.Reverse(path);
                    AddRoadLane(road, "left", lane, path, forward: false);
                }
            }
        }

        private void AddRoadLane(TsRoadItem road, string side, int laneIndex, float[][] path, bool forward)
        {
            if (path == null || path.Length < 2) return;

            string lane = side + ":" + laneIndex;
            string startId = "road:" + road.Uid.ToString("X") + ":" + lane + ":start";
            string endId = "road:" + road.Uid.ToString("X") + ":" + lane + ":end";
            string from = forward ? startId : endId;
            string to = forward ? endId : startId;

            AddNode(from, path[0][0], path[0][1], "road", road.Uid, lane);
            AddNode(to, path[path.Length - 1][0], path[path.Length - 1][1], "road", road.Uid, lane);
            _edges.Add(new LaneDebugEdge
            {
                From = from,
                To = to,
                Kind = "road",
                SourceUid = road.Uid.ToString("X"),
                Lane = lane,
                Path = path,
            });
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

                            string lane = "input:" + inputLaneIdx + ":to:" + curvePath.EndNodeIndex;
                            string startId = "prefab:" + prefab.Uid.ToString("X") + ":" + lane + ":start";
                            string endId = "prefab:" + prefab.Uid.ToString("X") + ":" + lane + ":end";
                            AddNode(startId, path[0][0], path[0][1], "prefab", prefab.Uid, lane);
                            AddNode(endId, path[path.Length - 1][0], path[path.Length - 1][1], "prefab", prefab.Uid, lane);
                            _edges.Add(new LaneDebugEdge
                            {
                                From = startId,
                                To = endId,
                                Kind = "prefab",
                                SourceUid = prefab.Uid.ToString("X"),
                                Lane = lane,
                                Path = path,
                            });
                        }
                    }
                }
            }
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
            return rightSide ? -offset : offset;
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
