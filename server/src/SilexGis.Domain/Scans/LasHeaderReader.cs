// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;

namespace SilexGis.Domain.Scans;

/// <summary>
/// Reads the public header and the variable-length records of a LAS or LAZ file, versions 1.0 to
/// 1.4, from the file's first bytes, and refuses anything that is not one in words rather than in
/// an exception.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written against the LAS specification rather than taken from a library, because the whole
/// of what the server needs from the file — how many points, in what frame, with what attributes,
/// compressed or not — sits in a fixed-layout header of at most 375 bytes and a handful of records
/// after it, all little-endian and all stored in clear even in a LAZ file. A point-cloud library
/// would bring a decompressor along for a job that never reads a point.
/// </para>
/// <para>
/// The reader works on a span the caller has already read: <see cref="BytesNeeded"/> says how much
/// of the file that is once the first bytes are in hand, and <see cref="Read(Stream, out LasRefusal?)"/>
/// does the two reads for a seekable stream. Nothing here reads past the point data's start, so a
/// multi-gigabyte scan costs the same to inspect as a tiny one.
/// </para>
/// <para>
/// Every check that fails is answered with a <see cref="LasRefusal"/> and a null header, never with
/// an exception, so that a pipeline stage can record the reason on the upload and stop. Exceptions
/// are kept for one thing only: a caller that hands the reader fewer bytes than the file has and
/// than the header needs, which is a programming error and not a property of the file.
/// </para>
/// </remarks>
public static class LasHeaderReader
{
    /// <summary>The four bytes every LAS file begins with.</summary>
    public static ReadOnlySpan<byte> Signature => "LASF"u8;

    /// <summary>The public header of a 1.0 to 1.2 file, and the least any version's can be.</summary>
    public const int MinimumHeaderBytes = 227;

    /// <summary>The public header of a 1.4 file, the largest the specification defines.</summary>
    public const int PublicHeaderBytes = 375;

    /// <summary>The fixed header of each variable-length record, before its payload.</summary>
    public const int VlrHeaderBytes = 54;

    /// <summary>The fixed header of each extended variable-length record of a 1.4 file, before its payload.</summary>
    public const int EvlrHeaderBytes = 60;

    /// <summary>
    /// The most extended records the reader walks looking for a coordinate system. A 1.4 file
    /// keeps its extended records after the points, where a writer may put as many as it likes;
    /// the coordinate system is among the first few when it is there at all.
    /// </summary>
    public const int MaxEvlrsWalked = 4096;

    /// <summary>
    /// The most of a file the reader will take in before the points: the header and every record
    /// before the point data. Real files use a few kilobytes; a writer that puts megabytes there is
    /// one this reader does not follow, and says so rather than reading on without bound.
    /// </summary>
    public const int MaxHeaderRegionBytes = 8 * 1024 * 1024;

    /// <summary>The writer identifier of the records the specification itself defines for coordinate systems.</summary>
    public const string ProjectionUserId = "LASF_Projection";

    /// <summary>The record number of the GeoTIFF key directory under <see cref="ProjectionUserId"/>.</summary>
    public const ushort GeoKeyDirectoryRecordId = 34735;

    /// <summary>The record number of the OGC coordinate-system WKT under <see cref="ProjectionUserId"/>.</summary>
    public const ushort OgcWktRecordId = 2112;

    /// <summary>The writer identifier and record number LASzip uses to announce its codec.</summary>
    public const string LaszipUserId = "laszip encoded";

    /// <inheritdoc cref="LaszipUserId"/>
    public const ushort LaszipRecordId = 22204;

    /// <summary>The GeoTIFF key value that means "defined by the user elsewhere", so not a code.</summary>
    private const ushort UserDefinedKeyValue = 32767;

    /// <summary>Where the fields sit in the public header; the specification's byte offsets.</summary>
    private static class At
    {
        public const int GlobalEncoding = 6;
        public const int VersionMajor = 24;
        public const int VersionMinor = 25;
        public const int SystemIdentifier = 26;
        public const int GeneratingSoftware = 58;
        public const int CreationDay = 90;
        public const int CreationYear = 92;
        public const int HeaderSize = 94;
        public const int OffsetToPointData = 96;
        public const int VlrCount = 100;
        public const int PointFormat = 104;
        public const int PointRecordLength = 105;
        public const int LegacyPointCount = 107;
        public const int Scale = 131;
        public const int Offset = 155;
        public const int MaxX = 179;
        public const int StartOfFirstEvlr = 235;
        public const int EvlrCount = 243;
        public const int ExtendedPointCount = 247;
        public const int NameLength = 32;
    }

