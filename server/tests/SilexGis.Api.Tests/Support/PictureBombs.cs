// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Pictures that are tiny on disk and enormous once decoded, written byte by byte so the test
/// never holds the pixels it declares.
/// </summary>
internal static class PictureBombs
{
    /// <summary>
    /// A valid one-bit greyscale picture of the given size, every pixel black. Written by hand
    /// because an imaging library asked for twenty thousand pixels a side would first hold them;
    /// a blank picture compresses to almost nothing, which is exactly what makes it a bomb.
    /// </summary>
    public static byte[] BlankPng(int width, int height)
    {
        using var raw = new MemoryStream();
        using (var deflate = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[1 + ((width + 7) / 8)]; // filter byte, then the packed bits
            for (var y = 0; y < height; y++)
            {
                deflate.Write(row);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 1; // bit depth; colour type 0 (grey), and the rest default
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        png.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
