using CommandLine;
using System;
using System.Windows.Forms;

namespace TsMap.Canvas
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Parser.Default.ParseArguments<RunOptions>(args)
                   .WithParsed<RunOptions>(o =>
                   {
                       RunOptionsContext.Current.Options = o;

                       Application.Run(new SetupFormAdvanced());
                   });


        }
    }
}
