// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.PhotoLibraries;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// Turning a photograph a neighbouring library holds into an object in this installation's own
/// registry.
///
/// <para>
/// The gesture is small and the reason for it is not: somebody stands at an entrance, photographs
/// it, and their library records where they stood. That coordinate is a measurement nobody has to
/// take twice — and until now the only way to get it into the registry was to read it off the map
/// and type it in, which is the step that loses the provenance. So the caller names a photograph,
/// never a coordinate, and the position is read here from the library that holds it. A request
/// carrying a latitude would be a way of putting an object anywhere at all while it looked as
/// though a camera had measured it.
/// </para>
/// <para>
/// Nothing goes the other way. The library is not told that anything was created, nothing is filed
/// into it, and no picture is copied here — what is stored is a point and two lines saying where it
/// was read from.
/// </para>
/// <para>
/// This is the only route in this slice that writes, so it is the only one that asks anything of
/// the caller beyond the audience rule the whole slice shares: it needs their ordinary right to
/// create features, decided against where the new object is going, exactly as every other way of
/// creating one does.
/// </para>
/// </summary>
public static class PhotoLibraryFeatureEndpoints
{
    /// <summary>
    /// A photograph the library does not report at that reference, inside the rectangle it was
    /// looked for in. Kept apart from the slice's other "not found" because this one is actionable:
    /// the map has moved, or the library no longer holds the picture.
    /// </summary>
    public const string PhotographNotFoundCode = "photo_library.photograph_not_found";

    /// <summary>A position the library reported that is not a position on the earth.</summary>
    public const string PositionInvalidCode = "photo_library.position_invalid";

    /// <summary>The named cave does not exist, or the caller may not read it — one answer for both.</summary>
    public const string CaveNotFoundCode = "photo_library.cave_not_found";

    /// <summary>The caller may read the named cave and may not add to it.</summary>
    public const string CaveForbiddenCode = "photo_library.cave_forbidden";

    /// <summary>This installation has no taxonomy row of the code or identity the new object needs.</summary>
    public const string TypeUnknownCode = "photo_library.type_unknown";

    /// <summary>
    /// The cave kind a cave built around a photograph gets. The same ordinary kind an imported
    /// waypoint becomes; the reader changes it on the cave's own form afterwards, and having to
    /// choose one in a two-field dialogue would be asking a question at the wrong moment.
    /// </summary>
    private const string DefaultCaveTypeCode = "cave";

    /// <summary>The entrance kind, on the same reasoning.</summary>
    private const string DefaultEntranceTypeCode = "natural";

    /// <summary>
    /// The most objects reported as already standing nearby. A warning is read at a glance or not
    /// at all: past a handful it stops being "this may already be recorded" and becomes a list.
    /// </summary>
    private const int MaxNearby = 5;

    public static RouteGroupBuilder MapPhotoLibraryFeatureEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var libraries = api.MapGroup("/photo-libraries").WithTags("PhotoLibraries");

        libraries.MapPost("/{source}/photographs/{reference}/feature", CreateFromPhotographAsync)
            .WithValidation<PhotoLibraryFeatureRequest>()
            .WithSummary(
                "Creates a cave, an entrance or another feature at the position one photograph in a "
                + "neighbouring library was taken; the position is read from the library, never sent.");

