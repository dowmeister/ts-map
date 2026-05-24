using System.Collections.Generic;

namespace TsMap.Routing
{
    public class LaneRoutingGraph
    {
        public Dictionary<string, LaneRoutingNode> Nodes { get; } = new Dictionary<string, LaneRoutingNode>();
        public List<LaneRoutingEdge> Edges { get; } = new List<LaneRoutingEdge>();
    }

    public class LaneRoutingNode
    {
        public string Uid { get; }
        public float X { get; }
        public float Z { get; }

        public LaneRoutingNode(string uid, float x, float z)
        {
            Uid = uid;
            X = x;
            Z = z;
        }
    }

    public class LaneRoutingEdge
    {
        public string From { get; }
        public string To { get; }
        public float Weight { get; }
        public float Length { get; }
        public string SpeedClass { get; }
        public string ItemType { get; }
        public float[][] Waypoints { get; }

        public float SpeedLimitKph => GraphEdge.SpeedClassToKph(SpeedClass);

        public LaneRoutingEdge(
            string from,
            string to,
            float weight,
            float length,
            string speedClass,
            string itemType,
            float[][] waypoints = null)
        {
            From = from;
            To = to;
            Weight = weight;
            Length = length;
            SpeedClass = speedClass;
            ItemType = itemType;
            Waypoints = waypoints;
        }
    }
}
