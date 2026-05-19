using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TsMap;
using TsMap.Common;
using TsMap.Routing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
                description: "Export format: geojson (tiles), routing (graph), or all",
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

            var mdbOption = new Option<string>(
                name: "--mdb",
                description: "Comma-separated list of map databases to export (e.g. 'europe,usa'). Omit to export all.");

            var rootCommand = new RootCommand("TsMap CLI - Export ETS2/ATS map data")
        {
            gameOption,
            outputOption,
            formatOption,
            modsDirOption,
            modsListOption,
            validateOption,
            mdbOption
        };

            rootCommand.SetHandler(async (gameDir, outputDir, format, modsDir, modsList, validate, mdb) =>
            {
                await ExportMapData(gameDir, outputDir, format, modsDir, modsList, validate, mdb);
            }, gameOption, outputOption, formatOption, modsDirOption, modsListOption, validateOption, mdbOption);

            return await rootCommand.InvokeAsync(args);
        }

        static async Task ExportMapData(DirectoryInfo gameDir, DirectoryInfo outputDir, ExportFormat format, DirectoryInfo modsDir, FileInfo modsList, bool validateRoutingGraph = false, string mdbFilter = null)
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
            if (!string.IsNullOrWhiteSpace(mdbFilter))
                Console.WriteLine($"Map DB Filter:  {mdbFilter}");

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

                if (!string.IsNullOrWhiteSpace(mdbFilter))
                {
                    mapper.MapFilter = mdbFilter
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .ToList();
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓");
                Console.ResetColor();

                // Parse map
                Console.Write("Parsing map sectors... ");
                mapper.Parse();
                mapper.Localization.ChangeLocalization("en_gb");
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

                var geoJsonPath = Path.Combine(outputDir.FullName, "geojson");
                var routingPath = Path.Combine(outputDir.FullName, "routing");
                RoutingGraph capturedGraph = null;

                // Export GeoJSON (tiles)
                if (format == ExportFormat.GeoJson || format == ExportFormat.All)
                {
                    Console.WriteLine("Exporting GeoJSON files...");
                    Directory.CreateDirectory(geoJsonPath);

                    var projectionBounds = MapProjection.GetWorldMapBounds();
                    var exporter = new GeoJsonExporter(mapper);

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

                        Console.Write("  → countries.geojson... ");
                        exporter.ExportCountries(Path.Combine(geoJsonPath, "countries.geojson"));
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
                        JsonHelper.SaveVectorTileMapInfo(
                            outputDir.FullName,
                            projectionBounds.MinX,
                            projectionBounds.MaxX,
                            projectionBounds.MinZ,
                            projectionBounds.MaxZ);
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

                        Console.Write("  → Poi.json... ");
                        ExportCitiesCompanies(mapper, Path.Combine(outputDir.FullName, "Poi.json"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();

                        /*
                        Console.Write("  → map_background/ (DDS + map_info.json)... ");
                        new MapBackgroundExporter(mapper).Export(outputDir.FullName);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓");
                        Console.ResetColor();
                        */
                    });

                    Console.WriteLine();
                }

                // Export routing graph
                if (format == ExportFormat.Routing || format == ExportFormat.All)
                {
                    Console.WriteLine("Exporting routing graph...");
                    Directory.CreateDirectory(routingPath);

                    await Task.Run(() =>
                    {
                        Console.Write("  → routing-graph.json... ");
                        capturedGraph = new RoutingGraphBuilder(mapper).Build();
                        var graphExporter = new GraphExporter(capturedGraph);
                        graphExporter.Export(Path.Combine(routingPath, "routing-graph.json"));
                        graphExporter.ExportPrefabPaths(Path.Combine(routingPath, "routing-edge-paths.json"));
                        var laneGraphDebugExporter = new LaneGraphDebugExporter(mapper);
                        laneGraphDebugExporter.ExportSplit(routingPath, "routing-lane-graph-debug");
                        laneGraphDebugExporter.ExportGeoJson(Path.Combine(routingPath, "routing-lane-graph-debug.geojson"));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ ({capturedGraph.Nodes.Count:N0} nodes, {capturedGraph.Edges.Count:N0} edges)");
                        Console.ResetColor();
                    });

                    if (validateRoutingGraph && capturedGraph != null)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Running routing graph validation...");
                        new GraphValidator(mapper, capturedGraph).Validate(routingPath);
                    }

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

        static void ExportCitiesCompanies(TsMapper mapper, string filePath)
        {
            var companyDefByInGameId = mapper.CompanyDefs
                .Where(d => d.InGameId != null)
                .GroupBy(d => d.InGameId.Split('.').Last())
                .ToDictionary(g => g.Key, g => g.First());

            var companiesByCityId = new Dictionary<string, List<JObject>>();

            foreach (var company in mapper.Companies)
            {
                if (company.Hidden) continue;
                if (company.Nodes == null || company.Nodes.Count == 0) continue;

                var node = mapper.GetNodeByUid(company.Nodes[0]);
                if (node == null) continue;

                var city = mapper.FindCity(node.X, node.Z) ?? mapper.FindNearestCity(node.X, node.Z);
                if (city == null) continue;

                var cityInGameId = ScsToken.TokenToString(city.Token);
                companyDefByInGameId.TryGetValue(company.CompanyDefId, out var companyDef);

                if (!companiesByCityId.ContainsKey(cityInGameId))
                {
                    companiesByCityId[cityInGameId] = new List<JObject>();
                }

                companiesByCityId[cityInGameId].Add(new JObject
                {
                    ["inGameId"] = company.CompanyDefId,
                    ["name"] = companyDef?.Name ?? company.CompanyDefId,
                    ["position"] = new JObject
                    {
                        ["x"] = node.X,
                        ["z"] = node.Z
                    }
                });
            }

            foreach (var cityCompanies in companiesByCityId.Values)
            {
                cityCompanies.Sort((a, b) =>
                    string.Compare((string)a["name"], (string)b["name"], StringComparison.OrdinalIgnoreCase));
            }

            var cities = new JArray();
            var countries = new JArray();

            foreach (var country in mapper.Countries.OrderBy(c => c.CountryId))
            {
                var countryName = mapper.Localization.GetLocaleValue(country.LocalizationToken, "en_gb")
                    ?? mapper.Localization.GetLocaleValue(country.LocalizationToken)
                    ?? country.Name;

                countries.Add(new JObject
                {
                    ["id"] = country.CountryId,
                    ["inGameId"] = ScsToken.TokenToString(country.Token),
                    ["name"] = countryName,
                    ["code"] = country.CountryCode,
                    ["position"] = new JObject
                    {
                        ["x"] = country.X,
                        ["z"] = country.Y
                    }
                });
            }

            foreach (var cityItem in mapper.Cities.OrderBy(c => c.City?.Name))
            {
                if (cityItem.Hidden) continue;
                if (cityItem.City == null) continue;

                var node = mapper.GetNodeByUid(cityItem.NodeUid);
                if (node == null) continue;

                var city = cityItem.City;
                var cityInGameId = ScsToken.TokenToString(city.Token);
                var country = mapper.GetCountryByTokenName(city.Country);
                var englishName = mapper.Localization.GetLocaleValue(city.LocalizationToken, "en_gb")
                    ?? mapper.Localization.GetLocaleValue(city.LocalizationToken)
                    ?? city.Name;

                companiesByCityId.TryGetValue(cityInGameId, out var cityCompanies);

                cities.Add(new JObject
                {
                    ["inGameId"] = cityInGameId,
                    ["name"] = englishName,
                    ["population"] = city.Population.HasValue ? new JValue(city.Population.Value) : JValue.CreateNull(),
                    ["position"] = new JObject
                    {
                        ["x"] = node.X,
                        ["z"] = node.Z
                    },
                    ["countryId"] = country == null ? JValue.CreateNull() : new JValue(country.CountryId),
                    ["companies"] = cityCompanies == null ? new JArray() : new JArray(cityCompanies)
                });
            }

            var root = new JObject
            {
                ["countries"] = countries,
                ["cities"] = cities
            };

            File.WriteAllText(filePath, root.ToString(Formatting.Indented));
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
        Routing,
        All
    }
}
