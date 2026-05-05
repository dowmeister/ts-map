using System;
using System.IO;
using TsMap.FileSystem;
using TsMap.TsItem;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap
{
    public class TsSector
    {
        public string FilePath { get; }
        public TsMapper Mapper { get; }


        public int Version { get; private set; }
        private bool _empty;

        public byte[] Stream { get; private set; }

        private readonly UberFile _file;

        public TsSector(TsMapper mapper, string filePath)
        {
            Mapper = mapper;
            FilePath = filePath;
            _file = UberFileSystem.Instance.GetFile(FilePath);
            if (_file == null)
            {
                _empty = true;
                return;
            }

            Stream = _file.Entry.Read();
        }

        public void Parse()
        {
            Version = BitConverter.ToInt32(Stream, 0x0);

            if (Version < 825)
            {
                Logger.Instance.Error($"{FilePath} version ({Version}) is too low, min. is 825");
                return;
            }

            var itemCount = BitConverter.ToUInt32(Stream, 0x10);
            if (itemCount == 0) _empty = true;
            if (_empty) return;

            var lastOffset = 0x14;

            for (var i = 0; i < itemCount; i++)
            {
                var type = (TsItemType)MemoryHelper.ReadUInt32(Stream, lastOffset);
                if (Version <= 825) type++; // after version 825 all types were pushed up 1

                TsItem.TsItem item = null;

                switch (type)
                {
                    case TsItemType.Terrain: // used to all be in .aux files, not sure why some are now in .base files
                    {
                        item = new TsTerrainItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Building: // used to all be in .aux files, not sure why some are now in .base files
                    {
                        item = new TsBuildingItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        if (item.Valid && !item.Hidden) Mapper.Buildings.Add((TsBuildingItem)item);
                        break;
                    }
                    case TsItemType.Road:
                    {
                        item = new TsRoadItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        if (item.Valid && !item.Hidden) Mapper.Roads.Add((TsRoadItem) item);
                        else if (item.Valid && item.Hidden) Mapper.HiddenRoads.Add((TsRoadItem) item);
                        break;
                    }
                    case TsItemType.Prefab:
                    {
                        item = new TsPrefabItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        if (item.Valid && !item.Hidden) Mapper.Prefabs.Add((TsPrefabItem) item);
                        else if (item.Valid && item.Hidden) Mapper.HiddenPrefabs.Add((TsPrefabItem) item);
                        break;
                    }
                    case TsItemType.Model: // used to all be in .aux files, not sure why some are now in .base files
                    {
                        item = new TsModelItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        if (item.Valid) Mapper.Models.Add((TsModelItem)item);
                        break;
                    }
                    case TsItemType.Compound:
                    {
                        item = new TsCompoundItem(this, lastOffset);
                        if (item.BlockSize <= 0)
                        {
                            Logger.Instance.Warning($"Compound BlockSize=0 in {Path.GetFileName(FilePath)} @ {lastOffset}, aborting sector");
                            return;
                        }
                        lastOffset += item.BlockSize;
                        if (item.Valid)
                        {
                            var compound = (TsCompoundItem)item;
                            Mapper.Models.AddRange(compound.ChildModels);
                            foreach (var node in compound.ChildNodes)
                                if (!Mapper.Nodes.ContainsKey(node.Uid))
                                    Mapper.Nodes.Add(node.Uid, node);
                        }
                        break;
                    }
                    case TsItemType.Company:
                    {
                        item = new TsCompanyItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        if (item.Valid && !item.Hidden) Mapper.Companies.Add((TsCompanyItem)item);
                        break;
                    }
                    case TsItemType.Service:
                    {
                        item = new TsServiceItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.CutPlane:
                    {
                        item = new TsCutPlaneItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.City:
                    {
                        item = new TsCityItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        if (item.Valid && !item.Hidden) Mapper.Cities.Add((TsCityItem) item);
                        break;
                    }
                    case TsItemType.MapOverlay:
                    {
                        item = new TsMapOverlayItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Ferry:
                    {
                        item = new TsFerryItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        if (item.Valid && !item.Hidden) Mapper.FerryConnections.Add((TsFerryItem) item);
                        break;
                    }
                    case TsItemType.Garage:
                    {
                        item = new TsGarageItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Trigger:
                    {
                        item = new TsTriggerItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.FuelPump:
                    {
                        item = new TsFuelPumpItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.RoadSideItem:
                    {
                        item = new TsRoadSideItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.BusStop:
                    {
                        item = new TsBusStopItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        Mapper.BusStops.Add((TsBusStopItem)item);
                        break;
                    }
                    case TsItemType.TrafficRule:
                    {
                        item = new TsTrafficRuleItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.BezierPatch: // used to all be in .aux files, not sure why some are now in .base files
                        {
                            item = new TsBezierPatchItem(this, lastOffset);
                            lastOffset += item.BlockSize;
                            break;
                        }
                    case TsItemType.TrajectoryItem:
                    {
                        item = new TsTrajectoryItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.MapArea:
                    {
                        item = new TsMapAreaItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        if (item.Valid && !item.Hidden) Mapper.MapAreas.Add((TsMapAreaItem) item);
                        break;
                    }
                    case TsItemType.Curve: // used to all be in .aux files, not sure why some are now in .base files
                    {
                        item = new TsCurveItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Cutscene:
                    {
                         item = new TsCutsceneItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Mover:
                    {
                        item = new TsMoverItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.NoWeather:
                    {
                        item = new TsNoWeatherItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Sound:
                    {
                        item = new TsSoundItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.FarModel:
                    {
                        item = new TsFarModelItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.CameraPoint:
                    {
                        item = new TsCameraPointItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Hookup:
                    {
                        item = new TsHookupItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Gate:
                    {
                        item = new TsGateItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    case TsItemType.Hinge:
                    {
                        item = new TsHingeItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }                    case TsItemType.Camera:
                    {
                        item = new TsCameraPathItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }                    case TsItemType.VisibilityArea:
                    {
                        item = new TsVisibilityAreaItem(this, lastOffset);
                        lastOffset += item.BlockSize;
                        break;
                    }
                    default:
                    {
                        Logger.Instance.Warning($"Unknown Type: {type} in {Path.GetFileName(FilePath)} @ {lastOffset}");
                        return;
                    }
                }

                 if (item != null && item.Valid && !item.Hidden) Mapper.MapItems.Add(item);
            }

            var nodeCount = MemoryHelper.ReadInt32(Stream, lastOffset);
            for (var i = 0; i < nodeCount; i++)
            {
                TsNode node = new TsNode(this, lastOffset += 0x04);
                Mapper.UpdateEdgeCoords(node);
                if (!Mapper.Nodes.ContainsKey(node.Uid))
                    Mapper.Nodes.Add(node.Uid, node);
                lastOffset += 0x34;
            }

            lastOffset += 0x04;
            if (Version >= 891)
            {
                var visAreaChildCount = BitConverter.ToInt32(Stream, lastOffset);
                lastOffset += 0x04 + (0x08 * visAreaChildCount); // 0x04(visAreaChildCount) + (visAreaChildUids)
            }
            if (lastOffset != Stream.Length)
            {
                Logger.Instance.Warning($"File '{Path.GetFileName(FilePath)}' from '{GetUberFile().Entry.GetArchiveFile().GetPath()}' was not read correctly. " +
                    $"Read offset was at 0x{lastOffset:X} while file is 0x{Stream.Length:X} bytes long.");
            }
        }

        internal UberFile GetUberFile()
        {
            return _file;
        }

        public void ClearFileData()
        {
            Stream = null;
        }
    }
}
