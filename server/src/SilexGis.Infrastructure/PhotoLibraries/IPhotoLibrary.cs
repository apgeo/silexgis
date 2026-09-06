// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Infrastructure.PhotoLibraries;

/// <summary>
/// One neighbouring photo library, read only, holding photographs this application does not own and
/// does not file. An installation may run one, several or none of them; none of them is this
/// application's own archive and nothing moves between them.
///
/// <para>
/// The products behind this interface answer a viewport in completely different ways — one takes a
/// rectangle and another only offers its whole located library — so the contract is deliberately
/// expressed as "what is inside these bounds, at most this many", which is the question a map asks,
/// rather than as a paging contract that would fit one product and be a lie about another.
/// </para>
/// </summary>
public interface IPhotoLibrary
{
    /// <summary>Which product this is, so a surface can name it.</summary>
    PhotoLibrarySource Source { get; }

    /// <summary>
    /// Whether an operator has supplied an address and a credential. False costs nothing: no socket
    /// is opened on any path, and every surface reports the library as absent rather than as broken.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Whether this library may currently be asked for image bytes. Starts true for a configured
    /// library and is set false, stickily, when a picture request came back as something that was
    /// not a picture. Nothing sets it back to true except <see cref="RecheckOriginalsAsync"/>.
    /// </summary>
    /// <remarks>
    /// On the interface rather than only inside each client because the map answer and the status
    /// surface both publish it, and a gate that is held but not published is a flag rather than a
    /// guard: a client that cannot see it goes on requesting pictures. It is a property and not a
    /// method — the value is already known, and asking for it must never open a socket.
    /// </remarks>
    bool PicturesAvailable { get; }

    /// <summary>
    /// Asks the library what state it is in: whether it answers at all, what it says it is, and
    /// whether the configured credential carries the rights this integration needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This never throws.</b> A library that is not there, not answering or refusing the
    /// credential is a result an operator has to read, not an error that fails the request they
    /// asked it in — the whole point of the call is to describe a failure, so failing at it would
    /// be answering the question with the question.
    /// </para>
    /// <para>
    /// It reaches only routes that describe the installation and the credential. Nothing here asks
    /// the library to resolve a file on disk, because that is the act that costs photographs when
    /// the disk is not there. Probing an unconfigured library opens no socket and reports that
    /// nothing was asked, which is a different answer from reporting that it did not answer.
    /// </para>
    /// <para>
    /// Answers are held briefly, per library: a status line refreshed twice, or looked at by two
    /// people at once, must not become a burst of requests against a neighbouring container.
    /// </para>
    /// </remarks>
    Task<LibraryHealth> ProbeAsync(CancellationToken ct);

    /// <summary>
    /// Photographs this library reports inside <paramref name="bounds"/>, at most
    /// <paramref name="limit"/> of them, together with when their positions were read.
    /// </summary>
    Task<LibraryPhotoPage> PhotosInAsync(Envelope bounds, int limit, CancellationToken ct);

    /// <summary>
    /// One derivative's bytes, streamed. <paramref name="reference"/> is how the far side names the
    /// derivative — which is not always how it names the photograph — and is refused unless it is a
    /// shape this application is willing to put in a request path.
    /// </summary>
    /// <remarks>
    /// <paramref name="ifNoneMatch"/> is the browser's own validator, passed through unchanged so a
    /// re-opened balloon costs a conditional request and no image bytes. Implementations make
    /// exactly one attempt and never retry: against a library whose originals are out of reach, a
    /// second attempt is a second deletion.
    /// </remarks>
    Task<LibraryThumbnail> ThumbnailAsync(
        string reference, LibraryThumbnailSize size, string? ifNoneMatch, CancellationToken ct);

    /// <summary>
    /// Reopens the byte path after it was closed by an answer that was not a picture. Deliberately
    /// the only way back, and deliberately not on a timer: an automatic re-probe against a detached
    /// drive is the deletion loop with a schedule attached.
    /// </summary>
    Task RecheckOriginalsAsync(CancellationToken ct);
}
