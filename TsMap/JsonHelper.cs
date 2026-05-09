using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TsMap
{
    public class JsonHelper
    {
        private static readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ts-map");
        public static void SaveTileMapInfo(string path, float x1, float x2, float y1, float y2, int minZoom, int maxZoom)
        {
            var tileMapInfo = new JObject
            {
                ["x1"] = x1,
                ["x2"] = x2,
                ["y1"] = y1,
                ["y2"] = y2,
                ["minZoom"] = minZoom,
                ["maxZoom"] = maxZoom,
                ["tileSize"] = SettingsManager.Current.Settings.TileGenerator.TileSize
            };

            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "TileMapInfo.json"), tileMapInfo.ToString(Formatting.Indented));
        }

        public static void SaveVectorTileMapInfo(string path, float minX, float maxX, float minZ, float maxZ)
        {
            var projection = MapProjection.ReadClimateProjectionSii();
            var vectorTileMapInfo = new JObject
            {
                ["x1"] = minX,
                ["x2"] = maxX,
                ["y1"] = minZ,
                ["y2"] = maxZ,
                ["minZoom"] = 4,
                ["maxZoom"] = 13,
                ["tileSize"] = 256,
                ["projection"] = new JObject
                {
                    ["type"] = projection.MapProjection,
                    ["standard_parallel_1"] = projection.StandardParallel1,
                    ["standard_parallel_2"] = projection.StandardParallel2,
                    ["map_origin"] = new JArray { projection.MapOrigin.lat, projection.MapOrigin.lon },
                    ["map_offset"] = new JArray { projection.MapOffset.x, projection.MapOffset.z },
                    ["map_factor"] = new JArray { projection.MapFactor.z, projection.MapFactor.x },
                    ["use_ets2_uk_scale"] = projection.UseEts2UkScale
                }
            };

            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "VectorTileMapInfo.json"), vectorTileMapInfo.ToString(Formatting.Indented));
        }

        public static void SaveSettings(Settings settings)
        {
            Directory.CreateDirectory(_settingsPath);
            File.WriteAllText(Path.Combine(_settingsPath, "Settings.json"), JsonConvert.SerializeObject(settings, Formatting.Indented));
        }

        public static Settings LoadSettings()
        {
            if (!File.Exists(Path.Combine(_settingsPath, "Settings.json"))) return new Settings();
            return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(Path.Combine(_settingsPath, "Settings.json")));
        }

        public static void SaveRoadPoints(List<dynamic> points)
        {
            Directory.CreateDirectory(_settingsPath);
            JavaScriptSerializer serializer = new JavaScriptSerializer();

            serializer.MaxJsonLength = Int32.MaxValue;
            File.WriteAllText(Path.Combine(_settingsPath, "RoadPoints.json"), serializer.Serialize(points));
        }
    }
}
