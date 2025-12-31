using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TsMap.Common;
using TsMap.Helpers;
using TsMap.Helpers.Logger;
using TsMap.TsItem;

namespace TsMap
{
    /// <summary>
    /// Exports TsMap data to GeoJSON format for use with vector tile generators like tippecanoe
    /// </summary>
    public class GeoJsonExporter
    {
        private readonly TsMapper _mapper;

        public GeoJsonExporter(TsMapper mapper)
        {
            _mapper = mapper;
        }

        /// <summary>
        /// Convert game coordinates to WGS84 longitude/latitude
        /// </summary>
        private (double lon, double lat) GameToLatLng(float gameX, float gameZ)
        {
            var width = _mapper.maxX - _mapper.minX;
            var height = _mapper.maxZ - _mapper.minZ;

            var normalizedX = (gameX - _mapper.minX) / width;
            var normalizedZ = (gameZ - _mapper.minZ) / height;

            var lon = (normalizedX * 360.0) - 180.0;
            var lat = 90.0 - (normalizedZ * 180.0);

            return (lon, lat);
        }

        /// <summary>
        /// Transform prefab local coordinates to world coordinates
        /// </summary>
        private (float X, float Z) TransformPrefabPoint(float localX, float localZ, float prefabStartX, float prefabStartZ, float rotation, TsNode origin)
        {
            var x = prefabStartX + localX;
            var z = prefabStartZ + localZ;

            var s = Math.Sin(rotation);
            var c = Math.Cos(rotation);
            var newX = x - origin.X;
            var newZ = z - origin.Z;
            var rotatedX = (newX * c) - (newZ * s) + origin.X;
            var rotatedZ = (newX * s) + (newZ * c) + origin.Z;

            return ((float)rotatedX, (float)rotatedZ);
        }

        /// <summary>
        /// Export all map data as GeoJSON files
        /// </summary>
        public void ExportAll(string outputPath)
        {
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            Logger.Instance.Info("Starting GeoJSON export...");

            ExportRoads(Path.Combine(outputPath, "roads.geojson"));
            ExportPrefabs(
                Path.Combine(outputPath, "prefab_roads.geojson"),
                Path.Combine(outputPath, "prefab_flat.geojson"),
                Path.Combine(outputPath, "prefab_buildings.geojson"));
            ExportCities(Path.Combine(outputPath, "cities.geojson"));
            ExportCompanies(Path.Combine(outputPath, "companies.geojson"));
            ExportFerries(Path.Combine(outputPath, "ferries.geojson"));
            ExportMapAreas(
                Path.Combine(outputPath, "map_flat.geojson"),
                Path.Combine(outputPath, "map_buildings.geojson"));

            Logger.Instance.Info($"GeoJSON export completed to: {outputPath}");
        }

