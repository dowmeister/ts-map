using System.Collections.Generic;

namespace TsMap.Routing
{
    public class RoutingGraph
    {
        public Dictionary<ulong, GraphNode> Nodes { get; } = new Dictionary<ulong, GraphNode>();
        public List<GraphEdge> Edges { get; } = new List<GraphEdge>();
    }
}