    /// <summary>Reads bytes at a position of the file into a buffer, answering false when the file does not reach that far.</summary>
    private delegate bool ReadAt(long position, Span<byte> into);

    /// <summary>GeoTIFF key numbers this reader understands.</summary>
    private static class GeoKey
    {
        public const ushort ModelType = 1024;
        public const ushort GeographicType = 2048;
        public const ushort ProjectedCsType = 3072;
        public const ushort VerticalCsType = 4096;
    }

    /// <summary>
    /// How many bytes from the file's start <see cref="Read(ReadOnlySpan{byte}, long, out LasRefusal?)"/>
    /// wants, once at least the fixed header's first hundred bytes are in hand: everything up to
    /// the point data, bounded by the file's length and by <see cref="MaxHeaderRegionBytes"/>. With
    /// fewer bytes than that, the answer is the largest public header, which is enough to ask again.
    /// </summary>
    public static int BytesNeeded(ReadOnlySpan<byte> firstBytes, long fileLength)
    {
        if (firstBytes.Length < At.OffsetToPointData + sizeof(uint))
        {
            return (int)Math.Min(PublicHeaderBytes, fileLength);
        }

        long offset = BinaryPrimitives.ReadUInt32LittleEndian(firstBytes[At.OffsetToPointData..]);
        var wanted = Math.Max(offset, PublicHeaderBytes);

        return (int)Math.Min(Math.Min(wanted, fileLength), MaxHeaderRegionBytes);
    }

    /// <summary>
    /// Reads a whole file held in memory: the header and the records before the points, and,
    /// for a 1.4 file that states its coordinate system only in an extended record after the
    /// points, that record too.
    /// </summary>
    public static LasHeader? Read(ReadOnlySpan<byte> wholeFile, out LasRefusal? refusal)
    {
        var header = Read(wholeFile, wholeFile.Length, out refusal);
        if (header is null || !MayHaveCoordinateSystemAfterThePoints(header))
        {
            return header;
        }

        // The walk reads at positions the extended records name, which a span cannot be held
        // across the calls that do it; the copy is made only on this path, for a file that is
        // in memory already.
        var file = wholeFile.ToArray();
        return WithCoordinateSystemFromEvlrs(header, file, file.LongLength, (position, into) =>
        {
            if (position < 0 || position + into.Length > file.LongLength)
            {
                return false;
            }

            file.AsSpan((int)position, into.Length).CopyTo(into);
            return true;
        });
    }

    /// <summary>
    /// Reads a seekable stream from its start: the fixed header first, then as much more as the
    /// header says the records before the points take, and, for a 1.4 file that states its
    /// coordinate system only in an extended record after the points, that record too.
    /// </summary>
    public static LasHeader? Read(Stream stream, out LasRefusal? refusal)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var length = stream.Length;
        stream.Position = 0;

        var first = new byte[(int)Math.Min(PublicHeaderBytes, length)];
        stream.ReadExactly(first);

        var needed = BytesNeeded(first, length);
        var region = first;
        if (needed > first.Length)
        {
            region = new byte[needed];
            first.CopyTo(region, 0);
            stream.ReadExactly(region, first.Length, needed - first.Length);
        }

        var header = Read(region, length, out refusal);
        if (header is null || !MayHaveCoordinateSystemAfterThePoints(header))
        {
            return header;
        }

