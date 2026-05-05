using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TsMap;
using TsMap.Routing;
using Newtonsoft.Json;

namespace TsMap.Cli
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var gameOption = new Option<DirectoryInfo>(
                name: "--game",
                description: "Path to ETS2 or ATS game directory")
            {
                IsRequired = true
            };
            gameOption.AddAlias("-g");

            var outputOption = new Option<DirectoryInfo>(
                name: "--output",
                description: "Output directory for exported data")
            {
                IsRequired = true
            };
            outputOption.AddAlias("-o");

            var formatOption = new Option<ExportFormat>(
                name: "--format",
                description: "Export format: geojson, json, or all",
                getDefaultValue: () => ExportFormat.All);
            formatOption.AddAlias("-f");

            var modsDirOption = new Option<DirectoryInfo>(
                name: "--mods-dir",
                description: "Path to directory containing mod files (optional)");
            modsDirOption.AddAlias("-m");

            var modsListOption = new Option<FileInfo>(
                name: "--mods-list",
                description: "Path to JSON file containing list of mods to load (optional)");
            modsListOption.AddAlias("-l");

            var validateOption = new Option<bool>(
                name: "--validate-routing-graph",
                description: "Run 8 validation checks after export and write routing-graph-validation.txt",
                getDefaultValue: () => false);
            validateOption.AddAlias("--validate");

            var rootCommand = new RootCommand("TsMap CLI - Export ETS2/ATS map data")
        {
            gameOption,
            outputOption,
            formatOption,
            modsDirOption,
            modsListOption,
            validateOption
        };

            rootCommand.SetHandler(async (gameDir, outputDir, format, modsDir, modsList, validate) =>
            {
                await ExportMapData(gameDir, outputDir, format, modsDir, modsList, validate);
            }, gameOption, outputOption, formatOption, modsDirOption, modsListOption, validateOption);

            return await rootCommand.InvokeAsync(args);
        }

        static async Task ExportMapData(DirectoryInfo gameDir, DirectoryInfo outputDir, ExportFormat format, DirectoryInfo modsDir, FileInfo modsList, bool validateRoutingGraph = false)
        {
            if (!gameDir.Exists)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Game directory not found: {gameDir.FullName}");
                Console.ResetColor();
                return;
            }

            outputDir.Create();

            Console.WriteLine("╔════════════════════════════════════════╗");
            Console.WriteLine("║       TsMap Data Exporter CLI          ║");
            Console.WriteLine("╚════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"Game Directory: {gameDir.FullName}");
            Console.WriteLine($"Output Directory: {outputDir.FullName}");
            Console.WriteLine($"Export Format: {format}");

            // Load mods if specified
            var mods = new List<Mod>();
            if (modsDir != null && modsList != null)
            {
                Console.WriteLine($"Mods Directory: {modsDir.FullName}");
                Console.WriteLine($"Mods List: {modsList.FullName}");
                mods = LoadMods(modsDir, modsList);
                Console.WriteLine($"Loaded {mods.Count(m => m.Load)} mod(s)");
            }

            Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Initialize TsMapper
                Console.Write("Loading game files... ");
                var mapper = new TsMapper(gameDir.FullName, mods);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓");
                Console.ResetColor();

                // Parse map
                Console.Write("Parsing map sectors... ");
                mapper.Parse();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓");
                Console.ResetColor();

                Console.WriteLine();
                Console.WriteLine($"Found {mapper.Roads.Count:N0} roads");
                Console.WriteLine($"Found {mapper.Prefabs.Count:N0} prefabs");
                Console.WriteLine($"Found {mapper.Cities.Count:N0} cities");
                Console.WriteLine($"Found {mapper.Companies.Count:N0} companies");
                Console.WriteLine($"Found {mapper.FerryConnections.Count:N0} ferry connections");
                Console.WriteLine();

                // Export GeoJSON
                if (format == ExportFormat.GeoJson || format == ExportFormat.All)
                {
                    Console.WriteLine("Exporting GeoJSON files...");
                    var geoJsonPath = Path.Combine(outputDir.FullName, "geojson");
                    Directory.CreateDirectory(geoJsonPath);

                    var exporter = new GeoJsonExporter(mapper);
                    RoutingGraph capturedGraph = null;

                    await Task.Run(() =>
                    {
                        Console.Write("  → roads.geojson... ");
                        exporter.ExportRoads(Path.Combine(geoJsonPath, "roads.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → prefab_roads.geojson, prefab_flat.geojson, prefab_buildings.geojson... ");
                        exporter.ExportPrefabs(
                            Path.Combine(geoJsonPath, "prefab_roads.geojson"),
                            Path.Combine(geoJsonPath, "prefab_flat.geojson"),
                            Path.Combine(geoJsonPath, "prefab_buildings.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → cities.geojson... ");
                        exporter.ExportCities(Path.Combine(geoJsonPath, "cities.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → companies.geojson... ");
                        exporter.ExportCompanies(Path.Combine(geoJsonPath, "companies.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → ferries.geojson... ");
                        exporter.ExportFerries(Path.Combine(geoJsonPath, "ferries.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → map_flat.geojson, map_buildings.geojson... ");
                        exporter.ExportMapAreas(
                            Path.Combine(geoJsonPath, "map_flat.geojson"),
                            Path.Combine(geoJsonPath, "map_buildings.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → buildings.geojson... ");
                        exporter.ExportBuildings(Path.Combine(geoJsonPath, "buildings.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → footprints.geojson... ");
                        exporter.ExportModelFootprints(Path.Combine(geoJsonPath, "footprints.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → overlays.geojson... ");
                        exporter.ExportOverlays(
                            Path.Combine(geoJsonPath, "overlays.geojson"),
                            Path.Combine(outputDir.FullName, "overlay_images")
                        );
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → VectorTileMapInfo.json... ");
                        JsonHelper.SaveVectorTileMapInfo(outputDir.FullName, mapper.minX, mapper.maxX, mapper.minZ, mapper.maxZ);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → hidden_roads.geojson... ");
                        exporter.ExportHiddenRoads(Path.Combine(geoJsonPath, "hidden_roads.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        Console.Write("  → hidden_prefabs.geojson... ");
                        exporter.ExportHiddenPrefabs(Path.Combine(geoJsonPath, "hidden_prefabs.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        mapper.ExportInfo(outputDir.FullName);

                        Console.Write("  → routing-graph.json... ");
                        capturedGraph = new RoutingGraphBuilder(mapper).Build();
                        var graphExporter = new GraphExporter(capturedGraph);
                        graphExporter.Export(Path.Combine(geoJsonPath, "routing-graph.json"));
                        graphExporter.ExportPrefabPaths(Path.Combine(geoJsonPath, "routing-edge-paths.json"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ ({capturedGraph.Nodes.Count:N0} nodes, {capturedGraph.Edges.Count:N0} edges)");
                        Console.ResetColor();
                    });

                    if (validateRoutingGraph && capturedGraph != null)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Running routing graph validation...");
                        new GraphValidator(mapper, capturedGraph).Validate(geoJsonPath);
                    }

                    Console.WriteLine();
                }

                // Export JSON data
                if (format == ExportFormat.Json || format == ExportFormat.All)
                {
                    Console.WriteLine("Exporting JSON data files...");

                    await Task.Run(() =>
                    {
                        mapper.ExportInfo(outputDir.FullName);
                    });

                    Console.WriteLine();
                }

                stopwatch.Stop();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("═══════════════════════════════════════");
                Console.WriteLine($"✓ Export completed in {stopwatch.Elapsed.TotalSeconds:F2}s");
                Console.WriteLine("═══════════════════════════════════════");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("Error during export:");
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
        }

        static List<Mod> LoadMods(DirectoryInfo modsDir, FileInfo modsList)
        {
            var mods = new List<Mod>();

            if (!modsDir.Exists)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Mods directory not found: {modsDir.FullName}");
                Console.ResetColor();
                return mods;
            }

            if (!modsList.Exists)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Mods list file not found: {modsList.FullName}");
                Console.ResetColor();
                return mods;
            }

            try
            {
                var json = File.ReadAllText(modsList.FullName);
                var config = JsonConvert.DeserializeObject<ModListConfig>(json);

                if (config?.Mods == null || config.Mods.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Warning: No mods found in configuration file");
                    Console.ResetColor();
                    return mods;
                }

                foreach (var fileName in config.Mods)
                {
                    var modPath = Path.Combine(modsDir.FullName, fileName);
                    if (File.Exists(modPath))
                    {
                        var mod = new Mod(modPath)
                        {
                            Load = true
                        };
                        mods.Add(mod);
                        Console.WriteLine($"  ✓ {fileName}");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  ⚠ Mod file not found: {fileName}");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error loading mods: {ex.Message}");
                Console.ResetColor();
            }

            return mods;
        }
    }

    class ModListConfig
    {
        public List<string> Mods { get; set; } = new List<string>();
    }

    enum ExportFormat
    {
        GeoJson,
        Json,
        All
    }
}