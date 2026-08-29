// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>What one trip has settled of the list it works through, and the act of settling a line.</summary>
public sealed record TripChecklistDto(
    Guid TripLogId,
    // Null where the trip's purpose names no list, and null just the same where it names one this
    // caller may not read. A list answers to its own audience; a trip referencing one is not
    // consent, and naming it here would hand a reader the identity of something nobody meant them
    // to have — which is enough to go and ask for it.
    Guid? ChecklistId,
    string? Title,
    string? Description,
    // How much of it is settled: ticked of total. Derived on the way out, stored nowhere, and
    // consulted by nothing that decides who may read anything.
    int Ticked,
    int Total,
    IReadOnlyList<TripChecklistItemDto> Items);

/// <summary>One line of the list, and what this trip has said about it.</summary>
public sealed record TripChecklistItemDto(
    Guid Id,
    string Text,
    int SortOrder,
    bool Ticked,
    // Who said so and when — the part of a confirmation that is a fact rather than a derivation,
    // which is why it is written down. Null on a line nobody has confirmed; the author is null on
    // a confirmation whose account has since gone, while the time it was made stands.
    Guid? TickedByUserId,
    DateTimeOffset? TickedAt);

/// <summary>
/// The checklist one trip works through: reading it, and confirming a line of it.
/// </summary>
/// <remarks>
/// <para>
/// Every route here is a sub-resource of one trip, and the trip is what governs them. A caller
/// who may not read the trip is answered as though it did not exist, so a refusal never confirms
/// a trip is there.
/// </para>
/// <para>
/// The list itself is a separate thing with an audience of its own, and referencing it from a
/// trip confers nothing. A caller who may read the trip but not the list is told the trip has no
/// list they can see — not the list's title, not its lines, not its identity.
/// </para>
/// <para>
/// Nothing here refuses anything for being unsettled, and nothing here is consulted when deciding
/// who may read the trip. How much of a list a party has worked through is a reading for the
/// party, not a state the trip is in and not a second rule about who sees a plan.
/// </para>
/// </remarks>
public static class TripChecklistEndpoints
{
    /// <summary>The trip, as the trip's own routes answer it — unreachable and absent read alike.</summary>
    public const string TripNotFoundCode = "trip_log.not_found";

    /// <summary>
    /// A line the trip's list does not have — or has, on a list this caller may not read. The two
    /// are one answer on purpose: telling them apart would say whether a list exists.
    /// </summary>
    public const string ItemNotFoundCode = "trip_checklist.item_not_found";

    /// <summary>Saying a line of the trip's preparation is settled is running the trip.</summary>
    public const string TickForbiddenCode = "trip_checklist.tick_forbidden";

    public static RouteGroupBuilder MapTripChecklistEndpoints(this RouteGroupBuilder api)
    {
        var checklist = api.MapGroup("/trip-logs/{tripLogId:guid}/checklist").WithTags("TripLogs");

        checklist.MapGet("/", GetAsync)
            .WithSummary(
                "The list this trip works through, its lines, who has confirmed each and when, "
                + "and how much of it is settled. Takes the right to read the trip; a list the "
                + "caller may not read is answered as no list.");

        checklist.MapPut("/items/{itemId:guid}", TickAsync)
            .WithSummary(
                "Confirms one line of the list as settled for this trip (Write permission on the "
                + "trip). Confirming again changes nothing: the first confirmation is the record "
                + "of who said so and when.");

        checklist.MapDelete("/items/{itemId:guid}", UntickAsync)
            .WithSummary(
                "Takes back a confirmation (Write permission on the trip). Taking back one that "
                + "was never made is not an error.");

        return api;
    }

    private static async Task<Results<Ok<TripChecklistDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        GetAsync(
            Guid tripLogId,
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

        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        var list = await TripChecklistReads.ReadableListForAsync(db, ctx, trip, ct);
        if (list is null)
        {
            return TypedResults.Ok(new TripChecklistDto(tripLogId, null, null, null, 0, 0, []));
        }

        var items = await db.ChecklistItems.AsNoTracking()
            .Where(x => x.ChecklistId == list.Id)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .ToListAsync(ct);

        var ticks = await db.TripChecklistTicks.AsNoTracking()
            .Where(x => x.TripLogId == tripLogId && x.ChecklistId == list.Id)
            .ToDictionaryAsync(x => x.ItemId, ct);

        // The figure comes from the one rule that defines it, over the rows just read, rather than
        // from a second count written here. Two expressions for "how settled is it" would agree
        // until either was changed alone.
        var readiness = TripReadiness.Of(
            [.. items.Select(x => x.Id)], [.. ticks.Keys]);

        return TypedResults.Ok(new TripChecklistDto(
            tripLogId,
            list.Id,
            list.Title,
            list.Description,
            readiness.Ticked,
            readiness.Total,
            [.. items.Select(item => Map(item, ticks.GetValueOrDefault(item.Id)))]));
    }

