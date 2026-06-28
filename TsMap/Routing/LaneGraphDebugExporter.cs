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
        private const string ExportMode = "prefabs-and-raw-roads";
        private static readonly bool IncludeRoads = true;
        private static readonly bool IncludeRoadToPrefabLinks = true;
        private static readonly bool IncludePrefabToPrefabLinks = true;
        private static readonly bool ApplyRoadSnapGeometry = false;
        private static readonly bool ApplyPrefabSnapGeometry = true;
        private static readonly float[][] TemporaryLeftHandTrafficPolygon =
        {
            new[] { -62030.5f, -6615.0f },
            new[] { -48707.7f, -4467.4f },
            new[] { -38804.2f, -4366.6f },
            new[] { -31103.3f, -7262.3f },
            new[] { -29374.2f, -13932.0f },
            new[] { -34086.6f, -25735.9f },
            new[] { -34958.8f, -30229.1f },
            new[] { -35291.0f, -39108.4f },
            new[] { -35586.4f, -50218.0f },
            new[] { -36149.6f, -55379.8f },
            new[] { -39478.5f, -57071.3f },
            new[] { -42587.4f, -57166.5f },
            new[] { -46972.9f, -55802.4f },
            new[] { -49538.2f, -53900.1f },
            new[] { -51035.4f, -51636.3f },
            new[] { -51913.2f, -48703.0f },
            new[] { -51140.2f, -44062.3f },
            new[] { -50612.0f, -40392.3f },
            new[] { -49390.4f, -37653.3f },
            new[] { -48675.5f, -34667.1f },
            new[] { -48892.8f, -32278.8f },
            new[] { -50510.7f, -29311.9f },
            new[] { -51833.3f, -26622.8f },
            new[] { -53665.7f, -24733.7f },
            new[] { -56046.1f, -22704.7f },
            new[] { -57413.0f, -20112.6f },
            new[] { -59098.8f, -17948.7f },
            new[] { -59607.7f, -16090.4f },
            new[] { -60268.0f, -13678.7f },
            new[] { -61046.6f, -10569.2f },
            new[] { -61630.4f, -9232.7f },
            new[] { -62109.9f, -6685.0f },
        };

        private readonly TsMapper _mapper;
        private readonly Dictionary<string, LaneDebugNode> _nodes = new Dictionary<string, LaneDebugNode>();
        private readonly List<LaneDebugEdge> _edges = new List<LaneDebugEdge>();
        private readonly List<LaneEndpoint> _endpoints = new List<LaneEndpoint>();
        private readonly HashSet<string> _matchedEndpointIds = new HashSet<string>();
        private readonly Dictionary<LaneDebugEdge, EdgeSnapTargets> _pendingRoadSnaps = new Dictionary<LaneDebugEdge, EdgeSnapTargets>();
        private readonly Dictionary<LaneDebugEdge, EdgeSnapTargets> _pendingPrefabSnaps = new Dictionary<LaneDebugEdge, EdgeSnapTargets>();
        private readonly Dictionary<string, List<LaneEndpoint>> _prefabEndpointsByStablePortKey = new Dictionary<string, List<LaneEndpoint>>();
        public LaneGraphDebugExporter(TsMapper mapper)
        {
            _mapper = mapper;
        }

        public LaneGraphSnapshot BuildSnapshot()
        {
            BuildDebugGraph();
            var snapshot = new LaneGraphSnapshot();
            foreach (var node in _nodes.Values)
            {
                snapshot.Nodes.Add(new SnapshotNode
                {
                    Id = node.Id,
                    X = node.X,
                    Z = node.Z,
                    Kind = node.Kind,
                    SourceUid = node.SourceUid,
                    Lane = node.Lane,
                    RawNodeUid = node.RawNodeUid,
                    SnapStatus = node.SnapStatus,
                    SnapDetail = node.SnapDetail,
                    InDegree = node.InDegree,
                    OutDegree = node.OutDegree,
                });
            }

            foreach (var edge in _edges)
            {
                snapshot.Edges.Add(new SnapshotEdge
                {
                    From = edge.From,
                    To = edge.To,
                    Kind = edge.Kind,
                    SourceUid = edge.SourceUid,
                    Lane = edge.Lane,
                    SpeedClass = edge.SpeedClass,
                    IsSecret = edge.IsSecret,
                    Path = CopyPath(edge.Path),
                });
            }
            return snapshot;
        }

        private void BuildDebugGraph()
        {
            _nodes.Clear();
            _edges.Clear();
            _endpoints.Clear();
            _matchedEndpointIds.Clear();
            _pendingRoadSnaps.Clear();
            _pendingPrefabSnaps.Clear();
            _prefabEndpointsByStablePortKey.Clear();
            if (IncludeRoads) ProcessRoads();
            ProcessPrefabs();
            if (IncludeRoadToPrefabLinks) SnapRoadEndpointsToPrefabs();
            SnapRoadEndpointsToRoads();
            if (IncludePrefabToPrefabLinks) SnapPrefabEndpointsToPrefabs();
            ComputeNodeDegrees();
        }

        public void Export(string filePath)
        {
            BuildDebugGraph();

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
                jw.WritePropertyName("mode"); jw.WriteValue(ExportMode);
                jw.WritePropertyName("generatedAt"); jw.WriteValue(DateTime.UtcNow.ToString("o"));
                jw.WriteEndObject();

                jw.WritePropertyName("nodes");
                jw.WriteStartArray();
                var sortedNodes = new List<LaneDebugNode>(_nodes.Values);
                sortedNodes.Sort((a, b) => SpatialSortKey(a.X, a.Z).CompareTo(SpatialSortKey(b.X, b.Z)));
                foreach (var node in sortedNodes)
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
                    jw.WritePropertyName("inDegree"); jw.WriteValue(node.InDegree);
                    jw.WritePropertyName("outDegree"); jw.WriteValue(node.OutDegree);
                    jw.WriteEndObject();
                }
                jw.WriteEndArray();

                jw.WritePropertyName("edges");
                jw.WriteStartArray();
                var sortedEdges = new List<LaneDebugEdge>(_edges);
                sortedEdges.Sort((a, b) => SpatialSortKey(a).CompareTo(SpatialSortKey(b)));
                foreach (var edge in sortedEdges)
                {
                    jw.WriteStartObject();
                    jw.WritePropertyName("from"); jw.WriteValue(edge.From);
                    jw.WritePropertyName("to"); jw.WriteValue(edge.To);
                    jw.WritePropertyName("kind"); jw.WriteValue(edge.Kind);
                    jw.WritePropertyName("sourceUid"); jw.WriteValue(edge.SourceUid);
                    jw.WritePropertyName("lane"); jw.WriteValue(edge.Lane);
                    jw.WritePropertyName("speedClass"); jw.WriteValue(edge.SpeedClass);
                    jw.WritePropertyName("isSecret"); jw.WriteValue(edge.IsSecret);
                    jw.WritePropertyName("direction"); jw.WriteValue(edge.Direction);
                    jw.WritePropertyName("trafficSide"); jw.WriteValue(edge.TrafficSide);
                    jw.WritePropertyName("isTemporaryLeftHandTrafficRoad"); jw.WriteValue(edge.IsTemporaryLeftHandTrafficRoad);
                    jw.WritePropertyName("midX"); jw.WriteValue(edge.MidX);
                    jw.WritePropertyName("midZ"); jw.WriteValue(edge.MidZ);
                    jw.WritePropertyName("pathStartRawNodeUid"); jw.WriteValue(edge.PathStartRawNodeUid);
                    jw.WritePropertyName("pathEndRawNodeUid"); jw.WriteValue(edge.PathEndRawNodeUid);
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

        public void ExportSplit(string directoryPath, string baseName)
        {
            const int NodeChunkSize = 100000;
            const int EdgeChunkSize = 50000;

            _nodes.Clear();
            _edges.Clear();
            _endpoints.Clear();
            _matchedEndpointIds.Clear();
            _pendingRoadSnaps.Clear();
            _pendingPrefabSnaps.Clear();
            _prefabEndpointsByStablePortKey.Clear();

            if (IncludeRoads) ProcessRoads();
            ProcessPrefabs();
            if (IncludeRoadToPrefabLinks) SnapRoadEndpointsToPrefabs();
            SnapRoadEndpointsToRoads();
            if (IncludePrefabToPrefabLinks) SnapPrefabEndpointsToPrefabs();
            ComputeNodeDegrees();

            Directory.CreateDirectory(directoryPath);
            DeleteExistingSplitFiles(directoryPath, baseName);

            Logger.Instance.Info($"[LaneGraphDebug] Writing split debug graph {Path.Combine(directoryPath, baseName)}.* ...");

            var nodeFiles = new List<string>();
            var nodeChunks = new List<ChunkInfo>();
            ChunkInfo currentNodeChunk = null;
            int nodeIndex = 0;
            int nodesInChunk = 0;
            JsonTextWriter nodeWriter = null;
            StreamWriter nodeStreamWriter = null;
            FileStream nodeFileStream = null;
            try
            {
                var sortedNodes = new List<KeyValuePair<long, LaneDebugNode>>();
                foreach (var node in _nodes.Values)
                    sortedNodes.Add(new KeyValuePair<long, LaneDebugNode>(SpatialSortKey(node.X, node.Z), node));
                sortedNodes.Sort((a, b) => a.Key.CompareTo(b.Key));

                foreach (var pair in sortedNodes)
                {
                    var node = pair.Value;
                    if (nodeWriter == null || nodesInChunk >= NodeChunkSize)
                    {
                        CloseArrayWriter(nodeWriter, nodeStreamWriter, nodeFileStream);
                        string fileName = $"{baseName}.nodes.{nodeIndex:000}.json";
                        nodeFiles.Add(fileName);
                        currentNodeChunk = new ChunkInfo { File = fileName };
                        nodeChunks.Add(currentNodeChunk);
                        nodeFileStream = new FileStream(Path.Combine(directoryPath, fileName), FileMode.Create, FileAccess.Write);
                        nodeStreamWriter = new StreamWriter(nodeFileStream);
                        nodeWriter = new JsonTextWriter(nodeStreamWriter) { Formatting = Formatting.None };
                        nodeWriter.WriteStartArray();
                        nodeIndex++;
                        nodesInChunk = 0;
                    }
                    WriteNodeObject(nodeWriter, node);
                    currentNodeChunk?.Include(node.X, node.Z);
                    nodesInChunk++;
                }
            }
            finally
            {
                CloseArrayWriter(nodeWriter, nodeStreamWriter, nodeFileStream);
            }

            var edgeFiles = new List<string>();
            var edgeChunks = new List<ChunkInfo>();
            ChunkInfo currentEdgeChunk = null;
            int edgeIndex = 0;
            int edgesInChunk = 0;
            JsonTextWriter edgeWriter = null;
            StreamWriter edgeStreamWriter = null;
            FileStream edgeFileStream = null;
            try
            {
                var sortedEdges = new List<KeyValuePair<long, LaneDebugEdge>>();
                foreach (var edge in _edges)
                    sortedEdges.Add(new KeyValuePair<long, LaneDebugEdge>(SpatialSortKey(edge), edge));
                sortedEdges.Sort((a, b) => a.Key.CompareTo(b.Key));

                foreach (var pair in sortedEdges)
                {
                    var edge = pair.Value;
                    if (edgeWriter == null || edgesInChunk >= EdgeChunkSize)
                    {
                        CloseArrayWriter(edgeWriter, edgeStreamWriter, edgeFileStream);
                        string fileName = $"{baseName}.edges.{edgeIndex:000}.json";
                        edgeFiles.Add(fileName);
                        currentEdgeChunk = new ChunkInfo { File = fileName };
                        edgeChunks.Add(currentEdgeChunk);
                        edgeFileStream = new FileStream(Path.Combine(directoryPath, fileName), FileMode.Create, FileAccess.Write);
                        edgeStreamWriter = new StreamWriter(edgeFileStream);
                        edgeWriter = new JsonTextWriter(edgeStreamWriter) { Formatting = Formatting.None };
                        edgeWriter.WriteStartArray();
                        edgeIndex++;
                        edgesInChunk = 0;
                    }
                    WriteEdgeObject(edgeWriter, edge);
                    currentEdgeChunk?.Include(edge.Path);
                    edgesInChunk++;
                }
            }
            finally
            {
                CloseArrayWriter(edgeWriter, edgeStreamWriter, edgeFileStream);
            }

            using (var fs = new FileStream(Path.Combine(directoryPath, baseName + ".json"), FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs))
            using (var jw = new JsonTextWriter(sw))
            {
                jw.Formatting = Formatting.Indented;
                jw.WriteStartObject();

                jw.WritePropertyName("meta");
                jw.WriteStartObject();
                jw.WritePropertyName("nodeCount"); jw.WriteValue(_nodes.Count);
                jw.WritePropertyName("edgeCount"); jw.WriteValue(_edges.Count);
                jw.WritePropertyName("mode"); jw.WriteValue(ExportMode);
                jw.WritePropertyName("split"); jw.WriteValue(true);
                jw.WritePropertyName("generatedAt"); jw.WriteValue(DateTime.UtcNow.ToString("o"));
                jw.WriteEndObject();

                jw.WritePropertyName("nodes");
                jw.WriteStartArray();
                foreach (var fileName in nodeFiles) jw.WriteValue(fileName);
                jw.WriteEndArray();

                jw.WritePropertyName("nodeChunks");
                WriteChunkInfoArray(jw, nodeChunks);

                jw.WritePropertyName("edges");
                jw.WriteStartArray();
                foreach (var fileName in edgeFiles) jw.WriteValue(fileName);
                jw.WriteEndArray();

                jw.WritePropertyName("edgeChunks");
                WriteChunkInfoArray(jw, edgeChunks);

                jw.WriteEndObject();
            }

            Logger.Instance.Info($"[LaneGraphDebug] Split export complete: {_nodes.Count} nodes in {nodeFiles.Count} files, {_edges.Count} edges in {edgeFiles.Count} files");
        }

        private static void WriteChunkInfoArray(JsonTextWriter jw, List<ChunkInfo> chunks)
        {
            jw.WriteStartArray();
            foreach (var chunk in chunks)
            {
                jw.WriteStartObject();
                jw.WritePropertyName("file"); jw.WriteValue(chunk.File);
                jw.WritePropertyName("minX"); jw.WriteValue(Math.Round(chunk.MinX, 4));
                jw.WritePropertyName("maxX"); jw.WriteValue(Math.Round(chunk.MaxX, 4));
                jw.WritePropertyName("minZ"); jw.WriteValue(Math.Round(chunk.MinZ, 4));
                jw.WritePropertyName("maxZ"); jw.WriteValue(Math.Round(chunk.MaxZ, 4));
                jw.WriteEndObject();
            }
            jw.WriteEndArray();
        }

        private static long SpatialSortKey(LaneDebugEdge edge)
        {
            if (edge.Path == null || edge.Path.Length == 0) return 0;
            double x = 0;
            double z = 0;
            int count = 0;
            foreach (var pt in edge.Path)
            {
                if (pt == null || pt.Length < 2) continue;
                x += pt[0];
                z += pt[1];
                count++;
            }
            if (count == 0) return 0;
            return SpatialSortKey((float)(x / count), (float)(z / count));
        }

        private static long SpatialSortKey(float x, float z)
        {
            const float CellSize = 10000f;
            const int Bias = 100000;
            long cellX = (long)Math.Floor(x / CellSize) + Bias;
            long cellZ = (long)Math.Floor(z / CellSize) + Bias;
            return (cellX << 32) ^ (cellZ & 0xffffffffL);
        }

        private static void DeleteExistingSplitFiles(string directoryPath, string baseName)
        {
            foreach (var file in Directory.GetFiles(directoryPath, baseName + ".nodes.*.json"))
                File.Delete(file);
            foreach (var file in Directory.GetFiles(directoryPath, baseName + ".edges.*.json"))
                File.Delete(file);
        }

        private static void CloseArrayWriter(JsonTextWriter writer, StreamWriter streamWriter, FileStream fileStream)
        {
            if (writer != null)
            {
                writer.WriteEndArray();
                writer.Flush();
                writer.Close();
                return;
            }

            streamWriter?.Close();
            fileStream?.Close();
        }

        private static void WriteNodeObject(JsonTextWriter jw, LaneDebugNode node)
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
            jw.WritePropertyName("inDegree"); jw.WriteValue(node.InDegree);
            jw.WritePropertyName("outDegree"); jw.WriteValue(node.OutDegree);
            jw.WriteEndObject();
        }

        private static void WriteEdgeObject(JsonTextWriter jw, LaneDebugEdge edge)
        {
            jw.WriteStartObject();
            jw.WritePropertyName("from"); jw.WriteValue(edge.From);
            jw.WritePropertyName("to"); jw.WriteValue(edge.To);
            jw.WritePropertyName("kind"); jw.WriteValue(edge.Kind);
            jw.WritePropertyName("sourceUid"); jw.WriteValue(edge.SourceUid);
            jw.WritePropertyName("lane"); jw.WriteValue(edge.Lane);
            jw.WritePropertyName("speedClass"); jw.WriteValue(edge.SpeedClass);
            jw.WritePropertyName("isSecret"); jw.WriteValue(edge.IsSecret);
            jw.WritePropertyName("direction"); jw.WriteValue(edge.Direction);
            jw.WritePropertyName("trafficSide"); jw.WriteValue(edge.TrafficSide);
            jw.WritePropertyName("isTemporaryLeftHandTrafficRoad"); jw.WriteValue(edge.IsTemporaryLeftHandTrafficRoad);
            jw.WritePropertyName("midX"); jw.WriteValue(edge.MidX);
            jw.WritePropertyName("midZ"); jw.WriteValue(edge.MidZ);
            jw.WritePropertyName("pathStartRawNodeUid"); jw.WriteValue(edge.PathStartRawNodeUid);
            jw.WritePropertyName("pathEndRawNodeUid"); jw.WriteValue(edge.PathEndRawNodeUid);
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

        public void ExportGeoJson(string filePath)
        {
            if (_nodes.Count == 0 && _edges.Count == 0)
            {
                _endpoints.Clear();
                _matchedEndpointIds.Clear();
                _pendingRoadSnaps.Clear();
                _pendingPrefabSnaps.Clear();
                _prefabEndpointsByStablePortKey.Clear();
                if (IncludeRoads) ProcessRoads();
                ProcessPrefabs();
                if (IncludeRoadToPrefabLinks) SnapRoadEndpointsToPrefabs();
                if (IncludePrefabToPrefabLinks) SnapPrefabEndpointsToPrefabs();
                ComputeNodeDegrees();
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
                    WriteProperties(jw, "edge", edge.Kind, edge.SourceUid, edge.Lane, edge.From, edge.To, edge: edge);
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
                    WriteProperties(jw, "node", node.Kind, node.SourceUid, node.Lane, node.Id, null, node.RawNodeUid, node.SnapStatus, node.SnapDetail, node.InDegree, node.OutDegree);
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
            string snapDetail = null,
            int inDegree = 0,
            int outDegree = 0,
            LaneDebugEdge edge = null)
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
                jw.WritePropertyName("inDegree"); jw.WriteValue(inDegree);
                jw.WritePropertyName("outDegree"); jw.WriteValue(outDegree);
            }
            else
            {
                jw.WritePropertyName("from"); jw.WriteValue(idOrFrom);
                jw.WritePropertyName("to"); jw.WriteValue(to);
                if (edge != null)
                {
                    jw.WritePropertyName("direction"); jw.WriteValue(edge.Direction);
                    jw.WritePropertyName("speedClass"); jw.WriteValue(edge.SpeedClass);
                    jw.WritePropertyName("isSecret"); jw.WriteValue(edge.IsSecret);
                    jw.WritePropertyName("trafficSide"); jw.WriteValue(edge.TrafficSide);
                    jw.WritePropertyName("isTemporaryLeftHandTrafficRoad"); jw.WriteValue(edge.IsTemporaryLeftHandTrafficRoad);
                    jw.WritePropertyName("midX"); jw.WriteValue(edge.MidX);
                    jw.WritePropertyName("midZ"); jw.WriteValue(edge.MidZ);
                    jw.WritePropertyName("pathStartRawNodeUid"); jw.WriteValue(edge.PathStartRawNodeUid);
                    jw.WritePropertyName("pathEndRawNodeUid"); jw.WriteValue(edge.PathEndRawNodeUid);
                }
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
                float roadMidX = (startNode.X + endNode.X) * 0.5f;
                float roadMidZ = (startNode.Z + endNode.Z) * 0.5f;
                bool invertRoadLaneDirection = IsTemporaryLeftHandTrafficRoad(roadMidX, roadMidZ);
                string speedClass = RoadSpeedClass(road.RoadLook);

                for (int lane = 0; lane < rightCount; lane++)
                {
                    float offset = RoadLaneOffset(road.RoadLook, lane, rightSide: true);
                    var path = OffsetPolyline(center, offset);
                    if (invertRoadLaneDirection) Array.Reverse(path);
                    AddRoadLane(
                        road,
                        "right",
                        lane,
                        path,
                        forward: !invertRoadLaneDirection,
                        invertRoadLaneDirection ? endNode.Uid : startNode.Uid,
                        invertRoadLaneDirection ? startNode.Uid : endNode.Uid,
                        invertRoadLaneDirection,
                        roadMidX,
                        roadMidZ,
                        speedClass);
                }

                for (int lane = 0; lane < leftCount; lane++)
                {
                    float offset = RoadLaneOffset(road.RoadLook, lane, rightSide: false);
                    var path = OffsetPolyline(center, offset);
                    if (!invertRoadLaneDirection) Array.Reverse(path);
                    AddRoadLane(
                        road,
                        "left",
                        lane,
                        path,
                        forward: invertRoadLaneDirection,
                        invertRoadLaneDirection ? startNode.Uid : endNode.Uid,
                        invertRoadLaneDirection ? endNode.Uid : startNode.Uid,
                        invertRoadLaneDirection,
                        roadMidX,
                        roadMidZ,
                        speedClass);
                }
            }
        }

        private static bool IsTemporaryLeftHandTrafficRoad(float midX, float midZ)
        {
            // Temporary ETS2 UK mainland polygon drawn in the viewer.
            // This keeps the diagnostic direction flip away from Calais and continental Europe.
            return IsPointInPolygon(midX, midZ, TemporaryLeftHandTrafficPolygon);
        }

        private static bool IsPointInPolygon(float x, float z, float[][] polygon)
        {
            bool inside = false;
            int count = polygon.Length;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                float xi = polygon[i][0];
                float zi = polygon[i][1];
                float xj = polygon[j][0];
                float zj = polygon[j][1];

                bool crossesZ = (zi > z) != (zj > z);
                if (!crossesZ) continue;

                float intersectionX = (xj - xi) * (z - zi) / (zj - zi) + xi;
                if (x < intersectionX) inside = !inside;
            }
            return inside;
        }

        private static string RoadSpeedClass(TsRoadLook look)
        {
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
            if (lane.IndexOf("motorway", StringComparison.OrdinalIgnoreCase) >= 0) return "motorway";
            if (lane.IndexOf("freeway", StringComparison.OrdinalIgnoreCase) >= 0) return "freeway";
            if (lane.IndexOf("expressway", StringComparison.OrdinalIgnoreCase) >= 0) return "expressway";
            if (lane.IndexOf("divided", StringComparison.OrdinalIgnoreCase) >= 0) return "divided";
            if (lane.IndexOf("slow_road", StringComparison.OrdinalIgnoreCase) >= 0) return "slow_road";
            if (lane.IndexOf("slow road", StringComparison.OrdinalIgnoreCase) >= 0) return "slow_road";
            return null;
        }

        private void AddRoadLane(
            TsRoadItem road,
            string side,
            int laneIndex,
            float[][] path,
            bool forward,
            ulong pathStartRawNodeUid,
            ulong pathEndRawNodeUid,
            bool isTemporaryLeftHandTrafficRoad = false,
            float midX = 0f,
            float midZ = 0f,
            string speedClass = "local_road")
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
                SpeedClass = speedClass,
                IsSecret = road.IsSecret,
                Direction = forward ? "start-to-end" : "end-to-start",
                TrafficSide = isTemporaryLeftHandTrafficRoad ? "temporary-left-hand" : "right-hand",
                IsTemporaryLeftHandTrafficRoad = isTemporaryLeftHandTrafficRoad,
                MidX = (float)Math.Round(midX, 4),
                MidZ = (float)Math.Round(midZ, 4),
                PathStartRawNodeUid = pathStartRawNodeUid.ToString("X"),
                PathEndRawNodeUid = pathEndRawNodeUid.ToString("X"),
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
                                SpeedClass = "local_road",
                                IsSecret = prefab.IsSecret,
                                Path = path,
                            };
                            _edges.Add(edge);
                            AddEndpoint(edge, atStart: true, fromRawUid, startId);
                            AddEndpoint(edge, atStart: false, toRawUid, endId);
                        }
                    }
                }
            }
        }

        private void AddEndpoint(LaneDebugEdge edge, bool atStart, ulong rawNodeUid, string stablePortKey = null)
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
            var endpoint = new LaneEndpoint
            {
                Edge = edge,
                AtStart = atStart,
                Id = atStart ? edge.From : edge.To,
                X = pt[0],
                Z = pt[1],
                DirX = dirX / len,
                DirZ = dirZ / len,
                RawNodeUid = rawNodeUid,
                StablePortKey = stablePortKey,
            };
            _endpoints.Add(endpoint);

            if (edge.Kind == "prefab" && !string.IsNullOrEmpty(stablePortKey))
            {
                if (!_prefabEndpointsByStablePortKey.TryGetValue(stablePortKey, out var list))
                {
                    list = new List<LaneEndpoint>();
                    _prefabEndpointsByStablePortKey[stablePortKey] = list;
                }
                list.Add(endpoint);
            }
        }

        private void SnapRoadEndpointsToPrefabs()
        {
            const float MaxSnapDistance = 12f;
            const float MaxAngleDeg = 110f;
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
            var matchedCountByGroupKey = new Dictionary<string, int>();
            var pairedGroupKeys = new HashSet<string>();
            foreach (var kv in roadGroups)
            {
                if (!prefabGroups.TryGetValue(kv.Key, out var prefabEndpoints)) continue;
                pairedGroupKeys.Add(kv.Key);
                if (CountUniqueEndpointIds(kv.Value) != CountUniqueEndpointIds(prefabEndpoints)) groupsWithMismatch++;
                int groupSnapped = SnapEndpointGroup(kv.Value, prefabEndpoints, MaxSnapDistance, minDot);
                matchedCountByGroupKey[kv.Key] = groupSnapped;
                snapped += groupSnapped;
            }

            if (ApplyRoadSnapGeometry) ApplyPendingRoadSnaps();
            if (ApplyPrefabSnapGeometry) ApplyPendingPrefabSnaps();
            var unmatched = MarkUnmatchedEndpoints(endpointGroupKeys, pairedGroupKeys, matchedCountByGroupKey, roadGroups, prefabGroups, MaxSnapDistance, minDot);
            Logger.Instance.Info($"[LaneGraphDebug] Matched {snapped} road lane endpoints to prefab endpoints");
            Logger.Instance.Info($"[LaneGraphDebug] Unmatched endpoints: road={unmatched.Road}, prefab={unmatched.Prefab}, count-mismatch groups={groupsWithMismatch}");
        }

        private void SnapPrefabEndpointsToPrefabs()
        {
            const float MaxSnapDistance = 12f;
            const float MaxAngleDeg = 110f;
            float minDot = (float)Math.Cos(MaxAngleDeg * Math.PI / 180.0);

            var startsByRawNode = new Dictionary<ulong, List<LaneEndpoint>>();
            var endsByRawNode = new Dictionary<ulong, List<LaneEndpoint>>();
            foreach (var endpoint in _endpoints)
            {
                if (endpoint.Edge.Kind != "prefab") continue;
                var groups = endpoint.AtStart ? startsByRawNode : endsByRawNode;
                if (!groups.TryGetValue(endpoint.RawNodeUid, out var list))
                {
                    list = new List<LaneEndpoint>();
                    groups[endpoint.RawNodeUid] = list;
                }
                list.Add(endpoint);
            }

            int snapped = 0;
            foreach (var kv in endsByRawNode)
            {
                if (!startsByRawNode.TryGetValue(kv.Key, out var starts)) continue;
                snapped += SnapPrefabEndpointGroup(kv.Value, starts, MaxSnapDistance, minDot);
            }

            Logger.Instance.Info($"[LaneGraphDebug] Matched {snapped} prefab endpoints to adjacent prefab endpoints");
        }

        private void SnapRoadEndpointsToRoads()
        {
            const float MaxSnapDistance = 20f;
            const float MaxAngleDeg = 110f;
            float minDot = (float)Math.Cos(MaxAngleDeg * Math.PI / 180.0);

            var startsByRawNode = new Dictionary<string, List<LaneEndpoint>>();
            var endsByRawNode = new Dictionary<string, List<LaneEndpoint>>();
            foreach (var endpoint in _endpoints)
            {
                if (endpoint.Edge.Kind != "road") continue;
                string key = RoadEndpointGroupKey(endpoint);
                var groups = endpoint.AtStart ? startsByRawNode : endsByRawNode;
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<LaneEndpoint>();
                    groups[key] = list;
                }
                list.Add(endpoint);
            }

            int snapped = 0;
            foreach (var kv in endsByRawNode)
            {
                if (!startsByRawNode.TryGetValue(kv.Key, out var starts)) continue;
                snapped += SnapRoadEndpointGroup(kv.Value, starts, MaxSnapDistance, minDot);
            }

            Logger.Instance.Info($"[LaneGraphDebug] Matched {snapped} road endpoints to adjacent road endpoints");
        }

        private static string RoadEndpointGroupKey(LaneEndpoint endpoint)
        {
            return endpoint.RawNodeUid.ToString("X") + ":" + RoadLaneSideRank(endpoint);
        }

        private int SnapRoadEndpointGroup(
            List<LaneEndpoint> endEndpoints,
            List<LaneEndpoint> startEndpoints,
            float maxSnapDistance,
            float minDot)
        {
            endEndpoints = UniqueEndpointsById(endEndpoints);
            startEndpoints = UniqueEndpointsById(startEndpoints);
            endEndpoints.RemoveAll(e => startEndpoints.Exists(s => s.Id == e.Id));
            if (endEndpoints.Count == 0 || startEndpoints.Count == 0) return 0;

            float avgX = 0f, avgZ = 0f;
            foreach (var endpoint in endEndpoints)
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
            endEndpoints.Sort((a, b) => Lateral(a, lateralX, lateralZ).CompareTo(Lateral(b, lateralX, lateralZ)));
            startEndpoints.Sort((a, b) => Lateral(a, lateralX, lateralZ).CompareTo(Lateral(b, lateralX, lateralZ)));

            var matches = FindBestOrderedMatches(endEndpoints, startEndpoints, maxSnapDistance, minDot);
            int snapped = 0;
            foreach (var match in matches)
            {
                var from = match.Road;
                var to = match.Prefab;
                if (from.Edge.SourceUid == to.Edge.SourceUid) continue;

                _edges.Add(new LaneDebugEdge
                {
                    From = from.Id,
                    To = to.Id,
                    Kind = match.Distance < 2f ? "road_link_good" : "road_link_adjusted",
                    SourceUid = from.Edge.SourceUid,
                    Lane = from.Edge.Lane + " -> " + to.Edge.Lane + $" ({match.Distance:0.0}m, dot {match.Dot:0.00})",
                    SpeedClass = from.Edge.SpeedClass ?? to.Edge.SpeedClass ?? "local_road",
                    IsSecret = from.Edge.IsSecret || to.Edge.IsSecret,
                    Path = new[] { new[] { from.X, from.Z }, new[] { to.X, to.Z } },
                });
                snapped++;
            }

            return snapped;
        }

        private int SnapPrefabEndpointGroup(
            List<LaneEndpoint> endEndpoints,
            List<LaneEndpoint> startEndpoints,
            float maxSnapDistance,
            float minDot)
        {
            endEndpoints = UniqueEndpointsById(endEndpoints);
            startEndpoints = UniqueEndpointsById(startEndpoints);
            endEndpoints.RemoveAll(e => startEndpoints.Exists(s => s.Id == e.Id));
            if (endEndpoints.Count == 0 || startEndpoints.Count == 0) return 0;

            float avgX = 0f, avgZ = 0f;
            foreach (var endpoint in endEndpoints)
            {
                avgX += endpoint.DirX;
                avgZ += endpoint.DirZ;
            }
            float avgLen = (float)Math.Sqrt(avgX * avgX + avgZ * avgZ);

            List<EndpointMatch> matches;
            if (avgLen < 0.001f)
            {
                // Two prefabs can meet at a single shared game node where the two
                // opposing carriageways come together (e.g. a country-border crossing).
                // In that case the group mixes lanes flowing in opposite directions, so the
                // averaged end direction cancels out and the lateral ordering axis is
                // undefined. Fall back to nearest-position matching, which still links the
                // coincident end/start pairs across the two prefabs (each pair is ~0m apart
                // and direction-aligned) instead of dropping the connection entirely.
                matches = FindBestProximityMatches(endEndpoints, startEndpoints, maxSnapDistance, minDot);
            }
            else
            {
                avgX /= avgLen;
                avgZ /= avgLen;

                float lateralX = -avgZ;
                float lateralZ = avgX;
                endEndpoints.Sort((a, b) => Lateral(a, lateralX, lateralZ).CompareTo(Lateral(b, lateralX, lateralZ)));
                startEndpoints.Sort((a, b) => Lateral(a, lateralX, lateralZ).CompareTo(Lateral(b, lateralX, lateralZ)));

                matches = FindBestOrderedMatches(endEndpoints, startEndpoints, maxSnapDistance, minDot);
            }
            int snapped = 0;
            foreach (var match in matches)
            {
                if (match.Road.Edge.SourceUid == match.Prefab.Edge.SourceUid) continue;

                _matchedEndpointIds.Add(match.Road.Id);
                _matchedEndpointIds.Add(match.Prefab.Id);
                if (_nodes.TryGetValue(match.Road.Id, out var fromNode))
                {
                    fromNode.SnapStatus = match.Distance < 2f ? "matched" : "adjusted";
                    fromNode.SnapDetail = "prefab to prefab";
                }
                if (_nodes.TryGetValue(match.Prefab.Id, out var toNode))
                {
                    toNode.SnapStatus = match.Distance < 2f ? "matched" : "adjusted";
                    toNode.SnapDetail = "prefab to prefab";
                }

                _edges.Add(new LaneDebugEdge
                {
                    From = match.Road.Id,
                    To = match.Prefab.Id,
                    Kind = match.Distance < 2f ? "prefab_link_good" : "prefab_link_adjusted",
                    SourceUid = match.Road.Edge.SourceUid,
                    Lane = match.Road.Edge.Lane + " -> " + match.Prefab.Edge.Lane + $" ({match.Distance:0.0}m, dot {match.Dot:0.00})",
                    SpeedClass = "local_road",
                    IsSecret = match.Road.Edge.IsSecret || match.Prefab.Edge.IsSecret,
                    Path = new[] { new[] { match.Road.X, match.Road.Z }, new[] { match.Prefab.X, match.Prefab.Z } },
                });
                snapped++;
            }
            return snapped;
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
            roadEndpoints.Sort(CompareRoadLaneOrder);
            prefabEndpoints.Sort((a, b) => Lateral(a, lateralX, lateralZ).CompareTo(Lateral(b, lateralX, lateralZ)));
            roadEndpoints = UniqueEndpointsById(roadEndpoints);
            prefabEndpoints = BestEndpointPerIdForReferences(prefabEndpoints, roadEndpoints, maxSnapDistance, minDot);

            int snapped = 0;
            var matches = FindBestOrderedMatches(roadEndpoints, prefabEndpoints, maxSnapDistance, minDot);
            foreach (var match in matches)
            {
                var roadEndpoint = match.Road;
                var best = match.Prefab;

                if (ApplyRoadSnapGeometry) QueueRoadEndpointSnap(roadEndpoint, best.X, best.Z);
                if (ApplyPrefabSnapGeometry) QueuePrefabEndpointSnap(best, roadEndpoint.X, roadEndpoint.Z);
                if (ApplyPrefabSnapGeometry)
                {
                    best.X = roadEndpoint.X;
                    best.Z = roadEndpoint.Z;
                }
                _matchedEndpointIds.Add(roadEndpoint.Id);
                _matchedEndpointIds.Add(best.Id);
                if (_nodes.TryGetValue(roadEndpoint.Id, out var node))
                {
                    if (ApplyRoadSnapGeometry)
                    {
                        node.X = best.X;
                        node.Z = best.Z;
                    }
                    node.RawNodeUid = roadEndpoint.RawNodeUid.ToString("X");
                    node.SnapStatus = match.Distance < 2f ? "matched" : "adjusted";
                    node.SnapDetail = "road to prefab link";
                }
                if (_nodes.TryGetValue(best.Id, out var prefabNode))
                {
                    if (ApplyPrefabSnapGeometry)
                    {
                        prefabNode.X = roadEndpoint.X;
                        prefabNode.Z = roadEndpoint.Z;
                    }
                    prefabNode.RawNodeUid = best.RawNodeUid.ToString("X");
                    prefabNode.SnapStatus = "matched";
                    prefabNode.SnapDetail = ApplyPrefabSnapGeometry ? "prefab snapped to road" : "road to prefab link";
                }

                string from = roadEndpoint.AtStart ? best.Id : roadEndpoint.Id;
                string to = roadEndpoint.AtStart ? roadEndpoint.Id : best.Id;
                float prefabX = ApplyPrefabSnapGeometry ? roadEndpoint.X : best.X;
                float prefabZ = ApplyPrefabSnapGeometry ? roadEndpoint.Z : best.Z;
                float[][] path = roadEndpoint.AtStart
                    ? new[] { new[] { prefabX, prefabZ }, new[] { roadEndpoint.X, roadEndpoint.Z } }
                    : new[] { new[] { roadEndpoint.X, roadEndpoint.Z }, new[] { prefabX, prefabZ } };

                _edges.Add(new LaneDebugEdge
                {
                    From = from,
                    To = to,
                    Kind = match.Distance < 2f ? "snap_good" : "snap_adjusted",
                    SourceUid = roadEndpoint.Edge.SourceUid,
                    Lane = roadEndpoint.Edge.Lane + " -> " + best.Edge.Lane + $" ({match.Distance:0.0}m, dot {match.Dot:0.00})",
                    SpeedClass = "local_road",
                    IsSecret = roadEndpoint.Edge.IsSecret || best.Edge.IsSecret,
                    Path = path,
                });
                snapped++;
            }

            return snapped;
        }

        private static int CompareRoadLaneOrder(LaneEndpoint a, LaneEndpoint b)
        {
            int side = RoadLaneSideRank(a).CompareTo(RoadLaneSideRank(b));
            if (side != 0) return side;
            int lane = RoadLaneIndex(a).CompareTo(RoadLaneIndex(b));
            if (lane != 0) return lane;
            return string.CompareOrdinal(a.Id, b.Id);
        }

        private static int RoadLaneSideRank(LaneEndpoint endpoint)
        {
            var lane = endpoint.Edge.Lane ?? "";
            if (lane.StartsWith("left:", StringComparison.Ordinal)) return 0;
            if (lane.StartsWith("right:", StringComparison.Ordinal)) return 1;
            return 2;
        }

        private static int RoadLaneIndex(LaneEndpoint endpoint)
        {
            var lane = endpoint.Edge.Lane ?? "";
            int colon = lane.IndexOf(':');
            if (colon < 0 || colon + 1 >= lane.Length) return 0;
            return int.TryParse(lane.Substring(colon + 1), out var value) ? value : 0;
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

        private void QueuePrefabEndpointSnap(LaneEndpoint endpoint, float targetX, float targetZ)
        {
            if (string.IsNullOrEmpty(endpoint.StablePortKey))
            {
                QueuePrefabEdgeEndpointSnap(endpoint, targetX, targetZ);
                return;
            }

            if (!_prefabEndpointsByStablePortKey.TryGetValue(endpoint.StablePortKey, out var candidates))
            {
                QueuePrefabEdgeEndpointSnap(endpoint, targetX, targetZ);
                return;
            }

            foreach (var candidate in candidates)
            {
                QueuePrefabEdgeEndpointSnap(candidate, targetX, targetZ);
            }
        }

        private void QueuePrefabEdgeEndpointSnap(LaneEndpoint endpoint, float targetX, float targetZ)
        {
            if (!_pendingPrefabSnaps.TryGetValue(endpoint.Edge, out var targets))
            {
                targets = new EdgeSnapTargets();
                _pendingPrefabSnaps[endpoint.Edge] = targets;
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

        private void ApplyPendingPrefabSnaps()
        {
            const float InfluenceDistance = 18f;

            foreach (var kv in _pendingPrefabSnaps)
            {
                var edge = kv.Key;
                var targets = kv.Value;
                if (edge.Path == null || edge.Path.Length == 0) continue;

                var path = edge.Path;
                int n = path.Length;
                float[] distanceFromStart = new float[n];
                for (int i = 1; i < n; i++)
                {
                    float dx = path[i][0] - path[i - 1][0];
                    float dz = path[i][1] - path[i - 1][1];
                    distanceFromStart[i] = distanceFromStart[i - 1] + (float)Math.Sqrt(dx * dx + dz * dz);
                }

                float totalLength = distanceFromStart[n - 1];
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
                    if (i == 0 && targets.HasStart)
                    {
                        path[i][0] = targets.StartX;
                        path[i][1] = targets.StartZ;
                        continue;
                    }
                    if (i == n - 1 && targets.HasEnd)
                    {
                        path[i][0] = targets.EndX;
                        path[i][1] = targets.EndZ;
                        continue;
                    }

                    float startWeight = targets.HasStart
                        ? Clamp01(1f - distanceFromStart[i] / InfluenceDistance)
                        : 0f;
                    float endWeight = targets.HasEnd
                        ? Clamp01(1f - (totalLength - distanceFromStart[i]) / InfluenceDistance)
                        : 0f;
                    float weightSum = startWeight + endWeight;
                    if (weightSum > 1f)
                    {
                        startWeight /= weightSum;
                        endWeight /= weightSum;
                    }

                    path[i][0] += startDx * startWeight + endDx * endWeight;
                    path[i][1] += startDz * startWeight + endDz * endWeight;
                }
            }
        }

        private static List<EndpointMatch> FindBestProximityMatches(
            List<LaneEndpoint> endEndpoints,
            List<LaneEndpoint> startEndpoints,
            float maxSnapDistance,
            float minDot)
        {
            // Greedy nearest-position assignment used when the endpoint group has no
            // well-defined flow direction (opposing carriageways meeting at a shared node).
            // Only cross-prefab pairs that pass the distance/angle test are considered, so
            // a shared game node links each prefab to its neighbour without inventing turns.
            var candidates = new List<EndpointMatch>();
            foreach (var end in endEndpoints)
            {
                foreach (var start in startEndpoints)
                {
                    if (end.Edge.SourceUid == start.Edge.SourceUid) continue;
                    if (!TryScoreMatch(end, start, maxSnapDistance, minDot, out var dist, out var dot, out var score)) continue;
                    candidates.Add(new EndpointMatch
                    {
                        Road = end,
                        Prefab = start,
                        Distance = dist,
                        Dot = dot,
                        Cost = score,
                    });
                }
            }

            candidates.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            var usedEnds = new HashSet<string>();
            var usedStarts = new HashSet<string>();
            var result = new List<EndpointMatch>();
            foreach (var candidate in candidates)
            {
                if (usedEnds.Contains(candidate.Road.Id) || usedStarts.Contains(candidate.Prefab.Id)) continue;
                usedEnds.Add(candidate.Road.Id);
                usedStarts.Add(candidate.Prefab.Id);
                result.Add(candidate);
            }
            return result;
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

        private static float Clamp01(float value)
        {
            if (value <= 0f) return 0f;
            if (value >= 1f) return 1f;
            return value;
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

        private static List<LaneEndpoint> BestEndpointPerIdForReferences(
            List<LaneEndpoint> endpoints,
            List<LaneEndpoint> references,
            float maxSnapDistance,
            float minDot)
        {
            var bestById = new Dictionary<string, LaneEndpointChoice>();
            foreach (var endpoint in endpoints)
            {
                var choice = ScoreEndpointAgainstReferences(endpoint, references, maxSnapDistance, minDot);
                if (!bestById.TryGetValue(endpoint.Id, out var current) ||
                    choice.ValidRank < current.ValidRank ||
                    (choice.ValidRank == current.ValidRank && choice.Score < current.Score))
                {
                    bestById[endpoint.Id] = choice;
                }
            }

            var result = new List<LaneEndpoint>(bestById.Count);
            var seen = new HashSet<string>();
            foreach (var endpoint in endpoints)
            {
                if (!seen.Add(endpoint.Id)) continue;
                result.Add(bestById[endpoint.Id].Endpoint);
            }
            return result;
        }

        private static LaneEndpointChoice ScoreEndpointAgainstReferences(
            LaneEndpoint endpoint,
            List<LaneEndpoint> references,
            float maxSnapDistance,
            float minDot)
        {
            int bestRank = 1;
            float bestScore = float.MaxValue;
            foreach (var reference in references)
            {
                float dx = endpoint.X - reference.X;
                float dz = endpoint.Z - reference.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                float dot = endpoint.DirX * reference.DirX + endpoint.DirZ * reference.DirZ;
                int rank = dist <= maxSnapDistance && dot >= minDot ? 0 : 1;
                float score = dist + (1f - dot) * 8f;
                if (rank < bestRank || (rank == bestRank && score < bestScore))
                {
                    bestRank = rank;
                    bestScore = score;
                }
            }

            return new LaneEndpointChoice
            {
                Endpoint = endpoint,
                ValidRank = bestRank,
                Score = bestScore,
            };
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
            Dictionary<string, int> matchedCountByGroupKey,
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
                int matchedInGroup = matchedCountByGroupKey.TryGetValue(groupKey, out var count) ? count : 0;
                if (endpoint.Edge.Kind == "road")
                {
                    node.Kind = "road_unmatched";
                    road++;
                }
                else if (endpoint.Edge.Kind == "prefab")
                {
                    if (matchedInGroup > 0)
                    {
                        node.Kind = "prefab_extra_soft";
                        node.SnapStatus = "extra";
                        node.SnapDetail = "extra prefab lane in partially matched group; " + node.SnapDetail;
                    }
                    else
                    {
                        node.Kind = "prefab_extra";
                        prefab++;
                    }
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
            foreach (var candidate in oppositeEndpoints)
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

        private void ComputeNodeDegrees()
        {
            foreach (var node in _nodes.Values)
            {
                node.InDegree = 0;
                node.OutDegree = 0;
            }

            foreach (var edge in _edges)
            {
                if (edge.Kind == "snap_good" || edge.Kind == "snap_adjusted") continue;
                if (_nodes.TryGetValue(edge.From, out var from)) from.OutDegree++;
                if (_nodes.TryGetValue(edge.To, out var to)) to.InDegree++;
            }

            foreach (var node in _nodes.Values)
            {
                if (node.Kind != "prefab" || node.SnapStatus != "pending") continue;
                if (node.InDegree == 0 || node.OutDegree == 0)
                {
                    node.Kind = "prefab_terminal";
                    node.SnapDetail = "prefab leaf without road snap";
                }
            }
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
                int prev = Math.Max(0, i - 1);
                int next = Math.Min(center.Length - 1, i + 1);
                float dx = center[next][0] - center[prev][0];
                float dz = center[next][1] - center[prev][1];
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 0.001f)
                {
                    result[i] = new[] { center[i][0], center[i][1] };
                    continue;
                }

                float nx = -dz / len;
                float nz = dx / len;
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
            CurvePath Prefix(CurvePath path, int curveIndex)
            {
                var indices = new List<int>(path.CurveIndices);
                indices.Insert(0, curveIndex);
                return new CurvePath { EndNodeIndex = path.EndNodeIndex, CurveIndices = indices };
            }

            List<CurvePath> GetPaths(int curveIndex, HashSet<int> pathSeen)
            {
                var paths = new List<CurvePath>();
                if (pathSeen.Contains(curveIndex)) return paths;

                if (endingCurveIndexToNodeIndex.TryGetValue(curveIndex, out var nodeIndex))
                {
                    paths.Add(new CurvePath { EndNodeIndex = nodeIndex, CurveIndices = new List<int>() });
                    return paths;
                }

                if (curveIndex < 0 || curveIndex >= desc.NavCurves.Count) return paths;
                var curve = desc.NavCurves[curveIndex];
                if (curve.NextLines == null) return paths;
                var nextSeen = new HashSet<int>(pathSeen) { curveIndex };
                foreach (var nextCurveIndex in curve.NextLines)
                    foreach (var path in GetPaths(nextCurveIndex, nextSeen))
                        paths.Add(Prefix(path, nextCurveIndex));
                return paths;
            }

            var result = GetPaths(inputLaneIndex, new HashSet<int>());
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
            public int InDegree;
            public int OutDegree;
        }

        private class LaneDebugEdge
        {
            public string From;
            public string To;
            public string Kind;
            public string SourceUid;
            public string Lane;
            public string SpeedClass;
            public bool IsSecret;
            public string Direction;
            public string TrafficSide;
            public bool IsTemporaryLeftHandTrafficRoad;
            public float MidX;
            public float MidZ;
            public string PathStartRawNodeUid;
            public string PathEndRawNodeUid;
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
            public string StablePortKey;
        }

        private class EndpointMatch
        {
            public LaneEndpoint Road;
            public LaneEndpoint Prefab;
            public float Distance;
            public float Dot;
            public float Cost;
        }

        private class LaneEndpointChoice
        {
            public LaneEndpoint Endpoint;
            public int ValidRank;
            public float Score;
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

        private class ChunkInfo
        {
            public string File;
            public float MinX = float.MaxValue;
            public float MaxX = float.MinValue;
            public float MinZ = float.MaxValue;
            public float MaxZ = float.MinValue;

            public void Include(float x, float z)
            {
                if (x < MinX) MinX = x;
                if (x > MaxX) MaxX = x;
                if (z < MinZ) MinZ = z;
                if (z > MaxZ) MaxZ = z;
            }

            public void Include(float[][] path)
            {
                if (path == null) return;
                foreach (var pt in path)
                {
                    if (pt == null || pt.Length < 2) continue;
                    Include(pt[0], pt[1]);
                }
            }
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

        public class LaneGraphSnapshot
        {
            public List<SnapshotNode> Nodes { get; } = new List<SnapshotNode>();
            public List<SnapshotEdge> Edges { get; } = new List<SnapshotEdge>();
        }

        public class SnapshotNode
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
            public int InDegree;
            public int OutDegree;
        }

        public class SnapshotEdge
        {
            public string From;
            public string To;
            public string Kind;
            public string SourceUid;
            public string Lane;
            public string SpeedClass;
            public bool IsSecret;
            public float[][] Path;
        }
    }
}