        /// <summary>
        /// Export roads as GeoJSON Polygons with actual width (not LineStrings)
        /// Creates filled polygons similar to prefab roads for seamless connections
        /// </summary>
        public void ExportRoads(string filePath)
        {
            var features = new JArray();

            foreach (var road in _mapper.Roads)
            {
                if (road.Hidden) continue;

                var startNode = road.GetStartNode();
                var endNode = road.GetEndNode();

                if (startNode == null || endNode == null) continue;

                var roadWidth = road.RoadLook?.GetWidth() ?? 10;
                var halfWidth = roadWidth / 2f;

                // Get curve points
                var curvePoints = new List<(float x, float z)>();

                if (!road.HasPoints())
                {
                    var sx = startNode.X;
                    var sz = startNode.Z;
                    var ex = endNode.X;
                    var ez = endNode.Z;

                    var radius = Math.Sqrt(Math.Pow(sx - ex, 2) + Math.Pow(sz - ez, 2));

                    var tanSx = Math.Cos(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
                    var tanEx = Math.Cos(-(Math.PI * 0.5f - endNode.Rotation)) * radius;
                    var tanSz = Math.Sin(-(Math.PI * 0.5f - startNode.Rotation)) * radius;
                    var tanEz = Math.Sin(-(Math.PI * 0.5f - endNode.Rotation)) * radius;

                    for (var i = 0; i < 16; i++)  // More points for smoother curves
                    {
                        var s = i / (float)(16 - 1);
                        var x = (float)TsRoadLook.Hermite(s, sx, ex, tanSx, tanEx);
                        var z = (float)TsRoadLook.Hermite(s, sz, ez, tanSz, tanEz);
                        curvePoints.Add((x, z));
                    }
                }
                else
                {
                    foreach (var point in road.GetPoints())
                    {
                        curvePoints.Add((point.X, point.Y));
                    }
                }

                if (curvePoints.Count < 2) continue;

                // Create polygon by offsetting curve perpendicular on both sides
                var leftSide = new List<(double lon, double lat)>();
                var rightSide = new List<(double lon, double lat)>();

                for (int i = 0; i < curvePoints.Count; i++)
                {
                    float dirX, dirZ;

                    if (i == 0)
                    {
                        // Use direction to next point
                        dirX = curvePoints[i + 1].x - curvePoints[i].x;
                        dirZ = curvePoints[i + 1].z - curvePoints[i].z;
                    }
                    else if (i == curvePoints.Count - 1)
                    {
                        // Use direction from previous point
                        dirX = curvePoints[i].x - curvePoints[i - 1].x;
                        dirZ = curvePoints[i].z - curvePoints[i - 1].z;
                    }
                    else
                    {
                        // Use average direction
                        dirX = curvePoints[i + 1].x - curvePoints[i - 1].x;
                        dirZ = curvePoints[i + 1].z - curvePoints[i - 1].z;
                    }

                    var length = (float)Math.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (length > 0)
                    {
                        dirX /= length;
                        dirZ /= length;
                    }

                    // Perpendicular offsets
                    var perpX = -dirZ * halfWidth;
                    var perpZ = dirX * halfWidth;

                    var (lon1, lat1) = GameToLatLng(curvePoints[i].x + perpX, curvePoints[i].z + perpZ);
                    var (lon2, lat2) = GameToLatLng(curvePoints[i].x - perpX, curvePoints[i].z - perpZ);

                    leftSide.Add((lon1, lat1));
                    rightSide.Add((lon2, lat2));
                }

                // Build closed polygon: left side forward + right side backward + close
                var polygonCoords = new JArray();
                foreach (var point in leftSide)
                {
                    polygonCoords.Add(new JArray { point.lon, point.lat });
                }
                for (int i = rightSide.Count - 1; i >= 0; i--)
                {
                    polygonCoords.Add(new JArray { rightSide[i].lon, rightSide[i].lat });
                }
                // Close the ring
                polygonCoords.Add(new JArray { leftSide[0].lon, leftSide[0].lat });

                features.Add(new JObject
                {
                    ["type"] = "Feature",
                    ["geometry"] = new JObject
                    {
                        ["type"] = "Polygon",
                        ["coordinates"] = new JArray { polygonCoords }
                    },
                    ["properties"] = new JObject
                    {
                        ["road_type"] = road.RoadLook?.Token.ToString() ?? "unknown",
                        ["width"] = roadWidth,
                        ["dlc_guard"] = road.DlcGuard
                    }
                });
            }

            File.WriteAllText(filePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = features
            }.ToString(Formatting.None));
            Logger.Instance.Info($"Exported {features.Count} roads to {Path.GetFileName(filePath)}");
        }

