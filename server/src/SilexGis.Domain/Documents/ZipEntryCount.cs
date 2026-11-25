// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;

namespace SilexGis.Domain.Documents;

/// <summary>
/// How many entries a ZIP archive holds, found without building the runtime's archive object.
/// </summary>
/// <remarks>
/// <para>
/// The runtime's <c>ZipArchive</c> turns every record of the central directory into an entry
/// object the first time its entries are touched — a few hundred bytes of objects for a record
/// that can be as small as 47 bytes on disk. A 500 MB upload made of millions of empty entries
/// therefore costs gigabytes of memory before any entry-count cap gets to say no. Reading the
/// count here first lets every reader refuse such an archive while it is still bytes on disk.
/// </para>
/// <para>
/// Two figures are looked at, and the larger one wins. The end-of-central-directory record
/// (and its zip64 extension, when the count does not fit in sixteen bits) states a total; the
/// central directory itself is also walked, record header by record header, because the
/// runtime keeps reading records for as long as their signature matches and only compares
/// with the stated total afterwards. An archive that states one entry and holds eleven million
/// would otherwise pass the stated-count check and still be fully materialised. The walk stops
/// one record past the figure the caller cares about, so it costs at most that many small
/// reads however large the archive is.
/// </para>
/// </remarks>
public static class ZipEntryCount
{
    private const uint EndOfDirectorySignature = 0x06054b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const uint Zip64EndOfDirectorySignature = 0x06064b50;
    private const uint DirectoryRecordSignature = 0x02014b50;

    private const int EndOfDirectoryLength = 22;
    private const int Zip64LocatorLength = 20;
    private const int Zip64EndOfDirectoryLength = 56;
    private const int DirectoryRecordLength = 46;
    private const int MaxCommentLength = ushort.MaxValue;

    /// <summary>
    /// Whether the archive in <paramref name="archive"/> holds more than
    /// <paramref name="maxEntries"/> entries.
    /// </summary>
    /// <remarks>
    /// Answers false when the stream cannot seek or carries no end-of-central-directory record:
    /// the first cannot be checked this way at all, and the second is not an archive the runtime
    /// will open either, so its own refusal is the one the caller should report. The stream's
    /// position is restored before returning, so a borrowed stream is left as it was found.
    /// </remarks>
    public static bool Exceeds(Stream archive, long maxEntries)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);

        if (!archive.CanSeek)
        {
            return false;
        }

        var position = archive.Position;
        try
        {
            return Count(archive, maxEntries + 1) is { } count && count > maxEntries;
        }
        finally
        {
            archive.Position = position;
        }
    }

    /// <summary>
    /// The archive's entry count — the larger of the stated total and the records actually
    /// present, the latter counted only up to <paramref name="countRecordsUpTo"/> — or null
    /// when the stream carries no end-of-central-directory record. The stream must seek; its
    /// position is left wherever the reads ended.
    /// </summary>
    public static long? Count(Stream archive, long countRecordsUpTo)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var length = archive.Length;
        if (length < EndOfDirectoryLength)
        {
            return null;
        }

        var tailLength = (int)Math.Min(length, EndOfDirectoryLength + MaxCommentLength + Zip64LocatorLength);
        var tailStart = length - tailLength;
        var tail = new byte[tailLength];
        archive.Position = tailStart;
        archive.ReadExactly(tail);

        var end = FindEndOfDirectory(tail);
        if (end < 0)
        {
            return null;
        }

        var record = tail.AsSpan(end);
        long stated = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        long directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);

        // A zip64 archive keeps its real figures in a second record, found through a locator
        // that sits immediately before the classic one. Its presence is what matters, not
        // whether the classic fields are saturated: a writer may fill both, and the larger
        // stated figure is the safer one to believe.
        var locator = end - Zip64LocatorLength;
        if (locator >= 0
            && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(locator)) == Zip64LocatorSignature)
        {
            var zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(locator + 8));
            if (length >= Zip64EndOfDirectoryLength
                && zip64Offset <= (ulong)(length - Zip64EndOfDirectoryLength))
            {
                var zip64 = new byte[Zip64EndOfDirectoryLength];
                archive.Position = (long)zip64Offset;
                archive.ReadExactly(zip64);
                if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) == Zip64EndOfDirectorySignature)
                {
                    var zip64Count = BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32));
                    stated = Math.Max(stated, (long)Math.Min(zip64Count, long.MaxValue));
                    directoryOffset = (long)Math.Min(
                        BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(48)), long.MaxValue);
                }
            }
        }

        return Math.Max(stated, CountRecords(archive, directoryOffset, length, countRecordsUpTo));
    }

    /// <summary>
    /// The end-of-central-directory record's offset within <paramref name="tail"/>, or -1.
    /// Searched backwards because the record ends the file apart from its comment, and a
    /// comment may itself contain the signature bytes.
    /// </summary>
    private static int FindEndOfDirectory(byte[] tail)
    {
        for (var i = tail.Length - EndOfDirectoryLength; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EndOfDirectorySignature)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// How many central-directory records start at <paramref name="offset"/>, counted the way
    /// the runtime reads them — for as long as the signature matches — and never past
    /// <paramref name="upTo"/>.
    /// </summary>
    private static long CountRecords(Stream archive, long offset, long length, long upTo)
    {
        var header = new byte[DirectoryRecordLength];
        long count = 0;
        var position = offset;

        while (count < upTo && position >= 0 && position <= length - DirectoryRecordLength)
        {
            archive.Position = position;
            archive.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != DirectoryRecordSignature)
            {
                break;
            }

            count++;
            position += DirectoryRecordLength
                + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28))
                + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30))
                + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
        }

        return count;
    }
}
