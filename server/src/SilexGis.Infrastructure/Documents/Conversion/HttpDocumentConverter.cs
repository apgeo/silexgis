// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Documents;
using SilexGis.Infrastructure.Outbound;

namespace SilexGis.Infrastructure.Documents.Conversion;

/// <summary>
/// Converts office-suite documents to portable ones by handing them to a conversion service over
/// the internal network.
/// </summary>
/// <remarks>
/// <para>
/// The service is an ordinary HTTP endpoint that takes a file and gives back a portable document
/// — the shape every headless office-conversion service exposes. Nothing vendor-specific is
/// compiled in beyond the request path, which is configuration, so an installation can point this
/// at whichever service it already runs.
/// </para>
/// <para>
/// Absent by default and absent in most installations. Every refusal here is careful to say which
/// kind of refusal it is: a service that is not deployed is a fact about this installation, while
/// a service that answered and could not read the bytes is a fact about the document, and showing
/// a person the wrong one of those two sentences is worse than showing neither.
/// </para>
/// </remarks>
public sealed class HttpDocumentConverter(
    IHttpClientFactory httpClientFactory, IOptions<ConversionOptions> options) : IDocumentConverter
{
    /// <summary>Name of the client; registered in <c>DependencyInjection</c>.</summary>
    public const string HttpClientName = "document-converter";

    /// <summary>
    /// Where a conversion is asked for, relative to the configured base address. Fixed rather
    /// than configurable: a second knob whose only correct value is this one is a way to get an
    /// installation into a state nobody can explain.
    /// </summary>
    private const string ConvertPath = "forms/libreoffice/convert";

    private readonly ConversionOptions options = options.Value;

    public bool IsConfigured => options.Enabled && !string.IsNullOrWhiteSpace(options.Url);

    public async Task ConvertToPortableAsync(
        Stream source, string originalName, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (!IsConfigured)
        {
            throw new InvalidOperationException("No document converter is configured.");
        }

        // The name travels with the bytes because a converter picks its reader from the
        // extension; a nameless upload would be refused for a reason nobody could act on.
        var name = MultipartFileName(originalName);

        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(source);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "files", name);

        var limit = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 600));
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = limit;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress(), ConvertPath))
        {
            Content = content,
        };

        HttpResponseMessage response;
        try
        {
            // Headers only: the converted document is streamed to its destination file rather
            // than gathered in memory first, which a large document would otherwise cost twice.
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Unreachable or too slow. Not a fact about the document: the installation said it
            // had a converter and the converter did not answer, which is an operator's problem.
            throw Unreachable(e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // A refusal with a status is the service having looked at the bytes. Anything
                // it cannot open comes back this way, so this is where "damaged document" is
                // an honest thing to record.
                throw new DocumentConversionException(
                    $"The conversion service returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            // The client's own time limit stops counting once the headers are in, so the body
            // gets the same allowance again on a deadline of its own: a service that sends its
            // headers and then stalls would otherwise hold the conversion lane indefinitely.
            using var deadline = OutboundBody.Deadline(limit, ct);
            try
            {
                await response.Content.CopyToAsync(destination, deadline.Token);
            }
            catch (Exception e) when (OutboundBody.IsLost(e, ct))
            {
                throw Unreachable(e);
            }
        }
    }

    /// <summary>
    /// The upload's name as it can travel in a multipart header. The converter only needs the
    /// extension to pick its reader, and a quotation mark, a backslash or a control character
    /// in a header parameter is refused by the request builder before anything is sent — a
    /// document called <c>Raport "Avenul".docx</c> would otherwise never convert at all.
    /// Letters outside ASCII are left alone: the header encodes them.
    /// </summary>
    internal static string MultipartFileName(string? originalName)
    {
        var name = string.IsNullOrWhiteSpace(originalName) ? "" : Path.GetFileName(originalName);
        var safe = string.Create(name.Length, name, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c is '"' or '\\' || char.IsControl(c) ? '_' : c;
            }
        });

        return string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(safe))
            ? "document" + Path.GetExtension(safe)
            : safe;
    }

    private InvalidOperationException Unreachable(Exception cause) =>
        new($"The document conversion service at {options.Url} could not be reached.", cause);

    /// <summary>
    /// The configured address as a base for a relative path — which needs a trailing slash, or
    /// the last segment of the configured URL is silently replaced.
    /// </summary>
    private Uri BaseAddress() =>
        new(options.Url.EndsWith('/') ? options.Url : options.Url + "/");
}
