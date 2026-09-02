// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// The path from an uploaded file to caves, entrances and features — reviewed, not blindly
/// created.
///
/// <para>
/// Nothing here commits until <c>commit</c> is called. The candidates are the file's own
/// already-parsed rows read through a rule set, so a preview is a scan rather than a write, and
/// a review that is abandoned leaves the registry exactly as it was. What the reviewer decides
/// is kept in a session of their own, because four hundred waypoints take a while to go through
/// and a review a closed tab throws away is a review nobody finishes.
/// </para>
/// <para>
/// One protection rule runs through all of it. The candidate coordinates are shown exactly —
/// they are the importer's own file. The <em>duplicate</em> answer is not: "there is already
/// something within twelve metres" is a position, and it is only ever computed against features
/// whose exact location this caller may already see.
/// </para>
/// </summary>
public static class StagedImportEndpoints
{
    public static RouteGroupBuilder MapStagedImportEndpoints(this RouteGroupBuilder api)
    {
        var import = api.MapGroup("/geofiles/{geofileId:guid}/import").WithTags("Import");

        import.MapGet("/session", GetSessionAsync)
            .WithSummary("The caller's review of this file, resumed where they left it.");
        import.MapPut("/session", SaveSessionAsync).WithValidation<ImportSessionWriteRequest>()
            .WithSummary("Saves the review as the reviewer works; nothing is created.");
        import.MapPost("/preview", PreviewAsync).WithValidation<ImportPreviewRequest>()
            .WithSummary("The dry run: what each rule claims, and the candidate list, with nothing committed.");
        import.MapPost("/commit", CommitAsync).WithValidation<ImportCommitRequest>()
            .WithSummary(
                "Queues the selected candidates to be created as one revertible batch, and answers "
                + "with the job to watch and the batch they will appear in. Everything that can "
                + "refuse the confirmation is decided before it is queued.");

        return api;
    }

    // ---------- session ----------

    private static async Task<Results<Ok<ImportSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetSessionAsync(
        Guid geofileId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TermRuleSetStore ruleSets,
        IAppSettingsService settings,
        CancellationToken ct)
    {
        var (ctx, geofile, problem) = await LoadReadableAsync(geofileId, db, access, accessAccessor, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var importSettings = await settings.GetImportAsync(ct);
        var session = await db.GeofileImportSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.GeofileId == geofile!.Id && s.UserId == ctx.UserId, ct);
        if (session is not null)
        {
            return TypedResults.Ok(new ImportSessionDto(
                geofile!.Id,
                ReadOptions(session.Options) ?? await DefaultOptionsAsync(ctx, ruleSets, importSettings, ct),
                ReadStoredDecisions(session.Decisions),
                importSettings.AllowCreateWithoutReview,
                session.UpdatedAt));
        }

        // No session yet: the answer is the options a first review would start from, not a
        // 404. Nothing is written — a review the reviewer has not begun is not a row.
        return TypedResults.Ok(new ImportSessionDto(
            geofile!.Id,
            await DefaultOptionsAsync(ctx, ruleSets, importSettings, ct),
            new Dictionary<string, ImportDecision>(),
            importSettings.AllowCreateWithoutReview,
            null));
    }

