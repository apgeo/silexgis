// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// The path from a trip's worth of photographs to caves, entrances and surface features.
///
/// <para>
/// Nothing here commits until <c>commit</c> is called: a preview reads the pictures, works out
/// where each was taken, groups the ones showing the same place and says what is already in the
/// registry near it. What a review stores is only what would otherwise be lost — which pictures
/// are being looked at, the whole-drop options, and the per-place decisions — so a closed tab
/// costs nothing.
/// </para>
/// <para>
/// Two protection rules run through all of it. A picture's own coordinates are shown to whoever
/// may already have the picture's original, and the pictures themselves are only ever offered as
/// renderings this application drew — never the uploaded bytes, which carry the fix inside them.
/// And the proximity list is a location oracle, so it is computed only against objects this
/// caller may already place exactly.
/// </para>
/// </summary>
public static class PhotoImportEndpoints
{
    public static RouteGroupBuilder MapPhotoImportEndpoints(this RouteGroupBuilder api)
    {
        var photos = api.MapGroup("/photo-import").WithTags("Import");

        photos.MapGet("/session", GetSessionAsync)
            .WithSummary("The caller's photo review, resumed where they left it.");
        photos.MapPut("/session", SaveSessionAsync).WithValidation<PhotoSessionWriteRequest>()
            .WithSummary("Saves the photo review as the reviewer works; nothing is created.");
        photos.MapPost("/preview", PreviewAsync).WithValidation<PhotoPreviewRequest>()
            .WithSummary("Groups the drop into places and says what is near each, with nothing committed.");
        photos.MapPost("/commit", CommitAsync).WithValidation<PhotoCommitRequest>()
            .WithSummary("Creates or files the selected places as one revertible batch.");
        photos.MapGet("/tracks", TracksAsync)
            .WithSummary("Uploaded tracks a picture with no fix of its own can be placed against.");

        api.MapPut("/files/{id:guid}/position", SetPositionAsync)
            .WithValidation<PhotoPositionRequest>()
            .WithTags("Files")
            .WithSummary("Gives a picture a position by hand, or forgets one; never rewrites the file.");

        api.MapPost("/features/{id:guid}/position-from-photo", PositionFromPhotoAsync)
            .WithValidation<FeaturePositionFromPhotoRequest>()
            .WithTags("Features")
            .WithSummary("Moves an object to the position one of its pictures records; an ordinary, audited write.");

        return api;
    }

    // ---------- placing a picture by hand ----------

    /// <summary>
    /// Writes a position onto a picture's record.
    /// </summary>
    /// <remarks>
    /// Never into the file. The uploaded bytes are what somebody sent, and an application that
    /// rewrote them would be editing evidence — a photograph whose stated fix disagrees with the
    /// registry is a fact worth keeping, not a mistake to tidy away.
    /// <para>
    /// A picture the camera already placed needs the request to say so. That is not a
    /// confirmation dialogue moved to the server; it is the server refusing to let a drag replace
    /// a measurement by accident, on the only surface where the difference is recorded.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PhotoPositionDto>, UnauthorizedHttpResult, ProblemHttpResult>> SetPositionAsync(
        Guid id,
        PhotoPositionRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var subject = await DocumentQueries.FileSubjectAsync(db, id, ct);
        if (subject is null || !await DocumentAccessRules.CanWriteFileAsync(db, access, ctx, subject, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed to non-writers
        }

        var file = await db.StoredFiles.FirstAsync(f => f.Id == id, ct);
        if (file.PositionSource == PhotoPositionSource.Exif && !request.ReplaceRecordedFix)
        {
            return ApiProblems.Conflict(
                "file.position_recorded",
                "This picture already carries the position its camera recorded. "
                + "Confirm that you mean to replace it.");
        }

        if (request.Position is { Count: 2 } position)
        {
            file.Geom = new NetTopologySuite.Geometries.Point(position[0], position[1]) { SRID = 4326 };
            file.PositionSource = PhotoPositionSource.Manual;

            // The altitude, bearing and fix quality belonged to the reading being replaced. A
            // point somebody dragged has none of them, and keeping the old ones would attach a
            // camera's measurements to a position it never took.
            file.AltitudeMeters = null;
            file.DirectionDegrees = null;
            file.DirectionIsMagnetic = false;
            file.PositionDop = null;
        }
        else
        {
            // Taking a position off is a decision a person made, so it is recorded as one:
            // "manual" means somebody settled this picture's position, and settling it at
            // "there is none" is one of the answers. Left as "nobody has said", the catch-up
            // pass would read the camera's own fix straight back onto it.
            file.Geom = null;
            file.PositionSource = PhotoPositionSource.Manual;
            file.AltitudeMeters = null;
            file.DirectionDegrees = null;
            file.DirectionIsMagnetic = false;
            file.PositionDop = null;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(file));
    }

