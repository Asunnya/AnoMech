using System;
using System.Buffers.Binary;

namespace AnoMech.Core.Game;

// Obfuscation scheme and table as in vgmstream's sqex_scd.c.
internal static class ScdOgg
{
    private const uint CodecOggVorbis = 0x06;

    // V3Table comes from vgmstream's src/meta/sqex_scd.c, under vgmstream's notice:
    //
    // Copyright (c) 2008-2025 Adam Gashlin, Fastelbja, Ronny Elfert, bnnm,
    //                         Christopher Snowhill, NicknineTheEagle, bxaimc,
    //                         Thealexbarney, CyberBotX, EdnessP, et al
    //
    // Portions Copyright (c) 2004-2008, Marko Kreen
    // Portions Copyright 2001-2007  jagarl / Kazunori Ueno <jagarl@creator.club.ne.jp>
    // Portions Copyright (c) 1998, Justin Frankel/Nullsoft Inc.
    // Portions Copyright (C) 2006 Nullsoft, Inc.
    // Portions Copyright (c) 2005-2007 Paul Hsieh
    // Portions Copyright (C) 2000-2004 Leshade Entis, Entis-soft.
    // Portions Public Domain originating with Sun Microsystems
    //
    // Permission to use, copy, modify, and distribute this software for any
    // purpose with or without fee is hereby granted, provided that the above
    // copyright notice and this permission notice appear in all copies.
    //
    // THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
    // WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    // MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
    // ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    // WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
    // ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
    // OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
    private static readonly byte[] V3Table =
    [
        0x3A, 0x32, 0x32, 0x32, 0x03, 0x7E, 0x12, 0xF7, 0xB2, 0xE2, 0xA2, 0x67, 0x32, 0x32, 0x22, 0x32,
        0x32, 0x52, 0x16, 0x1B, 0x3C, 0xA1, 0x54, 0x7B, 0x1B, 0x97, 0xA6, 0x93, 0x1A, 0x4B, 0xAA, 0xA6,
        0x7A, 0x7B, 0x1B, 0x97, 0xA6, 0xF7, 0x02, 0xBB, 0xAA, 0xA6, 0xBB, 0xF7, 0x2A, 0x51, 0xBE, 0x03,
        0xF4, 0x2A, 0x51, 0xBE, 0x03, 0xF4, 0x2A, 0x51, 0xBE, 0x12, 0x06, 0x56, 0x27, 0x32, 0x32, 0x36,
        0x32, 0xB2, 0x1A, 0x3B, 0xBC, 0x91, 0xD4, 0x7B, 0x58, 0xFC, 0x0B, 0x55, 0x2A, 0x15, 0xBC, 0x40,
        0x92, 0x0B, 0x5B, 0x7C, 0x0A, 0x95, 0x12, 0x35, 0xB8, 0x63, 0xD2, 0x0B, 0x3B, 0xF0, 0xC7, 0x14,
        0x51, 0x5C, 0x94, 0x86, 0x94, 0x59, 0x5C, 0xFC, 0x1B, 0x17, 0x3A, 0x3F, 0x6B, 0x37, 0x32, 0x32,
        0x30, 0x32, 0x72, 0x7A, 0x13, 0xB7, 0x26, 0x60, 0x7A, 0x13, 0xB7, 0x26, 0x50, 0xBA, 0x13, 0xB4,
        0x2A, 0x50, 0xBA, 0x13, 0xB5, 0x2E, 0x40, 0xFA, 0x13, 0x95, 0xAE, 0x40, 0x38, 0x18, 0x9A, 0x92,
        0xB0, 0x38, 0x00, 0xFA, 0x12, 0xB1, 0x7E, 0x00, 0xDB, 0x96, 0xA1, 0x7C, 0x08, 0xDB, 0x9A, 0x91,
        0xBC, 0x08, 0xD8, 0x1A, 0x86, 0xE2, 0x70, 0x39, 0x1F, 0x86, 0xE0, 0x78, 0x7E, 0x03, 0xE7, 0x64,
        0x51, 0x9C, 0x8F, 0x34, 0x6F, 0x4E, 0x41, 0xFC, 0x0B, 0xD5, 0xAE, 0x41, 0xFC, 0x0B, 0xD5, 0xAE,
        0x41, 0xFC, 0x3B, 0x70, 0x71, 0x64, 0x33, 0x32, 0x12, 0x32, 0x32, 0x36, 0x70, 0x34, 0x2B, 0x56,
        0x22, 0x70, 0x3A, 0x13, 0xB7, 0x26, 0x60, 0xBA, 0x1B, 0x94, 0xAA, 0x40, 0x38, 0x00, 0xFA, 0xB2,
        0xE2, 0xA2, 0x67, 0x32, 0x32, 0x12, 0x32, 0xB2, 0x32, 0x32, 0x32, 0x32, 0x75, 0xA3, 0x26, 0x7B,
        0x83, 0x26, 0xF9, 0x83, 0x2E, 0xFF, 0xE3, 0x16, 0x7D, 0xC0, 0x1E, 0x63, 0x21, 0x07, 0xE3, 0x01,
    ];