        /// <summary>
        /// Export prefab roads AND areas split into separate files
        /// Matches TsMapRenderer.cs lines 166-330 exactly
        /// </summary>
        public void ExportPrefabs(string roadFilePath, string flatAreasFilePath, string buildingsFilePath)
        {
            var roadFeatures = new JArray();
            var flatAreaFeatures = new JArray();
            var buildingFeatures = new JArray();

            foreach (var prefabItem in _mapper.Prefabs)
            {
                if (prefabItem.Hidden || prefabItem.Prefab == null) continue;

                var prefab = prefabItem.Prefab;
                if (prefabItem.Nodes == null || prefabItem.Nodes.Count == 0) continue;

                var origin = _mapper.GetNodeByUid(prefabItem.Nodes[0]);
                if (origin == null) continue;
                if (prefab.PrefabNodes == null) continue;
                if (prefab.MapPoints == null || prefab.MapPoints.Count == 0) continue;

                // Calculate transformation (matches TsMapRenderer lines 189-192)
                var mapPointOrigin = prefab.PrefabNodes[prefabItem.Origin];
                var rot = (float)(origin.Rotation - Math.PI - Math.Atan2(mapPointOrigin.RotZ, mapPointOrigin.RotX) + Math.PI / 2);
                var prefabStartX = origin.X - mapPointOrigin.X;
                var prefabStartZ = origin.Z - mapPointOrigin.Z;

                var pointsDrawn = new List<int>();
                var processedRoadPairs = new HashSet<string>();

                // Process MapPoints - SAME LOOP for both roads and areas (matches TsMapRenderer line 195)
                for (var i = 0; i < prefab.MapPoints.Count; i++)
                {
                    var mapPoint = prefab.MapPoints[i];
                    pointsDrawn.Add(i);

                    // NON-ROAD PREFAB (matches TsMapRenderer lines 200-256)
                    if (mapPoint.LaneCount == -1)
                    {
                        var polyPoints = new Dictionary<int, (float X, float Z)>();
                        var nextPoint = i;

                        do
                        {
                            if (prefab.MapPoints[nextPoint].Neighbours.Count == 0) break;

                            foreach (var neighbour in prefab.MapPoints[nextPoint].Neighbours)
                            {
                                if (!polyPoints.ContainsKey(neighbour))
                                {
                                    nextPoint = neighbour;
                                    var mp = prefab.MapPoints[nextPoint];
                                    var worldPoint = TransformPrefabPoint(mp.X, mp.Z, prefabStartX, prefabStartZ, rot, origin);
                                    polyPoints.Add(nextPoint, worldPoint);
                                    break;
                                }
                                nextPoint = -1;
                            }
                        } while (nextPoint != -1);

                        if (polyPoints.Count < 3) continue;

                        // Determine area type from PrefabColorFlags (matches TsMapRenderer lines 238-253)
                        var visualFlag = prefab.MapPoints[polyPoints.Keys.First()].PrefabColorFlags;
                        var roadOver = MemoryHelper.IsBitSet(visualFlag, 0);
                        string areaType;
                        string color;

                        if (MemoryHelper.IsBitSet(visualFlag, 1))
                        {
                            areaType = "light";
                            color = "#ffb366";
                        }
                        else if (MemoryHelper.IsBitSet(visualFlag, 2))
                        {
                            areaType = "dark";
                            color = "#ff8833";
                        }
                        else if (MemoryHelper.IsBitSet(visualFlag, 3))
                        {
                            areaType = "green";
                            color = "#66cc66";
                        }
                        else
                        {
                            areaType = "unknown";
                            color = "#999999";
                        }

                        var coordinates = new JArray();
                        var ring = new JArray();
                        foreach (var point in polyPoints.Values)
                        {
                            var (lon, lat) = GameToLatLng(point.X, point.Z);
                            ring.Add(new JArray { lon, lat });
                        }
                        ring.Add(ring[0]);
                        coordinates.Add(ring);

                        var feature = new JObject
                        {
                            ["type"] = "Feature",
                            ["geometry"] = new JObject
                            {
                                ["type"] = "Polygon",
                                ["coordinates"] = coordinates
                            },
                            ["properties"] = new JObject
                            {
                                ["prefab_token"] = prefab.Token,
                                ["area_type"] = areaType,
                                ["color"] = color,
                                ["elevated"] = roadOver,
                                ["dlc_guard"] = prefabItem.DlcGuard
                            }
                        };

                        if (areaType == "dark")
                            buildingFeatures.Add(feature);
                        else
                            flatAreaFeatures.Add(feature);

                        continue;
                    }

                    // ROAD PREFAB (matches TsMapRenderer lines 266-323)
                    var mapPointLaneCount = mapPoint.LaneCount;
                    if (mapPointLaneCount == -2 && i < prefab.PrefabNodes.Count)
                    {
                        if (mapPoint.ControlNodeIndex != -1)
                            mapPointLaneCount = prefab.PrefabNodes[mapPoint.ControlNodeIndex].LaneCount;
                    }

                    foreach (var neighbourPointIndex in mapPoint.Neighbours)
                    {
                        // Create unique key to avoid duplicate road segments
                        var pairKey = i < neighbourPointIndex ? $"{i}_{neighbourPointIndex}" : $"{neighbourPointIndex}_{i}";
                        if (processedRoadPairs.Contains(pairKey)) continue;
                        processedRoadPairs.Add(pairKey);

                        var neighbourPoint = prefab.MapPoints[neighbourPointIndex];

                        if ((mapPoint.Hidden || neighbourPoint.Hidden) &&
                            prefab.PrefabNodes.Count + 1 < prefab.MapPoints.Count)
                            continue;

                        var roadYaw = Math.Atan2(neighbourPoint.Z - mapPoint.Z, neighbourPoint.X - mapPoint.X);

                        var neighbourLaneCount = neighbourPoint.LaneCount;
                        if (neighbourLaneCount == -2 && neighbourPointIndex < prefab.PrefabNodes.Count)
                        {
                            if (neighbourPoint.ControlNodeIndex != -1)
                                neighbourLaneCount = prefab.PrefabNodes[neighbourPoint.ControlNodeIndex].LaneCount;
                        }

                        if (mapPointLaneCount == -2 && neighbourLaneCount != -2) mapPointLaneCount = neighbourLaneCount;
                        else if (neighbourLaneCount == -2 && mapPointLaneCount != -2) neighbourLaneCount = mapPointLaneCount;
                        else if (mapPointLaneCount == -2 && neighbourLaneCount == -2)
                        {
                            mapPointLaneCount = neighbourLaneCount = 1;
                        }

                        // Calculate 4 corners (matches TsMapRenderer lines 300-318)
                        var halfWidth1 = (Consts.LaneWidth * mapPointLaneCount + mapPoint.LaneOffset) / 2f;
                        var halfWidth2 = (Consts.LaneWidth * neighbourLaneCount + neighbourPoint.LaneOffset) / 2f;

                        var corner1X = mapPoint.X + (float)(halfWidth1 * Math.Cos(roadYaw + Math.PI / 2));
                        var corner1Z = mapPoint.Z + (float)(halfWidth1 * Math.Sin(roadYaw + Math.PI / 2));
                        var corner1 = TransformPrefabPoint(corner1X, corner1Z, prefabStartX, prefabStartZ, rot, origin);

                        var corner2X = neighbourPoint.X + (float)(halfWidth2 * Math.Cos(roadYaw + Math.PI / 2));
                        var corner2Z = neighbourPoint.Z + (float)(halfWidth2 * Math.Sin(roadYaw + Math.PI / 2));
                        var corner2 = TransformPrefabPoint(corner2X, corner2Z, prefabStartX, prefabStartZ, rot, origin);

                        var corner3X = neighbourPoint.X + (float)(halfWidth2 * Math.Cos(roadYaw - Math.PI / 2));
                        var corner3Z = neighbourPoint.Z + (float)(halfWidth2 * Math.Sin(roadYaw - Math.PI / 2));
                        var corner3 = TransformPrefabPoint(corner3X, corner3Z, prefabStartX, prefabStartZ, rot, origin);

                        var corner4X = mapPoint.X + (float)(halfWidth1 * Math.Cos(roadYaw - Math.PI / 2));
                        var corner4Z = mapPoint.Z + (float)(halfWidth1 * Math.Sin(roadYaw - Math.PI / 2));
                        var corner4 = TransformPrefabPoint(corner4X, corner4Z, prefabStartX, prefabStartZ, rot, origin);

                        var coordinates = new JArray();
                        var (lon1, lat1) = GameToLatLng(corner1.X, corner1.Z);
                        var (lon2, lat2) = GameToLatLng(corner2.X, corner2.Z);
                        var (lon3, lat3) = GameToLatLng(corner3.X, corner3.Z);
                        var (lon4, lat4) = GameToLatLng(corner4.X, corner4.Z);

                        coordinates.Add(new JArray { lon1, lat1 });
                        coordinates.Add(new JArray { lon2, lat2 });
                        coordinates.Add(new JArray { lon3, lat3 });
                        coordinates.Add(new JArray { lon4, lat4 });
                        coordinates.Add(new JArray { lon1, lat1 });

                        roadFeatures.Add(new JObject
                        {
                            ["type"] = "Feature",
                            ["geometry"] = new JObject
                            {
                                ["type"] = "Polygon",
                                ["coordinates"] = new JArray { coordinates }
                            },
                            ["properties"] = new JObject
                            {
                                ["road_type"] = "prefab",
                                ["prefab_token"] = prefab.Token,
                                ["lane_count"] = (mapPointLaneCount + neighbourLaneCount) / 2,
                                ["dlc_guard"] = prefabItem.DlcGuard
                            }
                        });
                    }
                }
            }

            File.WriteAllText(roadFilePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = roadFeatures
            }.ToString(Formatting.None));
            Logger.Instance.Info($"Exported {roadFeatures.Count} prefab road segments to {Path.GetFileName(roadFilePath)}");

