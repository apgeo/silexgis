// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Filters;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

public static partial class TripLogEndpoints
{
    /// <summary>Creates a trip written up after the event.</summary>
    private static Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        TripLogWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripLogWriteService writes,
        CancellationToken ct) =>
        CreateCoreAsync(
            TripCreationIntent.Report, request, http, db, access, accessAccessor, userAccessor,
            protection, writes, ct);

    /// <summary>Creates a trip that has not happened yet.</summary>
    private static Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreatePlanAsync(
        TripLogWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripLogWriteService writes,
        CancellationToken ct) =>
        CreateCoreAsync(
            TripCreationIntent.Plan, request, http, db, access, accessAccessor, userAccessor,
            protection, writes, ct);

    /// <summary>
    /// Creating a trip, whichever door it came through. The two doors differ in one thing and it
    /// is decided here, before anything is checked against it: the audience a request that names
    /// none falls back to. Everything after that point is identical, which is why they share a
    /// body rather than each growing their own copy of eight steps.
    /// </summary>
    private static async Task<Results<Created<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateCoreAsync(
        TripCreationIntent intent,
        TripLogWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripLogWriteService writes,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        TripLog trip;
        try
        {
            trip = await writes.CreateAsync(intent, ToInput(request), ctx, user.UserId, ct: ct);
        }
        catch (TripWriteException e)
        {
            return e.Denied ? ApiProblems.Forbidden(e.Code) : ApiProblems.BadRequest(e.Code, e.Message);
        }

        await db.SaveChangesAsync(ct);

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        // The version the new row has, so the first edit needs no read in between; the Location
        // header already says which resource the tag belongs to.
        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, trip.Id, ct);
        return TypedResults.Created(TripPath(trip.Id), items[0]);
    }

    /// <summary>
    /// Who would be able to read a trip this caller plans, if they name no audience themselves —
    /// the same rule the plan door applies, answered before the trip exists so a form can name
    /// the audience rather than recite the rule. The group's name travels with its id because a
    /// notice saying "your group" and a reader who belongs to one they had forgotten about are
    /// not the same thing.
    /// </summary>
    /// <remarks>
    /// Tells the caller nothing they do not already know: it reports their own membership, and
    /// only when it is the single one that decides the answer.
    /// </remarks>
    private static async Task<Results<Ok<TripPlanDefaultDto>, UnauthorizedHttpResult>> PlanDefaultAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (visibility, groupId) = TripAudienceRules.DefaultAudience(
            TripCreationIntent.Plan, ctx.CavingGroupIds);
        if (groupId is null)
        {
            return TypedResults.Ok(new TripPlanDefaultDto(visibility, null, null));
        }

        var name = await db.CavingGroups.AsNoTracking()
            .Where(group => group.Id == groupId.Value)
            .Select(group => group.Name)
            .FirstOrDefaultAsync(ct);
        return TypedResults.Ok(new TripPlanDefaultDto(visibility, groupId, name));
    }

    private static async Task<Results<Ok<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        TripLogWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        TripLogWriteService writes,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (ctx is null || user is null || !(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        TripWriteOutcome outcome;
        try
        {
            outcome = await writes.UpdateAsync(trip, ToInput(request), ctx, ct: ct);
        }
        catch (TripWriteException e)
        {
            return e.Denied ? ApiProblems.Forbidden(e.Code) : ApiProblems.BadRequest(e.Code, e.Message);
        }

        // A trip people are expecting to go on has changed under them, so the people it concerns
        // are told — everybody named on it and everybody asked about it, minus whoever this same
        // write has just told about it another way. What changed is not in the message: saying so
        // would mean saying which field, and the fields include the places the trip is about.
        //
        // Asked of the write rather than of the change tracker here, because queueing the roster
        // notice is itself a change and would answer the question for it: a request that carries
        // back exactly what it was given moves no column, and a notice announcing a change that
        // provably did not happen is how a category earns being muted along with the messages
        // that matter.
        if (outcome.SomethingChanged)
        {
            await TripPlanNotifier.ChangedAsync(db, access, user, trip, outcome.NewlyNamedUserIds, ct);
        }

        // A cave named onto a trip after people were asked onto it is the same pairing arriving in
        // the other order, and the people who can open it are told the same way.
        await TripCaveAccessNotifier.CavesAddedAsync(db, access, user, trip, outcome.AddedCaveIds, ct);
        try
        {
            if (outcome.RosterChanged)
            {
                if (await SaveAndMoveTheVersionAsync(http, db, trip, ct) is { } raced)
                {
                    return raced;
                }
            }
            else
            {
                await db.SaveChangesAsync(ct);
            }
        }
        catch (DbUpdateException e) when (TripPartyNumbers.LostTheRace(e))
        {
            // Two saves of this trip each named somebody new at the same moment. Both passed the
            // precondition, which is read and compared with nothing held, and both reached for the
            // same next party number; the table let one of them in. Nothing of this save was
            // written, so it is refused as the conflict it is and the caller reloads and repeats.
            return ApiProblems.Conflict(TripPartyNumbers.LostRaceCode, TripPartyNumbers.LostRaceMessage);
        }

        var items = await MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        // The version this write produced: what the next write must carry, handed over here so
        // that a second save a moment later is not refused against a version only a read knew.
        await Concurrency.EmitETagAsync(http, db, VersionedTable.TripLogs, trip.Id, ct);
        return TypedResults.Ok(items[0]);
    }

    /// <summary>
    /// Saves a write that changed the trip's roster, and stamps the trip's own row with it so
    /// that the trip's version moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The version a save of this form is checked against is the version of the trip's own row.
    /// A save that changes only who is on the trip, or their times, writes roster rows and leaves
    /// that row alone — so two people with the form open could each change a participant's time,
    /// each pass the precondition with the version they opened with, and the second would put the
    /// first one's times back to what its form was opened with, with nobody told. The roster is
    /// written whole, so every save that changes a row of it has to be visible to the next one.
    /// </para>
    /// <para>
    /// Stamped past the change tracker rather than by marking the trip modified, because a
    /// modified trip is a line on the trip's history, and "the trip was updated" beside the
    /// roster rows' own lines would say nothing those do not. One transaction for both: roster
    /// rows written under a version that did not move are exactly what the stamp is there to
    /// prevent. A save that changed no roster row does not come here and moves nothing.
    /// </para>
    /// </remarks>
    private static async Task<ProblemHttpResult?> SaveAndMoveTheVersionAsync(
        HttpContext http, SilexGisDbContext db, TripLog trip, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The precondition was compared earlier with nothing held, and the roster this write
        // diffed against was read then too. Taking the trip's row lock and comparing again closes
        // that gap: a roster write that committed since is a changed version and this one is
        // refused, and one still in flight makes this wait for it and then see what it left.
        var held = await ConcurrencySql.LockLiveTripLogVersionAsync(db, trip.Id, ct);
        if (Concurrency.CheckIfMatchVersion(http, held?.ToString(), required: true) is { } stale)
        {
            return stale;
        }

        await db.SaveChangesAsync(ct);

        var stamped = DateTimeOffset.UtcNow;
        await db.TripLogs
            .Where(x => x.Id == trip.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, stamped), ct);
        await transaction.CommitAsync(ct);

        // The answer is mapped from the trip in hand, which the statement above went round. It is
        // given what was written and told that this is what the database holds, so that it is
        // neither answered stale nor written a second time by a later save of this context.
        var updatedAt = db.Entry(trip).Property(x => x.UpdatedAt);
        updatedAt.CurrentValue = stamped;
        updatedAt.IsModified = false;
        return null;
    }

    /// <summary>
    /// Deletes a trip: marks it, and removes nothing.
    /// </summary>
    /// <remarks>
    /// The same right and the same answer as when a delete was final. What changed is what is
    /// left behind: every row the trip had, hidden with it by the model, so the trip can be put
    /// back exactly. From this save on it answers as not found on every ordinary read and write,
    /// to its owner as much as to anybody — the one place it can still be seen is the list of
    /// deleted trips, and the one thing that can be done to it is to restore it.
    /// </remarks>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        TripLogWriteService writes,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.TripLogs, trip.Id, ct) is { } stale)
        {
            return stale;
        }

        // What deleting a trip does is stated in one place, because the undo that takes a whole
        // imported spreadsheet back has to delete a trip the same way this route does.
        writes.SoftDelete(trip, ctx.UserId);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
