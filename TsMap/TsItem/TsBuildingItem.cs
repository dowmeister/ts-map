using System.IO;
using TsMap.Helpers;
using TsMap.Helpers.Logger;

namespace TsMap.TsItem
{
    public class TsBuildingItem : TsItem
    {
        public ulong SchemeToken { get; private set; }
        public ulong[] NodeUids { get; private set; }
        public float[] HeightValues { get; private set; }

        public TsBuildingItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = true;

            if (Sector.Version >= 858)
                TsBuildingItem858(startOffset);
            else
                Logger.Instance.Error($"Unknown base file version ({Sector.Version}) for item {Type} " +
                                      $"in file '{Path.GetFileName(Sector.FilePath)}' @ {startOffset} from '{Sector.GetUberFile().Entry.GetArchiveFile().GetPath()}'");
        }

        public void TsBuildingItem858(int startOffset)
        {
            var fileOffset = startOffset + 0x34; // Set position at start of flags

            // Binary layout (aux-building-template.bt):
            // +0x00: m_flags[5]
            // +0x05: m_scheme_id (u64)
            // +0x0D: m_look_id (u64)
            // +0x15: m_uid[0] (u64)
            // +0x1D: m_uid[1] (u64)
            // +0x25: dunno (float)
            // +0x29: m_seed (u32)
            // +0x2D: m_stretch (float)
            // +0x31: m_some_count (u32) = buildingHeightOffsetCount
            // +0x35: m_some_value[m_some_count] (float[])

            SchemeToken = MemoryHelper.ReadUInt64(Sector.Stream, fileOffset + 0x05);
            NodeUids = new ulong[]
            {
                MemoryHelper.ReadUInt64(Sector.Stream, fileOffset + 0x15),
                MemoryHelper.ReadUInt64(Sector.Stream, fileOffset + 0x1D)
            };

            var heightOffsetCount = MemoryHelper.ReadInt32(Sector.Stream, fileOffset += 0x05 + 0x2C);
            HeightValues = new float[heightOffsetCount];
            for (int i = 0; i < heightOffsetCount; i++)
                HeightValues[i] = MemoryHelper.ReadSingle(Sector.Stream, fileOffset + 0x04 + i * 0x04);

            fileOffset += 0x04 + 0x04 * heightOffsetCount;
            BlockSize = fileOffset - startOffset;
        }
    }
}
