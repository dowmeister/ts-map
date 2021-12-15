using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TsMap
{
    public class MapSetContext
    {
        private static readonly Lazy<MapSetContext> _instance = new Lazy<MapSetContext>(() =>
        {
            return new MapSetContext();
        });

        public TsMapSet MapSet { get; set; }

        public static MapSetContext Current
        {
            get
            {
                return _instance.Value;
            }
        }
    }
}
