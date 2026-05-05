using System.IO;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap.TsItem
{
    public class TsModelItem : TsItem
    {
        public ulong ModelToken { get; private set; }
        public ulong NodeUid { get; private set; }
        public float ScaleX { get; private set; }
        public float ScaleY { get; private set; }
        public float ScaleZ { get; private set; }

        public TsModelItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;

            if (Sector.Version >= 895)
                TsModelItem895(startOffset);
            else
                Logger.Instance.Error($"Unknown base file version ({Sector.Version}) for item {Type} " +
                    $"in file '{Path.GetFileName(Sector.FilePath)}' @ {startOffset} from '{Sector.GetUberFile().Entry.GetArchiveFile().GetPath()}'");
        }

        public void TsModelItem895(int startOffset)
        {
            var flagsOffset = startOffset + 0x34; // position at start of flags

            // +0x05 from flags: m_model_id (u64)
            ModelToken = MemoryHelper.ReadUInt64(Sector.Stream, flagsOffset + 0x05);

            // +0x1D from flags: m_additional_parts_count (u32)
            var addPartsCount = MemoryHelper.ReadInt32(Sector.Stream, flagsOffset + 0x1D);

            // after flags(5) + model(8) + look(8) + variant(8) + count(4) + parts(8*n): node_uid
            var nodeOffset = flagsOffset + 0x21 + (0x08 * addPartsCount);
            NodeUid = MemoryHelper.ReadUInt64(Sector.Stream, nodeOffset);

            // scale[3] immediately after node_uid
            ScaleX = MemoryHelper.ReadSingle(Sector.Stream, nodeOffset + 0x08);
            ScaleY = MemoryHelper.ReadSingle(Sector.Stream, nodeOffset + 0x0C);
            ScaleZ = MemoryHelper.ReadSingle(Sector.Stream, nodeOffset + 0x10);

            // node(8) + scale(12) + terrain_mat(8) + terrain_color(4) + terrain_rot(4) = 0x24
            var fileOffset = nodeOffset + 0x08 + 0x0C + 0x10;
            BlockSize = fileOffset - startOffset;

            Valid = true;
        }
    }
}
