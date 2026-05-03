namespace TsMap.Routing
{
    public class GraphEdge
    {
        public ulong From { get; }
        public ulong To { get; }
        public float Weight { get; }
        public float Length { get; }
        public string SpeedClass { get; }
        public string ItemType { get; }
        // NavCurve path waypoints in game world coordinates [x, z]; null for road/ferry edges
        public float[][] Waypoints { get; }

        public GraphEdge(ulong from, ulong to, float weight, float length,
                         string speedClass, string itemType, float[][] waypoints = null)
        {
            From       = from;
            To         = to;
            Weight     = weight;
            Length     = length;
            SpeedClass = speedClass;
            ItemType   = itemType;
            Waypoints  = waypoints;
        }
    }
}
