// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The LAS/LAZ header reader over files built byte by byte here: the layouts of 1.2 and 1.4, the
/// two signs of LAZ compression, the two ways a file states its coordinate system, and the ways a
/// file that is not a point cloud is turned away.
/// </summary>
public sealed class LasHeaderReaderTests
{
    [Fact]
    public void A_1_2_file_of_format_1_is_read_with_its_counts_scale_offset_and_extent()
    {
        var file = new LasFile { Minor = 2, PointFormat = 1, PointCount = 1234 }
            .WithScale(0.001, 0.001, 0.001)
            .WithOffset(100, 200, 300)
            .WithExtent(-5.5, -6.5, -1.25, 5.5, 6.5, 2.75)
            .WithNames("TEST SCANNER", "silexgis tests")
            .WithCreation(dayOfYear: 60, year: 2026)
            .Build();

        var header = LasHeaderReader.Read(file, out var refusal);

        refusal.ShouldBeNull();
        header.ShouldNotBeNull();
        header.Version.ShouldBe("1.2");
        header.HeaderSize.ShouldBe((ushort)227);
        header.OffsetToPointData.ShouldBe((uint)227);
        header.DeclaredVlrCount.ShouldBe(0u);
        header.PointFormat.ShouldBe((byte)1);
        header.PointRecordLength.ShouldBe((ushort)28);
        header.LegacyPointCount.ShouldBe(1234u);
        header.ExtendedPointCount.ShouldBeNull();
        header.PointCount.ShouldBe(1234);
        header.IsCompressed.ShouldBeFalse();
        header.HasGpsTime.ShouldBeTrue();
        header.HasRgb.ShouldBeFalse();
        header.ExtraBytesPerPoint.ShouldBe(0);
        header.Scale.ShouldBe(new LasVector3(0.001, 0.001, 0.001));
        header.Offset.ShouldBe(new LasVector3(100, 200, 300));
        header.Min.ShouldBe(new LasVector3(-5.5, -6.5, -1.25));
        header.Max.ShouldBe(new LasVector3(5.5, 6.5, 2.75));
        header.Size.ShouldBe(new LasVector3(11, 13, 4));
        header.ExtentStated.ShouldBeTrue();
        header.SystemIdentifier.ShouldBe("TEST SCANNER");
        header.GeneratingSoftware.ShouldBe("silexgis tests");
        header.CreatedOn.ShouldBe(new DateOnly(2026, 3, 1));
        header.CoordinateSystem.ShouldBeNull();
        header.Vlrs.ShouldBeEmpty();
    }

    [Fact]
    public void A_1_4_file_of_format_6_takes_its_count_from_the_extended_field()
    {
        // Five thousand million points do not fit the 32-bit count, so the header region is built
        // alone and the file's length is stated: the reader never needs the points themselves.
        const long points = 5_000_000_000;
        var file = new LasFile { Minor = 4, PointFormat = 6, PointCount = points }.WithFileLength(375).Build();

        var header = LasHeaderReader.Read(file, fileLength: 375 + (points * 30), out var refusal);

        refusal.ShouldBeNull();
        header.ShouldNotBeNull();
        header.Version.ShouldBe("1.4");
        header.HeaderSize.ShouldBe((ushort)375);
        header.PointRecordLength.ShouldBe((ushort)30);
        header.LegacyPointCount.ShouldBe(0u);
        header.ExtendedPointCount.ShouldBe(points);
        header.PointCount.ShouldBe(points);
        header.HasRgb.ShouldBeFalse();
        header.HasNir.ShouldBeFalse();
        header.HasGpsTime.ShouldBeTrue();
    }