    private static async Task<Results<Ok<TripChecklistItemDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        TickAsync(
            Guid tripLogId,
            Guid itemId,
            SilexGisDbContext db,
            IAccessService access,
            IAccessContextAccessor accessAccessor,
            IUserContextAccessor userAccessor,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        // The trip is asked about before anything else, so a caller with no right to it never
        // learns from a refusal whether it exists or what it is preparing for.
        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return ApiProblems.Forbidden(TickForbiddenCode);
        }

        var item = await LineOfTheTripsListAsync(db, ctx, trip, itemId, ct);
        if (item is null)
        {
            return ApiProblems.NotFound(ItemNotFoundCode);
        }

        var existing = await db.TripChecklistTicks
            .FirstOrDefaultAsync(x => x.TripLogId == tripLogId && x.ItemId == itemId, ct);

        // Confirming again is the same assertion said twice, and the record keeps the first: who
        // settled the permit and when is the question, and an answer that moved every time
        // somebody re-opened the page would answer a different one.
        if (existing is null)
        {
            existing = new TripChecklistTick
            {
                TripLogId = tripLogId,
                ChecklistId = item.ChecklistId,
                ItemId = item.Id,
                TickedByUserId = user.UserId,
                TickedAt = DateTimeOffset.UtcNow,
            };
            db.TripChecklistTicks.Add(existing);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (IsSameConfirmationRace(e))
            {
                // Two people ticking the same line at the same moment is the ordinary case on a
                // plan a party shares, and both of them are right. The key refuses the second
                // write; the second request is still answered with the confirmation that stands,
                // because confirming a line already confirmed is documented as changing nothing
                // and the caller asked for a state that is now the state.
                db.Entry(existing).State = EntityState.Detached;
                existing = await db.TripChecklistTicks.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TripLogId == tripLogId && x.ItemId == itemId, ct);
            }
        }

        return TypedResults.Ok(Map(item, existing));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>>
        UntickAsync(
            Guid tripLogId,
            Guid itemId,
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

        var trip = await ReadableTripAsync(db, access, ctx, tripLogId, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound(TripNotFoundCode);
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return ApiProblems.Forbidden(TickForbiddenCode);
        }

        var item = await LineOfTheTripsListAsync(db, ctx, trip, itemId, ct);
        if (item is null)
        {
            return ApiProblems.NotFound(ItemNotFoundCode);
        }

        // Taking back what was never confirmed is not an error: the caller asked for a state and
        // the state is what they asked for.
        await db.TripChecklistTicks
            .Where(x => x.TripLogId == tripLogId && x.ItemId == itemId)
            .ExecuteDeleteAsync(ct);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// The line, if it is on the list this trip works through and this caller may read that list.
    /// A line of some other list is not found here even where the caller may read it: the trip
    /// says which list its preparation is measured against, and a confirmation against any other
    /// would count towards nothing.
    /// </summary>
    private static async Task<ChecklistItem?> LineOfTheTripsListAsync(
        SilexGisDbContext db, AccessContext ctx, TripLog trip, Guid itemId, CancellationToken ct)
    {
        var list = await TripChecklistReads.ReadableListForAsync(db, ctx, trip, ct);
        return list is null
            ? null
            : await db.ChecklistItems.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == itemId && x.ChecklistId == list.Id, ct);
    }

    /// <summary>The key that holds one line of one trip to one confirmation.</summary>
    private const string TickPrimaryKey = "pk_trip_checklist_ticks";

    /// <summary>
    /// Whether a failed write is a second confirmation of the same line of the same trip arriving
    /// at the same moment. Both requests look for an existing confirmation and add one when they
    /// find none, so both can look, find nothing, and try to add; the key refuses the second, and
    /// it is answered with the confirmation that won rather than escaping as an unhandled fault.
    /// </summary>
    private static bool IsSameConfirmationRace(DbUpdateException e) =>
        e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: TickPrimaryKey,
        };

    private static TripChecklistItemDto Map(ChecklistItem item, TripChecklistTick? tick) => new(
        item.Id,
        item.Text,
        item.SortOrder,
        tick is not null,
        tick?.TickedByUserId,
        tick?.TickedAt);

    private static async Task<TripLog?> ReadableTripAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid tripLogId, CancellationToken ct)
    {
        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tripLogId, ct);
        return trip is not null && (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
            ? trip
            : null;
    }
}
