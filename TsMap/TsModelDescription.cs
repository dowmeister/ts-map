using System;
using TsMap.Helpers.Logger;

namespace TsMap
{
    /// <summary>
    /// Bounding-box data extracted from a .pmg file header.
    /// Mirrors the ModelDescription type in truckermudgeon-maps.
    /// X/Z are the game-world horizontal axes (Y is vertical).
    /// </summary>
    public class TsModelDescription
    {
        /// <summary>Center of the bounding box, local space (X/Z).</summary>
        public float CenterX { get; }
        public float CenterZ { get; }

        /// <summary>Min corner of the bounding box, local space (X/Z).</summary>
        public float StartX { get; }
        public float StartZ { get; }

        /// <summary>Max corner of the bounding box, local space (X/Z).</summary>
        public float EndX { get; }
        public float EndZ { get; }

        /// <summary>Vertical extent of the model (bboxEnd.Y - bboxStart.Y).</summary>
        public float Height { get; }

        private TsModelDescription(float cx, float cz, float sx, float sz, float ex, float ez, float h)
        {
            CenterX = cx;
            CenterZ = cz;
            StartX  = sx;
            StartZ  = sz;
            EndX    = ex;
            EndZ    = ez;
            Height  = h;
        }

        /// <summary>
        /// Parse the header of a .pmg file and return a TsModelDescription.
        /// Returns null if the file is invalid or too short.
        ///
        /// .pmg header layout (all little-endian):
        ///   [0]      version  (uint8)  — must be 21
        ///   [1..3]   magic    (3 chars) — "gmP" (reversed "Pmg")
        ///   [4..7]   numPieces (uint32)
        ///   [8..11]  numParts  (uint32)
        ///   [12..15] numBones  (uint32)
        ///   [16..19] weightWidth (uint32)
        ///   [20..23] numLocators (uint32)
        ///   [24..31] skeletonHash (uint64)
        ///   [32..43] bboxCenter (float3 = 3 × float32)
        ///   [44..47] bboxDiagonal (float32)
        ///   [48..59] bboxStart (float3)
        ///   [60..71] bboxEnd   (float3)
        /// </summary>
        public static TsModelDescription ParsePmgHeader(byte[] data)
        {
            const int MinHeaderSize = 72;
            if (data == null || data.Length < MinHeaderSize)
                return null;

            var version = data[0];
            if (version != 21)
            {
                Logger.Instance.Debug($"TsModelDescription: unknown .pmg version {version}, skipping");
                return null;
            }

            // magic bytes [1..3] should be 'g','m','P' (reversed "Pmg")
            if (data[1] != 'g' || data[2] != 'm' || data[3] != 'P')
            {
                Logger.Instance.Debug("TsModelDescription: invalid .pmg magic, skipping");
                return null;
            }

            // bboxCenter: [32..43] — X, Y (vertical, skipped), Z
            var cx = BitConverter.ToSingle(data, 32);
            var cz = BitConverter.ToSingle(data, 40);

            // bboxStart: [48..59] — X, Y, Z
            var sx = BitConverter.ToSingle(data, 48);
            var startY = BitConverter.ToSingle(data, 52);
            var sz = BitConverter.ToSingle(data, 56);

            // bboxEnd: [60..71] — X, Y, Z
            var ex = BitConverter.ToSingle(data, 60);
            var endY = BitConverter.ToSingle(data, 64);
            var ez = BitConverter.ToSingle(data, 68);

            var height = endY - startY;

            return new TsModelDescription(cx, cz, sx, sz, ex, ez, height);
        }
    }
}
