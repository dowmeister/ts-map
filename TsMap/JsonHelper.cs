﻿using System;
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
                ["maxZoom"] = maxZoom
            };

            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "TileMapInfo.json"), tileMapInfo.ToString(Formatting.Indented));
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

        public static List<TsMapSet> LoadMapSets()
        {
            if (!File.Exists(Path.Combine(_settingsPath, "MapSets.json"))) return new List<TsMapSet>();
            return JsonConvert.DeserializeObject<List<TsMapSet>>(File.ReadAllText(Path.Combine(_settingsPath, "MapSets.json")));
        }

        public static void SaveMapSets(List<TsMapSet> mapSets)
        {
            Directory.CreateDirectory(_settingsPath);
            File.WriteAllText(Path.Combine(_settingsPath, "MapSets.json"), JsonConvert.SerializeObject(mapSets, Formatting.Indented));
        }
    }
}
