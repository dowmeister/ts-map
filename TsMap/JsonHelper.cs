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
            // NOTE: minX/maxX/minZ/maxZ here are the "world map" camera-limit bounds
            // (huge/unreliable for some games, e.g. ATS's ui_map_camera_min/max span
            // ~1.6M units vs. the actual ~150K-unit map) — NOT safe input for the LCC
            // projection. Use the same texture/network bounds MapBackgroundExporter
            // already relies on (proven correct for every game) to compute the bbox.
            var textureBounds = MapProjection.GetUiMapTextureBounds();
            var bbox = MapProjection.ComputeWgs84Bbox(textureBounds, projection);
            var vectorTileMapInfo = new JObject
            {
                ["x1"] = minX,
                ["x2"] = maxX,
                ["y1"] = minZ,
                ["y2"] = maxZ,
                ["minZoom"] = 4,
                ["maxZoom"] = 13,
                ["tileSize"] = 256,
                // Bump on every export so the web-viewer/CDN can use it as a `?v=`
                // cache-busting query param for sprites/tiles served from R2.
                ["version"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                // Precomputed WGS84 bbox (edge-sampled through the projection below,
                // not just the 4 corners) — used by the web-viewer for the default
                // view/bounds check, so it doesn't need to reimplement the LCC math.
                ["bounds_wgs84"] = new JObject
                {
                    ["west"] = bbox.west,
                    ["south"] = bbox.south,
                    ["east"] = bbox.east,
                    ["north"] = bbox.north
                },
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
