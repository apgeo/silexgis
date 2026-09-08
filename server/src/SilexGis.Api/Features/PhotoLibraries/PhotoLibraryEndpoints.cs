// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// Reading photographs out of a photo library that runs beside this installation and showing them
/// on the map.
///
/// <para>
/// The library is a separate product with its own database, its own storage and its own accounts.
/// This application reads positions from it and passes its pictures through; it files nothing into
/// it, stores none of what it says, and nothing moves between it and this application's own
/// archive. An installation that has been given no library is a supported installation: the status
/// route says so, the routes serve nothing, and no socket is opened on any path.
/// </para>
/// <para>
/// Who may see any of it is one setting with two values — full administrators, which is what it
/// ships as, or every signed-in account. It is asked in one place for the three routes a signed-in
/// caller reaches, and the fourth route, which a browser reaches with nothing but an address,
/// carries the answer in a short-lived token minted by the first.
/// </para>
/// </summary>
public static class PhotoLibraryEndpoints
{
    /// <summary>
    /// Every refusal on the delivery route, whatever the reason. A route reachable with nothing but
    /// an address must not tell an unauthenticated caller which libraries this installation runs or
    /// which photographs are in them, so an expired token, an unknown product and a reference that
    /// is not a reference are one answer.
    /// </summary>
    public const string NotFoundCode = "photo_library.not_found";

    /// <summary>The two renderings a caller may ask for, named for what they are for.</summary>
    private const string SmallSize = "small";

    public static RouteGroupBuilder MapPhotoLibraryEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var libraries = api.MapGroup("/photo-libraries").WithTags("PhotoLibraries");

        libraries.MapGet("/status", StatusAsync)
            .WithSummary(
                "Which neighbouring photo libraries this installation has been given, whether the "
                + "caller may see them, and what each library said when it was last asked.");

        // One route per library rather than one merged route: the source is a path segment and not
        // a filter, because it selects which foreign installation is called, and a caller naming a
        // library this installation does not run gets the same "not found" the other routes give.
        libraries.MapGet("/{source}/map", MapAsync)
            .WithSummary(
                "Located photographs held in one neighbouring photo library, as GeoJSON points for "
                + "the given bbox.");

        // The picture is loaded by the browser as an image, which cannot carry a bearer token, so
        // the short-lived token in the address is the credential — the same arrangement this
        // application's own file thumbnails use. On the documented anonymous allow-list: a route
        // added here without a row there makes the unauthenticated surface larger than the record
        // of it, and the surface test fails.
        libraries.MapGet("/{source}/thumbnails/{reference}", ThumbnailAsync).AllowAnonymous()
            .WithSummary(
                "Streams one photograph's rendering from a neighbouring photo library; "
                + "token-authenticated.");

        libraries.MapPost("/{source}/recheck", RecheckAsync)
            .WithSummary(
                "Reopens a library's picture delivery after an answer that was not a picture closed "
                + "it. Deliberately the only way back: nothing reopens it on a timer.");

