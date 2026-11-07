// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using SilexGis.Domain.Scans;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// LAS files written by the tests themselves: a header the reader accepts, with zero points, so
/// that nothing here is a real scan.
/// </summary>
internal static class SyntheticLas
{
    /// <summary>
    /// A LAS 1.2 file as the specification lays it out: the 227-byte public header, a GeoTIFF key
    /// directory naming a projected system when asked and an OGC well-known-text record when
    /// asked (either, both or neither, as writers do), then zero point records of format 1 — or,
    /// for a "LAZ", the compression bit and no points at all, since the header is all that is read.
    /// </summary>
    public static byte[] Las(int points, bool compressed = false, (double X, double Y, double Z)? size = null, int? epsg = null, byte major = 1, string? wkt = null)
    {
        const int headerSize = 227;
        const ushort recordLength = 28;
        var records = 0u;
        byte[] vlr = [];
        if (epsg is { } code)
        {
            vlr = GeoKeyDirectory(code);
            records++;
        }

        if (wkt is not null)
        {
            vlr = [.. vlr, .. OgcWkt(wkt)];
            records++;
        }
        // A LAZ's point bytes cannot be counted from the header; a few stand in for them.
        var pointBytes = compressed ? 64 : points * recordLength;
        var file = new byte[headerSize + vlr.Length + pointBytes];
        var span = file.AsSpan();
        "LASF"u8.CopyTo(span);
        span[24] = major;
        span[25] = 2;
        Encoding.ASCII.GetBytes("silexgis tests").CopyTo(span[58..]);
        BinaryPrimitives.WriteUInt16LittleEndian(span[94..], headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[96..], (uint)(headerSize + vlr.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(span[100..], records);
        span[104] = (byte)(1 | (compressed ? LasPointFormats.CompressionBit : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(span[105..], recordLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[107..], (uint)points);
        var (sx, sy, sz) = size ?? (1, 1, 1);
        Doubles(span[131..], 0.01, 0.01, 0.01);
        Doubles(span[155..], 0, 0, 0);
        Doubles(span[179..], sx, 0, sy);
        Doubles(span[203..], 0, sz, 0);
        vlr.CopyTo(span[headerSize..]);
        return file;
    }

    private static byte[] GeoKeyDirectory(int epsg)
    {
        var data = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 3072);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)epsg);

        var record = new byte[LasHeaderReader.VlrHeaderBytes + data.Length];
        Encoding.ASCII.GetBytes(LasHeaderReader.ProjectionUserId).CopyTo(record, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(18), LasHeaderReader.GeoKeyDirectoryRecordId);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(20), (ushort)data.Length);
        data.CopyTo(record, LasHeaderReader.VlrHeaderBytes);
        return record;
    }

    /// <summary>The coordinate system as OGC well-known text, NUL-terminated, under the projection user id.</summary>
    private static byte[] OgcWkt(string wkt)
    {
        var data = new byte[Encoding.UTF8.GetByteCount(wkt) + 1];
        Encoding.UTF8.GetBytes(wkt, data);
        var record = new byte[LasHeaderReader.VlrHeaderBytes + data.Length];
        Encoding.ASCII.GetBytes(LasHeaderReader.ProjectionUserId).CopyTo(record, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(18), LasHeaderReader.OgcWktRecordId);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(20), (ushort)data.Length);
        data.CopyTo(record, LasHeaderReader.VlrHeaderBytes);
        return record;
    }

    private static void Doubles(Span<byte> at, double a, double b, double c)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(at, a);
        BinaryPrimitives.WriteDoubleLittleEndian(at[8..], b);
        BinaryPrimitives.WriteDoubleLittleEndian(at[16..], c);
    }
}
