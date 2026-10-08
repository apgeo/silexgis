// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Common;

/// <summary>The format a write-up's download is asked for in.</summary>
public enum ReportFormat
{
    /// <summary>The word-processor document this application writes itself.</summary>
    Docx = 0,

    /// <summary>A portable document, laid out by the installation's conversion service.</summary>
    Pdf = 1,
}

/// <summary>
/// The last step of handing somebody a write-up: in which format, and what is said when the
/// format asked for cannot be had.
/// </summary>
/// <remarks>
/// <para>
/// A trip's write-up and a camp's are built in different parts of the application and handed
/// over the same way, so the handing over is written once. Which words name a format, what a
/// portable copy is called, and the wording and code of each refusal are the same on every
/// route that downloads a write-up — two copies would be two sets of codes for a page to learn.
/// </para>
/// <para>
/// Only a download has a format. The copy filed against a trip or a camp stays the
/// word-processor document: it is the one a club edits afterwards, and it must not come to
/// depend on a service an installation may switch off.
/// </para>
/// </remarks>
public static class ReportDownloads
{
    /// <summary>A word that names no format a write-up comes in.</summary>
    public const string FormatInvalidCode = "report.format_invalid";

    /// <summary>A portable copy was asked for and this installation runs no conversion service.</summary>
    public const string PdfUnavailableCode = "report.pdf_unavailable";

    /// <summary>The conversion service is deployed and did not answer in the time allowed.</summary>
    public const string PdfNoAnswerCode = "report.pdf_no_answer";

    /// <summary>The conversion service answered and handed back no portable document.</summary>
    public const string PdfRefusedCode = "report.pdf_refused";

    /// <summary>A format other than the word-processor document was named on a request to file a write-up.</summary>
    public const string PdfNotFiledCode = "report.pdf_not_filed";

    /// <summary>
    /// Which format a caller named, or null for a word that names none. Saying nothing asks for
    /// the word-processor document, which is what every download was before there was a choice.
    /// </summary>
    public static ReportFormat? ReadFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return ReportFormat.Docx;
        }

        return RouteEnums.TryParse<ReportFormat>(format, out var named) ? named : null;
    }

    /// <summary>
    /// What can be refused about the format before any document is built: a word that names no
    /// format, or a portable copy asked of an installation that cannot make one.
    /// </summary>
    /// <remarks>
    /// Asked before the work of building, so an installation without the service does not
    /// gather a trip's photographs to refuse the request afterwards — and asked by each route
    /// only once it has found its subject readable, so somebody who is refused the write-up is
    /// told that and nothing about what they asked for.
    /// </remarks>
    public static ProblemHttpResult? RefusalBeforeBuilding(string? format, WriteUpPdfConverter pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        return ReadFormat(format) switch
        {
            null => FormatInvalid(format),
            ReportFormat.Pdf when !pdf.IsAvailable => PdfUnavailable(),
            _ => null,
        };
    }

    /// <summary>
    /// The refusal for a request to file a write-up that named a format, or null when it named
    /// none or named the one a filed copy is always in.
    /// </summary>
    public static ProblemHttpResult? RefusalWhenFiling(string? format) => ReadFormat(format) switch
    {
        null => FormatInvalid(format),
        ReportFormat.Docx => null,
        _ => ApiProblems.BadRequest(
            PdfNotFiledCode,
            "A write-up is filed as a word-processor document, which is the copy a club edits "
            + "afterwards. Download it to have a PDF of your own."),
    };

    /// <summary>
    /// A built write-up as the answer to a download, in the format that was asked for.
    /// </summary>
    /// <param name="document">The word-processor document as it was built.</param>
    /// <param name="fileName">Its file name, with the word-processor extension.</param>
    /// <param name="format">The word the caller named, already known to name a format.</param>
    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> AnswerAsync(
        byte[] document,
        string fileName,
        string? format,
        IDocumentWriter writer,
        WriteUpPdfConverter pdf,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(pdf);

        switch (ReadFormat(format))
        {
            case null:
                return FormatInvalid(format);
            case ReportFormat.Docx:
                return TypedResults.File(document, writer.ContentType, fileName);
            default:
                break;
        }

        var converted = await pdf.ConvertAsync(document, fileName, ct);
        return converted.Outcome switch
        {
            WriteUpPdfOutcome.Converted => TypedResults.File(
                converted.Bytes!,
                WriteUpPdfConverter.ContentType,
                Path.ChangeExtension(fileName, WriteUpPdfConverter.Extension)),
            WriteUpPdfOutcome.NotDeployed => PdfUnavailable(),
            WriteUpPdfOutcome.NoAnswer => ApiProblems.ServiceUnavailable(
                PdfNoAnswerCode,
                "The service that turns a write-up into a PDF did not answer in time. Try again, "
                + "or download the write-up as a Word document."),
            _ => ApiProblems.ServiceUnavailable(
                PdfRefusedCode,
                "The service that turns a write-up into a PDF could not lay this one out. "
                + "Download the write-up as a Word document instead."),
        };
    }

    private static ProblemHttpResult FormatInvalid(string? format) => ApiProblems.BadRequest(
        FormatInvalidCode, $"A write-up comes as 'docx' or 'pdf'; '{format}' is neither.");

    private static ProblemHttpResult PdfUnavailable() => ApiProblems.Conflict(
        PdfUnavailableCode,
        "This installation does not run the service that turns a write-up into a PDF. "
        + "Download the write-up as a Word document, or print it to PDF from the browser.");
}
