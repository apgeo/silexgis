// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// The path from a phone's export archive to a tracked trip's position history — reviewed scan by
/// scan, never inferred.
///
/// <para>
/// The upload itself is the ordinary file route: what arrives is a stored file like any other, and
/// the review points at it. Nothing between here and the confirmation is written except the
/// review, so an archive somebody opened and thought better of leaves the installation exactly as
/// it was.
/// </para>
/// <para>
/// Two translations happen here and both are somebody's decision rather than a rule. A device
/// records a physical marker and a tracked trip records a survey station, so each scan is offered
/// with the stations its marker's depth could mean and a person settles which. And a device
/// account is a login on a phone, not a caver: the mapping from one to the other is filled in by
/// hand, and a scan made by an account nobody mapped is refused rather than attributed.
/// </para>
/// <para>
/// What the archive holds beyond the recording is not read. The media it carries, and on some
/// builds a file of stored server credentials, are never opened — the import takes the database
/// and nothing else, and the only thing it says about a recording's documents is how many there
/// were.
/// </para>
/// </summary>
public static class SpeleolocImportEndpoints
{
    public static RouteGroupBuilder MapSpeleolocImportEndpoints(this RouteGroupBuilder api)
    {
        var import = api.MapGroup("/speleoloc-imports/{fileId:guid}").WithTags("Import");

        import.MapGet("/recordings", GetRecordingsAsync)
            .WithSummary("The recordings the uploaded archive holds, newest first.");
        import.MapGet("/session", GetSessionAsync)
            .WithSummary("The caller's review of this archive, resumed where they left it.");
        import.MapPut("/session", SaveSessionAsync).WithValidation<SpeleolocImportSessionWriteRequest>()
            .WithSummary("Saves the review as the reviewer works; nothing is created.");
        import.MapPost("/preview", PreviewAsync).WithValidation<SpeleolocImportPreviewRequest>()
            .WithSummary("Reads the chosen recording and answers each scan with the stations it could be.");
        import.MapPost("/commit", CommitAsync).WithValidation<SpeleolocImportCommitRequest>()
            .WithSummary("Records the chosen scans as tracking positions, as one batch that reverts as a unit."
                + " For an account under review nothing is written: the confirmation waits for a reviewer and the answer says so (held).");

        // The same review over a recording a phone sent rather than one read out of an archive:
        // how a recording the automatic conversion left waiting, or could not make, becomes a trip
        // by hand. Same requests, same answers, same refusals; there is no saved review, so the
        // decisions travel with the confirmation.
        var kept = api.MapGroup("/speleoloc-imports/recordings/{recordingId:guid}").WithTags("Import");
        kept.MapPost("/preview", PreviewRecordingAsync).WithValidation<SpeleolocImportPreviewRequest>()
            .WithName("previewTripRecordingImport")
            .WithSummary("Reads a recording a phone sent and answers each scan with the stations it could be.");
        kept.MapPost("/commit", CommitRecordingAsync).WithValidation<SpeleolocImportCommitRequest>()
            .WithName("commitTripRecordingImport")
            .WithSummary("Makes a recording a phone sent into tracking positions on a trip, as one batch that reverts as a unit."
                + " For an account under review nothing is written: the confirmation waits for a reviewer and the answer says so (held).");

        return api;
    }

    // ---------- the archive ----------

