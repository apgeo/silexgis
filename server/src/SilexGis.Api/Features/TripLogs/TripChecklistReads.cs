// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Which list a trip works through, and how much of it is settled.
/// </summary>
/// <remarks>
/// <para>
/// One question asked in one place, because more than one surface asks it — the trip's own
/// checklist route, and every listing of trips — and two readings of the same tables are how two
/// surfaces come to disagree about what a trip is measured against.
/// </para>
/// <para>
/// A trip works through the list its purpose names. The purpose references a list rather than
/// carrying a copy of one, so correcting a line corrects it for every trip of that purpose at
/// once; and because which list a purpose names can change, a confirmation records the list it
/// was made against rather than being re-attached to whatever the purpose names today.
/// </para>
/// <para>
/// The list is narrowed by its own audience on every path here. Being able to read a trip is not
/// being able to read the list it names: the list is an owned, shareable row in a domain of its
/// own, and a reference from a trip is not consent. A caller who may not read the list is told
/// the trip has none — the same answer as a trip whose purpose names none — because telling the
/// two apart would say a list exists.
/// </para>
/// </remarks>
internal static class TripChecklistReads
{
    /// <summary>The list this trip works through, if there is one this caller may read.</summary>
    internal static async Task<Checklist?> ReadableListForAsync(
        SilexGisDbContext db, AccessContext ctx, TripLog trip, CancellationToken ct)
    {
        if (trip.TripTypeId is not { } tripTypeId)
        {
            return null;
        }

        var listId = await db.TripTypes.AsNoTracking()
            .Where(x => x.Id == tripTypeId)
            .Select(x => x.DefaultChecklistId)
            .FirstOrDefaultAsync(ct);

        return listId is null
            ? null
            : await db.Checklists.AsNoTracking()
                .VisibleTo(ctx, AccessDomain.Checklists)
                .FirstOrDefaultAsync(x => x.Id == listId.Value, ct);
    }

    /// <summary>
    /// How settled each of a page of trips is, worked out for the whole page at once.
    /// </summary>
    /// <remarks>
    /// Resolved once per listing rather than once per row, the way an expectation belonging to a
    /// shelf is resolved once for the documents filed on it: which list a trip is measured against
    /// is a property of its purpose, not of the trip, and asking per row would put four queries on
    /// every trip of a page of fifty. Trips with no readable list are absent from the result rather
    /// than present with a zero — nothing settled and nothing to settle are different answers, and
    /// only one of them is a figure worth showing.
    /// </remarks>
    internal static async Task<IReadOnlyDictionary<Guid, (Guid ChecklistId, TripReadiness Readiness)>>
        ReadinessForAsync(
            SilexGisDbContext db, AccessContext ctx, IReadOnlyList<TripLog> trips, CancellationToken ct)
    {
        var typeIds = trips.Where(x => x.TripTypeId is not null).Select(x => x.TripTypeId!.Value)
            .Distinct().ToList();
        if (typeIds.Count == 0)
        {
            return new Dictionary<Guid, (Guid, TripReadiness)>();
        }

        var listOfType = await db.TripTypes.AsNoTracking()
            .Where(x => typeIds.Contains(x.Id) && x.DefaultChecklistId != null)
            .Select(x => new { x.Id, ChecklistId = x.DefaultChecklistId!.Value })
            .ToDictionaryAsync(x => x.Id, x => x.ChecklistId, ct);
        if (listOfType.Count == 0)
        {
            return new Dictionary<Guid, (Guid, TripReadiness)>();
        }

        // Narrowed in the statement rather than after it, for the reason every listing here is:
        // a filter applied to results is a filter somebody later forgets to apply.
        var named = listOfType.Values.Distinct().ToList();
        var readableIds = await db.Checklists.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Checklists)
            .Where(x => named.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (readableIds.Count == 0)
        {
            return new Dictionary<Guid, (Guid, TripReadiness)>();
        }

        var lines = (await db.ChecklistItems.AsNoTracking()
                .Where(x => readableIds.Contains(x.ChecklistId))
                .Select(x => new { x.ChecklistId, x.Id })
                .ToListAsync(ct))
            .GroupBy(x => x.ChecklistId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Guid>)[.. g.Select(x => x.Id)]);

        var tripIds = trips.Select(x => x.Id).ToList();
        var confirmed = (await db.TripChecklistTicks.AsNoTracking()
                .Where(x => tripIds.Contains(x.TripLogId) && readableIds.Contains(x.ChecklistId))
                .Select(x => new { x.TripLogId, x.ItemId })
                .ToListAsync(ct))
            .GroupBy(x => x.TripLogId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Guid>)[.. g.Select(x => x.ItemId)]);

        var readable = readableIds.ToHashSet();
        var answers = new Dictionary<Guid, (Guid ChecklistId, TripReadiness Readiness)>();
        foreach (var trip in trips)
        {
            if (trip.TripTypeId is not { } typeId
                || !listOfType.TryGetValue(typeId, out var listId)
                || !readable.Contains(listId))
            {
                continue;
            }

            answers[trip.Id] = (
                listId,
                TripReadiness.Of(
                    lines.GetValueOrDefault(listId, []),
                    confirmed.GetValueOrDefault(trip.Id, [])));
        }

        return answers;
    }
}