    // ---------- a picture's position offered to the object it shows ----------

    /// <summary>
    /// Moves an object to where one of its pictures was taken.
    /// </summary>
    /// <remarks>
    /// A photograph never moves anything on its own — it proposes, and this endpoint is the act
    /// of accepting the proposal. What it performs is an ordinary geometry write: it needs write
    /// access to the object, it is audited in that object's own history like any other edit, and
    /// it is undone there. There is no queue and nothing to approve, because somebody who may
    /// change the object may change it.
    /// <para>
    /// The picture must already hang on the object. Without that, this would be a way to move
    /// anybody's cave to any coordinates a caller could put in a photograph.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PhotoPositionDto>, UnauthorizedHttpResult, ProblemHttpResult>> PositionFromPhotoAsync(
        Guid id,
        FeaturePositionFromPhotoRequest request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        Infrastructure.Features.FeatureWriteService writer,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var feature = await db.Features.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, feature, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        // Writing a precise position while being shown a snapped one would save the obfuscated
        // echo over the real value — the same guard every other write path here carries.
        if (!(await protection.ExactViewIdsAsync(ctx, [feature.Id], ct)).Contains(feature.Id))
        {
            return ApiProblems.Forbidden("feature.exact_location_required");
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FileId, ct);
        if (file?.Geom is null)
        {
            return ApiProblems.BadRequest(
                "feature.photo_not_placed", "That picture records no position.");
        }

        var attached = await db.Attachments.AsNoTracking()
            .AnyAsync(a => a.FileId == request.FileId && a.FeatureId == feature.Id, ct);
        if (!attached)
        {
            return ApiProblems.BadRequest(
                "feature.photo_not_attached", "That picture is not attached to this object.");
        }

        feature.Geom = file.Geom;

        // An entrance carries the altitude as well as the point, and a cave's own geometry is a
        // cache of its main entrance — so the write goes through the same places an ordinary
        // edit of either would.
        var entrance = await db.CaveEntrances.FirstOrDefaultAsync(e => e.Id == feature.Id, ct);
        if (entrance is not null)
        {
            entrance.Altitude = file.AltitudeMeters is { } metres ? (decimal)metres : null;
            entrance.PositionQuality = file.PositionSource == PhotoPositionSource.Exif
                ? PositionQuality.Gps
                : PositionQuality.Estimated;
        }

        await db.SaveChangesAsync(ct);
        await writer.RecomputeDerivedStateAsync([feature.Id], ct);
        if (entrance is not null)
        {
            await writer.SyncCaveMirrorAsync(entrance.CaveFeatureId, ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(file));
    }

    private static PhotoPositionDto ToDto(StoredFile file) => new(
        file.Id,
        file.Geom is null ? null : GeoJsonGeometry.From(file.Geom),
        file.PositionSource,
        file.AltitudeMeters,
        file.DirectionDegrees,
        file.DirectionIsMagnetic,
        file.PositionDop,
        PositionConfidence.Of(file.PositionDop));

    // ---------- session ----------

