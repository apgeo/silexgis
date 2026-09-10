// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// The albums a neighbouring photo library keeps, so that a listing of it can be narrowed to one.
///
/// <para>
/// <b>Why albums and not everything else a library groups by.</b> A club files by expedition, and a
/// foreign album is usually one expedition's worth of pictures — so after "when", "which album" is
/// the narrowing that gets somebody from a library to the photographs they came for. The other
/// groupings these products offer are either restatements of facts a picture already carries or,
/// in one product's case, derived from where the picture was taken; an album is the one that
/// records a decision a club took.
/// </para>
/// <para>
/// <b>This route reads albums and nothing else.</b> It asks nothing about the photographs in one:
/// a chooser needs a name and a number, and the pictures are the listing's answer. It carries no
/// coordinate of any kind — both products will say where an album's photographs were taken and
/// neither is asked.
/// </para>
/// <para>
/// Nothing is stored and nothing is held between calls. The library is asked, its answer becomes a
/// chooser, and the answer is gone when the response is written. The credential that reaches the
/// library is the library client's own and is never read here: this route decides who may use the
/// feature, and the client decides how to reach the far side.
/// </para>
/// <para>
/// A library this installation is not using is asked nothing and opens no socket — the gate answers
/// before anything is constructed, and the client refuses underneath it for the routes that forget
/// to ask.
/// </para>
/// </summary>
public static class PhotoLibraryAlbumEndpoints
{
    /// <summary>
    /// A listing was narrowed to something that does not name an album at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Refused rather than answered with the whole library, which is the one wrong answer this
    /// narrowing must never give: on a screen, a full page of everything under an album's name
    /// cannot be told from an album that happens to hold everything.
    /// </para>
    /// <para>
    /// Whether a well-formed identifier names an album the library actually keeps is the library's
    /// to decide and not this application's — a value it does not know comes back as a listing with
    /// nothing in it, exactly as an album somebody has just emptied does. This code is for the
    /// narrower case of a value this application will not put in a request to a neighbour at all,
    /// which is unreachable from the chooser and reachable from an address somebody typed. Kept
    /// apart from the slice's other "not founds" — a product this installation does not run, a
    /// photograph the library no longer reports, a trip this account may not read — because all
    /// four draw an empty panel and each sends a reader somewhere different.
    /// </para>
    /// </remarks>
    public const string AlbumNotFoundCode = "photo_library.album_not_found";

    public static RouteGroupBuilder MapPhotoLibraryAlbumEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var libraries = api.MapGroup("/photo-libraries").WithTags("PhotoLibraries");

        libraries.MapGet("/{source}/albums", AlbumsAsync)
            .WithSummary(
                "The albums one neighbouring library keeps, for narrowing a listing to one. Names "
                + "and the library's own counts where it states them; no photographs, and no "
                + "position of any kind.");

        return api;
    }

    /// <summary>
    /// What albums one library keeps.
    /// </summary>
    /// <remarks>
    /// The counts on the way out are the library's own numbers, absent where the product publishes
    /// none, and they describe the library rather than whoever asked — one credential belongs to
    /// the whole installation, so there is one answer and every caller who may reach the feature
    /// gets it.
    /// </remarks>
    private static async Task<Results<Ok<LibraryAlbumsDto>, UnauthorizedHttpResult, ProblemHttpResult>> AlbumsAsync(
        string source,
        PhotoLibraryGate gate,
        IOptions<PhotoLibraryOptions> options,
        IAccessContextAccessor accessAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!PhotoLibraryAudienceRule.MayRead(ctx, options.Value.Audience))
        {
            return ApiProblems.Forbidden(PhotoLibraryAudienceRule.ForbiddenCode);
        }

        if (!PhotoLibrarySlugs.TryParse(source, out var which))
        {
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        var library = await gate.UsableAsync(which, ct);
        if (library is null)
        {
            // A library nobody has configured is absent rather than broken, and no socket is opened
            // on the way to saying so. One this installation has stopped using answers the same
            // way, deliberately: stopping it makes the application behave as though it had never
            // been connected, which includes having no albums to offer.
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        LibraryAlbumPage answer;
        try
        {
            answer = await library.AlbumsAsync(ct);
        }
        catch (PhotoLibraryException e)
        {
            // A library that did not answer fails the request rather than arriving as an empty
            // list. The two are the states hardest to tell apart on a chooser and the ones a reader
            // most needs told apart: one means this library keeps no albums, and the other means
            // nobody asked it anything successfully.
            loggerFactory.CreateLogger(typeof(PhotoLibraryAlbumEndpoints)).LogWarning(
                e, "The {Source} photo library did not answer about its albums.", which);

            return ApiProblems.ServiceUnavailable(e.Code, e.Message);
        }

        return TypedResults.Ok(LibraryAlbumsDto.Of(answer, which));
    }
}