        return WithCoordinateSystemFromEvlrs(header, region, length, (position, into) =>
        {
            if (position < 0 || position + into.Length > length)
            {
                return false;
            }

            stream.Position = position;
            stream.ReadExactly(into);
            return true;
        });
    }

    /// <summary>
    /// Whether the coordinate system, not found among the records before the points, could still
    /// be in an extended record after them: a 1.4 file is allowed to keep it there, and nothing
    /// before 1.4 has extended records at all.
    /// </summary>
    private static bool MayHaveCoordinateSystemAfterThePoints(LasHeader header) =>
        header.VersionMinor >= 4 && header.CoordinateSystem is null;

    /// <summary>
    /// The header with the coordinate system an extended record states, or as it was when none
    /// does. The extended records sit where the public header says, each a fixed header and a
    /// payload; only a coordinate-system record's payload is read, the others are stepped over
    /// by their declared length. A table that does not add up — a start past the file, a record
    /// running off its end — ends the walk and leaves the header as it was: the coordinate
    /// system is a fact about the cloud, not a condition of accepting it.
    /// </summary>
    private static LasHeader WithCoordinateSystemFromEvlrs(LasHeader header, ReadOnlySpan<byte> publicHeader, long fileLength, ReadAt read)
    {
        var start = BinaryPrimitives.ReadUInt64LittleEndian(publicHeader[At.StartOfFirstEvlr..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(publicHeader[At.EvlrCount..]);
        if (count == 0 || start == 0 || start > long.MaxValue || (long)start >= fileLength)
        {
            return header;
        }

        var keys = (LasCoordinateSystem?)null;
        var wkt = (string?)null;
        var sawProjection = false;
        Span<byte> head = stackalloc byte[EvlrHeaderBytes];
        var position = (long)start;
        for (var i = 0; i < count && i < MaxEvlrsWalked; i++)
        {
            if (position > fileLength - EvlrHeaderBytes || !read(position, head))
            {
                break;
            }

            var userId = Text(head.Slice(2, 16));
            var recordId = BinaryPrimitives.ReadUInt16LittleEndian(head[18..]);
            var length = BinaryPrimitives.ReadUInt64LittleEndian(head[20..]);
            var dataStart = position + EvlrHeaderBytes;
            if (length > (ulong)(fileLength - dataStart))
            {
                break;
            }

            if (string.Equals(userId, ProjectionUserId, StringComparison.Ordinal)
                && recordId is GeoKeyDirectoryRecordId or OgcWktRecordId
                && length <= MaxHeaderRegionBytes)
            {
                var data = new byte[(int)length];
                if (!read(dataStart, data))
                {
                    break;
                }

                sawProjection = true;
                if (recordId == GeoKeyDirectoryRecordId)
                {
                    keys = ReadGeoKeys(data);
                }
                else
                {
                    wkt = Wkt(data);
                }
            }

            position = dataStart + (long)length;
        }

        return sawProjection
            ? header with
            {
                CoordinateSystem = new LasCoordinateSystem(keys?.Kind ?? LasCrsKind.Unknown, keys?.HorizontalEpsg, keys?.VerticalEpsg, wkt),
            }
            : header;
    }

    /// <summary>
    /// Reads the header and the records before the points from the file's first bytes.
    /// </summary>
    /// <param name="bytes">
    /// The file's first bytes: at least <see cref="BytesNeeded"/> of them, or the whole file when it
    /// is shorter than that.
    /// </param>
    /// <param name="fileLength">The file's whole length, which the header's claims are checked against.</param>
    /// <param name="refusal">Why the file was not read, when the answer is null.</param>
    /// <returns>The header, or null with <paramref name="refusal"/> set.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="bytes"/> is longer than <paramref name="fileLength"/>, or shorter than the
    /// header needs while the file is not.
    /// </exception>
    public static LasHeader? Read(ReadOnlySpan<byte> bytes, long fileLength, out LasRefusal? refusal)
    {
        if (fileLength < bytes.Length)
        {
            throw new ArgumentException("The file cannot be shorter than the bytes read from it.", nameof(fileLength));
        }

        if (!Have(bytes, fileLength, Signature.Length, out refusal))
        {
            return null;
        }

        if (!bytes[..Signature.Length].SequenceEqual(Signature))
        {
            refusal = new LasRefusal(LasRefusalKind.NotLas, "The file does not begin with the LAS signature.");
            return null;
        }

        if (!Have(bytes, fileLength, At.VersionMinor + 1, out refusal))
        {
            return null;
        }

        var major = bytes[At.VersionMajor];
        var minor = bytes[At.VersionMinor];
        if (major != 1 || minor > 4)
        {
            refusal = new LasRefusal(LasRefusalKind.Unsupported, $"LAS version {major}.{minor} is not one this server reads; 1.0 to 1.4 are.");
            return null;
        }

        var minimumHeader = MinimumHeaderFor(minor);
        if (!Have(bytes, fileLength, minimumHeader, out refusal))
        {
            return null;
        }

        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes[At.HeaderSize..]);
        if (headerSize < minimumHeader)
        {
            refusal = new LasRefusal(LasRefusalKind.Inconsistent, $"The header says it is {headerSize} bytes, but a {major}.{minor} header is at least {minimumHeader}.");
            return null;
        }

        var offsetToPoints = BinaryPrimitives.ReadUInt32LittleEndian(bytes[At.OffsetToPointData..]);
        if (offsetToPoints < headerSize)
        {
            refusal = new LasRefusal(LasRefusalKind.Inconsistent, "The header says the point data begins inside the header itself.");
            return null;
        }

        if (offsetToPoints > fileLength)
        {
            refusal = new LasRefusal(LasRefusalKind.Truncated, "The file ends before its point data begins.");
            return null;
        }

        var formatByte = bytes[At.PointFormat];
        var compressionBit = (formatByte & (LasPointFormats.CompressionBit | LasPointFormats.LegacyCompressionBit)) != 0;
        var format = (byte)(formatByte & ~(LasPointFormats.CompressionBit | LasPointFormats.LegacyCompressionBit));
        if (!LasPointFormats.IsDefined(format))
        {
            refusal = new LasRefusal(LasRefusalKind.Unsupported, $"Point data record format {format} is not one the LAS specification defines.");
            return null;
        }

        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[At.PointRecordLength..]);
        var minimumRecord = LasPointFormats.MinimumRecordLength(format);
        if (recordLength < minimumRecord)
        {
            refusal = new LasRefusal(LasRefusalKind.Inconsistent, $"A point record of format {format} is at least {minimumRecord} bytes; the header says {recordLength}.");
            return null;
        }

        var legacyCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[At.LegacyPointCount..]);
        long? extendedCount = null;
        if (minor >= 4)
        {
            var extended = BinaryPrimitives.ReadUInt64LittleEndian(bytes[At.ExtendedPointCount..]);
            if (extended > long.MaxValue)
            {
                refusal = new LasRefusal(LasRefusalKind.Inconsistent, "The header's point count is beyond anything a file could hold.");
                return null;
            }

            extendedCount = (long)extended;
            if (legacyCount != 0 && extendedCount != 0 && legacyCount != extendedCount)
            {
                refusal = new LasRefusal(LasRefusalKind.Inconsistent, "The header's two point counts disagree.");
                return null;
            }
        }

        var scale = Vector(bytes, At.Scale);
        var offset = Vector(bytes, At.Offset);
        var max = new LasVector3(Double(bytes, At.MaxX), Double(bytes, At.MaxX + 16), Double(bytes, At.MaxX + 32));
        var min = new LasVector3(Double(bytes, At.MaxX + 8), Double(bytes, At.MaxX + 24), Double(bytes, At.MaxX + 40));

        if (!IsFinite(scale) || !IsFinite(offset) || !IsFinite(min) || !IsFinite(max))
        {
            refusal = new LasRefusal(LasRefusalKind.Inconsistent, "The header's scale, offset or extent holds a value that is not a number.");
            return null;
        }

        if (scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
        {
            refusal = new LasRefusal(LasRefusalKind.Inconsistent, "The header's scale factors have to be positive.");
            return null;
        }

        var pointCount = extendedCount is > 0 ? extendedCount.Value : legacyCount;
        if (pointCount > 0 && (min.X > max.X || min.Y > max.Y || min.Z > max.Z))
        {
            refusal = new LasRefusal(LasRefusalKind.Inconsistent, "The header's extent has a minimum above its maximum.");
            return null;
        }

        var declaredVlrs = BinaryPrimitives.ReadUInt32LittleEndian(bytes[At.VlrCount..]);
        var vlrs = ReadVlrs(bytes, fileLength, headerSize, offsetToPoints, declaredVlrs, out var crs, out var laszip, out refusal);
        if (vlrs is null)
        {
            return null;
        }

        if (!compressionBit && !laszip)
        {
            // Only a plain LAS file can be measured this way: LAZ holds the same count in fewer
            // bytes. Extended records of a 1.4 file sit after the points, so the file may be
            // longer than this, never shorter.
            var pointBytes = pointCount * recordLength;
            if (pointBytes > fileLength - offsetToPoints)
            {
                refusal = new LasRefusal(LasRefusalKind.Truncated, "The file ends before its last point record.");
                return null;
            }
        }

        refusal = null;
        return new LasHeader(
            major,
            minor,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[At.GlobalEncoding..]),
            Text(bytes.Slice(At.SystemIdentifier, At.NameLength)),
            Text(bytes.Slice(At.GeneratingSoftware, At.NameLength)),
            CreatedOn(
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[At.CreationDay..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[At.CreationYear..])),
            headerSize,
            offsetToPoints,
            declaredVlrs,
            format,
            compressionBit,
            recordLength,
            legacyCount,
            extendedCount,
            scale,
            offset,
            min,
            max,
            vlrs,
            crs,
            laszip);
    }

    /// <summary>
    /// Walks the records between the header and the points, keeping the list and reading the ones
    /// that describe the coordinate system or the codec.
    /// </summary>
    /// <returns>The records, or null with a refusal when they do not fit where the header puts them.</returns>
    private static List<LasVariableLengthRecord>? ReadVlrs(
        ReadOnlySpan<byte> bytes,
        long fileLength,
        ushort headerSize,
        uint offsetToPoints,
        uint declared,
        out LasCoordinateSystem? crs,
        out bool laszip,
        out LasRefusal? refusal)
    {
        var records = new List<LasVariableLengthRecord>();
        var keys = (LasCoordinateSystem?)null;
        var wkt = (string?)null;
        var sawProjection = false;
        laszip = false;
        crs = null;

        long position = headerSize;
        for (var i = 0; i < declared; i++)
        {
            if (position + VlrHeaderBytes > offsetToPoints)
            {
                refusal = new LasRefusal(LasRefusalKind.Inconsistent, $"The header declares {declared} variable-length records, but the point data begins before record {i + 1}.");
                return null;
            }

            if (!Have(bytes, fileLength, position + VlrHeaderBytes, out refusal))
            {
                return null;
            }

            var head = bytes.Slice((int)position, VlrHeaderBytes);
            var userId = Text(head.Slice(2, 16));
            var recordId = BinaryPrimitives.ReadUInt16LittleEndian(head[18..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(head[20..]);
            var description = Text(head.Slice(22, 32));

            var dataStart = position + VlrHeaderBytes;
            if (dataStart + length > offsetToPoints)
            {
                refusal = new LasRefusal(LasRefusalKind.Inconsistent, $"Variable-length record {i + 1} ({userId} {recordId}) runs past the start of the point data.");
                return null;
            }

            if (!Have(bytes, fileLength, dataStart + length, out refusal))
            {
                return null;
            }

            var data = bytes.Slice((int)dataStart, length);
            records.Add(new LasVariableLengthRecord(userId, recordId, description, length));

            if (string.Equals(userId, ProjectionUserId, StringComparison.Ordinal))
            {
                if (recordId == GeoKeyDirectoryRecordId)
                {
                    sawProjection = true;
                    keys = ReadGeoKeys(data);
                }
                else if (recordId == OgcWktRecordId)
                {
                    sawProjection = true;
                    wkt = Wkt(data);
                }
            }
            else if (recordId == LaszipRecordId && string.Equals(userId, LaszipUserId, StringComparison.Ordinal))
            {
                laszip = true;
            }

            position = dataStart + length;
        }

        if (sawProjection)
        {
            crs = new LasCoordinateSystem(
                keys?.Kind ?? LasCrsKind.Unknown,
                keys?.HorizontalEpsg,
                keys?.VerticalEpsg,
                wkt);
        }

        refusal = null;
        return records;
    }

    /// <summary>
    /// Reads the GeoTIFF key directory: a four-short header, then four shorts a key. Only keys whose
    /// value sits in the directory itself are read; the others point into records this reader does
    /// not follow, and nothing the server needs lives there. A directory that does not add up is
    /// read as naming nothing rather than refused: the coordinate system is a fact about the cloud,
    /// not a condition of accepting it.
    /// </summary>
    private static LasCoordinateSystem ReadGeoKeys(ReadOnlySpan<byte> data)
    {
        var kind = LasCrsKind.Unknown;
        int? horizontal = null;
        int? vertical = null;

        if (data.Length >= 8)
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            if (8 + (count * 8) <= data.Length)
            {
                for (var k = 0; k < count; k++)
                {
                    var entry = data.Slice(8 + (k * 8), 8);
                    var keyId = BinaryPrimitives.ReadUInt16LittleEndian(entry);
                    var location = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
                    var value = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
                    if (location != 0)
                    {
                        continue;
                    }

                    switch (keyId)
                    {
                        case GeoKey.ModelType:
                            kind = value switch
                            {
                                1 => LasCrsKind.Projected,
                                2 => LasCrsKind.Geographic,
                                3 => LasCrsKind.Geocentric,
                                _ => LasCrsKind.Unknown,
                            };
                            break;
                        case GeoKey.ProjectedCsType:
                            horizontal = Code(value) ?? horizontal;
                            if (kind == LasCrsKind.Unknown && horizontal is not null)
                            {
                                kind = LasCrsKind.Projected;
                            }

                            break;
                        case GeoKey.GeographicType:
                            // A projected system names its base geographic one too; the projected
                            // code is the one that places the points.
                            if (horizontal is null)
                            {
                                horizontal = Code(value);
                                if (kind == LasCrsKind.Unknown && horizontal is not null)
                                {
                                    kind = LasCrsKind.Geographic;
                                }
                            }

                            break;
                        case GeoKey.VerticalCsType:
                            vertical = Code(value);
                            break;
                    }
                }
            }
        }

        return new LasCoordinateSystem(kind, horizontal, vertical, null);
    }

    /// <summary>A key value as an EPSG code, or null for "none" and "user-defined".</summary>
    private static int? Code(ushort value) => value is 0 or UserDefinedKeyValue ? null : value;

    /// <summary>The WKT payload: text up to its terminating NUL, or null when there is none.</summary>
    private static string? Wkt(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        var text = Encoding.UTF8.GetString(end < 0 ? data : data[..end]).Trim();

        return text.Length == 0 ? null : text;
    }

    /// <summary>A fixed-width, NUL-padded text field of the header, as the specification defines them.</summary>
    private static string Text(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);

        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]).Trim();
    }

    private static DateOnly? CreatedOn(ushort dayOfYear, ushort year)
    {
        if (year is < 1 or > 9999 || dayOfYear < 1)
        {
            return null;
        }

        var daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;

        return dayOfYear <= daysInYear ? new DateOnly(year, 1, 1).AddDays(dayOfYear - 1) : null;
    }

    private static int MinimumHeaderFor(byte minor) => minor switch
    {
        <= 2 => MinimumHeaderBytes,
        3 => 235,
        _ => PublicHeaderBytes,
    };

    /// <summary>
    /// Whether the span reaches a given byte count. When it does not: a file that is itself that
    /// short is refused as truncated; a region past the bound is refused as unsupported; and a
    /// caller that read too little of a longer file is told so, because that is a bug, not a file.
    /// </summary>
    private static bool Have(ReadOnlySpan<byte> bytes, long fileLength, long needed, out LasRefusal? refusal)
    {
        if (needed <= bytes.Length)
        {
            refusal = null;
            return true;
        }

        if (fileLength < needed)
        {
            refusal = new LasRefusal(LasRefusalKind.Truncated, "The file ends before its header and the records after it do.");
            return false;
        }

        if (needed > MaxHeaderRegionBytes)
        {
            refusal = new LasRefusal(LasRefusalKind.Unsupported, $"The records before the point data take more than {MaxHeaderRegionBytes / (1024 * 1024)} MiB, which this server does not read.");
            return false;
        }

        throw new ArgumentException($"The reader needs the file's first {needed} bytes and was given {bytes.Length}.", nameof(bytes));
    }

    private static LasVector3 Vector(ReadOnlySpan<byte> bytes, int at) =>
        new(Double(bytes, at), Double(bytes, at + 8), Double(bytes, at + 16));

    private static double Double(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadDoubleLittleEndian(bytes[at..]);

    private static bool IsFinite(LasVector3 v) =>
        double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}
