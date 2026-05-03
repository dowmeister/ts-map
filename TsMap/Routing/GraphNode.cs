namespace TsMap.Routing
{
    public class GraphNode
    {
        public ulong Uid { get; }
        public float X { get; }
        public float Z { get; }

        public GraphNode(ulong uid, float x, float z)
        {
            Uid = uid;
            X = x;
            Z = z;
        }
    }
}