            File.WriteAllText(flatAreasFilePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = flatAreaFeatures
            }.ToString(Formatting.None));
            Logger.Instance.Info($"Exported {flatAreaFeatures.Count} prefab flat areas to {Path.GetFileName(flatAreasFilePath)}");

            File.WriteAllText(buildingsFilePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = buildingFeatures
            }.ToString(Formatting.None));
            Logger.Instance.Info($"Exported {buildingFeatures.Count} prefab buildings to {Path.GetFileName(buildingsFilePath)}");
        }

        /// <summary>
        /// Export cities as GeoJSON Points
        /// </summary>
        public void ExportCities(string filePath)
        {
            var features = new JArray();

            foreach (var cityItem in _mapper.Cities)
            {
                if (cityItem.Hidden) continue;

                var city = cityItem.City;
                if (city == null) continue;

                var node = _mapper.GetNodeByUid(cityItem.NodeUid);
                if (node == null) continue;

                var localizedName = _mapper.Localization?.GetLocaleValue(city.LocalizationToken) ?? city.Name;
                var (lon, lat) = GameToLatLng(node.X, node.Z);

                features.Add(new JObject
                {
                    ["type"] = "Feature",
                    ["geometry"] = new JObject
                    {
                        ["type"] = "Point",
                        ["coordinates"] = new JArray { lon, lat }
                    },
                    ["properties"] = new JObject
                    {
                        ["name"] = city.Name,
                        ["localized_name"] = localizedName,
                        ["country"] = city.Country,
                        ["token"] = city.Token
                    }
                });
            }

            File.WriteAllText(filePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = features
            }.ToString(Formatting.None));
            Logger.Instance.Info($"Exported {features.Count} cities to {Path.GetFileName(filePath)}");
        }

        /// <summary>
        /// Export companies as GeoJSON Points
        /// </summary>
        public void ExportCompanies(string filePath)
        {
            var features = new JArray();

            foreach (var company in _mapper.Companies)
            {
                if (company.Hidden) continue;

                if (company.Nodes == null || company.Nodes.Count == 0) continue;
                var node = _mapper.GetNodeByUid(company.Nodes[0]);
                if (node == null) continue;

                var (lon, lat) = GameToLatLng(node.X, node.Z);

                features.Add(new JObject
                {
                    ["type"] = "Feature",
                    ["geometry"] = new JObject
                    {
                        ["type"] = "Point",
                        ["coordinates"] = new JArray { lon, lat }
                    },
                    ["properties"] = new JObject
                    {
                        ["dlc_guard"] = company.DlcGuard
                    }
                });
            }

            File.WriteAllText(filePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = features
            }.ToString(Formatting.None));
            Logger.Instance.Info($"Exported {features.Count} companies to {Path.GetFileName(filePath)}");
        }

        /// <summary>
        /// Export ferry connections as GeoJSON LineStrings
        /// </summary>
        public void ExportFerries(string filePath)
        {
            var features = new JArray();
            var exportedConnections = new HashSet<string>(); // Track to avoid duplicates

            foreach (var ferryConnection in _mapper.FerryConnections)
            {
                if (ferryConnection.Hidden) continue;

                var connections = _mapper.LookupFerryConnection(ferryConnection.FerryPortId);

                if (connections == null || connections.Count == 0) continue;

                foreach (var conn in connections)
                {
                    // Create unique key to avoid duplicates (same connection rendered from both ports)
                    var connectionKey = $"{conn.StartPortToken}_{conn.EndPortToken}";
                    if (exportedConnections.Contains(connectionKey)) continue;
                    exportedConnections.Add(connectionKey);

                    var coordinates = new JArray();

                    if (conn.Connections.Count == 0) // no extra nodes -> straight line
                    {
                        var (startLon, startLat) = GameToLatLng(conn.StartPortLocation.X, conn.StartPortLocation.Y);
                        var (endLon, endLat) = GameToLatLng(conn.EndPortLocation.X, conn.EndPortLocation.Y);

                        coordinates.Add(new JArray { startLon, startLat });
                        coordinates.Add(new JArray { endLon, endLat });
                    }
                    else
                    {
                        // Build bezier curve with control points (similar to TsMapRenderer lines 72-113)
                        var bezierPoints = new List<(float x, float z)>();

                        // Start port to first connection node
                        var startYaw = Math.Atan2(conn.Connections[0].Z - conn.StartPortLocation.Y,
                            conn.Connections[0].X - conn.StartPortLocation.X);
                        var bezierNodes = RenderHelper.GetBezierControlNodes(conn.StartPortLocation.X,
                            conn.StartPortLocation.Y, startYaw, conn.Connections[0].X, conn.Connections[0].Z,
                            conn.Connections[0].Rotation);

                        bezierPoints.Add((conn.StartPortLocation.X, conn.StartPortLocation.Y));
                        bezierPoints.Add((conn.StartPortLocation.X + bezierNodes.Item1.X, conn.StartPortLocation.Y + bezierNodes.Item1.Y));
                        bezierPoints.Add((conn.Connections[0].X - bezierNodes.Item2.X, conn.Connections[0].Z - bezierNodes.Item2.Y));
                        bezierPoints.Add((conn.Connections[0].X, conn.Connections[0].Z));

                        // Loop through connection nodes
                        for (var i = 0; i < conn.Connections.Count - 1; i++)
                        {
                            var ferryPoint = conn.Connections[i];
                            var nextFerryPoint = conn.Connections[i + 1];

                            bezierNodes = RenderHelper.GetBezierControlNodes(ferryPoint.X, ferryPoint.Z, ferryPoint.Rotation,
                                nextFerryPoint.X, nextFerryPoint.Z, nextFerryPoint.Rotation);

                            bezierPoints.Add((ferryPoint.X + bezierNodes.Item1.X, ferryPoint.Z + bezierNodes.Item1.Y));
                            bezierPoints.Add((nextFerryPoint.X - bezierNodes.Item2.X, nextFerryPoint.Z - bezierNodes.Item2.Y));
                            bezierPoints.Add((nextFerryPoint.X, nextFerryPoint.Z));
                        }

                        // Last node to end port
                        var lastFerryPoint = conn.Connections[conn.Connections.Count - 1];
                        var endYaw = Math.Atan2(conn.EndPortLocation.Y - lastFerryPoint.Z,
                            conn.EndPortLocation.X - lastFerryPoint.X);

                        bezierNodes = RenderHelper.GetBezierControlNodes(lastFerryPoint.X,
                            lastFerryPoint.Z, lastFerryPoint.Rotation, conn.EndPortLocation.X, conn.EndPortLocation.Y,
                            endYaw);

                        bezierPoints.Add((lastFerryPoint.X + bezierNodes.Item1.X, lastFerryPoint.Z + bezierNodes.Item1.Y));
                        bezierPoints.Add((conn.EndPortLocation.X - bezierNodes.Item2.X, conn.EndPortLocation.Y - bezierNodes.Item2.Y));
                        bezierPoints.Add((conn.EndPortLocation.X, conn.EndPortLocation.Y));

                        // Convert bezier points to coordinates (sampling the curve)
                        for (int i = 0; i < bezierPoints.Count - 3; i += 3)
                        {
                            var p0 = bezierPoints[i];
                            var p1 = bezierPoints[i + 1];
                            var p2 = bezierPoints[i + 2];
                            var p3 = bezierPoints[i + 3];

                            // Sample the bezier curve
                            for (int t = 0; t <= 10; t++)
                            {
                                float s = t / 10f;
                                float x = (float)(Math.Pow(1 - s, 3) * p0.x +
                                                  3 * Math.Pow(1 - s, 2) * s * p1.x +
                                                  3 * (1 - s) * Math.Pow(s, 2) * p2.x +
                                                  Math.Pow(s, 3) * p3.x);
                                float z = (float)(Math.Pow(1 - s, 3) * p0.z +
                                                  3 * Math.Pow(1 - s, 2) * s * p1.z +
                                                  3 * (1 - s) * Math.Pow(s, 2) * p2.z +
                                                  Math.Pow(s, 3) * p3.z);

                                var (lon, lat) = GameToLatLng(x, z);
                                coordinates.Add(new JArray { lon, lat });
                            }
                        }
                    }

                    features.Add(new JObject
                    {
                        ["type"] = "Feature",
                        ["geometry"] = new JObject
                        {
                            ["type"] = "LineString",
                            ["coordinates"] = coordinates
                        },
                        ["properties"] = new JObject
                        {
                            ["ferry"] = true,
                            ["dlc_guard"] = ferryConnection.DlcGuard
                        }
                    });
                }
            }

            File.WriteAllText(filePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = features
            }.ToString(Formatting.None));
            Logger.Instance.Info($"Exported {features.Count} ferry connections to {Path.GetFileName(filePath)}");
        }

        /// <summary>
        /// Export map areas split into flat surfaces and buildings
        /// </summary>
        public void ExportMapAreas(string flatFilePath, string buildingsFilePath)
        {
            var flatFeatures = new JArray();
            var buildingFeatures = new JArray();
            var dlcGuards = _mapper.GetDlcGuardsForCurrentGame();
            var activeDlcGuards = dlcGuards.Where(x => x.Enabled).Select(x => x.Index).ToList();

            foreach (var mapArea in _mapper.MapAreas)
            {
                if (!activeDlcGuards.Contains(mapArea.DlcGuard) || mapArea.Hidden)
                {
                    continue;
                }

                var points = new List<(double lon, double lat)>();

                foreach (var mapAreaNode in mapArea.NodeUids)
                {
                    var node = _mapper.GetNodeByUid(mapAreaNode);
                    if (node == null) continue;
                    var (lon, lat) = GameToLatLng(node.X, node.Z);
                    points.Add((lon, lat));
                }

                if (points.Count < 3) continue;
                points.Add(points[0]); // Close the ring

                string fillColor = "#FFFFFF";
                int zIndex = mapArea.DrawOver ? 10 : 0;
                bool isBuilding = false;

                if ((mapArea.ColorIndex & 0x03) == 3)
                {
                    fillColor = "#AACB96";
                    zIndex = mapArea.DrawOver ? 13 : 3;
                }
                else if ((mapArea.ColorIndex & 0x02) == 2)
                {
                    fillColor = "#E1A338";
                    zIndex = mapArea.DrawOver ? 12 : 2;
                    isBuilding = true; // Dark areas are buildings
                }
                else if ((mapArea.ColorIndex & 0x01) == 1)
                {
                    fillColor = "#ECCB99";
                    zIndex = mapArea.DrawOver ? 11 : 1;
                }

                var coordinates = new JArray();
                foreach (var point in points)
                {
                    coordinates.Add(new JArray { point.lon, point.lat });
                }

                var feature = new JObject
                {
                    ["type"] = "Feature",
                    ["geometry"] = new JObject
                    {
                        ["type"] = "Polygon",
                        ["coordinates"] = new JArray { coordinates }
                    },
                    ["properties"] = new JObject
                    {
                        ["color"] = fillColor,
                        ["z_index"] = zIndex,
                        ["dlc_guard"] = mapArea.DlcGuard
                    }
                };

                if (isBuilding)
                    buildingFeatures.Add(feature);
                else
                    flatFeatures.Add(feature);
            }

            File.WriteAllText(flatFilePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = flatFeatures
            }.ToString(Formatting.None));

            File.WriteAllText(buildingsFilePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = buildingFeatures
            }.ToString(Formatting.None));

            Logger.Instance.Info($"Exported {flatFeatures.Count} map areas (flat) to {Path.GetFileName(flatFilePath)}");
            Logger.Instance.Info($"Exported {buildingFeatures.Count} map areas (buildings) to {Path.GetFileName(buildingsFilePath)}");
        }

        /// <summary>
        /// Export map overlays (icons) as GeoJSON Points matching TsMapRenderer logic (lines 405-427)
        /// Uses OverlayManager to get all overlay icons (companies, roads, services, bus stops, etc.)
        /// Also exports the actual bitmap images from the game files
        /// </summary>
        public void ExportOverlays(string geoJsonFilePath, string imagesDirectory)
        {
            var features = new JArray();
            var dlcGuards = _mapper.GetDlcGuardsForCurrentGame();
            var activeDlcGuards = dlcGuards.Where(x => x.Enabled).Select(x => x.Index).ToList();

            // Create images directory if it doesn't exist
            Directory.CreateDirectory(imagesDirectory);

            var exportedImages = new HashSet<string>();

            foreach (var overlay in _mapper.OverlayManager.GetOverlays())
            {
                if (!activeDlcGuards.Contains(overlay.DlcGuard) || overlay.IsSecret || !overlay.IsValid())
                {
                    continue;
                }

                var (lon, lat) = GameToLatLng(overlay.Position.X, overlay.Position.Y);

                // Determine icon type from overlay type and name
                string overlayTypeStr = overlay.OverlayType.ToString().ToLower();
                string iconName = overlay.OverlayName;

                // Create unique filename for this overlay
                string imageFileName = $"{overlayTypeStr}_{iconName}.png";
                string imagePath = Path.Combine(imagesDirectory, imageFileName);

                // Export the bitmap if we haven't already
                if (!exportedImages.Contains(imageFileName))
                {
                    try
                    {
                        var bitmap = overlay.GetBitmap();
                        if (bitmap != null)
                        {
                            bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
                            exportedImages.Add(imageFileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.Error($"Failed to export overlay image {imageFileName}: {ex.Message}");
                    }
                }

                features.Add(new JObject
                {
                    ["type"] = "Feature",
                    ["geometry"] = new JObject
                    {
                        ["type"] = "Point",
                        ["coordinates"] = new JArray { lon, lat }
                    },
                    ["properties"] = new JObject
                    {
                        ["overlay_type"] = overlayTypeStr,
                        ["overlay_name"] = iconName,
                        ["type_name"] = overlay.TypeName,
                        ["image"] = imageFileName,
                        ["dlc_guard"] = overlay.DlcGuard
                    }
                });
            }

            File.WriteAllText(geoJsonFilePath, new JObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = features
            }.ToString(Formatting.None));
            Logger.Instance.Info($"Exported {features.Count} overlay icons to {Path.GetFileName(geoJsonFilePath)}");
            Logger.Instance.Info($"Exported {exportedImages.Count} overlay images to {imagesDirectory}");
        }
    }
}
