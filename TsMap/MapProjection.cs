using System;
using System.Globalization;
using System.IO;
using System.Text;
using TsMap.FileSystem;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap
{
    public struct MapProjectionBounds
    {
        public float MinX;
        public float MaxX;
        public float MinZ;
        public float MaxZ;

        public float Width => MaxX - MinX;
        public float Height => MaxZ - MinZ;
    }

    public struct MapDataSiiInfo
    {
        public (float x, float z) MapCenter;
        public (float x, float z) MapSize;
        public (float x, float z) UiCameraMin;
        public (float x, float z) UiCameraMax;
        public (float x, float z) CameraLimitsMin7;
        public (float x, float z) CameraLimitsMax7;
        public float ZoomUplift7;
    }

    public struct ClimateProjectionInfo
    {
        public string MapProjection;
        public float StandardParallel1;
        public float StandardParallel2;
        public (float lat, float lon) MapOrigin;
        public (float x, float z) MapOffset;
        public (float z, float x) MapFactor;
        public bool UseEts2UkScale;
    }

    public static class MapProjection
    {
        private const double EarthRadiusMeters = 6370997.0;
        private static readonly double LengthOfDegree = EarthRadiusMeters * Math.PI / 180.0;

        public static MapDataSiiInfo ReadMapDataSii()
        {
            var info = new MapDataSiiInfo
            {
                MapCenter = (0f, 0f),
                MapSize = (1f, 1f),
                UiCameraMin = (-67000f, -45000f),
                UiCameraMax = (51000f, 60000f),
                CameraLimitsMin7 = (-82000f, -45000f),
                CameraLimitsMax7 = (51000f, 60000f),
                ZoomUplift7 = 45000f
            };

            var file = UberFileSystem.Instance.GetFile("/def/map_data.sii");
            if (file == null)
            {
                Logger.Instance.Warning("[MapProjection] /def/map_data.sii not found - using default map metadata");
                return info;
            }

            var lines = Encoding.UTF8.GetString(file.Entry.Read()).Split('\n');
            foreach (var rawLine in lines)
            {
                var (valid, key, value) = SiiHelper.ParseLine(rawLine);
                if (!valid) continue;
                switch (key)
                {
                    case "ui_map_center":
                        info.MapCenter = ParseVec2(value);
                        break;
                    case "ui_map_size":
                        info.MapSize = ParseVec2(value);
                        break;
                    case "ui_map_camera_min":
                    case "ui_map_camera_min[0]":
                        info.UiCameraMin = ParseVec2(value);
                        break;
                    case "ui_map_camera_max":
                    case "ui_map_camera_max[0]":
                        info.UiCameraMax = ParseVec2(value);
                        break;
                    case "camera_limits_min[7]":
                        info.CameraLimitsMin7 = ParseVec2(value);
                        break;
                    case "camera_limits_max[7]":
                        info.CameraLimitsMax7 = ParseVec2(value);
                        break;
                    case "zoom_uplift[7]":
                        info.ZoomUplift7 = ParseFloat(value, info.ZoomUplift7);
                        break;
                }
            }

            return info;
        }

        public static MapProjectionBounds GetWorldMapBounds(double aspectRatio = 1.0)
        {
            var mapData = ReadMapDataSii();
            return FitBoundsToAspect(mapData.UiCameraMin, mapData.UiCameraMax, aspectRatio);
        }

        public static MapProjectionBounds GetUiMapTextureBounds()
        {
            var mapData = ReadMapDataSii();
            var scale = mapData.ZoomUplift7 / 1000f;
            var width = mapData.MapSize.x * scale;
            var height = mapData.MapSize.z * scale;

            return new MapProjectionBounds
            {
                MinX = mapData.MapCenter.x - width / 2f,
                MaxX = mapData.MapCenter.x + width / 2f,
                MinZ = mapData.MapCenter.z - height / 2f,
                MaxZ = mapData.MapCenter.z + height / 2f
            };
        }

        public static ClimateProjectionInfo ReadClimateProjectionSii()
        {
            var file = UberFileSystem.Instance.GetFile("/def/climate.sii");
            if (file == null)
            {
                throw new FileNotFoundException("[MapProjection] Required file /def/climate.sii not found");
            }

            var info = new ClimateProjectionInfo();

            var lines = Encoding.UTF8.GetString(file.Entry.Read()).Split('\n');
            foreach (var rawLine in lines)
            {
                var (valid, key, value) = SiiHelper.ParseLine(rawLine);
                if (!valid) continue;

                switch (key)
                {
                    case "map_projection":
                        info.MapProjection = value.Trim();
                        break;
                    case "standard_paralel_1":
                        info.StandardParallel1 = ParseFloat(value, info.StandardParallel1);
                        break;
                    case "standard_paralel_2":
                        info.StandardParallel2 = ParseFloat(value, info.StandardParallel2);
                        break;
                    case "map_origin":
                        var origin = ParseVec2(value);
                        info.MapOrigin = (origin.x, origin.z);
                        break;
                    case "map_offset":
                        info.MapOffset = ParseVec2(value);
                        break;
                    case "map_factor":
                        var factor = ParseVec2(value);
                        info.MapFactor = (factor.x, factor.z);
                        break;
                }
            }

            info.UseEts2UkScale =
                Math.Abs(info.MapOrigin.lat - 50f) < 0.001f &&
                Math.Abs(info.MapOrigin.lon - 15f) < 0.001f &&
                Math.Abs(info.StandardParallel1 - 37f) < 0.001f &&
                Math.Abs(info.StandardParallel2 - 65f) < 0.001f;

            return info;
        }

        public static (double lon, double lat) GameToLatLng(float gameX, float gameZ)
        {
            return GameToLatLng(gameX, gameZ, ReadClimateProjectionSii());
        }

        public static (double lon, double lat) GameToLatLng(
            float gameX,
            float gameZ,
            ClimateProjectionInfo projection)
        {
            if (!string.Equals(projection.MapProjection, "lambert_conic", StringComparison.OrdinalIgnoreCase))
            {
                var lon = projection.MapOrigin.lon + (gameX - projection.MapOffset.z) * projection.MapFactor.x;
                var lat = projection.MapOrigin.lat + (gameZ - projection.MapOffset.x) * projection.MapFactor.z;
                return (lon, lat);
            }

            var x = (double)gameX - projection.MapOffset.x;
            var z = (double)gameZ - projection.MapOffset.z;

            if (projection.UseEts2UkScale)
            {
                const double ukScaleFactor = 0.75;
                const double calaisX = -31100.0;
                const double calaisZ = -5500.0;
                if (x * ukScaleFactor < calaisX && z * ukScaleFactor < calaisZ)
                {
                    x = (x + calaisX / 2.0) * ukScaleFactor;
                    z = (z + calaisZ / 2.0) * ukScaleFactor;
                }
            }

            var lccX = x * projection.MapFactor.x * LengthOfDegree;
            var lccY = z * projection.MapFactor.z * LengthOfDegree;
            return LambertConicInverse(lccX, lccY, projection);
        }

        private static (double lon, double lat) LambertConicInverse(
            double x,
            double y,
            ClimateProjectionInfo projection)
        {
            var phi1 = DegreesToRadians(projection.StandardParallel1);
            var phi2 = DegreesToRadians(projection.StandardParallel2);
            var phi0 = DegreesToRadians(projection.MapOrigin.lat);
            var lambda0 = DegreesToRadians(projection.MapOrigin.lon);

            var n = Math.Log(Math.Cos(phi1) / Math.Cos(phi2)) /
                    Math.Log(TanHalfPiPlus(phi2) / TanHalfPiPlus(phi1));
            var f = Math.Cos(phi1) * Math.Pow(TanHalfPiPlus(phi1), n) / n;
            var rho0 = EarthRadiusMeters * f / Math.Pow(TanHalfPiPlus(phi0), n);

            var rho = Math.Sqrt(x * x + (rho0 - y) * (rho0 - y));
            if (n < 0) rho = -rho;

            var theta = Math.Atan2(x, rho0 - y);
            var phi = 2.0 * Math.Atan(Math.Pow(EarthRadiusMeters * f / rho, 1.0 / n)) - Math.PI / 2.0;
            var lambda = lambda0 + theta / n;

            return (RadiansToDegrees(lambda), RadiansToDegrees(phi));
        }

        private static double TanHalfPiPlus(double radians)
        {
            return Math.Tan(Math.PI / 4.0 + radians / 2.0);
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        public static MapProjectionBounds FitBoundsToAspect(
            (float x, float z) min,
            (float x, float z) max,
            double aspectRatio)
        {
            var width = max.x - min.x;
            var height = max.z - min.z;

            if (width <= 0 || height <= 0)
            {
                return new MapProjectionBounds
                {
                    MinX = min.x,
                    MaxX = max.x,
                    MinZ = min.z,
                    MaxZ = max.z
                };
            }

            if (aspectRatio <= 0)
                aspectRatio = width / height;

            var centerX = (min.x + max.x) / 2f;
            var centerZ = (min.z + max.z) / 2f;
            var boundsAspect = width / height;

            if (boundsAspect < aspectRatio)
                width = (float)(height * aspectRatio);
            else if (boundsAspect > aspectRatio)
                height = (float)(width / aspectRatio);

            return new MapProjectionBounds
            {
                MinX = centerX - width / 2f,
                MaxX = centerX + width / 2f,
                MinZ = centerZ - height / 2f,
                MaxZ = centerZ + height / 2f
            };
        }

        private static (float x, float z) ParseVec2(string value)
        {
            var s = value.Trim().TrimStart('(').TrimEnd(')');
            var parts = s.Split(',');
            if (parts.Length < 2) return (0f, 0f);
            float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x);
            float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var z);
            return (x, z);
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }
}
