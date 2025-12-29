using System.IO;
using System.Collections.Generic;
using TsMap.Common;
using TsMap.Helpers;
using TsMap.Helpers.Logger;
using TsMap.Map.Overlays;
using System;

namespace TsMap.TsItem
{
    public class TsTriggerItem : TsItem
    {
        private bool _isSecret;

        public float Range { get; private set; }
        public float ResetDelay { get; private set; }
        public float ResetDistance { get; private set; }
        public float MinSpeed { get; private set; }
        public float MaxSpeed { get; private set; }
        public ulong[] TriggerActions { get; private set; }
        public TsTriggerType TriggerType { get; private set; }

        public TsTriggerItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = true;
            if (Sector.Version < 829)
                TsTriggerItem825(startOffset);
            else if (Sector.Version >= 829 && Sector.Version < 875)
                TsTriggerItem829(startOffset);
            else if (Sector.Version >= 875)
                TsTriggerItem875(startOffset);
            else
                Logger.Instance.Error($"Unknown base file version ({Sector.Version}) for item {Type} " +
                    $"in file '{Path.GetFileName(Sector.FilePath)}' @ {startOffset} from '{Sector.GetUberFile().Entry.GetArchiveFile().GetPath()}'");
        }

        private void ParseTriggerActions(List<ulong> actions)
        {
            TriggerActions = actions.ToArray();

            // Determine trigger type based on actions
            foreach (var action in actions)
            {
                //Console.WriteLine(ScsToken.TokenToString(action));

                if (action == ScsToken.StringToToken("hud_parking"))
                {
                    TriggerType = TsTriggerType.Parking;
                    Sector.Mapper.OverlayManager.AddOverlay("parking_ico", OverlayType.Map, X, Z, "Parking", DlcGuard, _isSecret);
                    AddServiceToList("Parking");
                }
                else if (action == ScsToken.StringToToken("hud_speed_limit"))
                {
                    TriggerType = TsTriggerType.SpeedCamera;
                    Sector.Mapper.OverlayManager.AddOverlay("speed_camera", OverlayType.Map, X, Z, "Speed Camera", DlcGuard, _isSecret);
                    AddServiceToList("Speed Camera");
                }
                else if (action == ScsToken.StringToToken("hud_toll_gate"))
                {
                    TriggerType = TsTriggerType.TollGate;
                    Sector.Mapper.OverlayManager.AddOverlay("toll_gate", OverlayType.Map, X, Z, "Toll Gate", DlcGuard, _isSecret);
                    AddServiceToList("Toll Gate");
                }
                else if (action == ScsToken.StringToToken("hud_weight_station"))
                {
                    TriggerType = TsTriggerType.WeightStation;
                    Sector.Mapper.OverlayManager.AddOverlay("weigh_station_ico", OverlayType.Map, X, Z, "Weight Station", DlcGuard, _isSecret);
                    AddServiceToList("Weight Station");
                }
                // Add more trigger types as discovered
            }
        }

        private void AddServiceToList(string serviceType)
        {
            var service = new TsServiceDef
            {
                X = X,
                Y = Z,
                Type = serviceType,
                City = Sector.Mapper.FindCityInGameId(X, Z),
                DlcGuard = DlcGuard,
                IsSecret = _isSecret
            };

            if (serviceType == "Speed Camera")
            {
                service.Properties["MinSpeed"] = MinSpeed;
                service.Properties["MaxSpeed"] = MaxSpeed;
                service.Properties["Range"] = Range;
            }

            Sector.Mapper.Services.Add(service);
        }

