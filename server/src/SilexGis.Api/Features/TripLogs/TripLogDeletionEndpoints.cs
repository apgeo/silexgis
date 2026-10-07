// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>A deleted trip, as the list of trips that can still be put back shows it.</summary>
/// <param name="RestorableUntil">
/// When it stops being restorable and is removed for good, or null on an installation that keeps
/// deleted trips until somebody says otherwise. What a "12 days left" line is drawn from.
/// </param>
/// <param name="DeletedByName">
/// Who deleted it, under the label this reader may be shown for them — never an address — or
/// null when the account is gone or its name is not this reader's to see.
/// </param>
public sealed record DeletedTripLogDto(
    Guid Id,
    string Title,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    DateTimeOffset DeletedAt,
    DateTimeOffset? RestorableUntil,
    Guid? DeletedByUserId,
    string? DeletedByName);

/// <summary>What this installation does with a deleted trip.</summary>
/// <param name="DeletedRetentionDays">
/// How many days a deleted trip can still be put back, or null when it is kept until somebody
/// says otherwise. Asked before the delete, so the confirmation can say what is about to happen
/// rather than recite a number the installation may have changed.
/// </param>
public sealed record TripLogConfigDto(int? DeletedRetentionDays);

/// <summary>
/// The deleted trips: listing the ones that can be put back, and putting one back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The right that deletes is the right that restores.</b> A deleted trip keeps its owner, its
/// audience and every rule written about it, so the ordinary decision can still be asked of it —
/// it is only the reads that the model hides. That is why nothing here falls back on "whoever
/// deleted it": an administrator who deleted somebody's trip by mistake is not the only one who
/// can undo it, and somebody who has since lost the right to delete it cannot bring it back
/// either. Reading it is asked for as well, because both routes answer with the trip's title.
/// </para>
/// <para>
/// Decided row by row, by the same evaluator every guard uses, over the access columns alone.
/// There is no filter twin for an action other than reading, and writing one for this list would
/// be a fourth statement of the access rule; the rows a deleted list holds are bounded by the
/// restore window, so reading their four access columns and deciding in memory is affordable.
/// </para>
/// </remarks>
internal static class TripLogDeletionEndpoints
{
    /// <summary>
    /// What a delete is about to do here, for any signed-in caller: it describes the
    /// installation, not a trip, so there is nothing in it to withhold from one of them.
    /// </summary>
    public static async Task<Results<Ok<TripLogConfigDto>, UnauthorizedHttpResult>> ConfigAsync(
        IAccessContextAccessor accessAccessor,
        IOptions<TripRetentionOptions> retention,
        CancellationToken ct)
    {
        if (await accessAccessor.GetAsync(ct) is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new TripLogConfigDto(
            retention.Value.Window is null ? null : retention.Value.DeletedRetentionDays));
    }

    public static async Task<Results<Ok<PagedResult<DeletedTripLogDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        IOptions<TripRetentionOptions> retention,
        TimeProvider clock,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var window = retention.Value.Window;
        var (p, size) = Paging.Normalize(page, pageSize);

        // Through the filter, which hides exactly the rows this list is about.
        var deleted = db.TripLogs.AsNoTracking().IgnoreQueryFilters().Where(t => t.DeletedAt != null);

        // A trip past its window is not offered. The pass that removes it may not have reached
        // it yet, but a restore would be refused, and a row nobody can act on is a row that
        // only says the pass is late.
        if (TripDeletionRules.PurgeCutoff(clock.GetUtcNow(), window) is { } cutoff)
        {
            deleted = deleted.Where(t => t.DeletedAt > cutoff);
        }

        var candidates = await deleted
            .OrderByDescending(t => t.DeletedAt)
            .ThenByDescending(t => t.Id)
            .Select(t => new DeletedTripRow
            {
                Id = t.Id,
                OwnerUserId = t.OwnerUserId,
                CavingGroupId = t.CavingGroupId,
                Visibility = t.Visibility,
                Title = t.Title,
                TripDate = t.TripDate,
                TripDateEnd = t.TripDateEnd,
                DeletedAt = t.DeletedAt!.Value,
                DeletedByUserId = t.DeletedByUserId,
            })
            .ToListAsync(ct);

        var mine = candidates.Where(row => MayRestore(ctx, row)).ToList();
        var shown = mine.Skip((p - 1) * size).Take(size).ToList();

