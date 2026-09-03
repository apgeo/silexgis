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
using SilexGis.Domain.Import.TripCsv;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// The path from a club's trip spreadsheet to trips it can read — reviewed, not blindly created.
///
/// <para>
/// The upload itself is the ordinary file route: what arrives is a stored file like any other, and
/// the review points at it. Nothing between here and confirmation is written except the review, so
/// a sheet somebody opened and thought better of leaves the installation exactly as it was.
/// </para>
/// <para>
/// The sheet is read again on every preview rather than staged. It carries no coordinates, so it
/// is not the kind of file the vector path stages — that path refuses a file with no position
/// column before it reads a row. And the reviewer can change what the rows mean: which header is
/// the date column, whether a numeric date is day-first, which characters separate several names
/// in one cell. A staged parse would be wrong the moment any of those moved.
/// </para>
/// </summary>
public static class TripImportEndpoints
{
    /// <summary>The upload is not there any more, or this caller may not read it.</summary>
    public const string FileNotFoundCode = "file.not_found";

    public static RouteGroupBuilder MapTripImportEndpoints(this RouteGroupBuilder api)
    {
        var import = api.MapGroup("/trip-imports/{fileId:guid}").WithTags("Import");

        import.MapGet("/session", GetSessionAsync)
            .WithSummary("The caller's review of this spreadsheet, resumed where they left it.");
        import.MapPut("/session", SaveSessionAsync).WithValidation<TripImportSessionWriteRequest>()
            .WithSummary("Saves the review as the reviewer works; nothing is created.");
        import.MapGet("/columns", GetColumnsAsync)
            .WithSummary("The sheet's header, and which field each column was taken for.");
        import.MapPost("/preview", PreviewAsync).WithValidation<TripImportPreviewRequest>()
            .WithSummary("Reads the sheet under the current choices and answers a page of it.");
        import.MapPost("/commit", CommitAsync).WithValidation<TripImportCommitRequest>()
            .WithSummary("Records the chosen rows as trips, as one batch that reverts as a unit.");

        return api;
    }

    // ---------- the review ----------

    private static async Task<Results<Ok<TripImportSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetSessionAsync(
        Guid fileId,
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

        if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        var session = await db.TripImportSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoredFileId == file!.Id && s.UserId == ctx.UserId, ct);

