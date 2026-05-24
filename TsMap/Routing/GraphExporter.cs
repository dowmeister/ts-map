using System;
using System.IO;
using Newtonsoft.Json;
using TsMap.Helpers.Logger;

namespace TsMap.Routing
{
    public class LegacyGraphExporter
    {
        private readonly RoutingGraph _graph;

        public LegacyGraphExporter(RoutingGraph graph)
        {
            _graph = graph;
        }

        /// <summary>
        /// Writes routing-graph.json using streaming JsonTextWriter.
        /// UIDs are written as decimal strings — JavaScript Number cannot safely
        /// represent 64-bit unsigned integers (MAX_SAFE_INTEGER = 2^53 - 1).
        /// </summary>
        public void Export(string filePath)
        {
            Logger.Instance.Info($"[Routing] Writing {filePath} ...");

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs))
            using (var jw = new JsonTextWriter(sw))
            {
                jw.Formatting = Formatting.None;

                jw.WriteStartObject();

                // ── meta ──────────────────────────────────────────────────────
                jw.WritePropertyName("meta");
                jw.WriteStartObject();
                jw.WritePropertyName("nodeCount");  jw.WriteValue(_graph.Nodes.Count);
                jw.WritePropertyName("edgeCount");  jw.WriteValue(_graph.Edges.Count);
                jw.WritePropertyName("generatedAt"); jw.WriteValue(DateTime.UtcNow.ToString("o"));
                jw.WriteEndObject();

                // ── nodes ─────────────────────────────────────────────────────
                jw.WritePropertyName("nodes");
                jw.WriteStartArray();
                foreach (var node in _graph.Nodes.Values)
                {
                    jw.WriteStartObject();
                    jw.WritePropertyName("uid"); jw.WriteValue(node.Uid.ToString());
                    jw.WritePropertyName("x");   jw.WriteValue(Math.Round(node.X, 4));
                    jw.WritePropertyName("z");   jw.WriteValue(Math.Round(node.Z, 4));
                    jw.WriteEndObject();
                }
                jw.WriteEndArray();

                // ── edges ─────────────────────────────────────────────────────
                jw.WritePropertyName("edges");
                jw.WriteStartArray();
                foreach (var edge in _graph.Edges)
                {
                    jw.WriteStartObject();
                    jw.WritePropertyName("from");          jw.WriteValue(edge.From.ToString());
                    jw.WritePropertyName("to");            jw.WriteValue(edge.To.ToString());
                    jw.WritePropertyName("weight");        jw.WriteValue(Math.Round(edge.Weight, 2));
                    jw.WritePropertyName("length");        jw.WriteValue(Math.Round(edge.Length, 2));
                    jw.WritePropertyName("speedClass");    jw.WriteValue(edge.SpeedClass);
                    jw.WritePropertyName("speedLimitKph"); jw.WriteValue(edge.SpeedLimitKph);
                    jw.WritePropertyName("itemType");      jw.WriteValue(edge.ItemType);
                    jw.WriteEndObject();
                }
                jw.WriteEndArray();

                jw.WriteEndObject();
            }

            Logger.Instance.Info(
                $"[Routing] Export complete: {_graph.Nodes.Count} nodes, " +
                $"{_graph.Edges.Count} edges → {filePath}");
        }

        /// <summary>
        /// Writes routing-prefab-paths.json — a separate file mapping "from-to" edge keys
        /// to NavCurve waypoint arrays. Kept separate from routing-graph.json so the routing
        /// service can load the lean graph at startup without the extra path geometry.
        /// </summary>
        public void ExportPrefabPaths(string filePath)
        {
            int count = 0;
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs))
            using (var jw = new JsonTextWriter(sw))
            {
                jw.Formatting = Formatting.None;
                jw.WriteStartObject();
                foreach (var edge in _graph.Edges)
                {
                    if (edge.Waypoints == null || edge.Waypoints.Length < 2) continue;
                    jw.WritePropertyName(edge.From + "-" + edge.To);
                    jw.WriteStartArray();
                    foreach (var pt in edge.Waypoints)
                    {
                        jw.WriteStartArray();
                        jw.WriteValue(Math.Round(pt[0], 1));
                        jw.WriteValue(Math.Round(pt[1], 1));
                        jw.WriteEndArray();
                    }
                    jw.WriteEndArray();
                    count++;
                }
                jw.WriteEndObject();
            }
            Logger.Instance.Info($"[Routing] Prefab paths: {count} entries → {filePath}");
        }
    }
}
