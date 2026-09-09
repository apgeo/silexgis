// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.Persistence;
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
/// These two routes take no words either. Asking a library what it holds and asking it what it
/// makes of a sentence are different questions with differently shaped answers — one of the two
/// products replies to the second with an ordering of its whole library rather than with a
/// narrowed list — so words have a route of their own, and the count it publishes says what it is
/// a count of. Keeping them apart also keeps one dangerous parameter in one place: the same
/// product parses a search box's text into the form its own filters bind to, geographic ones
/// included, so text passed through verbatim would let a list be narrowed to a circle around a
/// point, and a coordinate read off which page a photograph falls on is a coordinate this surface
/// published.
/// </para>
/// <para>
/// <b>The listing takes two narrowings, and the first is a trip.</b> A caller may name a trip
/// this installation holds, and the days that trip was out become the stretch of time the library is
/// asked about — so a trip's page can show the photographs taken while it was out without anything
/// having been filed against it. What a caller may <em>not</em> do is name the stretch of time
/// itself, and the difference is the feature rather than a precaution: a window somebody sent in is
/// a date filter, and a date filter with a trip's name written over it is a claim about where those
/// photographs came from that nothing checked. The trip is read here, its dates are read here, and
/// whether this account may read that trip at all is decided here by the same service that decides
/// it on the trip's own page.
/// </para>
/// <para>
/// <b>The second narrowing is an album, and unlike the trip it is a value a caller sends
/// directly.</b> An album is a set somebody over there put together and named — usually, in a club,
/// one expedition — so asking for one emits nothing about anybody's position and cannot be read
/// backwards into one. The difference from the trip is that an album has no second reading to
/// protect against: the identifier names a set the library already keeps, the answer is the
/// photographs in it, and the screen claims nothing about it beyond the name the library gave it.
/// What it must not do is carry a second question into somebody else's grammar — one of these
/// products parses this kind of value into the same form its own filters bind to, geographic ones
/// included — so the shape of it is checked here, before anything leaves the machine, against
/// exactly what this application is willing to put in a request to a neighbour. Whether it names an
/// album the library keeps is the library's to answer, and an identifier it does not know comes
/// back as a listing with nothing in it.
/// </para>
/// <para>
/// Nothing is stored and nothing is held between calls. The library is asked for one page, its
/// answer is turned into a response, and the answer is gone when the response is written — which is
/// why there is no table, no job and no migration behind any of this. Naming a trip changes none of
/// that: nothing is written, nothing is bound, and no photograph becomes the trip's. The credential
/// that reaches the library is the library client's own and is never read here: this route decides
/// who may use the feature, and the client decides how to reach the far side.
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
    /// A listing was asked for a trip this account may not read, one that is not there, or a value
    /// that names no trip at all.
    /// </summary>
    /// <remarks>
    /// One answer for all three, as everywhere else a row is read by identifier: telling somebody
    /// that a trip exists but is not theirs to read is itself a fact about the trip, and this route
    /// would be a way of asking it about every identifier in turn. A value that is not an
    /// identifier joins them because the alternative is worse than uninformative — refused by the
    /// framework instead, it carries no code of this application's, and a screen that cannot read
    /// one says the library did not answer. Kept apart from the slice's other two
    /// "not founds" — a product this installation does not run, and a photograph the library no
    /// longer reports — because all three draw an empty panel and only this one means the reader is
    /// looking at a trip that is not there for them.
    /// </remarks>
    public const string TripNotFoundCode = "photo_library.trip_not_found";

    /// <summary>
    /// The trip is there, and its dates do not describe a stretch of time this installation will
    /// ask a library about.
    /// </summary>
    /// <remarks>
    /// Its end precedes its start, its start was never filled in, or the two are so far apart that
    /// the window would be most of the library. Refused rather than answered with something
    /// plausible: a panel that quietly picked one reading of a contradictory pair of dates would
    /// present somebody else's photographs as this trip's, and the record that needs correcting
    /// would go on looking ordinary.
    /// </remarks>
    public const string TripWindowUnusableCode = "photo_library.trip_window_unusable";

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
    /// A page so far into a library that this installation will not ask for it.
    /// </summary>
    /// <remarks>
    /// One of these two products documents a hundred thousand as the largest offset it will accept,
    /// and this refuses a page beyond that rather than sending it. The refusal is the point: an
    /// out-of-range request sent anyway comes back as a failure from the far side, and a failure
    /// from the far side is reported to a reader as the library not answering — which sends an
    /// operator to look at a container that is working, for a request this application knew was out
    /// of range before it built it. Unreachable from the screen, which only ever steps one page at
    /// a time; reachable from an address somebody typed or was handed.
    /// </remarks>
    public const int MaxOffset = 100_000;

    /// <summary>
    /// A page number beyond what this installation will ask a library for.
    /// </summary>
    public const string PageTooDeepCode = "photo_library.page_too_deep";

    /// <summary>
    /// Whether this page begins further into the library than this installation will ask, with the
    /// refusal to answer with when it does.
    /// </summary>
    /// <remarks>
    /// Checked on the page's first photograph rather than on the page number alone, because how far
    /// in a page number reaches depends on how large the pages are: page 500 of sixty and page 500
    /// of two hundred are different distances into the same library.
    ///
    /// <para>
    /// Public because both routes that page through a library decide it here, and because it is
    /// pinned by a test: the failure it prevents is invisible in an answer, since a request that
    /// went out and came back refused looks from the outside like a library that is down.
    /// </para>
    /// </remarks>
    public static ProblemHttpResult? TooDeep(int page, int size) =>
        (page - 1L) * size > MaxOffset
            ? ApiProblems.BadRequest(
                PageTooDeepCode,
                $"This installation asks a photo library for at most the first {MaxOffset} "
                + "photographs, and that page begins past them.")
            : null;

    public static RouteGroupBuilder MapPhotoLibraryBrowseEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var libraries = api.MapGroup("/photo-libraries").WithTags("PhotoLibraries");

        libraries.MapGet("/{source}/photographs", ListAsync)
            .WithSummary(
                "One page of the photographs one neighbouring library holds, newest first — of the "
                + "whole library, of the days one trip was out, or of one of the library's albums. "
                + "Carries no position of any kind and takes no rectangle, no words and no dates.");

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
    /// <para>
    /// Where a trip was named, the narrowing is the library's too, for the same reason: it decided
    /// what this page is, so a photograph dropped from it here would leave a page short of the
    /// number beside it and a next page that skips whatever was dropped. The window put to the
    /// library is a little wider than the trip — a day past each end — because a trip's dates and a
    /// camera's clock are not in the same frame; the panel that draws this says so, and every
    /// photograph carries its own date.
    /// </para>
    /// <para>
    /// Where an album was named, the narrowing is the library's for the same reason again, and the
    /// number beside the page is whatever the library states for the narrowed question rather than
    /// for the whole library — so the sentence written over it has to say which of the two it is
    /// counting. Both narrowings may be asked for at once and each product can express both, but
    /// the screens here ask one at a time: a trip's panel offers no chooser, and the chooser has no
    /// trip behind it.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<LibraryPhotographPageDto>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        string source,
        int? page,
        int? pageSize,
        string? tripId,
        string? albumId,
        PhotoLibraryGate gate,
        ILibraryPhotoTokenService tokens,
        IOptions<PhotoLibraryOptions> options,
        IAccessContextAccessor accessAccessor,
        SilexGisDbContext db,
        IAccessService access,
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
            // A library nobody has configured is absent rather than broken, and a caller reaching
            // this route for one is looking at a page that should not have been offered — so the
            // answer is the same one a product this installation does not run gets, and no socket
            // is opened on the way to it. One this installation has stopped using is answered the
            // same way, deliberately: stopping it makes the application behave as though it had
            // never been connected.
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        LibraryPhotoWindow? window = null;
        if (tripId is not null)
        {
            // Taken as text and turned into an identifier here rather than bound as one. A value
            // the framework cannot bind never reaches this method at all: it is refused before it,
            // as a bare bad request carrying none of this application's own codes, and a screen
            // with no code to read reports that as the library not having answered — which sends
            // somebody to restart a container that was never asked anything. This is the one
            // parameter on this route a person types or pastes by hand, so it is worth the two
            // lines.
            //
            // An empty value is refused with the rest rather than read as "no trip at all". A
            // request that named a trip and is answered with the whole library is the one wrong
            // answer this feature must not give, and it would be given under the trip's own
            // heading.
            if (!Guid.TryParse(tripId, out var named))
            {
                return ApiProblems.NotFound(TripNotFoundCode);
            }

            // The trip is read here and its dates are turned into a window here, and that is the
            // whole of what makes this panel a trip's photographs rather than a date filter with a
            // trip's name written over it. A caller names a trip; it never names a stretch of time.
            var trip = await db.TripLogs.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == named, ct);

            // Whether this account may read the trip is asked of the one service that decides it,
            // about the row itself, exactly as the trip's own page asks. A window taken from a trip
            // somebody may not read would tell them which days it covered, in a panel that then
            // shows them what was photographed on those days.
            if (trip is null || !(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
            {
                return ApiProblems.NotFound(TripNotFoundCode);
            }

            window = TripPhotoWindow.For(trip.TripDate, trip.TripDateEnd);
            if (window is null)
            {
                return ApiProblems.BadRequest(
                    TripWindowUnusableCode,
                    "This trip's dates do not give a stretch of time to ask a photo library about. "
                    + "Check the day it started and the day it ended.");
            }
        }

        // Checked before anything leaves the machine, because this value is put into a question
        // asked of somebody else's server: one of these products reads it as a term in its own
        // search grammar, where a space or a colon would make it a second filter and every family
        // of that product's fields — the ones naming a place included — is reachable that way. The
        // other takes it in a body this application writes out itself, where a quotation mark would
        // end the string early and turn the rest into a different request. One shape answers both,
        // and it is the one every other foreign identifier here is already held to.
        //
        // An empty value is refused with the rest rather than read as "no album at all". A request
        // that named an album and is answered with the whole library is the one wrong answer this
        // narrowing must not give, and it would be given under the album's own heading.
        if (albumId is not null && !PhotoLibraryHttp.IsSafeReference(albumId))
        {
            return ApiProblems.NotFound(PhotoLibraryAlbumEndpoints.AlbumNotFoundCode);
        }

        var wanted = pageSize ?? DefaultPageSize;
        var size = Math.Clamp(wanted, 1, MaxPageSize);
        var number = Math.Max(1, page ?? 1);

        if (TooDeep(number, size) is { } tooDeep)
        {
            return tooDeep;
        }

        LibraryPhotoListPage answer;
        try
        {
            answer = await library.ListAsync(
                new LibraryPhotoQuery(number, size, window, albumId), ct);
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
        PhotoLibraryGate gate,
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

        var library = await gate.UsableAsync(which, ct);
        if (library is null)
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