        // Resolved rather than joined: the label an account may be shown under is a rule with
        // one home, and it is never their address.
        var names = await ProfileDirectory.ResolveLabelsAsync(
            db, user, shown.Where(row => row.DeletedByUserId != null).Select(row => row.DeletedByUserId!.Value), ct);

        return TypedResults.Ok(new PagedResult<DeletedTripLogDto>(
            [
                .. shown.Select(row => new DeletedTripLogDto(
                    row.Id,
                    row.Title,
                    row.TripDate,
                    row.TripDateEnd,
                    row.DeletedAt,
                    TripDeletionRules.RemovedAt(row.DeletedAt, window),
                    row.DeletedByUserId,
                    row.DeletedByUserId is { } by ? names.GetValueOrDefault(by) : null)),
            ],
            p,
            size,
            mine.Count));
    }

    /// <summary>
    /// Puts a deleted trip back, and answers it as its own page would.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ladder.</b> A trip this caller could not read answers as one that does not exist,
    /// deleted or not; one they could read but not delete is a refusal; one that is not deleted
    /// is a conflict, and so is one whose window has passed. The read is asked first and on its
    /// own, unlike on the delete route, because this route answers with the whole trip: a caller
    /// holding the right to delete but not to read must not be handed the record by restoring it.
    /// </para>
    /// <para>
    /// <b>No precondition header.</b> Every other write to a trip requires one, to stop two people
    /// overwriting each other's edits. Nobody holds a version of a deleted trip — there was no
    /// read to get one from — and two people restoring it at once are two people agreeing.
    /// </para>
    /// <para>
    /// Nobody is told. The people on the trip were never told it had gone, and a notice saying a
    /// trip is back would be the first they heard of either.
    /// </para>
    /// </remarks>
    public static async Task<Results<Ok<TripLogDto>, ProblemHttpResult>> RestoreAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        IOptions<TripRetentionOptions> retention,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);

        // Past the filter that hides deleted trips: one of them is what this route is for.
        var trip = await db.TripLogs.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null
            || ctx is null
            || user is null
            || !(await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Delete, trip, ct)).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        if (trip.DeletedAt is not { } deletedAt)
        {
            return ApiProblems.Conflict(TripDeletionRules.NotDeletedCode, "This trip is not deleted.");
        }

        if (!TripDeletionRules.IsRestorable(deletedAt, clock.GetUtcNow(), retention.Value.Window))
        {
            return ApiProblems.Conflict(
                TripDeletionRules.RestoreWindowPassedCode,
                "This trip was deleted too long ago to be restored.");
        }

        TripLogWriteService.Restore(trip);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The pass that removes deleted trips reached this one in the same instant and won:
            // the row this save meant to change is gone. The trip no longer exists, and that is
            // the answer.
            return ApiProblems.NotFound("trip_log.not_found");
        }

        var items = await TripLogEndpoints.MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        // The version the restore produced, filed against the trip's own path: the page it
        // opens on next edits that version.
        await Concurrency.EmitETagAsync(
            http, db, VersionedTable.TripLogs, trip.Id, ct, TripLogEndpoints.TripPath(trip.Id));
        return TypedResults.Ok(items[0]);
    }

    /// <summary>
    /// Whether this caller may put a deleted trip back: they could read it, and they could
    /// delete it. The same two questions the restore route asks, of the same columns.
    /// </summary>
    private static bool MayRestore(AccessContext ctx, DeletedTripRow row)
    {
        var facts = AccessTargetFacts.Of(row);
        return AccessEvaluator.Decide(ctx, AccessDomain.TripLogs, AccessAction.Read, facts).Allowed
            && AccessEvaluator.Decide(ctx, AccessDomain.TripLogs, AccessAction.Delete, facts).Allowed;
    }

    /// <summary>
    /// One deleted trip's access columns and what the list shows of it, read rather than
    /// materialised whole: the rows behind it carry geometry and whole written reports this list
    /// has no use for, and every deleted trip in the window is read to decide which are the
    /// caller's. In the shape the fact builder accepts, so a trip's facts are described in one
    /// place.
    /// </summary>
    private sealed class DeletedTripRow : IProtectedEntity
    {
        public required Guid Id { get; init; }

        public Guid OwnerUserId { get; set; }

        public Guid? CavingGroupId { get; set; }

        public Visibility Visibility { get; set; }

        public required string Title { get; init; }

        public DateOnly TripDate { get; init; }

        public DateOnly? TripDateEnd { get; init; }

        public DateTimeOffset DeletedAt { get; init; }

        public Guid? DeletedByUserId { get; init; }
    }
}