    private static async Task<Results<Ok<SpeleolocRecordingsDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetRecordingsAsync(
        Guid fileId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        SpeleolocArchiveReader reader,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Asked before the archive is fetched, as the trip sheet's header route asks it: a caller
        // who may not record trips is told that, rather than told about somebody's upload.
        if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            var trips = await reader.ListTripsAsync(file!, ct);
            return TypedResults.Ok(new SpeleolocRecordingsDto(
                file!.Id,
                file.OriginalName,
                [
                    .. trips.Select(t => new SpeleolocRecordingDto(
                        t.Id.ToString(),
                        t.Title,
                        t.CaveTitle,
                        t.StartedAt,
                        t.EndedAt,
                        t.DeviceUserId?.ToString(),
                        t.PointCount,
                        t.DocumentCount))
                ]));
        }
        catch (SpeleolocArchiveException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }
    }

    // ---------- the review ----------

    private static async Task<Results<Ok<SpeleolocImportSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetSessionAsync(
        Guid fileId,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        var session = await db.SpeleolocImportSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoredFileId == file!.Id && s.UserId == ctx.UserId, ct);

        var options = session is null
            ? new SpeleolocImportOptions()
            : ReadOptions(session.Options) ?? new SpeleolocImportOptions();
        var decisions = session is null
            ? new Dictionary<string, SpeleolocPointDecision>()
            : ReadDecisions(session.Decisions);

        // A stored decision names a station, so handing a review back is handing a position back.
        // The right to read one can be taken away after the review was saved, and a review is a
        // durable thing — so the question is asked again here rather than only when the decision
        // was made. The choices themselves still come back: they are what lets the reviewer see
        // why they are being told nothing.
        var open = decisions.Count == 0
            || await ModelUsableAsync(db, access, protection, ctx, options.SurveyModelId, ct) is not null;

        return TypedResults.Ok(new SpeleolocImportSessionDto(
            file!.Id,
            file.OriginalName,
            options,
            open ? decisions : new Dictionary<string, SpeleolocPointDecision>(),
            !open,
            session?.UpdatedAt));
    }

    private static async Task<Results<Ok<SpeleolocImportSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSessionAsync(
        Guid fileId,
        SpeleolocImportSessionWriteRequest request,
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

        // Creating is decided before the archive is fetched, and it is decided the same way
        // whatever the archive is: the answer does not depend on the upload, so asking first tells
        // a caller who may not record trips why, rather than telling them about somebody's file.
        if (RefuseCreate(ctx, request.Options!) is { } refusal)
        {
            return refusal;
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        var session = await db.SpeleolocImportSessions
            .FirstOrDefaultAsync(s => s.StoredFileId == file!.Id && s.UserId == ctx.UserId, ct);
        var creating = session is null;
        if (session is null)
        {
            session = new SpeleolocImportSession { StoredFileId = file!.Id, UserId = ctx.UserId };
            db.SpeleolocImportSessions.Add(session);
        }

        session.Options = ImportJson.Serialize(request.Options);
        session.Decisions = ImportJson.Serialize(request.Decisions);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (creating)
        {
            // Read-then-insert, and the reading is not what decides it: one review per person per
            // archive is a unique index, and two saves that start before either has finished both
            // read nothing and both try to write the first row. The loser merges into the row that
            // won rather than faulting — both carry the same reviewer's own decisions, so the
            // later one is simply the newer reading.
            db.Entry(session).State = EntityState.Detached;
            session = await db.SpeleolocImportSessions
                .FirstAsync(s => s.StoredFileId == file!.Id && s.UserId == ctx.UserId, ct);
            session.Options = ImportJson.Serialize(request.Options);
            session.Decisions = ImportJson.Serialize(request.Decisions);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new SpeleolocImportSessionDto(
            file!.Id, file.OriginalName, request.Options!, request.Decisions!, false, session.UpdatedAt));
    }

    // ---------- the dry run ----------

    private static async Task<Results<Ok<SpeleolocImportPreviewDto>, UnauthorizedHttpResult, ProblemHttpResult>> PreviewAsync(
        Guid fileId,
        SpeleolocImportPreviewRequest request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        SpeleolocArchiveReader reader,
        SpeleolocTripImportResolver resolver,
        SpeleolocTripImportCommitService commits,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var options = request.Options!;
        if (RefuseCreate(ctx, options) is { } refusal)
        {
            return refusal;
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!Guid.TryParse(options.TripUuid, out var recordingId))
        {
            return ApiProblems.BadRequest(
                SpeleolocImportCodes.RecordingNotFound, "No recording was chosen.");
        }

        (SpeleolocArchiveTrip Trip, IReadOnlyList<SpeleolocArchivePoint> Points)? read;
        try
        {
            read = await reader.ReadTripAsync(file!, recordingId, ct);
        }
        catch (SpeleolocArchiveException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        if (read is null)
        {
            return ApiProblems.NotFound(SpeleolocImportCodes.RecordingNotFound);
        }

        var decisions = ReadDecisions(await SessionDecisionsAsync(db, file!.Id, ctx.UserId, ct));
        return await PreviewReadAsync(
            read.Value.Trip, read.Value.Points, decisions, request, db, access, protection, ctx, resolver, commits, ct);
    }

    private static async Task<Results<Ok<SpeleolocImportPreviewDto>, UnauthorizedHttpResult, ProblemHttpResult>> PreviewRecordingAsync(
        Guid recordingId,
        SpeleolocImportPreviewRequest request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        SpeleolocTripImportResolver resolver,
        SpeleolocTripImportCommitService commits,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (RefuseCreate(ctx, request.Options!) is { } refusal)
        {
            return refusal;
        }

        var (recording, points, problem) = await LoadKeptRecordingAsync(recordingId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        return await PreviewReadAsync(
            SpeleolocTripImportCommitService.ArchiveTripOf(recording!, points.Count),
            [.. points.Select(p => SpeleolocTripImportResolver.ScanOf(recording!, p))],
            new Dictionary<string, SpeleolocPointDecision>(StringComparer.OrdinalIgnoreCase),
            request, db, access, protection, ctx, resolver, commits, ct);
    }

    /// <summary>The dry run over a recording already read, whichever way it arrived.</summary>
    private static async Task<Results<Ok<SpeleolocImportPreviewDto>, UnauthorizedHttpResult, ProblemHttpResult>> PreviewReadAsync(
        SpeleolocArchiveTrip recording,
        IReadOnlyList<SpeleolocArchivePoint> scans,
        Dictionary<string, SpeleolocPointDecision> decisions,
        SpeleolocImportPreviewRequest request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        SpeleolocTripImportResolver resolver,
        SpeleolocTripImportCommitService commits,
        CancellationToken ct)
    {
        var options = request.Options!;

        // A recording that already has its report, or is kept here under an account this caller
        // may not write for, is refused here exactly as the confirmation refuses it, so the dry run
        // never offers what the confirmation would turn down.
        try
        {
            await commits.CheckHeldAsync(recording.Id, ctx, ct);
        }
        catch (SpeleolocImportCommitException e)
        {
            return await HeldProblemAsync(db, access, ctx, e);
        }

        // The model the scans are placed in, only when this caller may both read its cave and
        // place it. Missing, unreadable and location-closed are one answer: a preview that told
        // them apart would say that a cave exists to anybody who can name a model.
        var usable = await ModelUsableAsync(db, access, protection, ctx, options.SurveyModelId, ct);

        // The trip the positions would go onto, guarded here exactly as the confirmation guards it.
        // Not a formality: what is read off it is its tracking configuration, whose reference
        // station and depth filter are station vocabulary the live surface withholds from anybody
        // who cannot place its cave, and they steer which scans resolve and which do not. Naming a
        // stranger's trip and reading the shape of the answer was a way to ask about their survey
        // one prefix at a time. It also stops the dry run and the confirmation disagreeing about
        // whose trip this is, which is the one thing a dry run may never do.
        Domain.Entities.TripTracking? tracking = null;
        HashSet<Guid>? roster = null;
        if (options.TripLogId is { } tripLogId)
        {
            var target = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
            if (await WriteGuardAsync(access, ctx, target, ct) is { } denied)
            {
                return denied;
            }

            tracking = await db.TripTrackings.AsNoTracking()
                .FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
            roster = [.. await db.TripLogParticipants.AsNoTracking()
                .Where(p => p.TripLogId == tripLogId)
                .Select(p => p.CaverId)
                .Distinct()
                .ToListAsync(ct)];
        }

        var resolution = await resolver.ResolveAsync(
            scans, options, usable?.Model, tracking, ctx, ct);

        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var pageItems = resolution.Points.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // What "select all" selects, answered here rather than in the browser: a scan set aside on
        // the first page has to stay set aside when the ninth page is the one on screen.
        //
        // It is answered against everything the confirmation will ask, and that is the point of the
        // step. Two of those questions have nothing to do with stations — whether anything says who
        // the scanning device account is, and whether that person is on the roster of the trip the
        // positions are going onto — and leaving them out made the dry run offer scans it knew the
        // confirmation would refuse, which it then did, all of them, so the whole act failed with
        // nothing created. A step whose stated purpose is to say beforehand does not get to find
        // that out afterwards. A trip the confirmation creates has no roster to fail: it is created
        // with exactly the people these scans name.
        var selectable = resolution.Points
            .Where(p => decisions.GetValueOrDefault(p.PointId)?.Action != SpeleolocPointAction.Skip)
            .Where(p => p.State == SpeleolocPointState.Proposed
                || decisions.GetValueOrDefault(p.PointId)?.StationName is not null)
            .Where(p => SpeleolocPointMeaning.CaverOf(p, decisions.GetValueOrDefault(p.PointId)) is { } caver
                && (roster is null || roster.Contains(caver)))
            .Select(p => p.PointId)
            .ToList();

        // Said at the top rather than left to be counted off the rows, for the same reason the
        // unmapped accounts are: one person the mapping names who is not on the trip costs every
        // scan they made, and the reviewer can fix it in one act if they are told which person.
        List<Guid> offRoster = roster is null
            ? []
            : [.. resolution.Points
                .Select(p => SpeleolocPointMeaning.CaverOf(p, decisions.GetValueOrDefault(p.PointId)))
                .Where(c => c is { } caver && !roster.Contains(caver))
                .Select(c => c!.Value)
                .Distinct()];

        return TypedResults.Ok(new SpeleolocImportPreviewDto(
            [.. pageItems.Select(p => ToDto(p, decisions))],
            page,
            pageSize,
            resolution.Points.Count,
            selectable,
            resolution.DeviceUsers,
            resolution.UnmappedDeviceUsers,
            resolution.KnownLinks,
            offRoster,
            resolution.ModelUsable,
            resolution.Points.Count(p => p.State == SpeleolocPointState.Proposed),
            resolution.Points.Count(p => p.State != SpeleolocPointState.Proposed),
            recording.Title,
            recording.StartedAt,
            recording.EndedAt,
            recording.DocumentCount));
    }

    // ---------- the confirmation ----------

    private static async Task<Results<Ok<SpeleolocImportCommitResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> CommitAsync(
        Guid fileId,
        SpeleolocImportCommitRequest request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        SpeleolocTripImportCommitService commits,
        IOptions<ImportLimitOptions> limits,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var options = request.Options!;
        if (RefuseCreate(ctx, options) is { } refusal)
        {
            return refusal;
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (target, problemOfTarget) = await CommitTargetAsync(options, db, access, protection, ctx, ct);
        if (problemOfTarget is not null)
        {
            return problemOfTarget;
        }

        var stored = ReadDecisions(await SessionDecisionsAsync(db, file!.Id, ctx.UserId, ct));
        var decisions = request.Decisions is null
            ? stored
            : Merged(stored, request.Decisions);

        // An account under review has nothing it commits land unreviewed: the confirmation is kept
        // with the decisions it was made under, with a waiting line for the trip it would create and
        // one per scan it would record, and a reviewer's acceptance records them. The saved review
        // and the archive stay as they are.
        if (await UploadReviewHold.AppliesToAsync(db, ctx.UserId, ct))
        {
            return await HoldAsync(
                db, commits, access, limits, ctx, new HeldSpeleolocImportRequest(file.Id, null, options, request.PointIds!, decisions),
                archive: true, target.Cave.Id, tripTitle: null, new Dictionary<string, DateTimeOffset>(), ct);
        }

        try
        {
            return Committed(await commits.CommitAsync(
                file,
                options,
                target.Model,
                target.Cave,
                target.Trip,
                target.Tracking,
                request.PointIds!,
                decisions,
                ctx,
                ct));
        }
        catch (Exception e) when (e is SpeleolocArchiveException or SpeleolocImportCommitException)
        {
            return await CommitProblemAsync(db, access, ctx, e);
        }
    }

    private static async Task<Results<Ok<SpeleolocImportCommitResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> CommitRecordingAsync(
        Guid recordingId,
        SpeleolocImportCommitRequest request,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        SpeleolocTripImportCommitService commits,
        IOptions<ImportLimitOptions> limits,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var options = request.Options!;
        if (RefuseCreate(ctx, options) is { } refusal)
        {
            return refusal;
        }

        var (recording, points, problem) = await LoadKeptRecordingAsync(recordingId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (target, problemOfTarget) = await CommitTargetAsync(options, db, access, protection, ctx, ct);
        if (problemOfTarget is not null)
        {
            return problemOfTarget;
        }

        if (await UploadReviewHold.AppliesToAsync(db, ctx.UserId, ct))
        {
            return await HoldAsync(
                db, commits, access, limits, ctx,
                new HeldSpeleolocImportRequest(
                    null, recording!.Id, options, request.PointIds!,
                    ReadDecisions(ImportJson.Serialize(request.Decisions ?? new Dictionary<string, SpeleolocPointDecision>()))),
                archive: false, target.Cave.Id, recording.Title,
                points.ToDictionary(p => p.Id.ToString(), p => p.ScannedAt), ct);
        }

        try
        {
            return Committed(await commits.CommitRecordingAsync(
                recording!,
                points,
                options,
                target.Model,
                target.Cave,
                target.Trip,
                target.Tracking,
                request.PointIds!,
                ReadDecisions(ImportJson.Serialize(request.Decisions ?? new Dictionary<string, SpeleolocPointDecision>())),
                ctx,
                ct));
        }
        catch (Exception e) when (e is SpeleolocArchiveException or SpeleolocImportCommitException)
        {
            return await CommitProblemAsync(db, access, ctx, e);
        }
    }

    /// <summary>
    /// Keeps a confirmation of an account under review for a reviewer, after every check the
    /// route makes and the recording's own: one that already has its report, or that this account
    /// may not change, is refused now as the commit would refuse it, and asked again when a
    /// reviewer accepts.
    /// </summary>
    private static async Task<Results<Ok<SpeleolocImportCommitResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> HoldAsync(
        SilexGisDbContext db,
        SpeleolocTripImportCommitService commits,
        IAccessService access,
        IOptions<ImportLimitOptions> limits,
        AccessContext ctx,
        HeldSpeleolocImportRequest request,
        bool archive,
        Guid caveId,
        string? tripTitle,
        IReadOnlyDictionary<string, DateTimeOffset> scannedAt,
        CancellationToken ct)
    {
        if (request.PointIds.Count == 0)
        {
            return ApiProblems.BadRequest(SpeleolocImportCodes.SelectionEmpty, "No scans were chosen.");
        }

        if (request.PointIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() > limits.Value.MaxCommitItems)
        {
            return ApiProblems.BadRequest(
                SpeleolocImportCodes.SelectionTooLarge,
                $"A single confirmation records at most {limits.Value.MaxCommitItems} positions.");
        }

        var recordingId = request.RecordingId
            ?? (Guid.TryParse(request.Options.TripUuid, out var parsed) ? parsed : (Guid?)null);
        if (recordingId is null)
        {
            return ApiProblems.BadRequest(SpeleolocImportCodes.RecordingNotFound, "No recording was chosen.");
        }

        try
        {
            await commits.CheckHeldAsync(recordingId.Value, ctx, ct);
        }
        catch (SpeleolocImportCommitException e)
        {
            return await CommitProblemAsync(db, access, ctx, e);
        }

        // Every chosen scan set aside is refused as the commit refuses it: held, the confirmation
        // would leave a reviewer at most a trip with nothing on it, which the commit never makes.
        var candidates = SpeleolocHeldImportHandler.CandidatesOf(
            request.Options, request.PointIds, request.Decisions, caveId, tripTitle, scannedAt);
        if (!candidates.Any(c => c.Key != SpeleolocTripImportCommitService.TripKey))
        {
            return ApiProblems.BadRequest(
                SpeleolocImportCodes.NothingCreated, "Every chosen scan was set aside, so nothing would be recorded.");
        }

        var held = new ImportBatch
        {
            Source = ImportSource.SpeleolocArchive,
            TripLogId = request.Options.TripLogId,
            ConfirmedByUserId = ctx.UserId,
            Mode = ImportBatchMode.Reviewed,
            Options = ImportJson.Serialize(request.Options),
        };
        await HeldFileImport.HoldAsync(
            db, held, ImportJson.Serialize(request),
            archive ? SpeleolocHeldImportHandler.ArchiveSection : SpeleolocHeldImportHandler.RecordingSection,
            candidates,
            ct);
        return TypedResults.Ok(new SpeleolocImportCommitResultDto(
            held.Id, request.Options.TripLogId, false, 0, 0, [], Held: true));
    }

    /// <summary>Where a confirmation's positions go, and the model they are placed on.</summary>
    internal sealed record CommitTarget(TripLog? Trip, Domain.Entities.TripTracking? Tracking, SurveyModel Model, Feature Cave);

    /// <summary>
    /// The trip and the model a confirmation writes to, guarded. Onto a trip this caller may write —
    /// 404 for one they were never shown, 403 for one they may read and not run, which is the
    /// masking every trip write route uses — or into one the confirmation creates, which the create
    /// rights already decided.
    /// </summary>
    internal static async Task<(CommitTarget Target, ProblemHttpResult? Problem)> CommitTargetAsync(
        SpeleolocImportOptions options,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        CancellationToken ct)
    {
        TripLog? trip = null;
        if (options.TripLogId is { } tripLogId)
        {
            trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
            if (await WriteGuardAsync(access, ctx, trip, ct) is { } denied)
            {
                return (null!, denied);
            }
        }
        else if (!options.CreateTrip)
        {
            return (null!, ApiProblems.BadRequest(
                SpeleolocImportCodes.TripMissing,
                "A recording is recorded onto a trip, or into one this confirmation creates."));
        }

        if (options.SurveyModelId is null)
        {
            return (null!, ApiProblems.Conflict(
                SpeleolocImportCodes.ModelMissing, "A position is a station of a survey model, and none was chosen."));
        }

        var usable = await ModelUsableAsync(db, access, protection, ctx, options.SurveyModelId, ct);
        if (usable is null)
        {
            return (null!, ApiProblems.Conflict(
                SpeleolocImportCodes.ModelUnavailable,
                "The survey model does not exist here, or its cave cannot be placed by this account."));
        }

        var tracking = trip is null
            ? null
            : await db.TripTrackings.FirstOrDefaultAsync(t => t.TripLogId == trip.Id, ct);
        return (new CommitTarget(trip, tracking, usable.Value.Model, usable.Value.Cave), null);
    }

    private static Ok<SpeleolocImportCommitResultDto> Committed(SpeleolocImportCommitResult result) =>
        TypedResults.Ok(new SpeleolocImportCommitResultDto(
            result.Batch.Id,
            result.TripLogId,
            result.CreatedTrip,
            result.CreatedEventCount,
            result.Batch.SkippedCount,
            [.. result.Failures.Select(f => new SpeleolocImportFailureDto(f.PointId, f.ScannedAt, f.Code, f.Reason))]));

    private static async Task<ProblemHttpResult> CommitProblemAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Exception error) => error switch
    {
        SpeleolocArchiveException e => ApiProblems.BadRequest(e.Code, e.Message),
        SpeleolocImportCommitException e when e.Code == CreateRules.ForbiddenCode
            || e.Code == CavingGroupBindingRules.ForbiddenCode => ApiProblems.Forbidden(e.Code),
        SpeleolocImportCommitException e when e.Code is SpeleolocImportCodes.RecordingAlreadyConverted
            or SpeleolocImportCodes.RecordingNotWritable => await HeldProblemAsync(db, access, ctx, e),
        SpeleolocImportCommitException e => ApiProblems.BadRequest(e.Code, e.Message),
        _ => throw error,
    };

    /// <summary>
    /// The refusal for a recording this installation holds and this confirmation may not make into
    /// a report: it has one already (named only to a caller who may read it), or it is not the
    /// caller's to change.
    /// </summary>
    private static async Task<ProblemHttpResult> HeldProblemAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, SpeleolocImportCommitException e) =>
        e.Code == SpeleolocImportCodes.RecordingAlreadyConverted
            ? await AlreadyConvertedProblemAsync(db, access, ctx, e.TripLogId, ct: default)
            : e.Code == SpeleolocImportCodes.RecordingNotWritable
                ? ApiProblems.Conflict(e.Code, e.Message)
                : ApiProblems.BadRequest(e.Code, e.Message);

    /// <summary>
    /// A recording a phone sent, and its live scans, read-gated. Missing, deleted and unreadable are
    /// one answer.
    /// </summary>
    internal static async Task<(TripRecording? Recording, List<TripRecordingPoint> Points, ProblemHttpResult? Problem)> LoadKeptRecordingAsync(
        Guid recordingId,
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        CancellationToken ct)
    {
        var recording = await db.TripRecordings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recordingId, ct);
        if (recording is null || !(await access.DecideAsync(ctx, AccessAction.Read, recording, ct)).Allowed)
        {
            return (null, [], ApiProblems.NotFound(SpeleolocImportCodes.KeptRecordingNotFound));
        }

        var points = await db.TripRecordingPoints.AsNoTracking()
            .Where(p => p.RecordingId == recording.Id)
            .OrderBy(p => p.ScannedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);
        return (recording, points, null);
    }

    // ---------- helpers ----------

    /// <summary>
    /// The survey model and its cave, only when the caller may both read the cave and place it.
    /// Missing, unreadable and location-closed all answer null — one shape, no leak. The same
    /// question the live tracking path asks, through the same helper, so the two surfaces cannot
    /// come to admit different people.
    /// </summary>
    private static async Task<(SurveyModel Model, Feature Cave)?> ModelUsableAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        Guid? surveyModelId,
        CancellationToken ct)
    {
        if (surveyModelId is null)
        {
            return null;
        }

        var model = await db.SurveyModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == surveyModelId, ct);
        if (model is null)
        {
            return null;
        }

        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == model.CaveFeatureId && f.Kind == FeatureKind.Cave, ct);
        if (cave is null)
        {
            return null;
        }

        return await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct) ? (model, cave) : null;
    }

    /// <summary>
    /// 404 for a trip the caller was never shown, 403 for one they may read but not run — the same
    /// masking every trip write route uses.
    /// </summary>
    private static async Task<ProblemHttpResult?> WriteGuardAsync(
        IAccessService access, AccessContext ctx, TripLog? trip, CancellationToken ct)
    {
        if (trip is null)
        {
            return ApiProblems.NotFound(SpeleolocImportCodes.TripNotFound);
        }

        if ((await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return null;
        }

        return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
            ? ApiProblems.Forbidden()
            : ApiProblems.NotFound(SpeleolocImportCodes.TripNotFound);
    }

    /// <summary>
    /// The refusal naming the report the recording became — named only to a caller who may read
    /// it. The caller holds the recording, so being told that it has a report says nothing they do
    /// not have; which trip it is, and so that a trip they were never shown exists, is the trip's
    /// own audience's to know.
    /// </summary>
    private static async Task<ProblemHttpResult> AlreadyConvertedProblemAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid? tripLogId, CancellationToken ct)
    {
        Guid? named = null;
        if (tripLogId is { } id)
        {
            var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is not null && (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
            {
                named = id;
            }
        }

        return ApiProblems.Conflict(
            SpeleolocImportCodes.RecordingAlreadyConverted,
            "This recording has already been made into a trip report, so its positions are already recorded.",
            "tripLogId",
            named);
    }

    private static ProblemHttpResult? RefuseCreate(AccessContext ctx, SpeleolocImportOptions options) =>
        SpeleolocImportCreateRights.Refusal(ctx, options) is { } refusal
            ? ApiProblems.Forbidden(refusal.Code)
            : null;

    /// <summary>
    /// The saved review, overlaid with whatever the confirming screen sent. The saved half holds a
    /// decision made on a page the request is not carrying; the sent half wins where both speak,
    /// because it is the one the person is looking at.
    /// </summary>
    private static Dictionary<string, SpeleolocPointDecision> Merged(
        Dictionary<string, SpeleolocPointDecision> stored,
        IReadOnlyDictionary<string, SpeleolocPointDecision> sent)
    {
        foreach (var (pointId, decision) in sent)
        {
            stored[pointId] = decision;
        }

        return stored;
    }

    private static SpeleolocPointDto ToDto(
        SpeleolocPointResolution point, IReadOnlyDictionary<string, SpeleolocPointDecision> decisions) => new(
        point.PointId,
        point.ScannedAt,
        point.Notes,
        point.PlaceId,
        point.PlaceTitle,
        point.PlaceDepthM,
        point.DeviceUserId,
        point.CaverId,
        point.State,
        point.Candidates,
        decisions.GetValueOrDefault(point.PointId));

    private static async Task<string> SessionDecisionsAsync(
        SilexGisDbContext db, Guid fileId, Guid userId, CancellationToken ct) =>
        await db.SpeleolocImportSessions.AsNoTracking()
            .Where(s => s.StoredFileId == fileId && s.UserId == userId)
            .Select(s => s.Decisions)
            .FirstOrDefaultAsync(ct) ?? "{}";

    private static SpeleolocImportOptions? ReadOptions(string json)
    {
        try
        {
            return ImportJson.Deserialize<SpeleolocImportOptions>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decisions keyed by the scan's own identifier. A key that is not one is dropped rather than
    /// refused — a stale saved review must not make the archive unopenable.
    /// </summary>
    internal static Dictionary<string, SpeleolocPointDecision> ReadDecisions(string json)
    {
        var decisions = new Dictionary<string, SpeleolocPointDecision>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var raw = ImportJson.Deserialize<Dictionary<string, SpeleolocPointDecision>>(json) ?? [];
            foreach (var (key, value) in raw)
            {
                if (value is not null && Guid.TryParse(key, out var id))
                {
                    decisions[id.ToString()] = value;
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return decisions;
    }

    /// <summary>
    /// Read-gated fetch of the archive being reviewed. Unreadable and missing are the same answer:
    /// an id that answers differently from one that does not exist is an id anybody can go looking
    /// for.
    /// </summary>
    internal static async Task<(StoredFile? File, ProblemHttpResult? Problem)> LoadReadableAsync(
        Guid fileId,
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        CancellationToken ct)
    {
        var (files, _) = await PhotoImportEndpoints.ReadableFilesAsync(db, access, ctx, [fileId], ct);
        return files.Count == 0
            ? (null, ApiProblems.NotFound(SpeleolocImportCodes.FileNotFound))
            : (files[0], null);
    }
}
