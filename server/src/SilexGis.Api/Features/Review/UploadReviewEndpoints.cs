// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Review;

/// <summary>
/// The review of what accounts under review uploaded: the queue of held batches, a batch with its
/// lines, the uploader's own held batches, and the decisions — a whole batch, or one line, or the
/// accepted fields of one edit.
/// </summary>
/// <remarks>
/// <para>
/// Reading the queue and the held content is <c>UploadReview·Read</c>; deciding is
/// <c>UploadReview·Write</c>. Both are installation-wide rights: a held batch belongs to no club
/// until it is written, so there is no narrower scope a reviewer could be given. The uploader may
/// always read their own held batches, and never decide them.
/// </para>
/// <para>
/// A decision reaches beyond what it names: accepting a line accepts the waiting lines it depends
/// on, before it, and rejecting a line rejects the waiting lines that depend on it. Accepting
/// writes with the uploader's rights as they are now — a line the uploader may no longer write is
/// decided as failed, with the reason — and every held content shown here passes the same
/// position rule as everything else that shows a row.
/// </para>
/// </remarks>
public static class UploadReviewEndpoints
{
    public const string BatchNotFoundCode = "review.batch_not_found";
    public const string ItemNotFoundCode = "review.item_not_found";
    public const string NotPendingCode = "review.not_pending";
    public const string ChangedSinceCode = "review.changed_since";
    public const string FieldsUnsupportedCode = "review.fields_unsupported";
    public const string OwnBatchCode = "review.own_batch";
    public const string SourceUnsupportedCode = "review.source_unsupported";

