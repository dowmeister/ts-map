using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;
using TsMap.FileSystem;
using TsMap.FileSystem.Hash;
using TsMap.Helpers;
using TsMap.Helpers.Logger;
using TsMap.Map.Overlays;

namespace TsMap
{
    public class MapBackgroundExporter
    {
        private static readonly string[] DdsNames = { "map", "map0", "map1", "map2", "map3" };

        private readonly TsMapper _mapper;

        public MapBackgroundExporter(TsMapper mapper)
        {
            _mapper = mapper;
        }

        public void Export(string outputDir)
        {
            var backgroundDir = Path.Combine(outputDir, "map_background");
            Directory.CreateDirectory(backgroundDir);

            var textures = new JArray();

            foreach (var name in DdsNames)
            {
                var info = ExportDds(name, backgroundDir);
                if (info != null) textures.Add(info);
            }

            var mapData = MapProjection.ReadMapDataSii();
            var projection = MapProjection.ReadClimateProjectionSii();
            var textureAspectRatio = GetTextureAspectRatio(textures);
            var mapAspectRatio = mapData.MapSize.x > 0 && mapData.MapSize.z > 0
                ? mapData.MapSize.x / mapData.MapSize.z
                : textureAspectRatio;
            var textureBounds = MapProjection.GetUiMapTextureBounds();
            var bbox = MapProjection.ComputeWgs84Bbox(textureBounds, projection);

            var mapWidth = textureBounds.Width;
            var mapHeight = textureBounds.Height;
            var aspectRatio = (double)mapWidth / mapHeight;

            var json = new JObject
            {
                ["game_bounds"] = new JObject
                {
                    ["min_x"] = _mapper.minX,
                    ["max_x"] = _mapper.maxX,
                    ["min_z"] = _mapper.minZ,
                    ["max_z"] = _mapper.maxZ
                },
                ["projection"] = new JObject
                {
                    ["type"] = projection.MapProjection,
                    ["standard_parallel_1"] = projection.StandardParallel1,
                    ["standard_parallel_2"] = projection.StandardParallel2,
                    ["map_origin"] = new JArray { projection.MapOrigin.lat, projection.MapOrigin.lon },
                    ["map_offset"] = new JArray { projection.MapOffset.x, projection.MapOffset.z },
                    ["map_factor"] = new JArray { projection.MapFactor.z, projection.MapFactor.x },
                    ["use_ets2_uk_scale"] = projection.UseEts2UkScale,
                    ["aspect_ratio"] = aspectRatio
                },
                ["map_data_sii"] = new JObject
                {
                    ["ui_map_center_x"] = mapData.MapCenter.x,
                    ["ui_map_center_z"] = mapData.MapCenter.z,
                    ["ui_map_size_x"] = mapData.MapSize.x,
                    ["ui_map_size_z"] = mapData.MapSize.z,
                    ["ui_map_camera_min_x"] = mapData.UiCameraMin.x,
                    ["ui_map_camera_min_z"] = mapData.UiCameraMin.z,
                    ["ui_map_camera_max_x"] = mapData.UiCameraMax.x,
                    ["ui_map_camera_max_z"] = mapData.UiCameraMax.z,
                    ["camera_limits_min_7_x"] = mapData.CameraLimitsMin7.x,
                    ["camera_limits_min_7_z"] = mapData.CameraLimitsMin7.z,
                    ["camera_limits_max_7_x"] = mapData.CameraLimitsMax7.x,
                    ["camera_limits_max_7_z"] = mapData.CameraLimitsMax7.z,
                    ["zoom_uplift_7"] = mapData.ZoomUplift7,
                    ["texture_aspect_ratio"] = textureAspectRatio,
                    ["ui_map_aspect_ratio"] = mapAspectRatio
                },
                ["texture_bbox_game"] = new JObject
                {
                    ["x_min"] = textureBounds.MinX,
                    ["z_min"] = textureBounds.MinZ,
                    ["x_max"] = textureBounds.MaxX,
                    ["z_max"] = textureBounds.MaxZ
                },
                ["texture_bbox_wgs84"] = new JObject
                {
                    ["west"] = bbox.west,
                    ["south"] = bbox.south,
                    ["east"] = bbox.east,
                    ["north"] = bbox.north
                },
                ["textures"] = textures,
                ["version"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            File.WriteAllText(
                Path.Combine(backgroundDir, "map_info.json"),
                json.ToString(Formatting.Indented));
        }

        private static double GetTextureAspectRatio(JArray textures)
        {
            foreach (var item in textures)
            {
                if (!(item is JObject texture)) continue;
                var width = texture.Value<double?>("width");
                var height = texture.Value<double?>("height");
                if (width.HasValue && height.HasValue && height.Value > 0)
                    return width.Value / height.Value;
            }

            return 1.0;
        }

        // Reads a single background DDS (via TOBJ, handling both V1 and V2 archives).
        // Returns a JObject with the texture metadata, or null if not found.
        private JObject ExportDds(string name, string outputDir)
        {
            var tobjPath = $"/material/ui/map/{name}.tobj";
            var file = UberFileSystem.Instance.GetFile(tobjPath);
            if (file == null)
            {
                Logger.Instance.Warning($"[MapBackground] {tobjPath} not found — skipping");
                return null;
            }

            byte[] ddsBytes;
            uint width, height;
            string format;

            if (file.Entry is HashEntryV2 v2Entry)
            {
                // V2: image data is embedded directly in the TOBJ entry.
                // Reconstruct a standard DDS file from the raw pixel data + ImgMetadata.
                if (!v2Entry._imgMetadata.HasValue)
                {
                    Logger.Instance.Warning($"[MapBackground] {tobjPath}: V2 entry has no ImgMetadata — skipping");
                    return null;
                }

                var meta = v2Entry._imgMetadata.Value;
                width = meta.Width;
                height = meta.Height;
                format = meta.Format.ToString().Replace("Format", "");

                var pixelData = file.Entry.Read();
                ddsBytes = BuildDds(width, height, meta.MipmapCount, meta.Format, pixelData);
            }
            else
            {
                // V1: TOBJ is a plain text-like binary; path to the DDS is at offset 0x30.
                var tobjData = file.Entry.Read();
                if (tobjData.Length <= 0x30)
                {
                    Logger.Instance.Warning($"[MapBackground] {tobjPath}: TOBJ too short — skipping");
                    return null;
                }

                var ddsPath = PathHelper.EnsureLocalPath(
                    Encoding.UTF8.GetString(tobjData, 0x30, tobjData.Length - 0x30)
                        .Split('\0')[0]); // null-terminated

                var ddsFile = UberFileSystem.Instance.GetFile(ddsPath);
                if (ddsFile == null)
                {
                    Logger.Instance.Warning($"[MapBackground] DDS not found at '{ddsPath}' — skipping");
                    return null;
                }

                ddsBytes = ddsFile.Entry.Read();

                if (ddsBytes.Length < 148 ||
                    MemoryHelper.ReadUInt32(ddsBytes, 0x00) != 0x20534444 ||
                    MemoryHelper.ReadUInt32(ddsBytes, 0x04) != 0x7C)
                {
                    Logger.Instance.Warning($"[MapBackground] Invalid DDS header for '{ddsPath}' — skipping");
                    return null;
                }

                height = MemoryHelper.ReadUInt32(ddsBytes, 0x0C);
                width = MemoryHelper.ReadUInt32(ddsBytes, 0x10);
                var fourCc = MemoryHelper.ReadUInt32(ddsBytes, 0x54);
                if (fourCc == MemoryHelper.MakeFourCc('D', 'X', '1', '0'))
                    format = ((DxgiFormat)MemoryHelper.ReadUInt32(ddsBytes, 0x80)).ToString().Replace("Format", "");
                else
                    format = Dds.GetDXGIFormat(new DdsPixelFormat
                    {
                        Size = MemoryHelper.ReadUInt32(ddsBytes, 0x4c),
                        Flags = MemoryHelper.ReadUInt32(ddsBytes, 0x50),
                        FourCc = fourCc,
                        RgbBitCount = MemoryHelper.ReadUInt32(ddsBytes, 0x58),
                        RBitMask = MemoryHelper.ReadUInt32(ddsBytes, 0x5c),
                        GBitMask = MemoryHelper.ReadUInt32(ddsBytes, 0x60),
                        BBitMask = MemoryHelper.ReadUInt32(ddsBytes, 0x64),
                        ABitMask = MemoryHelper.ReadUInt32(ddsBytes, 0x68),
                    }).ToString().Replace("Format", "");
            }

            File.WriteAllBytes(Path.Combine(outputDir, $"{name}.dds"), ddsBytes);

            return new JObject
            {
                ["file"] = $"{name}.dds",
                ["width"] = width,
                ["height"] = height,
                ["format"] = format,
                ["role"] = name == "map" ? "overview" : "quadrant"
            };
        }

        // Builds a valid DDS file (header + DX10 extension + pixel data) for BC-format images.
        private static byte[] BuildDds(uint width, uint height, uint mipmapCount, DxgiFormat format, byte[] pixelData)
        {
            using (var ms = new MemoryStream(148 + pixelData.Length))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(0x20534444u); // "DDS "

                // DDS_HEADER (124 bytes)
                bw.Write(124u); // dwSize
                uint flags = 0x0002100Fu; // CAPS|HEIGHT|WIDTH|PIXELFORMAT|LINEARSIZE
                if (mipmapCount > 1) flags |= 0x00020000u; // MIPMAPCOUNT
                bw.Write(flags);
                bw.Write(height);
                bw.Write(width);
                bw.Write(Math.Max(1u, (width + 3) / 4) * 16u); // dwPitchOrLinearSize (BC7: 16 bytes/block)
                bw.Write(0u); // dwDepth
                bw.Write(mipmapCount);
                for (int i = 0; i < 11; i++) bw.Write(0u); // dwReserved1[11]

                // DDS_PIXELFORMAT (32 bytes)
                bw.Write(32u);
                bw.Write(0x4u); // FOURCC flag
                bw.Write(0x30315844u); // "DX10"
                bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u);

                // dwCaps
                uint caps = 0x1000u; // TEXTURE
                if (mipmapCount > 1) caps |= 0x400008u; // COMPLEX|MIPMAP
                bw.Write(caps);
                bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u); // caps2/3/4, reserved2

                // DDS_HEADER_DXT10 (20 bytes)
                bw.Write((uint)format); // dxgiFormat
                bw.Write(3u);  // D3D10_RESOURCE_DIMENSION_TEXTURE2D
                bw.Write(0u);  // miscFlag
                bw.Write(1u);  // arraySize
                bw.Write(0u);  // miscFlags2

                bw.Write(pixelData);
                return ms.ToArray();
            }
        }

    }
}
