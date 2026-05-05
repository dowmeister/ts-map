using TsMap.Helpers;

namespace TsMap.TsItem
{
    // ── FarModel (type 43) ──────────────────────────────────────────────────
    // layout: flags(5) + width(4) + height(4)
    //         + models[count(4) + n*(token(8)+float3(12))]
    //         + childUids[count(4) + n*8]
    //         + nodeUids[count(4) + n*8]
    public class TsFarModelItem : TsItem
    {
        public TsFarModelItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            var offset = startOffset + 0x39; // header + flags(5)
            offset += 0x08; // width + height
            var modelsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (modelsCount * 0x14); // token(8)+float3(12)=20
            var childUidsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (childUidsCount * 0x08);
            var nodeUidsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (nodeUidsCount * 0x08);
            BlockSize = offset - startOffset;
        }
    }

    // ── CameraPoint (type 23) ───────────────────────────────────────────────
    // layout: flags(5) + tags[count(4) + n*8] + nodeUid(8)
    public class TsCameraPointItem : TsItem
    {
        public TsCameraPointItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            var offset = startOffset + 0x39;
            var tagsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (tagsCount * 0x08);
            offset += 0x08; // nodeUid
            BlockSize = offset - startOffset;
        }
    }

    // ── Hookup (type 47) ────────────────────────────────────────────────────
    // layout: flags(5) + name(uint64String) + nodeUid(8)
    // uint64String binary format: low_uint32(4) + high_uint32(4, padding) + string_data(low_uint32 bytes)
    public class TsHookupItem : TsItem
    {
        public TsHookupItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            var offset = startOffset + 0x39;
            var nameLen = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + 0x04 + nameLen; // low_len + high_len(padding) + name data
            offset += 0x08;                  // nodeUid
            BlockSize = offset - startOffset;
        }
    }

    // ── Gate (type 49) ──────────────────────────────────────────────────────
    // layout: flags(5) + model(8) + nodeUids[count(4)+n*8]
    //         + activationPointUnits[2 * (triggerUnitName(uint64String) + triggerNodeIndex(4))]
    // uint64String binary format: low_uint32(4) + high_uint32(4, padding) + string_data(low_uint32 bytes)
    public class TsGateItem : TsItem
    {
        public TsGateItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            var offset = startOffset + 0x39;
            offset += 0x08; // model token64
            var nodeUidsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (nodeUidsCount * 0x08);
            // activationPointUnits: exactly 2 entries (not a counted array)
            for (var k = 0; k < 2; k++)
            {
                var nameLen = MemoryHelper.ReadInt32(Sector.Stream, offset);
                offset += 0x04 + 0x04 + nameLen; // low_len + high_len(padding) + name data
                offset += 0x04;                  // triggerNodeIndex (int32)
            }
            BlockSize = offset - startOffset;
        }
    }

    // ── Hinge (type 13) ─────────────────────────────────────────────────────
    // layout: flags(5) + token(8) + look(8) + nodeUid(8) + minRot(4) + maxRot(4)
    //         = 5+8+8+8+4+4 = 37 = 0x25
    public class TsHingeItem : TsItem
    {
        public TsHingeItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            BlockSize = 0x34 + 0x25;
        }
    }

    // ── Sound (type 21) ─────────────────────────────────────────────────────
    // layout: flags(5) + sound_id(8) + full_intensity_distance(4)
    //         + activation_distance(4) + extra_flags(4) + node_uid(8) = 33 = 0x21
    public class TsSoundItem : TsItem
    {
        public TsSoundItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            BlockSize = 0x34 + 0x21;
        }
    }

    // ── CameraPath (type 45 = Camera) ────────────────────────────────────────
    // layout: flags(5)
    //         + tags[count+n*8]
    //         + nodeUids[count+n*8]
    //         + trackPointNodeUids[count+n*8]
    //         + curveControlNodeUids[count+n*8]
    //         + keyFrames[count+n*40]  (speedChange+rotationChange+speedCoef+fov+backTangent3+fwdTangent3)
    //         + speed(4)
    public class TsCameraPathItem : TsItem
    {
        public TsCameraPathItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            var offset = startOffset + 0x34 + 0x05; // past flags

            var tagsCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (tagsCount * 0x08);

            var nodeCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (nodeCount * 0x08);

            var tpCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (tpCount * 0x08);

            var ccCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (ccCount * 0x08);

            // keyFrames: speedChange(4)+rotationChange(4)+speedCoef(4)+fov(4)+backTangent(12)+fwdTangent(12) = 40 = 0x28
            var kfCount = MemoryHelper.ReadInt32(Sector.Stream, offset);
            offset += 0x04 + (kfCount * 0x28);

            offset += 0x04; // speed
            BlockSize = offset - startOffset;
        }
    }
}
