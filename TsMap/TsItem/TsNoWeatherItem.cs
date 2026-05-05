namespace TsMap.TsItem
{
    /// <summary>
    /// NoWeather item (type 11). Skip-only; not needed for rendering.
    /// Layout: common header (0x34) + flags(5) + width(4) + height(4) + fogMaskPresetId(4) + unknown(16) + nodeUid(8)
    /// The 16 unknown bytes are always present in current game versions (v901+).
    /// </summary>
    public class TsNoWeatherItem : TsItem
    {
        public TsNoWeatherItem(TsSector sector, int startOffset) : base(sector, startOffset)
        {
            Valid = false;
            // flags(5)+width(4)+height(4)+fogMaskPresetId(4)+unknown(16)+nodeUid(8) = 41 = 0x29
            BlockSize = 0x34 + 0x29;
        }
    }
}