    private static async Task<Results<Ok<PhotoSessionDto>, UnauthorizedHttpResult>> GetSessionAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var session = await db.PhotoImportSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == ctx.UserId, ct);
        var defaults = await DefaultOptionsAsync(settings, ct);
        if (session is null)
        {
            // No review yet is not a 404: the answer is what a first drop would start from, and
            // a review nobody has begun is not a row.
            return TypedResults.Ok(new PhotoSessionDto([], defaults, new Dictionary<string, PhotoDecision>(), null));
        }

        return TypedResults.Ok(new PhotoSessionDto(
            ReadIds(session.FileIds),
            ReadOptions(session.Options) ?? defaults,
            ReadStoredDecisions(session.Decisions),
            session.UpdatedAt));
    }

    private static async Task<Results<Ok<PhotoSessionDto>, UnauthorizedHttpResult>> SaveSessionAsync(
        PhotoSessionWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var session = await db.PhotoImportSessions.FirstOrDefaultAsync(s => s.UserId == ctx.UserId, ct);
        if (session is null)
        {
            session = new PhotoImportSession { UserId = ctx.UserId };
            db.PhotoImportSessions.Add(session);
        }

        session.FileIds = ImportJson.Serialize(request.FileIds);
        session.Options = ImportJson.Serialize(request.Options);
        session.Decisions = ImportJson.Serialize(request.Decisions);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new PhotoSessionDto(
            request.FileIds, request.Options, request.Decisions, session.UpdatedAt));
    }

    // ---------- the drop, grouped ----------

    private static async Task<Results<Ok<PhotoPreviewDto>, UnauthorizedHttpResult, ProblemHttpResult>> PreviewAsync(
        PhotoPreviewRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IFileAccessTokenService tokens,
        PhotoCandidateService candidateService,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (files, unreadable) = await ReadableFilesAsync(db, access, ctx, request.FileIds, ct);
        var decisions = ReadDecisions(request.Decisions);
        var built = await candidateService.BuildAsync(files, request.Options, decisions, ctx, ct);
        var candidates = built.Candidates;

        var filtered = Filter(candidates, request).ToList();
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return TypedResults.Ok(new PhotoPreviewDto(
            [.. pageItems.Select(c => ToDto(c, decisions, tokens))],
            page,
            pageSize,
            filtered.Count,
            [.. filtered.Select(c => c.Key)],
            // A place can be confirmed when something puts it somewhere. What it becomes always
            // has an answer — the drop's default kind — so position is the only thing that can
            // be missing, and a bulk selector that took an unplaced picture anyway would produce
            // a failure line per row at confirmation.
            [.. filtered.Where(c => c.Geom is not null).Select(c => c.Key)],
            candidates.Count(c => c.Geom is not null),
            candidates.Count(c => c.Geom is null),
            files.Count,
            built.TrackFixCount,
            built.TrackFirstFixAt,
            built.TrackLastFixAt,
            unreadable));
    }

    // ---------- confirmation ----------

    private static async Task<Results<Ok<ImportCommitResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> CommitAsync(
        PhotoCommitRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        PhotoCommitService commitService,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Creating is decided once, here, before anything is read: a drop binds everything it
        // creates to the same club, so one answer covers the batch. Filing pictures on an object
        // that already exists is a different question and is asked per object, against that
        // object — which is the only place a permission bites in this flow.
        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, request.Options.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (request.Options.CavingGroupId is { } groupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, groupId))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        if (request.Options.TripLogId is { } tripId)
        {
            // Filing a drop under a trip writes on the trip: the pictures become part of its
            // record, and the caves it names gain a member.
            var trip = await db.TripLogs.FirstOrDefaultAsync(t => t.Id == tripId, ct);
            if (trip is null || !(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
            {
                return ApiProblems.NotFound("trip_log.not_found");
            }

            if (!(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
            {
                return ApiProblems.Forbidden();
            }
        }

        var (files, _) = await ReadableFilesAsync(db, access, ctx, request.FileIds, ct);
        if (files.Count == 0)
        {
            return ApiProblems.BadRequest(
                "photo_import.no_files", "None of the chosen pictures could be read.");
        }

        try
        {
            var result = await commitService.CommitAsync(
                files, request.Options, request.Selection, ReadDecisions(request.Decisions), ctx, ct);

            await ClearSpentReviewAsync(db, ctx.UserId, request.FileIds, ct);

            return TypedResults.Ok(new ImportCommitResultDto(
                StagedImportEndpoints.ToDto(result.Batch, geofileName: null, canRevert: true),
                [.. result.Failures.Select(f => new ImportFailureDto(f.SourceId, f.Name, f.Code, f.Reason))]));
        }
        catch (ImportCommitException ex)
        {
            return ApiProblems.BadRequest(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// Throws away the saved review once the pictures it was about have been filed.
    /// </summary>
    /// <remarks>
    /// Only when every picture it held was part of this confirmation. A review is spent when its
    /// decisions describe pictures that are now filed, and leaving it would offer to file them a
    /// second time — but filing one photograph from the panel beside it, while a whole trip's
    /// drop sits half-reviewed in another tab, spends nothing. Clearing unconditionally would
    /// throw that afternoon away without saying so.
    /// </remarks>
    private static async Task ClearSpentReviewAsync(
        SilexGisDbContext db, Guid userId, IReadOnlyList<Guid> committed, CancellationToken ct)
    {
        var session = await db.PhotoImportSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (session is null)
        {
            return;
        }

        var reviewed = ReadIds(session.FileIds);
        if (reviewed.Count == 0 || reviewed.All(committed.Contains))
        {
            await db.PhotoImportSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        }
    }

    // ---------- tracks ----------

    /// <summary>
    /// The uploaded tracks a picture can be placed against. Only GPX: it is the only accepted
    /// format that records a time per point, and offering the others would be offering a choice
    /// that silently places nothing.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<PhotoTrackOptionDto>>, UnauthorizedHttpResult>> TracksAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var rows = await db.Geofiles.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Geofiles)
            .Where(g => g.Format == GeofileFormat.Gpx && g.ImportStatus == GeofileImportStatus.Imported)
            .OrderByDescending(g => g.CreatedAt)
            .Take(MaxTrackOptions)
            .Select(g => new PhotoTrackOptionDto(g.Id, g.Name, g.CreatedAt))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<PhotoTrackOptionDto>>(rows);
    }

    /// <summary>Enough recent uploads to find the day's track in; not the whole archive.</summary>
    private const int MaxTrackOptions = 100;

    // ---------- helpers ----------

    private static IEnumerable<PhotoCandidate> Filter(
        IReadOnlyList<PhotoCandidate> candidates, PhotoPreviewRequest request)
    {
        IEnumerable<PhotoCandidate> query = candidates;

        if (request.Placed is { } placed)
        {
            query = query.Where(c => (c.Geom is not null) == placed);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var needle = FoldedText.Of(request.Search).Value;
            query = query.Where(c =>
                FoldedText.Of(c.ProposedName).Value.Contains(needle, StringComparison.Ordinal)
                || c.Members.Any(m =>
                    FoldedText.Of(m.OriginalName).Value.Contains(needle, StringComparison.Ordinal)));
        }

        return query;
    }

    private static PhotoCandidateDto ToDto(
        PhotoCandidate candidate,
        IReadOnlyDictionary<Guid, PhotoDecision> decisions,
        IFileAccessTokenService tokens) => new(
        candidate.Key,
        candidate.Geom is null ? null : GeoJsonGeometry.From(candidate.Geom),
        candidate.PositionSource,
        [.. candidate.Members.Select(m => ToDto(m, tokens))],
        candidate.ProposedName,
        candidate.AltitudeMeters,
        candidate.DirectionDegrees,
        candidate.DirectionIsMagnetic,
        candidate.Dop,
        PositionConfidence.Of(candidate.Dop),
        candidate.TrackMatchSecondsFromFix,
        candidate.TrackMatchInterpolated,
        [
            .. candidate.Nearby.Select(n => new PhotoNearbyDto(
                n.FeatureId, n.Name, n.Kind, Math.Round(n.DistanceMeters, 1), n.CaveFeatureId, n.CaveName))
        ],
        decisions.GetValueOrDefault(candidate.Key));

    private static PhotoCandidateMemberDto ToDto(PhotoMemberInfo member, IFileAccessTokenService tokens) => new(
        member.FileId,
        member.OriginalName,
        member.MimeType,
        member.Kind,
        member.CapturedAt,
        member.PositionSource,
        member.AltitudeMeters,
        member.DirectionDegrees,
        member.DirectionIsMagnetic,
        member.Dop,
        PositionConfidence.Of(member.Dop),
        member.HasOwnPosition,
        // Renderings only, always. The uploaded bytes carry the capture fix inside them, and
        // this table shows that fix on screen anyway — but a URL is a decision that outlives the
        // page it was drawn on, and handing out an original here would publish the coordinates
        // to anything the picture is later shown by.
        member.Kind == FileKind.Image
            ? $"/api/v1/files/{member.FileId}/thumbnail?size=160&token={Uri.EscapeDataString(tokens.CreateToken(member.FileId, FileDelivery.DerivativesOnly))}"
            : null);

    /// <summary>
    /// Of the requested pictures, the ones this caller may read — and the ids of the ones they
    /// may not, so the review can say so rather than quietly showing a shorter list.
    /// </summary>
    /// <remarks>
    /// The document walk in bulk: one read for the rows, one for whichever cabinets could
    /// decide them, one for the attachment reach. Asking per file would be five hundred walks
    /// for a drop, and asking none would be a second copy of the rule.
    /// <para>
    /// Superseded revisions are excluded. A version is replaced precisely when something in it
    /// had to go, and deriving a cave from a photograph the document no longer serves would file
    /// the registry against a picture nobody is looking at.
    /// </para>
    /// </remarks>
    internal static async Task<(IReadOnlyList<StoredFile> Files, IReadOnlyList<Guid> Unreadable)> ReadableFilesAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyList<Guid> fileIds,
        CancellationToken ct)
    {
        var ids = fileIds.Distinct().Take(PhotoImportOptions.MaxFiles).ToList();
        if (ids.Count == 0)
        {
            return ([], []);
        }

        var rows = await (from file in db.StoredFiles.AsNoTracking()
                          join version in db.DocumentVersions.AsNoTracking()
                              on file.DocumentVersionId equals version.Id
                          join document in db.Documents.AsNoTracking()
                              on version.DocumentId equals document.Id
                          where ids.Contains(file.Id) && version.IsCurrent
                          select new { File = file, Document = document })
            .ToListAsync(ct);

        var documentIds = rows.Select(r => r.Document.Id).Distinct().ToList();
        var cabinetReach = await DocumentAccessRules.CabinetReachAsync(
            db, ctx, AccessAction.Read, documentIds, ct);
        var reached = await DocumentAccessRules.ReachedByAttachmentAsync(db, ctx, documentIds, ct);

        var readable = rows
            .Where(r => DocumentAccessRules.AllowedByOwnRulesOrAttachment(
                ctx, r.Document, AccessAction.Read, cabinetReach, reached.Contains(r.Document.Id)))
            .ToList();

        // The order the caller asked in is the order the review shows, so a drop keeps whatever
        // order the browser sent — which is the order the pictures were chosen in.
        var byId = readable.ToDictionary(r => r.File.Id, r => r.File);
        return (
            [.. ids.Where(byId.ContainsKey).Select(id => byId[id])],
            [.. ids.Where(id => !byId.ContainsKey(id))]);
    }

    private static PhotoImportOptions? ReadOptions(string json)
    {
        try
        {
            return ImportJson.Deserialize<PhotoImportOptions>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<PhotoImportOptions> DefaultOptionsAsync(
        IAppSettingsService settings, CancellationToken ct)
    {
        var import = await settings.GetImportAsync(ct);
        return new PhotoImportOptions
        {
            ProximityRadiusMeters = import.PhotoProximityRadiusMeters,
            ClusterRadiusMeters = import.PhotoClusterRadiusMeters,
        };
    }

    private static IReadOnlyList<Guid> ReadIds(string json)
    {
        try
        {
            return ImportJson.Deserialize<List<Guid>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, PhotoDecision> ReadStoredDecisions(string json)
    {
        try
        {
            return ImportJson.Deserialize<Dictionary<string, PhotoDecision>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Decisions keyed by candidate. The wire form keys by string because JSON object keys are
    /// strings; a key that is not a candidate id is dropped rather than refused — a stale saved
    /// review must not make the drop unopenable.
    /// </summary>
    internal static Dictionary<Guid, PhotoDecision> ReadDecisions(
        IReadOnlyDictionary<string, PhotoDecision>? raw)
    {
        var decisions = new Dictionary<Guid, PhotoDecision>();
        foreach (var (key, value) in raw ?? new Dictionary<string, PhotoDecision>())
        {
            if (Guid.TryParse(key, out var candidateKey))
            {
                decisions[candidateKey] = value;
            }
        }

        return decisions;
    }
}
