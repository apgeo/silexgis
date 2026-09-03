// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
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
    /// The file as text. UTF-8, with a byte-order mark honoured where the file carries one, which
    /// is what a spreadsheet writes. Bytes that are not valid UTF-8 decode to the replacement
    /// character rather than throwing: a sheet saved in an older code page still reads, its
    /// diacritics visibly wrong to the reviewer instead of the whole import failing on the first
    /// row — and every name in it is matched folded, which strips the diacritics anyway.
    /// </summary>
    public async Task<string> ReadTextAsync(StoredFile file, CancellationToken ct)
    {
        var ceiling = MaxBytesFor(limits.Value.MaxScanRows);
        if (file.SizeBytes > ceiling)
        {
            throw new TripCsvReadException(
                TripCsvReadException.TooLargeCode,
                $"The upload is larger than {ceiling / (1024 * 1024)} MB, which is more than one review reads.");
        }

        try
        {
            await using var stream = await files.OpenReadAsync(file.StoragePath, ct);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(ct);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            throw new TripCsvReadException(
                TripCsvReadException.MissingCode, "The uploaded file could not be read.");
        }
    }

    /// <summary>The sheet, read under the choices made so far.</summary>
    public async Task<TripCsvParseResult> ParseAsync(
        StoredFile file, TripImportOptions options, CancellationToken ct)
    {
        var text = await ReadTextAsync(file, ct);
        return TripCsvParser.Parse(text, options.ToParserOptions(), options.ToMapping());
    }
}