        // No review yet is not a 404: the answer is the options a first reading would start
        // from. Nothing is written — a review nobody has begun is not a row.
        return TypedResults.Ok(new TripImportSessionDto(
            file!.Id,
            file.OriginalName,
            session is null ? TripImportOptions.Default : ReadOptions(session.Options) ?? TripImportOptions.Default,
            session is null ? new Dictionary<string, TripImportDecision>() : ReadStoredDecisions(session.Decisions),
            session?.UpdatedAt));
    }

    private static async Task<Results<Ok<TripImportSessionDto>, UnauthorizedHttpResult, ProblemHttpResult>> SaveSessionAsync(
        Guid fileId,
        TripImportSessionWriteRequest request,
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

        // Creating is decided before the sheet is even fetched, and it is decided the same way
        // whatever the file is: the answer does not depend on the upload, so asking first tells
        // a caller who may not record trips why, rather than telling them about somebody's file.
        if (RefuseCreate(ctx, request.Options) is { } refusal)
        {
            return refusal;
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        var session = await db.TripImportSessions
            .FirstOrDefaultAsync(s => s.StoredFileId == file!.Id && s.UserId == ctx.UserId, ct);
        var creating = session is null;
        if (session is null)
        {
            session = new TripImportSession { StoredFileId = file!.Id, UserId = ctx.UserId };
            db.TripImportSessions.Add(session);
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
            // file is a unique index, and two saves that start before either has finished both
            // read nothing and both try to write the first row. That happens for real — the
            // review saves itself as somebody works through the sheet, and a second tab on the
            // same upload does it too. The loser merges into the row that won rather than
            // faulting: both requests carry the same reviewer's own decisions, so the later one
            // is simply the newer reading, which is what a save of a review means anyway.
            db.Entry(session).State = EntityState.Detached;
            session = await db.TripImportSessions
                .FirstAsync(s => s.StoredFileId == file!.Id && s.UserId == ctx.UserId, ct);
            session.Options = ImportJson.Serialize(request.Options);
            session.Decisions = ImportJson.Serialize(request.Decisions);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new TripImportSessionDto(
            file!.Id, file.OriginalName, request.Options, request.Decisions, session.UpdatedAt));
    }

    // ---------- the header ----------

    private static async Task<Results<Ok<TripImportColumnsDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetColumnsAsync(
        Guid fileId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TripCsvFileReader reader,
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

        var options = await SessionOptionsAsync(db, file!.Id, ctx.UserId, ct);
        TripCsvParseResult parsed;
        try
        {
            parsed = await reader.ParseAsync(file, options, ct);
        }
        catch (TripCsvReadException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        // The header answers even when the reading of the rows did not, because a mapping that
        // needs re-pointing is exactly what a failed reading asks somebody to do.
        return TypedResults.Ok(new TripImportColumnsDto(
            parsed.Header,
            parsed.ResolvedColumns,
            parsed.UnmappedColumns,
            [.. parsed.FileDiagnostics.Select(ToDto)]));
    }

    // ---------- the dry run ----------

    private static async Task<Results<Ok<TripImportPreviewDto>, UnauthorizedHttpResult, ProblemHttpResult>> PreviewAsync(
        Guid fileId,
        TripImportPreviewRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TripCsvFileReader reader,
        TripImportResolver resolver,
        IOptions<ImportLimitOptions> limits,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Creating is decided before the sheet is even fetched, and it is decided the same way
        // whatever the file is: the answer does not depend on the upload, so asking first tells
        // a caller who may not record trips why, rather than telling them about somebody's file.
        if (RefuseCreate(ctx, request.Options) is { } refusal)
        {
            return refusal;
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        TripCsvParseResult parsed;
        try
        {
            parsed = await reader.ParseAsync(file!, request.Options, ct);
        }
        catch (TripCsvReadException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        var ceiling = limits.Value.MaxScanRows;
        var truncated = parsed.Rows.Count > ceiling;
        var rows = truncated ? [.. parsed.Rows.Take(ceiling)] : parsed.Rows;

        // A row the reading refused is not offered for selection. It goes among the file's
        // problems instead: a row that cannot be created produces one failure line per row at
        // confirmation, and telling the reviewer afterwards teaches them nothing they could not
        // have been told first.
        var failed = rows.Where(r => r.HasError).ToList();
        var readable = rows.Where(r => !r.HasError).ToList();

        var decisions = ReadDecisions(await SessionDecisionsAsync(db, file!.Id, ctx.UserId, ct));

        // What every row would mean, worked out here and not only at the confirmation. A preview
        // that showed the sheet's own words back to the reviewer would be asking them to approve
        // something they had not been told: which names matched, which two people answer to,
        // which caves and areas the switches would invent. Over the whole readable file rather
        // than over the page, because the candidate tables below count distinct values and a
        // total that changed when somebody turned a page would be worthless.
        //
        // Nothing here writes. The resolver proposes and the caller's own reach decides what it
        // may propose, so this is as safe to run as often as the reviewer likes.
        //
        // Over the readable rows only. A row the reading refused can never be recorded, so a type
        // word or a cave name appearing on nothing else would otherwise be counted among the
        // things a confirmation would add, and the reviewer would be shown a number no
        // confirmation can produce.
        var resolution = await resolver.ResolveAsync(readable, request.Options, ctx, ct);

        var filtered = Filter(readable, request.Search).ToList();
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // What "select all" selects, answered here rather than in the browser: a row set aside on
        // the first page has to stay set aside when the ninth page is the one on screen.
        var selectable = filtered
            .Where(r => ActionOf(decisions, r) != TripImportRowAction.Skip)
            .Select(r => r.Line)
            .ToList();

        return TypedResults.Ok(new TripImportPreviewDto(
            [.. pageItems.Select(r => ToDto(r, decisions, resolution))],
            page,
            pageSize,
            filtered.Count,
            [.. filtered.Select(r => r.Line)],
            selectable,
            truncated,
            rows.Count,
            readable.Count,
            failed.Count,
            readable.Count(r => ActionOf(decisions, r) == TripImportRowAction.Skip),
            parsed.Header,
            parsed.ResolvedColumns,
            parsed.UnmappedColumns,
            parsed.DateOrder,
            parsed.DateOrderSource,
            parsed.AmbiguousDateRows,
            [.. parsed.FileDiagnostics.Concat(failed.SelectMany(r => r.Diagnostics)).Select(ToDto)],
            Proposals(resolution)));
    }

    /// <summary>
    /// What the sheet as a whole would do to the installation: every distinct name it wrote with
    /// what it was taken for, and the counts of what would be added.
    ///
    /// <para>
    /// The counts are stated even when they are zero. A screen that hides a row saying "0 caves
    /// will be created" reads exactly like a screen that has not worked it out yet, and the
    /// reviewer's whole job here is to know which of the two they are looking at.
    /// </para>
    /// </summary>
    private static TripImportProposalsDto Proposals(TripImportResolutionSet resolution) => new(
        resolution.TripTypes,
        resolution.People,
        resolution.Caves,
        resolution.Areas,
        resolution.NewTripTypes.Count,
        resolution.NewCavers.Count,
        resolution.NewCaves.Count,
        resolution.NewAreas.Count,
        resolution.People.Count(p => p.State == TripImportMatchState.Ambiguous),
        resolution.Caves.Concat(resolution.Areas).Count(f => f.State == TripImportMatchState.Ambiguous));

    // ---------- the confirmation ----------

    /// <summary>
    /// Records the chosen rows. One batch, one transaction, and one undo: the batch is taken back
    /// through the route every other import's batch is taken back through, which is why nothing
    /// here mints a second way to reverse it.
    /// </summary>
    private static async Task<Results<Ok<TripImportCommitResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> CommitAsync(
        Guid fileId,
        TripImportCommitRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TripImportCommitService commits,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Creating is decided here, once, before the sheet is fetched: an import binds every
        // trip it records to the same group, so one answer covers the batch. The commit service
        // asks again, which is what catches a group the caller may read but not bind to.
        if (RefuseCreate(ctx, request.Options) is { } refusal)
        {
            return refusal;
        }

        var (file, problem) = await LoadReadableAsync(fileId, db, access, ctx, ct);
        if (problem is not null)
        {
            return problem;
        }

        var stored = ReadDecisions(await SessionDecisionsAsync(db, file!.Id, ctx.UserId, ct));
        var decisions = request.Decisions is null
            ? stored
            : Merged(stored, ReadDecisions(ImportJson.Serialize(request.Decisions)));

        try
        {
            var result = await commits.CommitAsync(
                file, request.Options, request.Lines, decisions, ctx, ct);
            return TypedResults.Ok(new TripImportCommitResultDto(
                result.Batch.Id,
                result.CreatedTripCount,
                result.CreatedFeatureCount,
                result.Batch.SkippedCount,
                [.. result.Failures.Select(f => new TripImportFailureDto(f.Line, f.Title, f.Code, f.Reason))]));
        }
        catch (TripCsvReadException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }
        catch (TripImportCommitException e) when (e.Code == CreateRules.ForbiddenCode
            || e.Code == CavingGroupBindingRules.ForbiddenCode)
        {
            return ApiProblems.Forbidden(e.Code);
        }
        catch (TripImportCommitException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }
    }

    /// <summary>
    /// The saved review, overlaid with whatever the confirming screen sent. The saved half is
    /// what holds a decision made on a page the request is not carrying; the sent half wins where
    /// both speak, because it is the one the person is looking at.
    /// </summary>
    private static Dictionary<int, TripImportDecision> Merged(
        Dictionary<int, TripImportDecision> stored, Dictionary<int, TripImportDecision> sent)
    {
        foreach (var (line, decision) in sent)
        {
            stored[line] = decision;
        }

        return stored;
    }

    // ---------- helpers ----------

    private static TripImportRowAction ActionOf(
        IReadOnlyDictionary<int, TripImportDecision> decisions, TripCsvRow row) =>
        decisions.GetValueOrDefault(row.Line)?.Action ?? TripImportRowAction.Create;

    /// <summary>
    /// Narrows the table the way the review asks. Applied over the parsed rows rather than in
    /// SQL, because nothing about a sheet being reviewed is in the database yet.
    /// </summary>
    private static IEnumerable<TripCsvRow> Filter(IReadOnlyList<TripCsvRow> rows, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return rows;
        }

        var needle = FoldedText.Of(search).Value;
        return rows.Where(r =>
            Holds(r.Title, needle)
            || Holds(r.Massif, needle)
            || Holds(r.SubArea, needle)
            || Holds(r.TripType, needle)
            || r.Caves.Any(c => Holds(c, needle))
            || r.Participants.Any(p => Holds(p, needle))
            || r.Proposers.Any(p => Holds(p, needle)));
    }

    private static bool Holds(string? value, string needle) =>
        FoldedText.Of(value).Value.Contains(needle, StringComparison.Ordinal);

    /// <summary>
    /// Whether this caller may do what these choices ask for, asked through the one place that
    /// holds the list so that the route and the confirmation cannot disagree. An import binds
    /// every trip it records to the same group and grows the same registers whatever the file
    /// says, so one answer covers the whole of it; the commit asks again because it is the layer
    /// that knows what is being bound.
    /// </summary>
    private static ProblemHttpResult? RefuseCreate(AccessContext ctx, TripImportOptions options) =>
        TripImportCreateRights.Refusal(ctx, options) is { } refusal
            ? ApiProblems.Forbidden(refusal.Code)
            : null;

    private static TripImportProblemDto ToDto(TripCsvDiagnostic diagnostic) => new(
        diagnostic.Line,
        diagnostic.Severity,
        diagnostic.Code,
        diagnostic.Field,
        diagnostic.Column,
        diagnostic.Detail);

    private static TripImportRowDto ToDto(
        TripCsvRow row,
        IReadOnlyDictionary<int, TripImportDecision> decisions,
        TripImportResolutionSet resolution) => new(
        row.Line,
        row.SourceId,
        row.StartDate,
        row.EndDate,
        row.StartDateText,
        row.EndDateText,
        row.Title,
        row.Country,
        row.Massif,
        row.SubArea,
        row.Caves,
        row.Proposers,
        row.Participants,
        row.Details,
        row.Details2,
        row.TripType,
        row.Errors,
        row.Unmapped,
        [.. row.Diagnostics.Select(ToDto)],
        decisions.GetValueOrDefault(row.Line),
        resolution.Rows.GetValueOrDefault(row.Line));

    private static async Task<TripImportOptions> SessionOptionsAsync(
        SilexGisDbContext db, Guid fileId, Guid userId, CancellationToken ct)
    {
        var stored = await db.TripImportSessions.AsNoTracking()
            .Where(s => s.StoredFileId == fileId && s.UserId == userId)
            .Select(s => s.Options)
            .FirstOrDefaultAsync(ct);
        return (stored is null ? null : ReadOptions(stored)) ?? TripImportOptions.Default;
    }

    private static async Task<string> SessionDecisionsAsync(
        SilexGisDbContext db, Guid fileId, Guid userId, CancellationToken ct) =>
        await db.TripImportSessions.AsNoTracking()
            .Where(s => s.StoredFileId == fileId && s.UserId == userId)
            .Select(s => s.Decisions)
            .FirstOrDefaultAsync(ct) ?? "{}";

    private static TripImportOptions? ReadOptions(string json)
    {
        try
        {
            return ImportJson.Deserialize<TripImportOptions>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The saved review in its wire form, keyed as JSON keys are.</summary>
    private static Dictionary<string, TripImportDecision> ReadStoredDecisions(string json)
    {
        try
        {
            return ImportJson.Deserialize<Dictionary<string, TripImportDecision>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Decisions keyed by the row's physical line. The wire form keys by string because JSON
    /// object keys are strings; a key that is not a line number is dropped rather than refused —
    /// a stale saved review must not make the file unopenable.
    /// </summary>
    internal static Dictionary<int, TripImportDecision> ReadDecisions(string json)
    {
        var decisions = new Dictionary<int, TripImportDecision>();
        foreach (var (key, value) in ReadStoredDecisions(json))
        {
            if (int.TryParse(key, out var line) && value is not null)
            {
                decisions[line] = value;
            }
        }

        return decisions;
    }

    /// <summary>
    /// Read-gated fetch of the spreadsheet being reviewed. Unreadable and missing are the same
    /// answer: an id that answers differently from one that does not exist is an id anybody can
    /// go looking for.
    /// </summary>
    private static async Task<(StoredFile? File, ProblemHttpResult? Problem)> LoadReadableAsync(
        Guid fileId,
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        CancellationToken ct)
    {
        var (files, _) = await PhotoImportEndpoints.ReadableFilesAsync(db, access, ctx, [fileId], ct);
        return files.Count == 0 ? (null, ApiProblems.NotFound(FileNotFoundCode)) : (files[0], null);
    }
}
