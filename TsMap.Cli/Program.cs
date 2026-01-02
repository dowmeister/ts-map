using System.CommandLine;
using System.Diagnostics;
using TsMap;

namespace TsMap.Cli;

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

        var rootCommand = new RootCommand("TsMap CLI - Export ETS2/ATS map data")
        {
            gameOption,
            outputOption,
            formatOption
        };

        rootCommand.SetHandler(async (gameDir, outputDir, format) =>
        {
            await ExportMapData(gameDir, outputDir, format);
        }, gameOption, outputOption, formatOption);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task ExportMapData(DirectoryInfo gameDir, DirectoryInfo outputDir, ExportFormat format)
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
        Console.WriteLine();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Initialize TsMapper
            Console.Write("Loading game files... ");
            var mapper = new TsMapper(gameDir.FullName, new List<Mod>());
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

                    mapper.ExportInfo(outputDir.FullName);
                });

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
}

enum ExportFormat
{
    GeoJson,
    Json,
    All
}
