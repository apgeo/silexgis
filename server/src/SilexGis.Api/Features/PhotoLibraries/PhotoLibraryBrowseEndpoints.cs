// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// Looking through a neighbouring photo library, as a list of pictures rather than as a map.
///
/// <para>
/// Until now the only way to see what one of these libraries holds was as points on a map, which
/// answers "what was photographed here" and cannot answer "what is in the club's library" — and the
/// second is most of it: everything taken underground, every scan, every camera with no receiver
/// has no position and appears on no map. These two routes answer that instead, and they answer it
/// without a coordinate of any kind.
/// </para>
/// <para>
/// <b>Positionless by construction, not by filtering.</b> There is no latitude on these responses,
/// no rectangle in these requests, and no sort or filter derived from a coordinate. That is a
/// design property rather than a protection measure: it keeps the surface simple and keeps it
/// honest about what it is. A later phase decides what may be said about where a photograph was
/// taken; it decides that where positions are emitted, and nothing is emitted here.
/// </para>
/// <para>
/// One thing this route takes could undo that on its own, and it is dealt with where it is sent
/// rather than here: the words from a search box. One of the two products parses that parameter
/// into the same form its own filters bind to, geographic ones included, so text passed through
/// verbatim would let a listing be narrowed to a circle around a point — and a coordinate read off
/// which page a photograph falls on is a coordinate this surface published. The client that talks
/// to that product reduces what it is given to words before sending it, and says so at length.
/// </para>
/// <para>
/// Nothing is stored and nothing is held between calls. The library is asked for one page, its
/// answer is turned into a response, and the answer is gone when the response is written — which is
/// why there is no table, no job and no migration behind any of this. The credential that reaches
/// the library is the library client's own and is never read here: this route decides who may use
/// the feature, and the client decides how to reach the far side.
/// </para>
/// </summary>
public static class PhotoLibraryBrowseEndpoints
{
    /// <summary>
    /// The library reports no photograph under that identifier — deleted over there, or
    /// re-identified since the page naming it was drawn. Kept apart from the slice's other "not
    /// found", which means this installation does not run the product that was named.
    /// </summary>
    public const string PhotographNotFoundCode = "photo_library.photograph_not_found";

    /// <summary>
    /// Words were sent to a library that does not match text. Refused rather than dropped: a
    /// parameter the far end neither honours nor rejects comes back as a full unfiltered page, with
    /// nothing in it saying so and the reader's words still in the search box.
    /// </summary>
    public const string TextSearchUnsupportedCode = "photo_library.text_search_unsupported";

    /// <summary>Words longer than this installation will put in a request to a neighbour.</summary>
    public const string SearchTooLongCode = "photo_library.search_too_long";

    /// <summary>
    /// Pictures per page when a caller asks for no particular number. The same number this
    /// application's own gallery shows, because it is the same gesture on the same screens.
    /// </summary>
    public const int DefaultPageSize = 60;

    /// <summary>
    /// The largest page this installation will ask a library for, whatever a caller asks for.
    /// </summary>
    /// <remarks>
    /// Clamped here rather than trusted to the far end, because a limit that lives only at the far
    /// end stops existing the day the far end changes: one of these two products will serve a
    /// hundred thousand photographs into a browser that cannot draw them. A caller who asked for
    /// more is told the page was capped rather than left to notice a short list.
    /// </remarks>
    public const int MaxPageSize = 200;

    /// <summary>
    /// The longest run of words passed to a library's own search. Long enough for a sentence and
    /// short enough that nothing unbounded is put into a request to a neighbour.
    /// </summary>
    public const int MaxSearchLength = 200;

    public static RouteGroupBuilder MapPhotoLibraryBrowseEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var libraries = api.MapGroup("/photo-libraries").WithTags("PhotoLibraries");

        libraries.MapGet("/{source}/photographs", ListAsync)
            .WithSummary(
                "One page of the photographs one neighbouring library holds, newest first. Carries "
                + "no position of any kind and takes no rectangle.");

        libraries.MapGet("/{source}/photographs/{photographId}", DetailAsync)
            .WithSummary(
                "Everything one neighbouring library will say about one photograph it holds, "
                + "except where it was taken.");

