using TsMap.Helpers;

namespace TsMap.TsItem
{
    public class TsMoverItem : TsItem
    {
        public TsMoverItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;

            var offset = startOffset + 0x34; // skip common header

            // flags: 5 bytes
            offset += 0x05;

            // tags: count(4) + entries(8*n)
            var tagsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (tagsCount * 0x08);

            // model(8) + look(8) + variant(8) + speed(4) + endDelay(4) + width(4) + count(4)
            offset += 0x08 + 0x08 + 0x08 + 0x04 + 0x04 + 0x04 + 0x04;

            // lengths: count(4) + entries(4*m)
            var lengthsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (lengthsCount * 0x04);

            // nodeUids: count(4) + entries(8*k)
            var nodeUidsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (nodeUidsCount * 0x08);

            BlockSize = offset - startOffset;
            Valid = false; // not needed for rendering
        }
    }
}
