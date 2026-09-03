// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// One derivative's response, borrowed from a neighbouring library and owned by the caller until it
/// is disposed.
///
/// <para>
/// A wrapper rather than a byte array: the picture is copied to the outgoing response as it
/// arrives, so nothing here ever holds a whole image in memory, and a library that stops mid-answer
/// fails on the copy rather than after a buffer has been filled. Disposal is the caller's — the
/// route takes it in an <c>await using</c> and copies inside it, because a stream handed to a
/// result that executes after the handler returns is disposed before it is read.
/// </para>
/// <para>
/// <see cref="NotModified"/> is a first-class outcome and not an error: the browser's validator is
/// passed through to the library and the library's answer is passed back, so a re-opened balloon
/// costs a conditional request and no image bytes.
/// </para>
/// </summary>
public sealed class LibraryThumbnail(HttpResponseMessage response) : IAsyncDisposable
{
    /// <summary>The library answered that the caller's copy is still current.</summary>
    public bool NotModified => response.StatusCode == HttpStatusCode.NotModified;

    /// <summary>The far side's own validator, passed through unchanged, or null when it sent none.</summary>
    public string? ETag => response.Headers.ETag?.ToString();

    /// <summary>Checked before this object exists; never a non-picture type on a successful answer.</summary>
    public string ContentType =>
        response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

    /// <summary>How many bytes the library says it is sending, where it says.</summary>
    public long? ContentLength => response.Content.Headers.ContentLength;

    /// <summary>Opens the picture for copying. Valid until this object is disposed.</summary>
    public Task<Stream> OpenAsync(CancellationToken ct) => response.Content.ReadAsStreamAsync(ct);

    public ValueTask DisposeAsync()
    {
        response.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>What came back when a picture was asked for.</summary>
public enum PictureVerdict
{
    /// <summary>A picture. Forward it.</summary>
    Picture = 0,

    /// <summary>The caller's copy is still current.</summary>
    NotModified = 1,

    /// <summary>The library would not accept this installation's credential.</summary>
    Unauthorized = 2,

    /// <summary>The library will not serve the rendering that was asked for. A defect on this side.</summary>
    WrongRendering = 3,

    /// <summary>
    /// Not a picture, and not for a reason that can be told apart from the dangerous one. Treated
    /// as "this library may be unable to reach its originals".
    /// </summary>
    NotAPicture = 4,
}

/// <summary>
/// Reading answers from a neighbouring photo library: what is safe to put in a request path to one,
/// and whether what came back from one is a picture.
/// </summary>
public static class PhotoLibraryHttp
{
    /// <summary>
    /// Whether a foreign identifier is one this application is willing to put in a request path.
    /// These libraries name photographs and their derivatives with letters, digits, hyphens and
    /// underscores — one of them appends a crop area to a hash with a hyphen, which this admits.
    /// Anything else is refused rather than passed on: these values are interpolated into a URL, and
    /// a value carrying a slash, a dot-dot or a question mark asks the neighbour a different
    /// question than the one intended.
    /// </summary>
    public static bool IsSafeReference(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= 128
        && value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// Refuses an answer to a question asked in JSON that did not come back as JSON. A library
    /// having trouble answers with a page of markup and HTTP 200 as readily as with an error, and a
    /// parser fed markup fails somewhere far from the cause.
    /// </summary>
    public static void EnsureJson(HttpResponseMessage response, PhotoLibrarySource source)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (mediaType is not null
            && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new PhotoLibraryException(
            PhotoLibraryException.RejectedCode,
            $"The {source} photo library answered a question with {mediaType ?? "no content type"} rather than JSON.");
    }

    /// <summary>
    /// Decides whether what came back is a picture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One of these libraries answers a failed picture request with a placeholder drawing carrying
    /// HTTP 200 or 403 rather than with an error, so the content type is the signal and the status
    /// code is not. Two of its placeholder cases are known and are not dangerous: a rejected
    /// credential, which arrives as 403 and is decided before any file on the far side is touched,
    /// and a rendering size the instance does not serve, which is a defect on this side. Everything
    /// else that is not a picture is treated as the dangerous case — because a request that failed
    /// while resolving a file on disk is the case that marks the file missing and deletes the
    /// photograph from the far side's index, and the documented behaviour does not let it be told
    /// apart from the rest.
    /// </para>
    /// <para>
    /// The verdict is returned rather than acted on here: closing the picture path is sticky
    /// per-library state, and the library that owns the state is the one that owns the decision.
    /// </para>
    /// </remarks>
    public static PictureVerdict ClassifyPicture(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return PictureVerdict.NotModified;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return PictureVerdict.Unauthorized;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var placeholder = string.Equals(mediaType, "image/svg+xml", StringComparison.OrdinalIgnoreCase);

        if (placeholder && response.IsSuccessStatusCode)
        {
            return PictureVerdict.WrongRendering;
        }

        var isPicture = response.IsSuccessStatusCode
            && mediaType is not null
            && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !placeholder;

        return isPicture ? PictureVerdict.Picture : PictureVerdict.NotAPicture;
    }
}