        return api;
    }

    /// <summary>
    /// One page of a library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Paged through the library's own paging rather than by reading more than a page and cutting
    /// it up here: the far side is the only thing that knows what its second page is, and a
    /// listing assembled from oversized reads would ask a neighbouring container for photographs
    /// nobody is looking at.
    /// </para>
    /// <para>
    /// The order is the library's, asked for as newest first by each product's own way of saying
    /// it. Nothing is re-sorted here — a page re-ordered on this side would disagree with the
    /// paging it came out of, which is how the same photograph appears on two pages and another on
    /// none.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<LibraryPhotographPageDto>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        string source,
        int? page,
        int? pageSize,
        string? q,
        IEnumerable<IPhotoLibrary> libraries,
        ILibraryPhotoTokenService tokens,
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

        var library = libraries.FirstOrDefault(l => l.Source == which);
        if (library is null || !library.IsConfigured)
        {
            // A library nobody has configured is absent rather than broken, and a caller reaching
            // this route for one is looking at a page that should not have been offered — so the
            // answer is the same one a product this installation does not run gets, and no socket
            // is opened on the way to it.
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        var wanted = pageSize ?? DefaultPageSize;
        var size = Math.Clamp(wanted, 1, MaxPageSize);
        var number = Math.Max(1, page ?? 1);
        var text = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        if (text is { Length: > MaxSearchLength })
        {
            return ApiProblems.BadRequest(
                SearchTooLongCode,
                $"A search of at most {MaxSearchLength} characters is passed to a photo library.");
        }

        if (text is not null && !library.SupportsTextSearch)
        {
            return ApiProblems.BadRequest(
                TextSearchUnsupportedCode,
                "This photo library does not match text, so words are not sent to it.");
        }

        LibraryPhotoListPage answer;
        try
        {
            answer = await library.ListAsync(new LibraryPhotoQuery(number, size, text), ct);
        }
        catch (PhotoLibraryException e)
        {
            // A library that did not answer fails the request rather than arriving as an empty
            // page: a grid that quietly draws nothing cannot be told apart from a library nobody
            // has put anything in, and that is the mistake this feature is most likely to make
            // while looking correct.
            loggerFactory.CreateLogger(typeof(PhotoLibraryBrowseEndpoints)).LogWarning(
                e, "The {Source} photo library did not answer a listing.", which);

            return ApiProblems.ServiceUnavailable(e.Code, e.Message);
        }

        // The credential the browser will carry for the pictures is minted here, once for the whole
        // page, and only for a caller the audience rule has just admitted.
        var picturesAvailable = library.PicturesAvailable;
        var template = picturesAvailable
            ? LibraryPictureAddress.Template(which, tokens.CreateToken(which))
            : null;

        return TypedResults.Ok(new LibraryPhotographPageDto(
            PhotoLibrarySlugs.Slug(which),
            PhotoLibrarySlugs.Name(which),
            [.. answer.Photos.Select(LibraryPhotographMapping.Of)],
            number,
            size,
            answer.Total,
            answer.HasMore,
            PageSizeCapped: wanted > size,
            library.SupportsTextSearch,
            picturesAvailable,
            template,
            answer.ReadAt));
    }

    /// <summary>
    /// Everything one library will say about one photograph.
    /// </summary>
    /// <remarks>
    /// The identifier is the photograph's own — the one a listing carries beside the picture's, and
    /// not the picture's, which on one of the two products is a hash of the bytes and names no
    /// photograph at all.
    /// </remarks>
    private static async Task<Results<Ok<LibraryPhotographDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> DetailAsync(
        string source,
        string photographId,
        IEnumerable<IPhotoLibrary> libraries,
        ILibraryPhotoTokenService tokens,
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

        var library = libraries.FirstOrDefault(l => l.Source == which);
        if (library is null || !library.IsConfigured)
        {
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        // Checked before anything leaves the machine, because this value is interpolated into an
        // address sent to somebody else's server: a value carrying a slash or a question mark asks
        // the neighbour a different question than the one intended.
        if (!PhotoLibraryHttp.IsSafeReference(photographId))
        {
            return ApiProblems.NotFound(PhotographNotFoundCode);
        }

        LibraryPhotoDetail? detail;
        try
        {
            detail = await library.DetailAsync(photographId, ct);
        }
        catch (PhotoLibraryException e)
        {
            loggerFactory.CreateLogger(typeof(PhotoLibraryBrowseEndpoints)).LogWarning(
                e, "The {Source} photo library did not answer about one photograph.", which);

            return ApiProblems.ServiceUnavailable(e.Code, e.Message);
        }

        if (detail is null)
        {
            // Kept apart from a library that did not answer, because the two send a reader
            // somewhere different: this one means the photograph is gone from over there, and the
            // list it was opened from is describing a library as it was a moment ago.
            return ApiProblems.NotFound(PhotographNotFoundCode);
        }

        var picturesAvailable = library.PicturesAvailable;
        var template = picturesAvailable
            ? LibraryPictureAddress.Template(which, tokens.CreateToken(which))
            : null;

        return TypedResults.Ok(
            LibraryPhotographMapping.Of(detail, which, picturesAvailable, template));
    }
}
