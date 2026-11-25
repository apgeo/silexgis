// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.IO.Compression;
using Shouldly;
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading an archive's entry count from its own records, so that an archive of millions of
/// entries is refused before the runtime builds an object for each of them. Every archive here
/// is generated: the hostile ones are a few hundred bytes that state an enormous directory.
/// </summary>
public sealed class ZipEntryCountTests
{
    [Fact]
    public void An_ordinary_archive_is_counted_exactly_and_the_stream_is_left_where_it_was()
    {
        using var archive = new MemoryStream(Archive(3));
        archive.Position = 7;

        ZipEntryCount.Exceeds(archive, 3).ShouldBeFalse();
        ZipEntryCount.Exceeds(archive, 2).ShouldBeTrue();
        archive.Position.ShouldBe(7);
    }

    [Fact]
    public void A_zip64_end_record_stating_ten_million_entries_is_refused_from_a_few_bytes()
    {
        var bytes = Zip64EndRecordsStating(10_000_000);
        bytes.Length.ShouldBeLessThan(200);

        ZipEntryCount.Exceeds(new MemoryStream(bytes), 5000).ShouldBeTrue();
    }

    [Fact]
    public void An_archive_stating_fewer_entries_than_its_directory_holds_is_counted_by_its_records()
    {
        // The runtime reads directory records for as long as their signature matches and only
        // then compares with the stated total, so the stated figure alone is not a bound.
        var bytes = Archive(10);
        var end = EndRecordOffset(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(end + 8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(end + 10), 1);

        ZipEntryCount.Exceeds(new MemoryStream(bytes), 5).ShouldBeTrue();
        ZipEntryCount.Exceeds(new MemoryStream(bytes), 10).ShouldBeFalse();
    }

    [Fact]
    public void Something_that_is_not_an_archive_is_left_for_the_archive_reader_to_refuse()
    {
        ZipEntryCount.Exceeds(new MemoryStream("not an archive at all"u8.ToArray()), 0).ShouldBeFalse();
        ZipEntryCount.Exceeds(new MemoryStream([]), 0).ShouldBeFalse();
        ZipEntryCount.Count(new MemoryStream("PK\u0005\u0006"u8.ToArray()), 10).ShouldBeNull();
    }

    [Fact]
    public void A_stream_that_cannot_seek_is_not_judged()
    {
        ZipEntryCount.Exceeds(new ForwardOnly(Archive(3)), 0).ShouldBeFalse();
    }

    private static byte[] Archive(int entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < entries; i++)
            {
                using var stream = zip.CreateEntry($"e{i}.txt").Open();
                stream.WriteByte((byte)'x');
            }
        }

        return buffer.ToArray();
    }

    private static int EndRecordOffset(byte[] bytes)
    {
        for (var i = bytes.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == 0x06054b50)
            {
                return i;
            }
        }

        throw new InvalidOperationException("no end record");
    }

    /// <summary>
    /// A zip64 end record, its locator and a classic end record whose count is saturated —
    /// the whole tail of an archive, with no directory behind it at all.
    /// </summary>
    private static byte[] Zip64EndRecordsStating(long entries)
    {
        var bytes = new byte[56 + 20 + 22];

        var zip64 = bytes.AsSpan(0, 56);
        BinaryPrimitives.WriteUInt32LittleEndian(zip64, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[4..], 44);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[24..], (ulong)entries);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[32..], (ulong)entries);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[40..], (ulong)entries * 47);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[48..], 0);

        var locator = bytes.AsSpan(56, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator[8..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);

        var end = bytes.AsSpan(76, 22);
        BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(end[8..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(end[10..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(end[12..], uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(end[16..], uint.MaxValue);

        return bytes;
    }

    private sealed class ForwardOnly(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
