using System.Collections.Generic;
using System.IO;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap.TsItem
{
    /// <summary>
    /// Compound item — container for embedded child SimpleItems and their nodes.
    ///
    /// Binary layout after the k-DOP item header (itemStart + 0x39):
    ///   +0x00  rootNodeUid     uint64
    ///   +0x08  childItemCount  uint32
    ///   +0x0C  childItems[]    (full SimpleItem format: type+kdop+payload)
    ///          childNodeCount  uint32
    ///          childNodes[]    (0x34 bytes each)
    /// </summary>
    public class TsCompoundItem : TsItem
    {
        public List<TsModelItem> ChildModels { get; } = new List<TsModelItem>();
        public List<TsNode> ChildNodes { get; } = new List<TsNode>();

        public TsCompoundItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            ParseCompound(startOffset);
        }

        private void ParseCompound(int startOffset)
        {
            // k-DOP item header: type(4)+uid(8)+kdop_bbox(40)+flags(4)+viewDist(1) = 0x39
            var offset = startOffset + 0x39;

            offset += 0x08; // skip rootNodeUid

            if (offset + 4 > Sector.Stream.Length) return;
            var childItemCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04;

            for (var i = 0; i < childItemCount; i++)
            {
                if (offset + 4 > Sector.Stream.Length) return;

                var childType = (TsItemType)MemoryHelper.ReadUInt32(Sector.Stream, offset);
                var childItem = CreateChildItem(childType, offset);

                if (childItem == null)
                {
                    Logger.Instance.Warning(
                        $"Compound: unknown child type {childType} ({(int)childType}) @ 0x{offset:X} " +
                        $"in {Path.GetFileName(Sector.FilePath)}, aborting");
                    return;
                }

                if (childItem.BlockSize <= 0)
                {
                    Logger.Instance.Warning(
                        $"Compound: child {childType} BlockSize=0 @ 0x{offset:X} " +
                        $"in {Path.GetFileName(Sector.FilePath)}, aborting");
                    return;
                }

                if (childType == TsItemType.Model && childItem.Valid)
                    ChildModels.Add((TsModelItem)childItem);

                offset += childItem.BlockSize;
            }

            if (offset + 4 > Sector.Stream.Length) return;
            var childNodeCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04;

            for (var i = 0; i < childNodeCount; i++)
            {
                if (offset + 0x38 > Sector.Stream.Length) return;
                ChildNodes.Add(new TsNode(Sector, offset));
                offset += 0x38; // SectorNode is 0x38 bytes: 0x34 data + 0x04 flags (not read by TsNode)
            }

            BlockSize = offset - startOffset;
            Valid = true;
        }

        private TsItem CreateChildItem(TsItemType type, int offset)
        {
            switch (type)
            {
                case TsItemType.Terrain:        return new TsTerrainItem(Sector, offset);
                case TsItemType.Building:       return new TsBuildingItem(Sector, offset);
                case TsItemType.Road:           return new TsRoadItem(Sector, offset);
                case TsItemType.Prefab:         return new TsPrefabItem(Sector, offset);
                case TsItemType.Model:          return new TsModelItem(Sector, offset);
                case TsItemType.Company:        return new TsCompanyItem(Sector, offset);
                case TsItemType.Service:        return new TsServiceItem(Sector, offset);
                case TsItemType.CutPlane:       return new TsCutPlaneItem(Sector, offset);
                case TsItemType.City:           return new TsCityItem(Sector, offset);
                case TsItemType.MapOverlay:     return new TsMapOverlayItem(Sector, offset);
                case TsItemType.Ferry:          return new TsFerryItem(Sector, offset);
                case TsItemType.Garage:         return new TsGarageItem(Sector, offset);
                case TsItemType.Trigger:        return new TsTriggerItem(Sector, offset);
                case TsItemType.FuelPump:       return new TsFuelPumpItem(Sector, offset);
                case TsItemType.RoadSideItem:   return new TsRoadSideItem(Sector, offset);
                case TsItemType.BusStop:        return new TsBusStopItem(Sector, offset);
                case TsItemType.TrafficRule:    return new TsTrafficRuleItem(Sector, offset);
                case TsItemType.BezierPatch:    return new TsBezierPatchItem(Sector, offset);
                case TsItemType.TrajectoryItem: return new TsTrajectoryItem(Sector, offset);
                case TsItemType.MapArea:        return new TsMapAreaItem(Sector, offset);
                case TsItemType.Curve:          return new TsCurveItem(Sector, offset);
                case TsItemType.Cutscene:       return new TsCutsceneItem(Sector, offset);
                case TsItemType.VisibilityArea: return new TsVisibilityAreaItem(Sector, offset);
                case TsItemType.Mover:          return new TsMoverItem(Sector, offset);
                case TsItemType.NoWeather:      return new TsNoWeatherItem(Sector, offset);
                case TsItemType.Sound:          return new TsSoundItem(Sector, offset);
                case TsItemType.FarModel:       return new TsFarModelItem(Sector, offset);
                case TsItemType.CameraPoint:    return new TsCameraPointItem(Sector, offset);
                case TsItemType.Hookup:         return new TsHookupItem(Sector, offset);
                case TsItemType.Gate:           return new TsGateItem(Sector, offset);
                case TsItemType.Hinge:          return new TsHingeItem(Sector, offset);
                case TsItemType.Camera:         return new TsCameraPathItem(Sector, offset);
                default:                        return null;
            }
        }
    }
}
