// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using SilexGis.Domain.Documents;

namespace SilexGis.Infrastructure.Documents;

/// <summary>How asking for a write-up as a portable document ended.</summary>
public enum WriteUpPdfOutcome
{
    /// <summary>The conversion service laid the document out; the bytes are a portable document.</summary>
    Converted = 0,

    /// <summary>
    /// This installation runs no conversion service. A standing fact about the installation,
    /// and the ordinary state of a small one — not a failure of anything.
    /// </summary>
    NotDeployed = 1,

    /// <summary>
    /// A conversion service is deployed and did not answer within the time a person waiting for
    /// a download is given. A fact about this one attempt.
    /// </summary>
    NoAnswer = 2,

    /// <summary>The conversion service answered and did not hand back a portable document.</summary>
    Refused = 3,
}

/// <summary>A write-up as a portable document, or why there is none.</summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Bytes">The portable document; null unless the outcome is <see cref="WriteUpPdfOutcome.Converted"/>.</param>
public readonly record struct WriteUpPdf(WriteUpPdfOutcome Outcome, byte[]? Bytes);

/// <summary>
/// Hands a generated write-up to the installation's conversion service and brings back a
/// portable document, inside the request that asked for it.
/// </summary>
/// <remarks>
/// <para>
/// The application writes one format itself — the word-processor document — because laying a
/// page out needs fonts and a text engine, and neither belongs in an application that mostly
/// stores caves. An installation that wants its uploaded office documents drawn page by page
/// already runs a service that has both; a write-up is one more office document to it. So a
/// portable copy of a write-up exists exactly where that service does, and nowhere else.
/// </para>
/// <para>
/// The three ways of not getting one are kept apart, because they are three different sentences
/// to show a person. No service is a fact about the installation and the page should not have
/// offered the choice. A service that did not answer is a fact about this attempt: try again,
/// or take the word-processor document. A service that answered without a document is neither
/// the reader's fault nor fixed by waiting.
/// </para>
/// <para>
/// The wait is bounded here, more tightly than the service's own patience. That patience is
/// sized for a queue converting a long upload in the background; somebody who pressed a button
/// is waiting with a spinner, and is better served by a refusal they can act on than by a
/// request held open for minutes. Nothing is stored at any point: the document goes to the
/// service from memory and the answer goes to the caller from memory.
/// </para>
/// </remarks>
public sealed class WriteUpPdfConverter(IDocumentConverter converter, IOptions<ReportOptions> options)
{
    /// <summary>The media type a portable document is served as.</summary>
    public const string ContentType = "application/pdf";

    /// <summary>The file extension of a portable document, without a leading dot.</summary>
    public const string Extension = "pdf";

    /// <summary>The five bytes every portable document begins with.</summary>
    private static readonly byte[] Signature = "%PDF-"u8.ToArray();

    /// <summary>
    /// Whether this installation can produce a portable copy at all. What a page reads to decide
    /// whether to offer one.
    /// </summary>
    public bool IsAvailable => converter.IsConfigured;

    /// <summary>Converts one generated write-up.</summary>
    /// <param name="document">The word-processor document, whole.</param>
    /// <param name="fileName">
    /// Its file name. The service chooses its reader by the extension, so this is content.
    /// </param>
    /// <param name="ct">The request's own cancellation; a caller who went away is not an outcome.</param>
    public async Task<WriteUpPdf> ConvertAsync(byte[] document, string fileName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!converter.IsConfigured)
        {
            return new WriteUpPdf(WriteUpPdfOutcome.NotDeployed, null);
        }

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(options.Value.PdfWait);

        using var source = new MemoryStream(document, writable: false);
        using var produced = new MemoryStream();
        try
        {
            await converter.ConvertToPortableAsync(source, fileName, produced, bound.Token);
        }
        catch (DocumentConversionException)
        {
            return new WriteUpPdf(WriteUpPdfOutcome.Refused, null);
        }
        catch (Exception unanswered) when (unanswered is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Unreachable, too slow for the bound above, or anything else on the way there and
            // back: the service is deployed and this attempt got no document out of it. The one
            // thing let through is the caller's own request having been abandoned.
            return new WriteUpPdf(WriteUpPdfOutcome.NoAnswer, null);
        }

        // Believed only when it is what was asked for. A proxy's error page answered with a
        // success status would otherwise be handed to somebody as a document that will not open.
        var bytes = produced.ToArray();
        return bytes.AsSpan().StartsWith(Signature)
            ? new WriteUpPdf(WriteUpPdfOutcome.Converted, bytes)
            : new WriteUpPdf(WriteUpPdfOutcome.Refused, null);
    }
}
