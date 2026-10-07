using System.Buffers.Binary;
using System.IO.Compression;

namespace HsSteel.Knowledge.Ingest;

/// <summary>Result of normalizing an icon file: the stored image bytes plus its pixel size.</summary>
public sealed record IconImage(int Width, int Height, int BitsPerPixel, string Mime, byte[] Data, bool Normalized);

/// <summary>Pure C# BMP (1/4/8/16/24/32 bpp, uncompressed or bitfields) to PNG converter; PNG input is passed through unchanged.</summary>
public static class IconCodec
{
    private static readonly byte[] PngSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static IconImage Normalize(byte[] file)
    {
        if (file.Length >= 24 && file.AsSpan(0, 8).SequenceEqual(PngSig))
        {
            var w = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(16));
            var h = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(20));
            return new IconImage(w, h, file[24], "image/png", file, false);
        }

        if (file.Length >= 26 && file[0] == (byte)'B' && file[1] == (byte)'M')
        {
            return FromBmp(file);
        }

        throw new InvalidDataException("not a BMP or PNG file");
    }

    private static IconImage FromBmp(byte[] f)
    {
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(10));
        var hdr = BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(14));
        int width, height, bpp, comp = 0, colors = 0;
        var palEntry = 4;
        if (hdr == 12)
        {
            width = BinaryPrimitives.ReadInt16LittleEndian(f.AsSpan(18));
            height = BinaryPrimitives.ReadInt16LittleEndian(f.AsSpan(20));
            bpp = BinaryPrimitives.ReadInt16LittleEndian(f.AsSpan(24));
            palEntry = 3;
        }
        else if (hdr >= 40)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(18));
            height = BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(22));
            bpp = BinaryPrimitives.ReadInt16LittleEndian(f.AsSpan(28));
            comp = BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(30));
            colors = BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(46));
        }
        else
        {
            throw new InvalidDataException($"unsupported BMP header size {hdr}");
        }

        var topDown = height < 0;
        height = Math.Abs(height);
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
        {
            throw new InvalidDataException("bad BMP dimensions");
        }

        if (comp is not (0 or 3))
        {
            throw new InvalidDataException($"unsupported BMP compression {comp}");
        }

        var palette = Array.Empty<(byte R, byte G, byte B)>();
        if (bpp <= 8)
        {
            var n = colors > 0 ? colors : 1 << bpp;
            palette = new (byte, byte, byte)[n];
            var pos = 14 + hdr;
            for (var i = 0; i < n && pos + palEntry <= f.Length; i++, pos += palEntry)
            {
                palette[i] = (f[pos + 2], f[pos + 1], f[pos]);
            }
        }

        uint rMask = 0xFF0000, gMask = 0x00FF00, bMask = 0x0000FF, aMask = 0;
        if (bpp == 16)
        {
            (rMask, gMask, bMask) = (0x7C00, 0x03E0, 0x001F);
        }

        if (comp == 3 && hdr >= 52)
        {
            rMask = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(54));
            gMask = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(58));
            bMask = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(62));
            if (hdr >= 56)
            {
                aMask = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(66));
            }
        }

        var stride = ((width * bpp + 31) / 32) * 4;
        if (dataOffset < 14 + hdr || dataOffset + (long)stride * height > f.Length)
        {
            throw new InvalidDataException("truncated BMP pixel data");
        }

        var rgba = new byte[width * height * 4];
        var anyAlpha = false;
        for (var y = 0; y < height; y++)
        {
            var srcRow = dataOffset + (topDown ? y : height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;
                switch (bpp)
                {
                    case 1:
                    case 4:
                    case 8:
                    {
                        var bit = x * bpp;
                        var idx = (f[srcRow + bit / 8] >> (8 - bpp - bit % 8)) & ((1 << bpp) - 1);
                        (r, g, b) = idx < palette.Length ? palette[idx] : ((byte)0, (byte)0, (byte)0);
                        break;
                    }

                    case 16:
                    {
                        var v = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(srcRow + x * 2));
                        r = Scale(v, rMask);
                        g = Scale(v, gMask);
                        b = Scale(v, bMask);
                        break;
                    }

                    case 24:
                        b = f[srcRow + x * 3];
                        g = f[srcRow + x * 3 + 1];
                        r = f[srcRow + x * 3 + 2];
                        break;
                    case 32:
                    {
                        var v = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(srcRow + x * 4));
                        if (comp == 3)
                        {
                            r = Scale(v, rMask);
                            g = Scale(v, gMask);
                            b = Scale(v, bMask);
                            a = aMask != 0 ? Scale(v, aMask) : (byte)255;
                        }
                        else
                        {
                            b = (byte)v;
                            g = (byte)(v >> 8);
                            r = (byte)(v >> 16);
                            a = (byte)(v >> 24);
                        }

                        anyAlpha |= a != 0;
                        break;
                    }

                    default:
                        throw new InvalidDataException($"unsupported BMP bit depth {bpp}");
                }

                var o = (y * width + x) * 4;
                rgba[o] = r;
                rgba[o + 1] = g;
                rgba[o + 2] = b;
                rgba[o + 3] = a;
            }
        }

        if (bpp == 32 && !anyAlpha)
        {
            for (var i = 3; i < rgba.Length; i += 4)
            {
                rgba[i] = 255; // 32-bit BMPs without a real alpha channel (all zero) are opaque
            }
        }

        return new IconImage(width, height, bpp, "image/png", EncodePng(width, height, rgba), true);
    }

    private static byte Scale(uint v, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        var val = (v & mask) >> shift;
        return (byte)(val * 255 / max);
    }

    /// <summary>Encodes 8-bit RGBA as a PNG with filter 0 and a fixed zlib stream (deterministic for a given runtime).</summary>
    public static byte[] EncodePng(int width, int height, byte[] rgba)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }

        using var ms = new MemoryStream();
        ms.Write(PngSig);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(ms, "IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.SmallestSize, true))
            {
                zs.Write(raw);
            }

            WriteChunk(ms, "IDAT", z.ToArray());
        }

        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = Crc32(t, data);
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }

        return c;
    }).ToArray();

    private static uint Crc32(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a)
        {
            c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        }

        foreach (var x in b)
        {
            c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }
}
