using System.Collections.Generic;

namespace TsMap
{
    public class TsServiceDef
    {
        public float X { get; set; }
        public float Y { get; set; }
        public string Type { get; set; }
        public string City { get; set; }
        public byte DlcGuard { get; set; }
        public bool IsSecret { get; set; }
        public Dictionary<string, object> Properties { get; set; }

        public TsServiceDef()
        {
            Properties = new Dictionary<string, object>();
        }
    }
}