    [Fact]
    public void An_extent_left_at_zero_by_the_writer_is_read_but_not_taken_as_stated()
    {
        var unfilled = new LasFile { Minor = 2, PointFormat = 3, PointCount = 100 }.WithExtent(0, 0, 0, 0, 0, 0).Build();
        var empty = new LasFile { Minor = 2, PointFormat = 3, PointCount = 0 }.WithExtent(0, 0, 0, 0, 0, 0).Build();

        var unfilledHeader = LasHeaderReader.Read(unfilled, out var refusal);
        var emptyHeader = LasHeaderReader.Read(empty, out _);

        refusal.ShouldBeNull();
        unfilledHeader.ShouldNotBeNull();
        unfilledHeader.ExtentStated.ShouldBeFalse();
        emptyHeader.ShouldNotBeNull();
        emptyHeader.ExtentStated.ShouldBeTrue();
    }

    [Fact]
    public void A_1_4_file_whose_two_counts_disagree_is_inconsistent()
    {
        var file = new LasFile { Minor = 4, PointFormat = 7, PointCount = 10, LegacyPointCount = 11 }.Build();

        LasHeaderReader.Read(file, out var refusal).ShouldBeNull();

        refusal.ShouldNotBeNull();
        refusal.Kind.ShouldBe(LasRefusalKind.Inconsistent);
        refusal.Detail.ShouldContain("point counts");
    }

    [Fact]
    public void A_laz_file_is_known_by_its_compression_bit_and_its_laszip_record()
    {
        var file = new LasFile { Minor = 2, PointFormat = 3, PointCount = 40_000, Compressed = true }
            .WithVlr(LasHeaderReader.LaszipUserId, LasHeaderReader.LaszipRecordId, "laszip", new byte[52])
            .WithFileLength(9_000)
            .Build();

        var header = LasHeaderReader.Read(file, 9_000, out var refusal);

        refusal.ShouldBeNull();
        header.ShouldNotBeNull();
        header.PointFormat.ShouldBe((byte)3);
        header.CompressionBitSet.ShouldBeTrue();
        header.HasLaszipVlr.ShouldBeTrue();
        header.IsCompressed.ShouldBeTrue();
        header.HasRgb.ShouldBeTrue();
        header.Vlrs.Count.ShouldBe(1);
        header.Vlrs[0].UserId.ShouldBe(LasHeaderReader.LaszipUserId);
        header.Vlrs[0].DataLength.ShouldBe(52);
    }

    [Fact]
    public void The_laszip_record_alone_marks_a_file_compressed()
    {
        // The compression bit is the other half of the convention; either half is enough. A plain
        // LAS file with neither is checked for its point bytes and this one would be far too short.
        var file = new LasFile { Minor = 2, PointFormat = 1, PointCount = 40_000 }
            .WithVlr(LasHeaderReader.LaszipUserId, LasHeaderReader.LaszipRecordId, "laszip", new byte[52])
            .WithFileLength(9_000)
            .Build();

        var header = LasHeaderReader.Read(file, 9_000, out var refusal);

        refusal.ShouldBeNull();
        header.ShouldNotBeNull();
        header.CompressionBitSet.ShouldBeFalse();
        header.IsCompressed.ShouldBeTrue();
    }

    [Fact]
    public void A_geotiff_key_directory_names_the_projected_system_over_its_geographic_base()
    {
        var file = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3 }
            .WithGeoKeys((1024, 1), (1025, 1), (2048, 4258), (3072, 3844), (4096, 5781))
            .Build();

        var header = LasHeaderReader.Read(file, out var refusal);

