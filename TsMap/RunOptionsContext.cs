using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TsMap
{
    public class RunOptions
    {
        [Option('m', "mapSet", Required = false, HelpText = "Autoload a Map Set")]
        public string MapSet { get; set; }

        [Option('g', "tiles", Required = false, HelpText = "Automatically Export Tiles")]
        public bool GenerateTiles { get; set; }

        [Option('i', "exportInfo", Required = false, HelpText = "Automatically Export Info")]
        public bool ExportInfo { get; set; }
    }

    public class RunOptionsContext
    {
        public RunOptions Options { get; set; }

        private static readonly Lazy<RunOptionsContext> _instance = new Lazy<RunOptionsContext>(() =>
        {
            return new RunOptionsContext();
        });

        public static RunOptionsContext Current
        {
            get
            {
                return _instance.Value;
            }
        }
    }
}
