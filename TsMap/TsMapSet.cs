using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TsMap
{
    public class TsMapSet
    {
        public TsMapSet()
        {
            this.Mods = new List<Mod>();
        }

        public string Name { get; set; }
        public string Path { get; set; }
        public string ModFolderPath { get; set; }

        public string FilesFilter { get; set; }

        public bool LoadMods { get; set; }
        public List<Mod> Mods { get; set; }
        public string OutputPath { get; set; }
    }
}