    private static async Task<Results<Ok<ImportSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSessionAsync(
        Guid geofileId,
        ImportSessionWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IAppSettingsService settings,
        CancellationToken ct)
    {
        var (ctx, geofile, problem) = await LoadReadableAsync(geofileId, db, access, accessAccessor, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var session = await db.GeofileImportSessions
            .FirstOrDefaultAsync(s => s.GeofileId == geofile!.Id && s.UserId == ctx.UserId, ct);
        if (session is null)
        {
            session = new GeofileImportSession { GeofileId = geofile!.Id, UserId = ctx.UserId };
            db.GeofileImportSessions.Add(session);
        }

        session.Options = ImportJson.Serialize(request.Options);
        session.Decisions = ImportJson.Serialize(request.Decisions);
        await db.SaveChangesAsync(ct);

        var importSettings = await settings.GetImportAsync(ct);
        return TypedResults.Ok(new ImportSessionDto(
            geofile!.Id,
            request.Options,
            request.Decisions,
            importSettings.AllowCreateWithoutReview,
            session.UpdatedAt));
    }

    // ---------- dry run ----------

    private static async Task<Results<Ok<ImportPreviewDto>, UnauthorizedHttpResult, ProblemHttpResult>> PreviewAsync(
        Guid geofileId,
        ImportPreviewRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TermRuleSetStore ruleSets,
        ImportCandidateService candidateService,
        CancellationToken ct)
    {
        var (ctx, geofile, problem) = await LoadReadableAsync(geofileId, db, access, accessAccessor, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (geofile!.ImportStatus != GeofileImportStatus.Imported)
        {
            return ApiProblems.BadRequest(
                "import.file_not_ready",
                "The upload has not finished being read yet, so there is nothing to review.");
        }

        var ruleSet = await ruleSets.ResolveAsync(ctx, request.Options.TermRuleSetId, ct);
        if (request.Options.TermRuleSetId is not null && ruleSet is null)
        {
            return ApiProblems.NotFound("term_rules.not_found");
        }

        var rules = TermRuleSetStore.RulesOf(ruleSet);
        var (scanned, truncated, fileRows) = await candidateService.ScanAsync(geofile.Id, rules, request.Options, ct);

        var filtered = Filter(scanned, request).ToList();
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var details = await candidateService.HydrateAsync(geofile.Id, pageItems, request.Options, ctx, ct);
        var decisions = ReadDecisions(await SessionDecisionsAsync(db, geofile.Id, ctx.UserId, ct));

        // A row is selectable when something says what it should become — the rules, or a
        // decision the reviewer has already made, possibly several pages ago.
        var selectable = filtered
            .Where(c => c.ProposedKind is not null || decisions.GetValueOrDefault(c.SourceId)?.Kind is not null)
            .Select(c => c.SourceId)
            .ToList();

        return TypedResults.Ok(new ImportPreviewDto(
            [.. details.Select(d => ToDto(d, decisions))],
            page,
            pageSize,
            filtered.Count,
            [.. filtered.Select(c => c.SourceId)],
            selectable,
            scanned.Count(c => c.Geometry == CandidateGeometry.Point),
            scanned.Count(c => c.RuleId is not null),
            scanned.Count(c => c.Geometry == CandidateGeometry.Point && c.RuleId is null),
            scanned.Count(c => c.Geometry == CandidateGeometry.Line),
            scanned.Count(c => c.Geometry == CandidateGeometry.Area),
            truncated,
            fileRows,
            scanned.Count,
            [.. ImportCandidateService.HitsOf(scanned, rules).Select(h => new RuleHitDto(h.RuleId, h.RuleName, h.Count))],
            ruleSet is null ? null : ruleSet.ToDto(rules.Count, TermRuleSetRules.MayEdit(ctx, ruleSet), TermRuleSetRules.MayDelete(ctx, ruleSet))));
    }

    // ---------- confirmation ----------

    private static async Task<Results<Accepted<ImportCommitAcceptedDto>, UnauthorizedHttpResult, ProblemHttpResult>> CommitAsync(
        Guid geofileId,
        ImportCommitRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TermRuleSetStore ruleSets,
        ImportCandidateService candidateService,
        IOptions<ImportLimitOptions> limits,
        IAppSettingsService settings,
        CancellationToken ct)
    {
        var (ctx, geofile, problem) = await LoadReadableAsync(geofileId, db, access, accessAccessor, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (geofile!.ImportStatus != GeofileImportStatus.Imported)
        {
            return ApiProblems.BadRequest("import.file_not_ready", "The upload has not finished being read yet.");
        }

        // Creating is decided here, once, before anything is scanned: an import binds every
        // object it creates to the same group, so one answer covers the batch. The commit
        // service asks again per object, which is what catches a group the caller may read but
        // not bind to.
        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, request.Options.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (request.Options.CavingGroupId is { } groupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, groupId))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        var ruleSet = await ruleSets.ResolveAsync(ctx, request.Options.TermRuleSetId, ct);
        if (request.Options.TermRuleSetId is not null && ruleSet is null)
        {
            return ApiProblems.NotFound("term_rules.not_found");
        }

        var mode = ImportBatchMode.Reviewed;
        var selection = request.Selection;
        if (request.WithoutReview)
        {
            var importSettings = await settings.GetImportAsync(ct);
            if (!importSettings.AllowCreateWithoutReview)
            {
                return ApiProblems.Forbidden(
                    "import.review_required",
                    "This installation reviews imports before anything is created.");
            }

            // Everything the rules claimed, and nothing they did not: an unmatched waypoint is
            // precisely the row nobody has looked at, so creating it unreviewed would be the
            // opposite of what the setting allows.
            var rules = TermRuleSetStore.RulesOf(ruleSet);
            var (scanned, _, _) = await candidateService.ScanAsync(geofile.Id, rules, request.Options, ct);
            selection = [.. scanned.Where(c => c.ProposedKind is not null).Select(c => c.SourceId)];
            mode = ImportBatchMode.AutoCreated;
        }

        // The one refusal that belongs here rather than on the queue: it is a property of the
        // selection the caller just made, so it can be answered now, and a job queued only to
        // fail on it would report a ceiling the reviewer could have been told about instantly.
        if (selection.Count > limits.Value.MaxCommitItems)
        {
            return ApiProblems.BadRequest(
                "import.selection_too_large",
                $"A single confirmation creates at most {limits.Value.MaxCommitItems} objects; "
                + $"{selection.Count} were selected. Confirm them in smaller batches — each one reverts on its own.");
        }

        // The address of the result, decided before the work starts. Everything that could
        // refuse this has been decided above, so what remains is work rather than judgement and
        // the reviewer can be sent straight to the batch it will appear in.
        var batchId = Guid.CreateVersion7();
        var job = new ProcessingJob
        {
            Kind = ProcessingJobKinds.ImportCommit,
            RequestedBy = ctx.UserId,
            Payload = ImportJson.Serialize(new ImportCommitPayload(
                geofile.Id,
                batchId,
                ctx.UserId,
                ruleSet?.Id,
                ImportJson.Serialize(request.Options),
                ImportJson.Serialize(ReadDecisions(request.Decisions)),
                selection,
                mode)),
        };
        db.ProcessingJobs.Add(job);
        await db.SaveChangesAsync(ct);

        return TypedResults.Accepted(
            $"/api/v1/jobs/{job.Id}",
            new ImportCommitAcceptedDto(job.Id, batchId, selection.Count));
    }

    // ---------- helpers ----------

    /// <summary>
    /// Filters the scanned rows the way the review table asks. Applied in memory over the
    /// scan, because the scan already holds every row's classification and re-deriving it in
    /// SQL would mean a second, drifting copy of the rules.
    /// </summary>
    private static IEnumerable<CandidateSummary> Filter(
        IReadOnlyList<CandidateSummary> scanned, ImportPreviewRequest request)
    {
        IEnumerable<CandidateSummary> query = scanned;

        if (request.Geometry is { } geometry)
        {
            query = query.Where(c => c.Geometry == geometry);
        }

        if (!string.IsNullOrWhiteSpace(request.Rule))
        {
            query = string.Equals(request.Rule, "none", StringComparison.OrdinalIgnoreCase)
                ? query.Where(c => c.RuleId is null)
                : query.Where(c => c.RuleId == request.Rule);
        }

        if (request.Kind is { } kind)
        {
            query = query.Where(c => c.ProposedKind == kind);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var needle = FoldedText.Of(request.Search).Value;
            query = query.Where(c =>
                FoldedText.Of(c.SourceName).Value.Contains(needle, StringComparison.Ordinal)
                || FoldedText.Of(c.SourceDescription).Value.Contains(needle, StringComparison.Ordinal));
        }

        return query;
    }

    private static async Task<ImportOptions> DefaultOptionsAsync(
        AccessContext ctx, TermRuleSetStore ruleSets, ImportSettings settings, CancellationToken ct)
    {
        var set = await ruleSets.ResolveDefaultAsync(ctx, ct);
        return new ImportOptions
        {
            TermRuleSetId = set?.Id,
            DuplicateRadiusMeters = settings.DuplicateRadiusMeters,
        };
    }

    private static async Task<string> SessionDecisionsAsync(
        SilexGisDbContext db, Guid geofileId, Guid userId, CancellationToken ct) =>
        await db.GeofileImportSessions.AsNoTracking()
            .Where(s => s.GeofileId == geofileId && s.UserId == userId)
            .Select(s => s.Decisions)
            .FirstOrDefaultAsync(ct) ?? "{}";

    private static ImportCandidateDto ToDto(
        CandidateDetail detail, IReadOnlyDictionary<long, ImportDecision> decisions)
    {
        var summary = detail.Summary;
        return new ImportCandidateDto(
            summary.SourceId,
            summary.Geometry,
            summary.SourceName,
            summary.SourceDescription,
            summary.SourceCode,
            summary.SourceElevation,
            summary.RuleId,
            summary.RuleName,
            summary.ConflictingRuleNames,
            summary.ProposedKind,
            summary.ProposedCaveTypeCode,
            summary.ProposedEntranceTypeCode,
            summary.ProposedFeatureTypeCode,
            summary.ProposedName,
            // Only the point: a track's own shape is drawn by the file's map layer, which
            // already exists, and sending it here would put tens of thousands of vertices into
            // a page of a table.
            detail.Geom is NetTopologySuite.Geometries.Point point ? GeoJsonGeometry.From(point) : null,
            detail.Duplicate is { } duplicate
                ? new ImportDuplicateDto(
                    duplicate.FeatureId,
                    duplicate.Name,
                    duplicate.Kind,
                    Math.Round(duplicate.DistanceMeters, 1),
                    Math.Round(duplicate.NameSimilarity, 3),
                    duplicate.CaveFeatureId,
                    duplicate.CaveName)
                : null,
            decisions.GetValueOrDefault(summary.SourceId));
    }

    internal static ImportBatchDto ToDto(ImportBatch batch, string? geofileName, bool canRevert) => new(
        batch.Id,
        batch.Source,
        batch.GeofileId,
        geofileName,
        batch.TripLogId,
        batch.TermRuleSetId,
        batch.TermRuleSetName,
        batch.ConfirmedByUserId,
        batch.Mode,
        batch.CreatedCount,
        batch.AttachedCount,
        batch.SkippedCount,
        batch.CreatedAt,
        batch.RevertedAt,
        batch.RevertedByUserId,
        canRevert && !batch.IsReverted,
        batch.Failures is null
            ? []
            : ImportJson.Deserialize<List<ImportFailure>>(batch.Failures) is { } read
                ? [.. read.Select(f => new ImportFailureDto(f.SourceId, f.Name, f.Code, f.Reason))]
                : []);

    private static ImportOptions? ReadOptions(string json)
    {
        try
        {
            return ImportJson.Deserialize<ImportOptions>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decisions keyed by source row. The wire form keys by string because JSON object keys are
    /// strings; a key that is not a row id is dropped rather than refused — a stale saved
    /// review must not make the file unopenable.
    /// </summary>
    internal static Dictionary<long, ImportDecision> ReadDecisions(string json) =>
        ReadDecisions(ReadStoredDecisions(json));

    /// <summary>The saved review in its wire form, keyed as JSON keys are.</summary>
    private static Dictionary<string, ImportDecision> ReadStoredDecisions(string json)
    {
        try
        {
            return ImportJson.Deserialize<Dictionary<string, ImportDecision>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static Dictionary<long, ImportDecision> ReadDecisions(
        IReadOnlyDictionary<string, ImportDecision> raw)
    {
        var decisions = new Dictionary<long, ImportDecision>();
        foreach (var (key, value) in raw)
        {
            if (long.TryParse(key, out var sourceId))
            {
                decisions[sourceId] = value;
            }
        }

        return decisions;
    }

    /// <summary>Read-gated fetch of the file being reviewed; unreadable and missing are both 404.</summary>
    private static async Task<(AccessContext? Ctx, Geofile? Geofile, ProblemHttpResult? Problem)> LoadReadableAsync(
        Guid geofileId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == geofileId, ct);
        if (ctx is null || geofile is null || !(await access.DecideAsync(ctx, AccessAction.Read, geofile, ct)).Allowed)
        {
            return (ctx, null, ApiProblems.NotFound("geofile.not_found"));
        }

        return (ctx, geofile, null);
    }
}
