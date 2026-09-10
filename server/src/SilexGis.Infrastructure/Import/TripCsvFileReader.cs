// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Import.TripCsv;

namespace SilexGis.Infrastructure.Import;

/// <summary>Raised when the uploaded spreadsheet cannot be read at all.</summary>
public sealed class TripCsvReadException(string code, string message) : Exception(message)
{
    /// <summary>The one that says the file is not there any more.</summary>
    public const string MissingCode = "trip_import.file_unreadable";

    /// <summary>The one that says the upload is larger than a review reads.</summary>
    public const string TooLargeCode = "trip_import.file_too_large";

    public string Code { get; } = code;
}

/// <summary>
/// Reads an uploaded trip spreadsheet and parses it, every time it is asked.
///
/// <para>
/// Nothing is staged between the upload and the import, and that is the design rather than an
/// omission. The sheet carries no coordinates, so it is not the kind of file the vector path
/// stages — that path refuses a file with no position column outright, before a row is read. And
/// the reviewer can change what the rows *mean*: which header is the date, whether a numeric date
/// is day-first, which characters separate several names in one cell. A staged parse would be
/// wrong the moment any of those moved, and a staged parse nobody re-ran is worse than no stage at
/// all. Re-reading a few thousand rows of text costs less than the round trip that asked for it.
/// </para>
/// </summary>
public sealed class TripCsvFileReader(IFileStore files, IOptions<ImportLimitOptions> limits)
{
    /// <summary>
    /// The largest upload a review will read into memory. Sized from the row ceiling the rest of
    /// the importer already works to rather than from a new number: a sheet at that many rows of
    /// ordinary text is far below this, so a file above it is a spreadsheet saved as something
    /// else or an archive somebody renamed.
    /// </summary>
    public static long MaxBytesFor(int maxScanRows) => Math.Max(4L * 1024 * 1024, maxScanRows * 512L);

    /// <summary>
    /// The file as text, with the encoding it was read under and what settled that.
    ///
    /// <para>
    /// A spreadsheet states nothing trustworthy about its encoding, and a club archive that runs
    /// back far enough holds sheets written on tools that saved a Central European code page. Read
    /// as UTF-8 regardless, those do not fail loudly: every accented letter becomes a replacement
    /// character and the reviewer reads a page of names with the diacritics eaten. So the bytes are
    /// read, the encoding is worked out from them, and the answer is reported so it can be
    /// overruled — which is what <see cref="TripImportOptions.Encoding"/> does when it is set.
    /// </para>
    /// </summary>
    public async Task<TripCsvDecodedText> ReadTextAsync(
        StoredFile file, TripImportOptions options, CancellationToken ct)
    {
        var ceiling = MaxBytesFor(limits.Value.MaxScanRows);
        if (file.SizeBytes > ceiling)
        {
            throw new TripCsvReadException(
                TripCsvReadException.TooLargeCode,
                $"The upload is larger than {ceiling / (1024 * 1024)} MB, which is more than one review reads.");
        }

        byte[] bytes;
        try
        {
            await using var stream = await files.OpenReadAsync(file.StoragePath, ct);
            bytes = await ReadBoundedAsync(stream, ceiling, ct);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            throw new TripCsvReadException(
                TripCsvReadException.MissingCode, "The uploaded file could not be read.");
        }

        return TripCsvEncodings.Decode(bytes, options.Encoding);
    }

    /// <summary>The sheet, read under the choices made so far.</summary>
    public async Task<TripCsvParseResult> ParseAsync(
        StoredFile file, TripImportOptions options, CancellationToken ct)
    {
        var decoded = await ReadTextAsync(file, options, ct);
        var parsed = TripCsvParser.Parse(decoded.Text, options.ToParserOptions(), options.ToMapping());
        return parsed with { Encoding = decoded.Encoding, EncodingSource = decoded.Source };
    }

    /// <summary>
    /// The stream's bytes, refusing anything past the ceiling.
    /// </summary>
    /// <remarks>
    /// The recorded size was already checked, but that is metadata: it is what the upload said it
    /// was, and the read itself is what has to be bounded. One byte past the ceiling is enough to
    /// tell that the stream is longer than it may be, so the buffer is a byte larger than the
    /// limit and a full one is the refusal.
    /// </remarks>
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long ceiling, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > ceiling)
            {
                throw new TripCsvReadException(
                    TripCsvReadException.TooLargeCode,
                    $"The upload is larger than {ceiling / (1024 * 1024)} MB, which is more than one review reads.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