        refusal.ShouldBeNull();
        header.ShouldNotBeNull();
        header.CoordinateSystem.ShouldNotBeNull();
        header.CoordinateSystem.Kind.ShouldBe(LasCrsKind.Projected);
        header.CoordinateSystem.HorizontalEpsg.ShouldBe(3844);
        header.CoordinateSystem.VerticalEpsg.ShouldBe(5781);
        header.CoordinateSystem.Wkt.ShouldBeNull();
        header.CoordinateSystem.IsGeoreferenced.ShouldBeTrue();
    }

    [Fact]
    public void A_geographic_key_directory_and_a_user_defined_one_are_read_as_what_they_say()
    {
        var geographic = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3 }
            .WithGeoKeys((1024, 2), (2048, 4326))
            .Build();
        var userDefined = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3 }
            .WithGeoKeys((1024, 1), (3072, 32767))
            .Build();

        var geographicHeader = LasHeaderReader.Read(geographic, out _);
        var userDefinedHeader = LasHeaderReader.Read(userDefined, out _);

        geographicHeader.ShouldNotBeNull();
        geographicHeader.CoordinateSystem.ShouldNotBeNull();
        geographicHeader.CoordinateSystem.Kind.ShouldBe(LasCrsKind.Geographic);
        geographicHeader.CoordinateSystem.HorizontalEpsg.ShouldBe(4326);

        // Keys that say "user-defined" and carry no WKT describe the system nowhere the reader can
        // see, so the file is a point cloud with keys and no code — not georeferenced, not refused.
        userDefinedHeader.ShouldNotBeNull();
        userDefinedHeader.CoordinateSystem.ShouldNotBeNull();
        userDefinedHeader.CoordinateSystem.Kind.ShouldBe(LasCrsKind.Projected);
        userDefinedHeader.CoordinateSystem.HorizontalEpsg.ShouldBeNull();
        userDefinedHeader.CoordinateSystem.IsGeoreferenced.ShouldBeFalse();
    }

    [Fact]
    public void An_ogc_wkt_record_is_kept_verbatim_up_to_its_terminator()
    {
        const string wkt = "PROJCS[\"Pulkovo 1942(58) / Stereo70\",GEOGCS[\"Pulkovo 1942(58)\"]]";
        var payload = Encoding.ASCII.GetBytes(wkt + "\0padding the writer left");
        var file = new LasFile { Minor = 4, PointFormat = 6, PointCount = 3, GlobalEncoding = 0x10 }
            .WithVlr(LasHeaderReader.ProjectionUserId, LasHeaderReader.OgcWktRecordId, "OGC WKT", payload)
            .Build();

        var header = LasHeaderReader.Read(file, out var refusal);

        refusal.ShouldBeNull();
        header.ShouldNotBeNull();
        header.CoordinateSystem.ShouldNotBeNull();
        header.CoordinateSystem.Wkt.ShouldBe(wkt);
        header.CoordinateSystem.HorizontalEpsg.ShouldBeNull();
        header.CoordinateSystem.Kind.ShouldBe(LasCrsKind.Unknown);
        header.CoordinateSystem.IsGeoreferenced.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(100)]
    [InlineData(226)]
    public void A_file_cut_inside_its_header_is_truncated(int length)
    {
        var whole = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3 }.Build();

        LasHeaderReader.Read(whole.AsSpan(0, length), out var refusal).ShouldBeNull();

        refusal.ShouldNotBeNull();
        refusal.Kind.ShouldBe(LasRefusalKind.Truncated);
    }

    [Fact]
    public void A_file_cut_inside_its_points_or_its_records_is_truncated()
    {
        var points = new LasFile { Minor = 2, PointFormat = 1, PointCount = 100 }.Build();
        var records = new LasFile { Minor = 2, PointFormat = 1, PointCount = 1 }
            .WithVlr("silexgis tests", 1, "extra", new byte[300])
            .Build();

        LasHeaderReader.Read(points.AsSpan(0, points.Length - 1), out var pointsRefusal).ShouldBeNull();
        LasHeaderReader.Read(records.AsSpan(0, 227 + 54 + 10), out var recordsRefusal).ShouldBeNull();

        pointsRefusal.ShouldNotBeNull();
        pointsRefusal.Kind.ShouldBe(LasRefusalKind.Truncated);
        pointsRefusal.Detail.ShouldContain("last point");
        recordsRefusal.ShouldNotBeNull();
        recordsRefusal.Kind.ShouldBe(LasRefusalKind.Truncated);
    }

    [Fact]
    public void A_file_with_another_signature_is_not_las()
    {
        var zip = new byte[400];
        "PK\u0003\u0004"u8.CopyTo(zip);
        var text = Encoding.ASCII.GetBytes("survey main\n  centreline\n  endcentreline\nendsurvey\n".PadRight(400));

        LasHeaderReader.Read(zip, out var zipRefusal).ShouldBeNull();
        LasHeaderReader.Read(text, out var textRefusal).ShouldBeNull();

        zipRefusal.ShouldNotBeNull();
        zipRefusal.Kind.ShouldBe(LasRefusalKind.NotLas);
        textRefusal.ShouldNotBeNull();
        textRefusal.Kind.ShouldBe(LasRefusalKind.NotLas);
    }

    [Fact]
    public void A_version_or_point_format_the_specification_does_not_define_is_unsupported()
    {
        var version = new LasFile { Minor = 5, PointFormat = 1, PointCount = 3 }.Build();
        var format = new LasFile { Minor = 4, PointFormat = 11, PointCount = 3 }.Build();

        LasHeaderReader.Read(version, out var versionRefusal).ShouldBeNull();
        LasHeaderReader.Read(format, out var formatRefusal).ShouldBeNull();

        versionRefusal.ShouldNotBeNull();
        versionRefusal.Kind.ShouldBe(LasRefusalKind.Unsupported);
        formatRefusal.ShouldNotBeNull();
        formatRefusal.Kind.ShouldBe(LasRefusalKind.Unsupported);
    }

    [Fact]
    public void A_header_that_contradicts_itself_is_inconsistent()
    {
        var pointsInsideHeader = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3, OffsetToPointData = 100 }.Build();
        var shortRecords = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3, PointRecordLength = 20 }.Build();
        var zeroScale = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3 }.WithScale(0, 0.01, 0.01).Build();
        var swappedExtent = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3 }.WithExtent(1, 0, 0, 0, 1, 1).Build();
        var recordPastPoints = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3, OffsetToPointData = 227 + 54 + 10 }
            .WithVlr("silexgis tests", 1, "too long", new byte[64])
            .Build();

        foreach (var (file, expected) in new[]
                 {
                     (pointsInsideHeader, "inside the header"),
                     (shortRecords, "at least 28 bytes"),
                     (zeroScale, "positive"),
                     (swappedExtent, "minimum above its maximum"),
                     (recordPastPoints, "runs past"),
                 })
        {
            LasHeaderReader.Read(file, out var refusal).ShouldBeNull();
            refusal.ShouldNotBeNull();
            refusal.Kind.ShouldBe(LasRefusalKind.Inconsistent);
            refusal.Detail.ShouldContain(expected);
        }
    }

    [Fact]
    public void Bytes_needed_says_how_far_to_read_and_the_stream_reader_reads_that_far()
    {
        var file = new LasFile { Minor = 4, PointFormat = 6, PointCount = 10 }
            .WithGeoKeys((1024, 1), (3072, 3844))
            .WithVlr("silexgis tests", 7, "filler", new byte[2_000])
            .Build();

        LasHeaderReader.BytesNeeded(file.AsSpan(0, 50), file.Length).ShouldBe(LasHeaderReader.PublicHeaderBytes);
        var needed = LasHeaderReader.BytesNeeded(file.AsSpan(0, LasHeaderReader.PublicHeaderBytes), file.Length);
        needed.ShouldBe(375 + 54 + 8 + 16 + 54 + 2_000);

        using var stream = new MemoryStream(file);
        var header = LasHeaderReader.Read(stream, out var refusal);

        refusal.ShouldBeNull();
        header.ShouldNotBeNull();
        header.Vlrs.Count.ShouldBe(2);
        header.CoordinateSystem.ShouldNotBeNull();
        header.CoordinateSystem.HorizontalEpsg.ShouldBe(3844);
        LasHeaderReader.Read(file.AsSpan(0, needed), file.Length, out _).ShouldNotBeNull();
    }

    [Fact]
    public void A_1_4_file_whose_coordinate_system_sits_in_an_extended_record_after_the_points_is_placed()
    {
        var keyed = new LasFile { Minor = 4, PointFormat = 6, PointCount = 5 }
            .WithEvlr("silexgis tests", 9, new byte[300])
            .WithGeoKeysInEvlr((1024, 1), (3072, 3844))
            .Build();
        var worded = new LasFile { Minor = 4, PointFormat = 6, PointCount = 5 }
            .WithEvlr(LasHeaderReader.ProjectionUserId, LasHeaderReader.OgcWktRecordId, Encoding.ASCII.GetBytes("PROJCS[\"Pulkovo 1942(58) / Stereo70\"]\0"))
            .Build();

        using var keyedStream = new MemoryStream(keyed);
        var fromStream = LasHeaderReader.Read(keyedStream, out var refusal);
        refusal.ShouldBeNull();
        fromStream.ShouldNotBeNull();
        fromStream.Vlrs.ShouldBeEmpty();
        fromStream.CoordinateSystem.ShouldNotBeNull();
        fromStream.CoordinateSystem.Kind.ShouldBe(LasCrsKind.Projected);
        fromStream.CoordinateSystem.HorizontalEpsg.ShouldBe(3844);
        fromStream.CoordinateSystem.IsGeoreferenced.ShouldBeTrue();

        var fromMemory = LasHeaderReader.Read(keyed, out _);
        fromMemory.ShouldNotBeNull();
        fromMemory.CoordinateSystem.ShouldBe(fromStream.CoordinateSystem);

        var wkt = LasHeaderReader.Read(worded, out _);
        wkt.ShouldNotBeNull();
        wkt.CoordinateSystem.ShouldNotBeNull();
        wkt.CoordinateSystem.HorizontalEpsg.ShouldBeNull();
        wkt.CoordinateSystem.Wkt.ShouldBe("PROJCS[\"Pulkovo 1942(58) / Stereo70\"]");
        wkt.CoordinateSystem.IsGeoreferenced.ShouldBeTrue();
    }

    [Fact]
    public void Extended_records_that_name_no_coordinate_system_or_do_not_add_up_leave_the_file_unplaced()
    {
        var other = new LasFile { Minor = 4, PointFormat = 6, PointCount = 5 }
            .WithEvlr("silexgis tests", 9, new byte[300])
            .Build();
        var header = LasHeaderReader.Read(other, out var refusal);
        refusal.ShouldBeNull();
        header.ShouldNotBeNull();
        header.CoordinateSystem.ShouldBeNull();

        // The table says the records begin past the end of the file: the header is read, the
        // records are not, and the file is a cloud that states no coordinate system.
        var astray = new LasFile { Minor = 4, PointFormat = 6, PointCount = 5 }
            .WithGeoKeysInEvlr((1024, 1), (3072, 3844))
            .Build();
        BinaryPrimitives.WriteUInt64LittleEndian(astray.AsSpan(235), (ulong)astray.Length + 1);
        var unread = LasHeaderReader.Read(astray, out refusal);
        refusal.ShouldBeNull();
        unread.ShouldNotBeNull();
        unread.CoordinateSystem.ShouldBeNull();

        // A record that claims more bytes than the file has left ends the walk the same way.
        var overlong = new LasFile { Minor = 4, PointFormat = 6, PointCount = 5 }
            .WithGeoKeysInEvlr((1024, 1), (3072, 3844))
            .Build();
        var firstRecord = (int)BinaryPrimitives.ReadUInt64LittleEndian(overlong.AsSpan(235));
        BinaryPrimitives.WriteUInt64LittleEndian(overlong.AsSpan(firstRecord + 20), ulong.MaxValue);
        LasHeaderReader.Read(overlong, out _).ShouldNotBeNull().CoordinateSystem.ShouldBeNull();

        // Before 1.4 there are no extended records, whatever the bytes past the points say.
        var older = new LasFile { Minor = 3, PointFormat = 1, PointCount = 5 }.Build();
        LasHeaderReader.Read(older, out _).ShouldNotBeNull().CoordinateSystem.ShouldBeNull();
    }

    [Fact]
    public void Too_few_bytes_of_a_longer_file_is_the_callers_mistake_not_the_files()
    {
        var file = new LasFile { Minor = 2, PointFormat = 1, PointCount = 3 }
            .WithVlr("silexgis tests", 1, "extra", new byte[300])
            .Build();

        Should.Throw<ArgumentException>(() => LasHeaderReader.Read(file.AsSpan(0, 300), file.Length, out _));
    }

    /// <summary>
    /// Writes a LAS file the way the specification lays it out, so that every test states what it
    /// puts in each field rather than carrying a binary fixture whose contents nobody can read.
    /// </summary>
    private sealed class LasFile
    {
        private readonly List<byte[]> vlrs = [];
        private readonly List<byte[]> evlrs = [];
        private double sx = 0.01, sy = 0.01, sz = 0.01;
        private double ox, oy, oz;
        private double minX, minY, minZ, maxX = 1, maxY = 1, maxZ = 1;
        private string system = "", software = "";
        private ushort day, year;
        private long? fileLength;

        public byte Minor { get; init; } = 2;

        public byte PointFormat { get; init; }

        public bool Compressed { get; init; }

        public ushort GlobalEncoding { get; init; }

        public long PointCount { get; init; }

        /// <summary>The 32-bit count; by default the point count when it fits and the format is below 6, as a writer would do.</summary>
        public uint? LegacyPointCount { get; init; }

        public ushort? PointRecordLength { get; init; }

        public uint? OffsetToPointData { get; init; }

        public LasFile WithScale(double x, double y, double z)
        {
            (sx, sy, sz) = (x, y, z);
            return this;
        }

        public LasFile WithOffset(double x, double y, double z)
        {
            (ox, oy, oz) = (x, y, z);
            return this;
        }

        public LasFile WithExtent(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            (minX, minY, minZ, maxX, maxY, maxZ) = (x0, y0, z0, x1, y1, z1);
            return this;
        }

        public LasFile WithNames(string systemIdentifier, string generatingSoftware)
        {
            (system, software) = (systemIdentifier, generatingSoftware);
            return this;
        }

        public LasFile WithCreation(ushort dayOfYear, ushort year)
        {
            (day, this.year) = (dayOfYear, year);
            return this;
        }

        /// <summary>The file's declared whole length, for a LAZ whose point bytes cannot be counted.</summary>
        public LasFile WithFileLength(long length)
        {
            fileLength = length;
            return this;
        }

        public LasFile WithVlr(string userId, ushort recordId, string description, byte[] data)
        {
            var record = new byte[LasHeaderReader.VlrHeaderBytes + data.Length];
            Encoding.ASCII.GetBytes(userId).CopyTo(record, 2);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(18), recordId);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(20), (ushort)data.Length);
            Encoding.ASCII.GetBytes(description).CopyTo(record, 22);
            data.CopyTo(record, LasHeaderReader.VlrHeaderBytes);
            vlrs.Add(record);
            return this;
        }

        /// <summary>An extended record, written after the points as a 1.4 file keeps them.</summary>
        public LasFile WithEvlr(string userId, ushort recordId, byte[] data)
        {
            var record = new byte[LasHeaderReader.EvlrHeaderBytes + data.Length];
            Encoding.ASCII.GetBytes(userId).CopyTo(record, 2);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(18), recordId);
            BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(20), (ulong)data.Length);
            data.CopyTo(record, LasHeaderReader.EvlrHeaderBytes);
            evlrs.Add(record);
            return this;
        }

        /// <summary>A GeoTIFF key directory kept as an extended record, which 1.4 allows.</summary>
        public LasFile WithGeoKeysInEvlr(params (ushort Key, ushort Value)[] keys) =>
            WithEvlr(LasHeaderReader.ProjectionUserId, LasHeaderReader.GeoKeyDirectoryRecordId, GeoKeyDirectory(keys));

        /// <summary>A GeoTIFF key directory of inline keys, as GDAL and LAStools write one.</summary>
        public LasFile WithGeoKeys(params (ushort Key, ushort Value)[] keys) =>
            WithVlr(LasHeaderReader.ProjectionUserId, LasHeaderReader.GeoKeyDirectoryRecordId, "GeoTIFF keys", GeoKeyDirectory(keys));

        private static byte[] GeoKeyDirectory((ushort Key, ushort Value)[] keys)
        {
            var data = new byte[8 + (keys.Length * 8)];
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)keys.Length);
            for (var i = 0; i < keys.Length; i++)
            {
                var entry = data.AsSpan(8 + (i * 8));
                BinaryPrimitives.WriteUInt16LittleEndian(entry, keys[i].Key);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 0);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], keys[i].Value);
            }

            return data;
        }

        /// <summary>
        /// The file: header, records, then as many zero point records as the count says, unless
        /// a whole length was given (a LAZ), in which case that many bytes in all; then, for a
        /// 1.4 file, its extended records, which the header is made to point at.
        /// </summary>
        public byte[] Build()
        {
            var headerSize = Minor switch { <= 2 => 227, 3 => 235, _ => 375 };
            var format = PointFormat;
            var recordLength = PointRecordLength
                ?? (ushort)(format <= LasPointFormats.Highest ? LasPointFormats.MinimumRecordLength(format) : 1);
            var vlrBytes = vlrs.Sum(v => v.Length);
            var offset = OffsetToPointData ?? (uint)(headerSize + vlrBytes);
            var legacy = LegacyPointCount
                ?? (format >= 6 || PointCount > uint.MaxValue ? 0u : (uint)PointCount);
            var pointBytes = fileLength is null && !Compressed ? PointCount * recordLength : 0;
            var evlrBytes = Minor >= 4 ? evlrs.Sum(e => e.Length) : 0;
            var total = (fileLength ?? headerSize + vlrBytes + pointBytes) + evlrBytes;

            var file = new byte[total];
            var span = file.AsSpan();
            "LASF"u8.CopyTo(span);
            BinaryPrimitives.WriteUInt16LittleEndian(span[6..], GlobalEncoding);
            span[24] = 1;
            span[25] = Minor;
            Encoding.ASCII.GetBytes(system).CopyTo(span[26..]);
            Encoding.ASCII.GetBytes(software).CopyTo(span[58..]);
            BinaryPrimitives.WriteUInt16LittleEndian(span[90..], day);
            BinaryPrimitives.WriteUInt16LittleEndian(span[92..], year);
            BinaryPrimitives.WriteUInt16LittleEndian(span[94..], (ushort)headerSize);
            BinaryPrimitives.WriteUInt32LittleEndian(span[96..], offset);
            BinaryPrimitives.WriteUInt32LittleEndian(span[100..], (uint)vlrs.Count);
            span[104] = (byte)(format | (Compressed ? LasPointFormats.CompressionBit : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(span[105..], recordLength);
            BinaryPrimitives.WriteUInt32LittleEndian(span[107..], legacy);
            Write(span, 131, sx, sy, sz);
            Write(span, 155, ox, oy, oz);
            Write(span, 179, maxX, minX, maxY);
            Write(span, 203, minY, maxZ, minZ);
            if (Minor >= 4)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(span[247..], (ulong)PointCount);
                if (evlrs.Count > 0)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(span[235..], (ulong)(total - evlrBytes));
                    BinaryPrimitives.WriteUInt32LittleEndian(span[243..], (uint)evlrs.Count);
                }
            }

            var at = headerSize;
            foreach (var vlr in vlrs)
            {
                vlr.CopyTo(span[at..]);
                at += vlr.Length;
            }

            at = (int)(total - evlrBytes);
            foreach (var evlr in evlrs)
            {
                evlr.CopyTo(span[at..]);
                at += evlr.Length;
            }

            return file;
        }

        private static void Write(Span<byte> span, int at, double a, double b, double c)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(span[at..], a);
            BinaryPrimitives.WriteDoubleLittleEndian(span[(at + 8)..], b);
            BinaryPrimitives.WriteDoubleLittleEndian(span[(at + 16)..], c);
        }
    }
}