        return api;
    }

    /// <summary>
    /// Creates one object at the position a photograph records.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="bbox"/> is a search rectangle and emphatically not a position. The library is
    /// asked the same question the map overlay asks — "what have you got inside this rectangle" —
    /// and the photograph is picked out of its answer by reference, so the coordinate that ends up
    /// in the registry is one the library stated, whatever the caller sent. A rectangle naming
    /// somewhere the photograph is not simply fails to find it. It exists because one of the two
    /// products can only be asked about a rectangle, and asking it the question it already answers
    /// is worth far more than a lookup route that would have to be invented, and would then have to
    /// be trusted, for that one product alone.
    /// </para>
    /// <para>
    /// The right asked is the caller's ordinary right to create a feature, decided against where the
    /// object is going, and nothing else — with one addition on one path: naming a cave to hang an
    /// entrance on is a write on that cave, because a cave's own point on the map is its main
    /// entrance's and adding the first entrance moves it.
    /// </para>
    /// <para>
    /// Everything goes through the service that owns feature creation, the way an import or a hand
    /// edit does: containment edges, ancestor arrays, effective protection and the cave's entrance
    /// mirror are exactly what a second creation path gets wrong, months before anybody notices.
    /// </para>
    /// <para>
    /// A later phase adds location protection here — this route turns a photograph's coordinate into
    /// a registry position, which is the disclosure those rules govern — and it is deliberately
    /// absent for now.
    /// </para>
    /// </remarks>
    private static async Task<Results<Created<PhotoLibraryFeatureCreatedDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateFromPhotographAsync(
        string source,
        string reference,
        string bbox,
        PhotoLibraryFeatureRequest request,
        SilexGisDbContext db,
        IEnumerable<IPhotoLibrary> libraries,
        FeatureWriteService writer,
        VisibleProximitySearch proximity,
        IAccessService access,
        IAppSettingsService settings,
        IOptions<PhotoLibraryOptions> options,
        IOptions<MapOptions> mapOptions,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The same audience rule the reading routes apply, asked before anything else: somebody who
        // may not see these photographs may not build anything out of one either.
        if (!PhotoLibraryAudienceRule.MayRead(ctx, options.Value.Audience))
        {
            return ApiProblems.Forbidden(PhotoLibraryAudienceRule.ForbiddenCode);
        }

        if (!PhotoLibrarySlugs.TryParse(source, out var which))
        {
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        if (!Bbox.TryParse(bbox, out var box))
        {
            return ApiProblems.BadRequest("map.invalid_bbox", "bbox must be 'west,south,east,north'.");
        }

        var library = libraries.FirstOrDefault(l => l.Source == which && l.IsConfigured);
        if (library is null || !PhotoLibraryHttp.IsSafeReference(reference))
        {
            return ApiProblems.NotFound(PhotoLibraryEndpoints.NotFoundCode);
        }

        LibraryPhoto photo;
        try
        {
            var found = await LocateAsync(
                library,
                reference,
                new Envelope(box.West, box.East, box.South, box.North),
                Math.Max(1, mapOptions.Value.MaxPoints),
                ct);
            if (found is not { } located)
            {
                return ApiProblems.NotFound(PhotographNotFoundCode);
            }

            photo = located;
        }
        catch (PhotoLibraryException e)
        {
            // A library that did not answer fails the request. Guessing a position, or falling back
            // to anything at all, would put a coordinate in the registry that no camera measured.
            return ApiProblems.ServiceUnavailable(e.Code, e.Message);
        }

        if (!IsOnEarth(photo.Longitude, photo.Latitude))
        {
            return ApiProblems.BadRequest(
                PositionInvalidCode, "The library reported a position that is not on the earth.");
        }

        // No altitude, and none invented: a library's position feed carries none, and a Z of zero
        // would read as sea level rather than as silence.
        var point = new Point(photo.Longitude, photo.Latitude) { SRID = 4326 };

        Feature? cave = null;
        if (request.Kind == FeatureKind.CaveEntrance)
        {
            cave = await db.Features.FirstOrDefaultAsync(
                f => f.Id == request.CaveFeatureId && f.Kind == FeatureKind.Cave, ct);
            if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
            {
                // A cave the caller cannot read is reported exactly like one that is not there: a
                // body reference must never confirm the existence of a row they may not see.
                return ApiProblems.NotFound(CaveNotFoundCode);
            }

            if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
            {
                return ApiProblems.Forbidden(CaveForbiddenCode);
            }
        }

        // Creation is decided against the context the object is going into — the cave it will hang
        // under, where there is one — so a grant scoped to one subtree reaches exactly as far as it
        // was meant to. No club binding is requested: this dialogue asks for a kind and a name, and
        // an object created here belongs to whoever created it until somebody says otherwise.
        var createFacts = await CreateContext.ParentCreateFactsAsync(
            db, ctx, cave?.Id, cavingGroupId: null, request.Kind, ct, request.FeatureTypeId);
        if (createFacts is null
            || !CreateRules.MayCreate(ctx, AccessDomain.Features, createFacts))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        // Asked before anything is written, so the answer is what was already standing there rather
        // than a list this request has just added itself to.
        var nearby = await NearbyAsync(proximity, settings, ctx, point, ct);

        var name = request.Name.Trim();
        Guid createdId;
        try
        {
            createdId = request.Kind switch
            {
                FeatureKind.Cave => await CreateCaveAsync(db, writer, ctx, which, reference, name, point, ct),
                FeatureKind.CaveEntrance => await CreateEntranceAsync(
                    db, writer, ctx, which, reference, name, point, cave!.Id, ct),
                _ => await CreateGenericAsync(
                    writer, ctx, which, reference, name, point, request.FeatureTypeId!.Value, ct),
            };
        }
        catch (FeatureWriteException e)
        {
            // Includes the kind whose properties schema will not admit the two provenance keys. That
            // is refused rather than quietly written without them: where the position came from is
            // the whole point of creating it this way, so dropping it to make the write succeed
            // would leave an object that looks the same as one somebody typed in by hand.
            return ApiProblems.BadRequest(e.Code, string.Join("; ", e.Errors));
        }
        catch (PhotoLibraryTaxonomyMissingException e)
        {
            return ApiProblems.BadRequest(TypeUnknownCode, e.Message);
        }

        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/features/{createdId}",
            new PhotoLibraryFeatureCreatedDto(createdId, name, request.Kind, nearby));
    }

    // ---------- reading the position back out of the library ----------

    /// <summary>
    /// The photograph the library reports under <paramref name="reference"/> inside
    /// <paramref name="bounds"/>, or null when it reports none by that name there.
    /// </summary>
    /// <remarks>
    /// Deliberately the overlay's own question and not a new one. Both products already answer it:
    /// one asks its neighbour over the network exactly as it does for a viewport, and the other
    /// answers from the whole located library it holds in memory, so neither needs a route this
    /// application has never exercised. Asking with the limit the map answers under is what makes it
    /// deterministic — a photograph drawn on the screen was inside that answer, so it is inside this
    /// one.
    /// </remarks>
    private static async Task<LibraryPhoto?> LocateAsync(
        IPhotoLibrary library, string reference, Envelope bounds, int limit, CancellationToken ct)
    {
        var page = await library.PhotosInAsync(bounds, limit, ct);
        foreach (var photo in page.Photos)
        {
            if (string.Equals(photo.Reference, reference, StringComparison.Ordinal))
            {
                return photo;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a pair of degrees is a place. A library is a separate product with its own idea of
    /// what an unset coordinate looks like, and a point off the earth reaches the database as a
    /// geometry the whole map then has to carry.
    /// </summary>
    private static bool IsOnEarth(double longitude, double latitude) =>
        double.IsFinite(longitude) && double.IsFinite(latitude)
        && longitude is >= -180 and <= 180
        && latitude is >= -90 and <= 90;

    // ---------- what is already here ----------

    /// <summary>
    /// Objects already in the registry near the photograph, nearest first.
    /// </summary>
    /// <remarks>
    /// Through the shared proximity search rather than a query of its own, and its rule about who
    /// may be in the answer is left exactly as it is: "there is something within twelve metres of
    /// this point" is itself a position, so the search runs only over objects this caller may
    /// already both read and place. An object they may read but not place is left out, which means
    /// this can be silent beside something that is really there — that is the accepted cost of not
    /// answering a position to somebody entitled to none, and it is not to be traded away to make
    /// the warning more useful.
    ///
    /// <para>
    /// The distance is the photographer's, not the object's: the radius comes from the setting for
    /// pictures rather than the one for surveyed waypoints, and is wider on purpose, because a
    /// picture is taken from where somebody stood and rarely from what they were looking at.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<PhotoLibraryNearbyFeatureDto>> NearbyAsync(
        VisibleProximitySearch proximity,
        IAppSettingsService settings,
        AccessContext ctx,
        Point point,
        CancellationToken ct)
    {
        var import = await settings.GetImportAsync(ct);
        var radius = Math.Min(
            import.PhotoProximityRadiusMeters, PhotoImportOptions.MaxProximityRadiusMeters);
        if (radius <= 0)
        {
            return [];
        }

        var hits = await proximity.NearAsync([point], radius, ctx, ct);
        return
        [
            .. hits
                .Select(hit => (Hit: hit, Distance: Geodesy.DistanceMeters(point.Coordinate, hit.Geom.Coordinate)))
                .Where(x => x.Distance <= radius)
                .OrderBy(x => x.Distance)
                .Take(MaxNearby)
                .Select(x => new PhotoLibraryNearbyFeatureDto(
                    x.Hit.FeatureId, x.Hit.Name, x.Hit.Kind, Math.Round(x.Distance, 1)))
        ];
    }

    // ---------- creating the object ----------

    /// <summary>
    /// A cave, and the entrance that gives it its position.
    /// </summary>
    /// <remarks>
    /// Two objects, and that is not an extra. A cave's own point on the map is a cache of its main
    /// entrance's, maintained by the write service; a cave created with no entrance would draw
    /// nowhere at all, which is the opposite of what somebody creating one from a photograph asked
    /// for. The same shape an imported waypoint takes when it becomes a cave.
    /// </remarks>
    private static async Task<Guid> CreateCaveAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        AccessContext ctx,
        PhotoLibrarySource source,
        string reference,
        string name,
        Point point,
        CancellationToken ct)
    {
        var feature = NewFeature(ctx, source, reference, name, point);
        var cave = new Cave { CaveTypeId = await CaveTypeIdAsync(db, ct) };
        feature.Cave = cave;
        await writer.CreateCaveAsync(feature, cave, [], ct);

        // Unnamed on purpose: the name the reader typed is the cave's, and an entrance carrying it
        // as well would read as a second place with the same name on every list that shows both. It
        // carries the provenance all the same — this is the row the coordinate actually lives on,
        // and a reader who opens the entrance to ask where its position came from is asking the
        // question these two keys exist to answer.
        var entranceFeature = NewFeature(ctx, source, reference, name: null, point);
        var entrance = new CaveEntrance
        {
            CaveFeatureId = feature.Id,
            EntranceTypeId = await EntranceTypeIdAsync(db, ct),
            IsMain = true,
            PositionQuality = PositionQuality.Gps,
        };
        await writer.CreateEntranceAsync(entranceFeature, entrance, ct);
        return feature.Id;
    }

    /// <summary>Another entrance of a cave that is already in the registry.</summary>
    private static async Task<Guid> CreateEntranceAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        AccessContext ctx,
        PhotoLibrarySource source,
        string reference,
        string name,
        Point point,
        Guid caveFeatureId,
        CancellationToken ct)
    {
        // A cave's first entrance is its representative point, so it becomes the main one — the
        // same rule the entrance editor applies, and the reason it is asked here rather than
        // assumed: this cave may already have entrances that were placed by hand.
        var isFirst = !await db.CaveEntrances.AnyAsync(e => e.CaveFeatureId == caveFeatureId, ct);
        var feature = NewFeature(ctx, source, reference, name, point);
        var entrance = new CaveEntrance
        {
            CaveFeatureId = caveFeatureId,
            EntranceTypeId = await EntranceTypeIdAsync(db, ct),
            IsMain = isFirst,
            PositionQuality = PositionQuality.Gps,
        };

        await writer.CreateEntranceAsync(feature, entrance, ct);
        if (entrance.IsMain)
        {
            await writer.SetMainEntranceAsync(caveFeatureId, feature.Id, ct);
        }

        return feature.Id;
    }

    /// <summary>A feature of one of the installation's own kinds.</summary>
    /// <remarks>
    /// A kind this installation does not have is left to the write service to refuse, which it does
    /// by name and with its own code. Checking here first would be a second copy of the same rule
    /// and one more round trip for an answer already on the way.
    /// </remarks>
    private static async Task<Guid> CreateGenericAsync(
        FeatureWriteService writer,
        AccessContext ctx,
        PhotoLibrarySource source,
        string reference,
        string name,
        Point point,
        long featureTypeId,
        CancellationToken ct)
    {
        var feature = NewFeature(ctx, source, reference, name, point);
        feature.FeatureTypeId = featureTypeId;

        // No containment edge. Where a new object belongs in the hierarchy is a decision about the
        // registry rather than about the photograph, and a kind that cannot exist outside a
        // container refuses here with the write service's own reason — which is the honest answer,
        // because a container guessed from a coordinate would be a claim nobody made.
        await writer.CreateGenericAsync(feature, [], ct);
        return feature.Id;
    }

    /// <summary>
    /// The parts every kind starts from, including the provenance the whole gesture exists for.
    /// </summary>
    /// <remarks>
    /// Visibility is left at the entity's own default, which is the most restrictive one. That is
    /// deliberate for a two-field dialogue: publishing something is a decision, and a decision
    /// nobody was asked to make must not be taken for them.
    /// </remarks>
    private static Feature NewFeature(
        AccessContext ctx, PhotoLibrarySource source, string reference, string? name, Point point) =>
        new()
        {
            Name = name,
            Geom = point,
            OwnerUserId = ctx.UserId,
            Properties = PhotoLibraryProvenance
                .Properties(PhotoLibrarySlugs.Slug(source), reference)
                .ToJsonString(),
        };

    private static async Task<long> CaveTypeIdAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var id = await db.CaveTypes.AsNoTracking()
            .Where(t => t.Code == DefaultCaveTypeCode).Select(t => (long?)t.Id).FirstOrDefaultAsync(ct);
        return id ?? throw new PhotoLibraryTaxonomyMissingException(
            $"This installation has no '{DefaultCaveTypeCode}' cave kind to create a cave as.");
    }

    private static async Task<long> EntranceTypeIdAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var id = await db.EntranceTypes.AsNoTracking()
            .Where(t => t.Code == DefaultEntranceTypeCode).Select(t => (long?)t.Id).FirstOrDefaultAsync(ct);
        return id ?? throw new PhotoLibraryTaxonomyMissingException(
            $"This installation has no '{DefaultEntranceTypeCode}' entrance kind to create an entrance as.");
    }
}

/// <summary>
/// This installation has no taxonomy row the new object needs. A refusal rather than a silent
/// substitution: an installation whose lookup tables have been edited down is a real one, and
/// creating a cave of some other kind than the reader will expect is worse than saying so.
/// </summary>
public sealed class PhotoLibraryTaxonomyMissingException(string message) : InvalidOperationException(message);
