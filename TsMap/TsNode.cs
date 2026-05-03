using System;
using TsMap.Helpers;

namespace TsMap
{
    public class TsNode
    {
        public ulong Uid { get; }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Rotation { get; }

        // UIDs of adjacent items, read from node binary at +0x24/+0x2C
        public ulong BackwardItemUID { get; private set; }
        public ulong ForwardItemUID  { get; private set; }

        // Resolved in TsMapper.Parse() after all items are loaded
        public TsItem.TsItem ForwardItem  { get; set; }
        public TsItem.TsItem BackwardItem { get; set; }

        public TsNode(TsSector sector, int fileOffset)
        {
            Uid = MemoryHelper.ReadUInt64(sector.Stream, fileOffset);

            // Adjacent item UIDs stored in the node binary block
            BackwardItemUID = BitConverter.ToUInt64(sector.Stream, fileOffset + 0x24);
            ForwardItemUID  = BitConverter.ToUInt64(sector.Stream, fileOffset + 0x2C);

            X = MemoryHelper.ReadInt32(sector.Stream, fileOffset += 0x08) / 256f;
            Y = MemoryHelper.ReadInt32(sector.Stream, fileOffset += 0x04) / 256f;
            Z = MemoryHelper.ReadInt32(sector.Stream, fileOffset += 0x04) / 256f;

            var rX = MemoryHelper.ReadSingle(sector.Stream, fileOffset += 0x04);
            var rZ = MemoryHelper.ReadSingle(sector.Stream, fileOffset + 0x08);

            var rot = Math.PI - Math.Atan2(rZ, rX);
            Rotation = (float)(rot % Math.PI * 2);
        }
    }
}