    public static void MapUploadReviewEndpoints(this RouteGroupBuilder api)
    {
        var review = api.MapGroup("/review").WithTags("Review");

        review.MapGet("/batches", QueueAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("The held batches waiting for a reviewer, oldest first.");
        review.MapGet("/mine", MineAsync)
            .WithSummary("The caller's own held batches, waiting and decided, newest first.");
        review.MapGet("/batches/{id:guid}", DetailAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One held batch with its lines, as the caller may be shown them.");
        review.MapPost("/batches/{id:guid}/accept", AcceptBatchAsync)
            .WithValidation<ReviewBatchDecisionRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Accepts every waiting line of a held batch, with what they depend on.");
        review.MapPost("/batches/{id:guid}/reject", RejectBatchAsync)
            .WithValidation<ReviewBatchDecisionRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Rejects every waiting line of a held batch, and what depends on them.");
        review.MapPost("/items/{reviewId:guid}/accept", AcceptItemAsync)
            .WithValidation<ReviewItemDecisionRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Accepts one held line, or the named fields of one held edit, with what it depends on.");
        review.MapPost("/items/{reviewId:guid}/reject", RejectItemAsync)
            .WithValidation<ReviewItemDecisionRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Rejects one held line, and the waiting lines that depend on it.");
    }

    /// <summary>
    /// Review is somebody else looking at what an account sent, so nobody decides their own
    /// uploads — a reviewer whose own account is under review included.
    /// </summary>
    private static ProblemHttpResult OwnBatch() =>
        ApiProblems.Forbidden(OwnBatchCode, "Uploads are decided by somebody other than the account that sent them.");

    private static bool MayRead(AccessContext ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.UploadReview, AccessAction.Read, null).Allowed;

    private static bool MayDecide(AccessContext ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.UploadReview, AccessAction.Write, null).Allowed;

    private static async Task<Results<Ok<PagedResult<HeldBatchDto>>, UnauthorizedHttpResult, ProblemHttpResult>> QueueAsync(
        int? page, int? pageSize, SilexGisDbContext db, IAccessContextAccessor accessor, CancellationToken ct)
    {
        var ctx = await accessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!MayRead(ctx))
        {
            return ApiProblems.Forbidden();
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.ImportBatches.AsNoTracking()
            .Where(b => b.ReviewState == ImportReviewState.Pending
                && db.ImportBatchItems.Any(i => i.ImportBatchId == b.Id && i.ReviewDecision == ImportReviewDecision.Pending))
            .OrderBy(b => b.HeldAt).ThenBy(b => b.Id);
        var total = await query.CountAsync(ct);
        var batches = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return TypedResults.Ok(new PagedResult<HeldBatchDto>(await SummariesAsync(db, batches, ctx, ct), p, size, total));
    }

    private static async Task<Results<Ok<PagedResult<HeldBatchDto>>, UnauthorizedHttpResult>> MineAsync(
        int? page, int? pageSize, SilexGisDbContext db, IAccessContextAccessor accessor, CancellationToken ct)
    {
        var ctx = await accessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.ImportBatches.AsNoTracking()
            .Where(b => b.ConfirmedByUserId == ctx.UserId && b.ReviewState != ImportReviewState.NotHeld)
            .OrderByDescending(b => b.HeldAt).ThenByDescending(b => b.Id);
        var total = await query.CountAsync(ct);
        var batches = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return TypedResults.Ok(new PagedResult<HeldBatchDto>(await SummariesAsync(db, batches, ctx, ct), p, size, total));
    }

    private static async Task<Results<Ok<HeldBatchDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> DetailAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessor,
        IEnumerable<IHeldBatchHandler> handlers,
        CancellationToken ct)
    {
        var ctx = await accessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Somebody else's held batch is answered like one that is not there to a caller who may
        // not read the queue: that an account has uploads waiting is itself held content.
        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && b.ReviewState != ImportReviewState.NotHeld, ct);
        if (batch is null || (batch.ConfirmedByUserId != ctx.UserId && !MayRead(ctx)))
        {
            return ApiProblems.NotFound(BatchNotFoundCode);
        }

        var lines = await db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == id && i.ReviewDecision != null)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);
        var summary = (await SummariesAsync(db, [batch], ctx, ct))[0];
        return TypedResults.Ok(new HeldBatchDetailDto(summary, await LinesAsync(db, handlers, batch.Source, lines, ctx, ct)));
    }

    private static Task<Results<Ok<ReviewDecisionResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> AcceptBatchAsync(
        Guid id,
        ReviewBatchDecisionRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessor,
        IEnumerable<IHeldBatchHandler> handlers,
        UploadReviewService review,
        CancellationToken ct) =>
        DecideBatchAsync(id, request, accept: true, db, accessor, handlers, review, ct);

    private static Task<Results<Ok<ReviewDecisionResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> RejectBatchAsync(
        Guid id,
        ReviewBatchDecisionRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessor,
        IEnumerable<IHeldBatchHandler> handlers,
        UploadReviewService review,
        CancellationToken ct) =>
        DecideBatchAsync(id, request, accept: false, db, accessor, handlers, review, ct);

    private static async Task<Results<Ok<ReviewDecisionResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> DecideBatchAsync(
        Guid id,
        ReviewBatchDecisionRequest request,
        bool accept,
        SilexGisDbContext db,
        IAccessContextAccessor accessor,
        IEnumerable<IHeldBatchHandler> handlers,
        UploadReviewService review,
        CancellationToken ct)
    {
        var ctx = await accessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && b.ReviewState != ImportReviewState.NotHeld, ct);
        if (batch is null || (batch.ConfirmedByUserId != ctx.UserId && !MayRead(ctx)))
        {
            return ApiProblems.NotFound(BatchNotFoundCode);
        }

        if (!MayDecide(ctx))
        {
            return ApiProblems.Forbidden();
        }

        if (batch.ConfirmedByUserId == ctx.UserId)
        {
            return OwnBatch();
        }

        var waiting = await db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == id && i.ReviewDecision == ImportReviewDecision.Pending)
            .Select(i => i.Id)
            .ToListAsync(ct);
        if (waiting.Count == 0)
        {
            return ApiProblems.Conflict(NotPendingCode, "Nothing in this batch is waiting for a decision.");
        }

        var outcome = await DecideAsync(
            db, handlers, review, ctx, batch, waiting, new Dictionary<long, IReadOnlyList<string>>(),
            request.Note, request.OverCurrent ?? false, accept, reopen: false, ct);
        if (outcome.Result is { } problem)
        {
            return problem;
        }

        // The note on a whole batch is the batch's own, for the uploader to read beside it.
        if (request.Note is not null)
        {
            await db.ImportBatches.Where(b => b.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(b => b.ReviewNote, request.Note), ct);
        }

        return TypedResults.Ok(await ResultAsync(db, handlers, batch.Source, outcome.Decided!, ctx, ct));
    }

    private static Task<Results<Ok<ReviewDecisionResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> AcceptItemAsync(
        Guid reviewId,
        ReviewItemDecisionRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessor,
        IEnumerable<IHeldBatchHandler> handlers,
        UploadReviewService review,
        CancellationToken ct) =>
        DecideItemAsync(reviewId, request, accept: true, db, accessor, handlers, review, ct);

    private static Task<Results<Ok<ReviewDecisionResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> RejectItemAsync(
        Guid reviewId,
        ReviewItemDecisionRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessor,
        IEnumerable<IHeldBatchHandler> handlers,
        UploadReviewService review,
        CancellationToken ct) =>
        DecideItemAsync(reviewId, request, accept: false, db, accessor, handlers, review, ct);

    private static async Task<Results<Ok<ReviewDecisionResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> DecideItemAsync(
        Guid reviewId,
        ReviewItemDecisionRequest request,
        bool accept,
        SilexGisDbContext db,
        IAccessContextAccessor accessor,
        IEnumerable<IHeldBatchHandler> handlers,
        UploadReviewService review,
        CancellationToken ct)
    {
        var ctx = await accessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await (from item in db.ImportBatchItems.AsNoTracking()
                           join b in db.ImportBatches.AsNoTracking() on item.ImportBatchId equals b.Id
                           where item.ReviewId == reviewId
                           select new { Item = item, Batch = b }).FirstOrDefaultAsync(ct);
        if (found is null || (found.Batch.ConfirmedByUserId != ctx.UserId && !MayRead(ctx)))
        {
            return ApiProblems.NotFound(ItemNotFoundCode);
        }

        if (!MayDecide(ctx))
        {
            return ApiProblems.Forbidden();
        }

        if (found.Batch.ConfirmedByUserId == ctx.UserId)
        {
            return OwnBatch();
        }

        // A rejected line can be accepted after all, while its bytes are still kept and no later
        // upload replaced it; the service says whether that is still so.
        var reopen = accept && found.Item.ReviewDecision == ImportReviewDecision.Rejected;
        if (found.Item.ReviewDecision != ImportReviewDecision.Pending && !reopen)
        {
            return ApiProblems.Conflict(NotPendingCode, "This line has already been decided.");
        }

        var fields = new Dictionary<long, IReadOnlyList<string>>();
        if (accept && request.Fields is { } named)
        {
            var handler = handlers.FirstOrDefault(h => h.Source == found.Batch.Source);
            var acceptable = handler?.FieldsOf(found.Item) ?? [];
            var unknown = named.Where(f => !acceptable.Contains(f, StringComparer.Ordinal)).ToList();
            if (unknown.Count > 0)
            {
                return ApiProblems.BadRequest(
                    FieldsUnsupportedCode,
                    acceptable.Count == 0
                        ? "This line can only be accepted whole."
                        : $"This line can be accepted by {string.Join(", ", acceptable)}; not by {string.Join(", ", unknown)}.");
            }

            fields[found.Item.Id] = named;
        }

        var outcome = await DecideAsync(
            db, handlers, review, ctx, found.Batch, [found.Item.Id], fields,
            request.Note, request.OverCurrent ?? false, accept, reopen, ct);
        if (outcome.Result is { } problem)
        {
            return problem;
        }

        return TypedResults.Ok(await ResultAsync(db, handlers, found.Batch.Source, outcome.Decided!, ctx, ct));
    }

    private static async Task<(UploadReviewResult? Decided, ProblemHttpResult? Result)> DecideAsync(
        SilexGisDbContext db,
        IEnumerable<IHeldBatchHandler> handlers,
        UploadReviewService review,
        AccessContext ctx,
        ImportBatch batch,
        IReadOnlyCollection<long> chosen,
        IReadOnlyDictionary<long, IReadOnlyList<string>> fields,
        string? note,
        bool overCurrent,
        bool accept,
        bool reopen,
        CancellationToken ct)
    {
        if (!handlers.Any(h => h.Source == batch.Source))
        {
            return (null, ApiProblems.Conflict(
                SourceUnsupportedCode, "This kind of held batch cannot be decided by this server; its lines still wait."));
        }

        try
        {
            return accept
                ? (await review.AcceptAsync(
                    ctx.UserId, batch.ConfirmedByUserId, batch.Source, chosen, fields, note, overCurrent, reopen, ct), null)
                : (await review.RejectAsync(ctx.UserId, batch.ConfirmedByUserId, batch.Source, chosen, note, ct), null);
        }
        catch (HeldNotPendingException)
        {
            db.ChangeTracker.Clear();
            return (null, ApiProblems.Conflict(
                NotPendingCode,
                "What this decision reaches was decided already, by this or another decision, or can no longer be changed."));
        }
        catch (HeldWriteUnavailableException e)
        {
            db.ChangeTracker.Clear();
            return (null, ApiProblems.ServiceUnavailable(e.Code, e.Message));
        }
        catch (HeldChangedSinceException e)
        {
            db.ChangeTracker.Clear();
            return (null, ApiProblems.Conflict(
                ChangedSinceCode,
                "Some rows changed on the server after they were uploaded. Accept them over the current rows, or reject them.",
                "reviewIds",
                e.ReviewIds));
        }
    }

    private static async Task<ReviewDecisionResultDto> ResultAsync(
        SilexGisDbContext db,
        IEnumerable<IHeldBatchHandler> handlers,
        ImportSource source,
        UploadReviewResult decided,
        AccessContext ctx,
        CancellationToken ct)
    {
        var batches = await db.ImportBatches.AsNoTracking()
            .Where(b => decided.Batches.Select(x => x.Id).Contains(b.Id))
            .OrderBy(b => b.HeldAt)
            .ToListAsync(ct);
        return new ReviewDecisionResultDto(
            await LinesAsync(db, handlers, source, decided.Decided, ctx, ct),
            await SummariesAsync(db, batches, ctx, ct));
    }

    private static async Task<IReadOnlyList<HeldBatchDto>> SummariesAsync(
        SilexGisDbContext db, IReadOnlyList<ImportBatch> batches, AccessContext viewer, CancellationToken ct)
    {
        var ids = batches.Select(b => b.Id).ToList();
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db,
            new UserContext(viewer.UserId, viewer.CavingGroupIds),
            batches.Select(b => b.ConfirmedByUserId).Concat(batches.Where(b => b.DecidedByUserId != null).Select(b => b.DecidedByUserId!.Value)),
            ct);
        // The bytes the batch's waiting lines keep parked, so a reviewer sees the queue's weight.
        var bytes = await (from item in db.ImportBatchItems.AsNoTracking()
                           join session in db.UploadSessions.AsNoTracking() on item.HeldUploadSessionId equals (Guid?)session.Id
                           where ids.Contains(item.ImportBatchId) && item.ReviewDecision == ImportReviewDecision.Pending
                           select new { item.ImportBatchId, session.Id, session.DeclaredSizeBytes })
            .Distinct()
            .ToListAsync(ct);
        var counts = await db.ImportBatchItems.AsNoTracking()
            .Where(i => ids.Contains(i.ImportBatchId) && i.ReviewDecision != null)
            .GroupBy(i => new { i.ImportBatchId, i.ProposalSection, Pending = i.ReviewDecision == ImportReviewDecision.Pending })
            .Select(g => new { g.Key.ImportBatchId, g.Key.ProposalSection, g.Key.Pending, Count = g.Count() })
            .ToListAsync(ct);
        return [.. batches.Select(b =>
        {
            var mine = counts.Where(c => c.ImportBatchId == b.Id).ToList();
            return new HeldBatchDto(
                b.Id, b.Source, b.ConfirmedByUserId, b.ReviewState, b.HeldAt, b.DecidedAt, b.DecidedByUserId, b.ReviewNote,
                mine.Where(c => c.Pending).Sum(c => c.Count),
                mine.Sum(c => c.Count),
                bytes.Where(x => x.ImportBatchId == b.Id).Sum(x => x.DeclaredSizeBytes),
                mine.Where(c => c.Pending)
                    .GroupBy(c => c.ProposalSection ?? "")
                    .ToDictionary(g => g.Key, g => g.Sum(c => c.Count), StringComparer.Ordinal),
                labels.GetValueOrDefault(b.ConfirmedByUserId),
                b.DecidedByUserId is { } decider ? labels.GetValueOrDefault(decider) : null);
        })];
    }

    private static async Task<IReadOnlyList<HeldLineDto>> LinesAsync(
        SilexGisDbContext db,
        IEnumerable<IHeldBatchHandler> handlers,
        ImportSource source,
        IReadOnlyList<ImportBatchItem> lines,
        AccessContext viewer,
        CancellationToken ct)
    {
        var handler = handlers.FirstOrDefault(h => h.Source == source);
        // Without a handler nothing can be checked, so nothing of a proposal is shown.
        var shown = handler is null
            ? new Dictionary<long, HeldLineView>()
            : await handler.ShowAsync(lines, viewer, ct);
        var dependencyIds = lines.SelectMany(l => l.DependsOnItemIds ?? []).Distinct().ToList();
        var dependencies = dependencyIds.Count == 0
            ? new Dictionary<long, Guid?>()
            : await db.ImportBatchItems.AsNoTracking()
                .Where(i => dependencyIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.ReviewId, ct);
        return [.. lines.OrderBy(l => l.Id).Select(l =>
        {
            var view = shown.GetValueOrDefault(l.Id);
            var visible = view?.Proposal is not null;
            return new HeldLineDto(
                l.ReviewId ?? Guid.Empty,
                l.ProposalSection,
                // The row id of a line the viewer may not be shown names a row they may not read.
                visible ? l.ProposalRowId : null,
                l.Action,
                l.ReviewDecision ?? ImportReviewDecision.Pending,
                [.. (l.DependsOnItemIds ?? []).Select(on => dependencies.GetValueOrDefault(on)).OfType<Guid>()],
                l.AcceptedFields,
                handler?.FieldsOf(l) ?? [],
                l.DecisionNote,
                l.DecidedAt,
                l.DecidedByUserId,
                l.ReviewFailureCode,
                l.ReviewFailureDetail,
                view?.ChangedSince ?? false,
                visible ? JsonDocument.Parse(view!.Proposal!).RootElement.Clone() : null);
        })];
    }
}