        public void TsTriggerItem825(int startOffset)
        {
            var fileOffset = startOffset + 0x34; // Set position at start of flags
            DlcGuard = MemoryHelper.ReadUint8(Sector.Stream, fileOffset + 0x01);
            var nodeCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x05); // 0x05(flags)
            var tagCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x04 + (0x08 * nodeCount)); // 0x04(nodeCount) + nodeUids
            var triggerActionCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x04 + (0x08 * tagCount)); // 0x04(tagCount) + tags
            fileOffset += 0x04; // cursor after triggerActionCount

            var actions = new List<ulong>();
            for (var i = 0; i < triggerActionCount; i++)
            {
                var action = MemoryHelper.ReadUInt64(Sector.Stream, fileOffset);
                actions.Add(action);
                
                var hasParameters = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x08); // 0x08(action)
                fileOffset += 0x04; // set cursor after hasParameters
                if (hasParameters == 1)
                {
                    var parametersLength = MemoryHelper.ReadInt32(Sector.Stream, fileOffset);
                    fileOffset += 0x04 + 0x04 + parametersLength; // 0x04(parametersLength) + 0x04(padding) + text(parametersLength * 0x01)
                }
                else if (hasParameters == 3) fileOffset += 0x08; // 0x08 (m_some_uid)

                var targetTagCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset);
                fileOffset += 0x04 + targetTagCount * 0x08; // 0x04(targetTagCount) + targetTags
            }

            // Parse speed and range data FIRST
            Range = MemoryHelper.ReadSingle(Sector.Stream, fileOffset);
            ResetDelay = MemoryHelper.ReadSingle(Sector.Stream, fileOffset += 0x04);
            ResetDistance = MemoryHelper.ReadSingle(Sector.Stream, fileOffset += 0x04);
            MinSpeed = MemoryHelper.ReadSingle(Sector.Stream, fileOffset += 0x04);
            MaxSpeed = MemoryHelper.ReadSingle(Sector.Stream, fileOffset += 0x04);
            fileOffset += 0x04 + 0x04; // flags2 + padding

            // Now parse actions with speed data available
            ParseTriggerActions(actions);
        }
        public void TsTriggerItem829(int startOffset)
        {
            var fileOffset = startOffset + 0x34; // Set position at start of flags
            DlcGuard = MemoryHelper.ReadUint8(Sector.Stream, fileOffset + 0x01);
            var tagCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x05); // 0x05(flags)
            var nodeCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x04 + (0x08 * tagCount)); // 0x04(nodeCount) + tags

            var triggerActionCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x04 + (0x08 * nodeCount)); // 0x04(nodeCount) + nodeUids
            fileOffset += 0x04; // cursor after triggerActionCount

            var actions = new List<ulong>();
            for (var i = 0; i < triggerActionCount; i++)
            {
                var action = MemoryHelper.ReadUInt64(Sector.Stream, fileOffset);
                actions.Add(action);

                var hasOverride = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x08); // 0x08(action)
                if (hasOverride > 0) fileOffset += 0x04 * hasOverride;

                var hasParameters = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x04); // 0x04(hasOverride)
                fileOffset += 0x04; // set cursor after hasParameters
                if (hasParameters == 1)
                {
                    var parametersLength = MemoryHelper.ReadInt32(Sector.Stream, fileOffset);
                    fileOffset += 0x04 + 0x04 + parametersLength; // 0x04(parametersLength) + 0x04(padding) + text(parametersLength * 0x01)
                }
                else if (hasParameters == 3) fileOffset += 0x08; // 0x08 (m_some_uid)

                var targetTagCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x08); // 0x08(unk/padding)
                fileOffset += 0x04 + targetTagCount * 0x08; // 0x04(targetTagCount) + targetTags
            }

            ParseTriggerActions(actions);

            BlockSize = fileOffset - startOffset;
        }

        public void TsTriggerItem875(int startOffset)
        {
            var fileOffset = startOffset + 0x34; // Set position at start of flags
            DlcGuard = MemoryHelper.ReadUint8(Sector.Stream, fileOffset + 0x01);
            _isSecret = MemoryHelper.IsBitSet(MemoryHelper.ReadUint8(Sector.Stream, fileOffset + 0x02), 2);
            var tagCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x05); // 0x05(flags)
            var nodeCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x04 + (0x08 * tagCount)); // 0x04(nodeCount) + tags

            var triggerActionCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x04 + (0x08 * nodeCount)); // 0x04(nodeCount) + nodeUids
            fileOffset += 0x04; // cursor after triggerActionCount

            var actions = new List<ulong>();
            for (var i = 0; i < triggerActionCount; i++)
            {
                var action = MemoryHelper.ReadUInt64(Sector.Stream, fileOffset);
                actions.Add(action);

                var hasOverride = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x08); // 0x08(action)
                fileOffset += 0x04; // set cursor after hasOverride
                if (hasOverride < 0) continue;
                fileOffset += 0x04 * hasOverride; // set cursor after override values

                var parameterCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset);
                fileOffset += 0x04; // set cursor after parameterCount

                for (var j = 0; j < parameterCount; j++)
                {
                    var paramLength = MemoryHelper.ReadInt32(Sector.Stream, fileOffset);
                    fileOffset += 0x04 + 0x04 + paramLength; // 0x04(paramLength) + 0x04(padding) + (param)
                }
                var targetTagCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset);

                fileOffset += 0x04 + targetTagCount * 0x08 + 0x08; // 0x04(targetTagCount) + targetTags + 0x04(m_range & m_type)
            }

            // Read range data BEFORE parsing actions
            if (nodeCount == 1)
            {
                Range = MemoryHelper.ReadSingle(Sector.Stream, fileOffset);
                fileOffset += 0x04; // 0x04(m_radius)
            }

            // Now parse actions with range data available
            ParseTriggerActions(actions);

            BlockSize = fileOffset - startOffset;
        }
    }
}
