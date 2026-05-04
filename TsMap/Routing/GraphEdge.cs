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

        // Speed limit in km/h derived from SpeedClass — used by the routing service for
        // time-based "fastest" weighting and exported to routing-graph.json as metadata.
        public float SpeedLimitKph => SpeedClassToKph(SpeedClass);

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

        public static float SpeedClassToKph(string speedClass)
        {
            if (speedClass == "freeway"    || speedClass == "motorway")   return 130f;
            if (speedClass == "expressway")                                return 110f;
            if (speedClass == "divided")                                   return 100f;
            if (speedClass == "slow_road")                                 return  50f;
            return 90f; // local_road, prefab default, and unknown
        }
    }
}