        return api;
    }

    /// <summary>
    /// What this installation has, and what each of them said when it was last asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A caller outside the audience is told <c>mayRead: false</c> and given empty lists rather
    /// than a refusal: the client needs one answer to decide whether to offer the layer at all, and
    /// a refusal that also named the products would answer a question the caller may not ask.
    /// Nothing is asked of any library on their behalf.
    /// </para>
    /// <para>
    /// The libraries are asked together rather than one after another. They are separate
    /// installations with separate uptime, and asking them in turn would make one stopped container
    /// cost the whole answer its own timeout before the other was even reached — the same reason
    /// each of them is drawn by its own overlay rather than merged into one.
    /// </para>
    /// <para>
    /// A probe never throws, so this route answers whatever the libraries do. Its cost is bounded
    /// on the far side of the call: each library holds its last answer for a short window, so a
    /// status page refreshed twice, or read by two people at once, costs one round of requests.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PhotoLibraryStatusDto>, UnauthorizedHttpResult>> StatusAsync(
        IEnumerable<IPhotoLibrary> libraries,
        IOptions<PhotoLibraryOptions> options,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!PhotoLibraryAudienceRule.MayRead(ctx, options.Value.Audience))
        {
            return TypedResults.Ok(new PhotoLibraryStatusDto(false, [], []));
        }

        var configured = libraries.Where(library => library.IsConfigured).ToList();
        var health = await Task.WhenAll(configured.Select(library => library.ProbeAsync(ct)));

        var providers = configured
            .Select((library, index) => new PhotoLibraryProviderDto(
                PhotoLibrarySlugs.Slug(library.Source),
                PhotoLibrarySlugs.Name(library.Source),
                LibraryPhotographMapping.MatchingSlug(library.SearchMatching),
                Configured: true,
                PhotoLibraryHealthDto.Of(health[index], library.PicturesAvailable)))
            .ToList();

        // Which products this installation could run and has not is told to a full administrator
        // and to nobody else, and the decision is taken here rather than by a panel that declines
        // to draw a line. It is the answer to the one question an empty layer panel cannot answer
        // on its own: whether there is nothing to see because nothing was connected.
        var unconfigured = new List<PhotoLibraryProviderDto>();
        if (ctx.IsFullAdmin)
        {
            unconfigured.AddRange(libraries
                .Where(library => !library.IsConfigured)
                .Select(library => new PhotoLibraryProviderDto(
                    PhotoLibrarySlugs.Slug(library.Source),
                    PhotoLibrarySlugs.Name(library.Source),
                    // Answerable for a library nobody has configured, because it is a fact about
                    // the product rather than about this installation's copy of it, and nothing is
                    // asked of anybody to know it.
                    LibraryPhotographMapping.MatchingSlug(library.SearchMatching),
                    Configured: false,
                    PhotoLibraryHealthDto.Of(LibraryHealth.NotAsked, picturesAvailable: false))));
        }

        return TypedResults.Ok(new PhotoLibraryStatusDto(true, providers, unconfigured));
    }

    /// <summary>
    /// One library's photographs inside one rectangle.
    /// </summary>
    /// <remarks>
    /// Nothing is stored. The library is asked, its answer is turned into points, and the answer is
    /// gone when the response is written — which is why there is no table, no job and no migration
    /// behind any of this.
    /// </remarks>
    private static async Task<Results<Ok<LibraryPhotoFeatureCollection>, UnauthorizedHttpResult, ProblemHttpResult>> MapAsync(
        string source,
        string bbox,
        IEnumerable<IPhotoLibrary> libraries,
        ILibraryPhotoTokenService tokens,
        IOptions<PhotoLibraryOptions> options,
        IOptions<MapOptions> mapOptions,
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
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Parsed the way every other viewport route in this application parses one, rather than
        // through a validator: no map layer route here validates its rectangle any other way, and
        // being the only one that did would make this slice the odd one out for no gain.
        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var library = libraries.FirstOrDefault(l => l.Source == which);
        if (library is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // A library nobody has configured is absent rather than broken: an empty answer, no socket
        // opened, and no template — which is exactly what the overlay draws for a library that
        // holds nothing here. The client learns which libraries exist from the status route and
        // does not normally ask this one at all.
        if (!library.IsConfigured)
        {
            return TypedResults.Ok(LibraryPhotoFeatureCollection.Of(
                [], which, picturesAvailable: false, pictureUrlTemplate: null,
                readAt: DateTimeOffset.UtcNow, truncated: false, omittedCount: 0));
        }

        // The point limit every other layer in this application answers under, not a new one: a
        // foreign photograph costs a browser the same as one of this installation's own.
        var limit = Math.Max(1, mapOptions.Value.MaxPoints);
        var bounds = new Envelope(box.West, box.East, box.South, box.North);

        LibraryPhotoPage page;
        try
        {
            page = await library.PhotosInAsync(bounds, limit, ct);
        }
        catch (PhotoLibraryException e)
        {
            // A library that did not answer fails the request rather than arriving as an empty
            // collection: an overlay that quietly draws nothing cannot be told apart from a place
            // nobody has photographed, and that is the mistake this whole feature is most likely to
            // make while looking correct.
            loggerFactory.CreateLogger(typeof(PhotoLibraryEndpoints)).LogWarning(
                e, "The {Source} photo library did not answer a viewport.", which);

            return ApiProblems.ServiceUnavailable(e.Code, e.Message);
        }

        var truncated = page.Truncated;
        var features = new List<GeoFeature>(Math.Min(page.Photos.Count, limit));
        var omitted = 0;

        foreach (var photo in page.Photos)
        {
            if (features.Count >= limit)
            {
                truncated = true;
                omitted++;
                continue;
            }

            features.Add(GeoFeature.Of(
                new Point(photo.Longitude, photo.Latitude) { SRID = 4326 },
                new Dictionary<string, object?>
                {
                    // Deliberately not carried: locality, region and country, which the library
                    // also returns. A place name is most of what a coordinate says, and a field
                    // that is safe only because of a rule applied somewhere else is how it ends up
                    // on a path without the rule.
                    ["source"] = PhotoLibrarySlugs.Slug(which),
                    ["reference"] = photo.Reference,
                    ["takenAt"] = photo.TakenAt,
                    ["title"] = photo.Title,
                    ["kind"] = photo.Kind?.ToString().ToLowerInvariant(),
                }));
        }

        // The credential the browser will carry for the pictures is minted here, once for the whole
        // collection, and only for a caller the audience rule has just admitted. Nothing downstream
        // of it decides anything again: an address handed out is a decision already taken.
        var picturesAvailable = library.PicturesAvailable;
        var template = picturesAvailable
            ? LibraryPictureAddress.Template(which, tokens.CreateToken(which))
            : null;

        return TypedResults.Ok(LibraryPhotoFeatureCollection.Of(
            features, which, picturesAvailable, template, page.ReadAt, truncated, omitted));
    }

    /// <summary>
    /// One photograph's rendering, streamed through this application from the library that holds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Proxied rather than linked, and that is not a preference. The library serves its pictures to
    /// anyone who knows the address — measured, not assumed — so a page carrying the library's own
    /// address would publish every photograph in it to whoever reads the markup, and would also
    /// name a host that is not meant to be reachable from a browser at all.
    /// </para>
    /// <para>
    /// Nothing here re-strips metadata or re-encodes anything: the bytes are the library's, passed
    /// through unchanged, and this application makes no claim about what is in them.
    /// </para>
    /// <para>
    /// Exactly one attempt, ever. When this product cannot resolve a file on disk while serving a
    /// picture it marks the file missing and deletes the photograph from its own index — on a plain
    /// GET, with no indexing run involved — so with the originals out of reach a retry loop is a
    /// deletion loop and a map viewport is enough to purge a library. The client below makes one
    /// attempt and closes its own picture path on the first answer that was not a picture; this
    /// route must never add a retry, a backoff or a timer in front of it.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ThumbnailAsync(
        string source,
        string reference,
        string? size,
        string? token,
        HttpContext http,
        IEnumerable<IPhotoLibrary> libraries,
        ILibraryPhotoTokenService tokens,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (!PhotoLibrarySlugs.TryParse(source, out var which) || !tokens.Validate(token, which))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var library = libraries.FirstOrDefault(l => l.Source == which && l.IsConfigured);
        if (library is null || !PhotoLibraryHttp.IsSafeReference(reference))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var wanted = string.Equals(size, SmallSize, StringComparison.OrdinalIgnoreCase)
            ? LibraryThumbnailSize.Small
            : LibraryThumbnailSize.Large;

        LibraryThumbnail picture;
        try
        {
            // The browser's own validator goes to the library unchanged, so a balloon opened twice
            // costs a conditional request and no image bytes.
            var ifNoneMatch = http.Request.Headers.IfNoneMatch.ToString();
            picture = await library.ThumbnailAsync(
                reference, wanted, string.IsNullOrEmpty(ifNoneMatch) ? null : ifNoneMatch, ct);
        }
        catch (PhotoLibraryException e)
        {
            return ApiProblems.ServiceUnavailable(e.Code, e.Message);
        }

        await using (picture)
        {
            if (picture.NotModified)
            {
                return TypedResults.StatusCode(StatusCodes.Status304NotModified);
            }

            // The far side's validator is passed straight through and the answer is marked
            // cacheable for a day: these bytes never change for one reference at one size, so a
            // re-opened balloon costs a conditional request. Dropping these would push every
            // picture through this process on every open, which is the whole of what proxying is
            // accused of costing. Private, because the answer was minted for one viewer.
            if (picture.ETag is { } etag)
            {
                http.Response.Headers.ETag = etag;
            }

            http.Response.Headers.CacheControl = "private, max-age=86400";
            http.Response.ContentType = picture.ContentType;
            if (picture.ContentLength is { } length)
            {
                http.Response.ContentLength = length;
            }

            // Copied here rather than handed to a result. The bytes come from a response this
            // handler owns and disposes; a stream given to a result that runs after the handler
            // returns is disposed before anything reads it.
            await using var content = await picture.OpenAsync(ct);
            await content.CopyToAsync(http.Response.Body, ct);
            return TypedResults.Empty;
        }
    }

    /// <summary>
    /// Reopens a library's picture delivery after an answer that was not a picture closed it.
    /// </summary>
    /// <remarks>
    /// Deliberately the only way back, and deliberately a person rather than a schedule: an
    /// automatic re-probe against a drive that is not there is the deletion loop with a timer
    /// attached. The recheck itself asks the library about its configuration and not about a
    /// picture, so it proves the address and the credential without asking it to resolve a single
    /// file on disk.
    /// </remarks>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RecheckAsync(
        string source,
        IEnumerable<IPhotoLibrary> libraries,
        IOptions<PhotoLibraryOptions> options,
        IAccessContextAccessor accessAccessor,
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
            return ApiProblems.NotFound(NotFoundCode);
        }

        var library = libraries.FirstOrDefault(l => l.Source == which && l.IsConfigured);
        if (library is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        try
        {
            await library.RecheckOriginalsAsync(ct);
        }
        catch (PhotoLibraryException e)
        {
            return ApiProblems.ServiceUnavailable(e.Code, e.Message);
        }

        return TypedResults.NoContent();
    }
}