    public static byte[]? Extract(byte[] scd, out string error)
    {
        error = "";
        try
        {
            if (scd.Length < 0x40 || !scd.AsSpan(0, 8).SequenceEqual("SEDBSSCF"u8)) { error = "not an SCD"; return null; }
            var tables = BinaryPrimitives.ReadUInt16LittleEndian(scd.AsSpan(0x0E));
            var headers = (int)U32(scd, tables + 0x0C);
            var entry = (int)U32(scd, headers);
            var streamSize = (int)U32(scd, entry);
            var codec = U32(scd, entry + 0x0C);
            if (codec != CodecOggVorbis) { error = $"codec 0x{codec:X} is not Ogg Vorbis"; return null; }
            var auxCount = U32(scd, entry + 0x1C);
            var extra = entry + 0x20;
            for (var i = 0; i < auxCount; i++)
            {
                var size = (int)U32(scd, extra + 4);
                if (size <= 0) break;
                extra += size;
            }
            var version = scd[extra];
            var xorByte = scd[extra + 2];
            if (version is not (2 or 3)) { error = $"SCD Ogg version {version}"; return null; }
            var seekTableSize = (int)U32(scd, extra + 0x10);
            var vorbisHeaderSize = (int)U32(scd, extra + 0x14);
            var start = extra + 0x20 + seekTableSize;
            var length = vorbisHeaderSize + streamSize;
            if (start <= 0 || length <= 0 || start + length > scd.Length) { error = "stream out of range"; return null; }

            var ogg = scd.AsSpan(start, length).ToArray();
            if (version == 2)
            {
                if (xorByte != 0)
                    for (var i = 0; i < vorbisHeaderSize; i++) ogg[i] ^= xorByte;
            }
            else
            {
                var key = streamSize & 0xFF;
                var mask = (byte)(key & 0x7F);
                var shift = key & 0x3F;
                for (var i = 0; i < ogg.Length; i++) ogg[i] = (byte)(V3Table[(shift + i) & 0xFF] ^ ogg[i] ^ mask);
            }
            if (!ogg.AsSpan(0, 4).SequenceEqual("OggS"u8)) { error = "decrypted stream is not Ogg"; return null; }
            return ogg;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            error = "truncated SCD";
            return null;
        }
    }

    // The first sound entry's own mix level, which the game applies on top of the BGM settings.
    public static float SoundVolume(byte[] scd)
    {
        try
        {
            var tables = BinaryPrimitives.ReadUInt16LittleEndian(scd.AsSpan(0x0E));
            if (BinaryPrimitives.ReadInt16LittleEndian(scd.AsSpan(tables)) < 1) return 1f;
            var sound = (int)U32(scd, tables + 0x20);
            var volume = BinaryPrimitives.ReadSingleLittleEndian(scd.AsSpan(sound + 8, 4));
            return float.IsFinite(volume) && volume >= 0f ? volume : 1f;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return 1f;
        }
    }

    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
