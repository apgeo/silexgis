// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// Of the caves a trip's roles name, the ones a given caller may be told about at all.
/// </summary>
/// <remarks>
/// <para>
/// Two gates, in this order. The first is readability: <em>naming a cave is a read of the
/// cave</em>. A trip's own audience is not the cave's — a cave nobody but its owner may open
/// can be named on a trip half the club reads, and a trip's readership is in general a list
/// somebody types. Handing over the identifier of such a cave hands over the one thing that
/// is enough to go and ask for the cave elsewhere, so the identifier is withheld rather than
/// the name alone — on the trip's own cave list and on everything built from it, which is
/// what this rule governs and the whole of what it claims. A trip carries other panels with
/// withholding rules of their own, and they answer for themselves.
/// </para>
/// <para>
/// The second is placement, and it is a separate question with a separate answer: a trip
/// carries its own exact geometry, so "this trip reached that cave" places a guarded cave by
/// proximity even when the cave itself is perfectly readable. Neither gate implies the other
/// — the placement walk deliberately answers only about position and reads its rows past
/// every visibility filter, so it can never stand in for the first.
/// </para>
/// <para>
/// This is the one home of that rule for a trip's cave list. Every surface carrying the list
/// asks here — the read that produces it and the write that reconciles it alike — because
/// the write has to put back exactly what the read took out, and two copies of the predicate
/// are two answers waiting to drift apart. It sits beside the write core rather than inside any
/// one surface for that reason.
/// </para>
/// </remarks>
public static class TripCaveDisclosure
{
    public static async Task<HashSet<Guid>> DisclosableCaveIdsAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyCollection<Guid> namedCaveIds,
        CancellationToken ct)
    {
        var ids = namedCaveIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        // Narrowed in the statement rather than after it, and narrowed to caves in the same
        // predicate: the list promises caves, and a role naming a spring belongs to the roles.
        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => ids.Contains(f.Id) && f.Kind == FeatureKind.Cave)
            .Select(f => f.Id)
            .ToListAsync(ct);

        // Asked over what survived the first gate, never over the whole named set: the position
        // rule cannot be expressed in the same statement, and asking it first would let an
        // unguarded private cave through on the strength of having no position to guard.
        var redacted = await protection.RedactedLinkTargetIdsAsync(ctx, readable, ct);
        return [.. readable.Where(id => !redacted.Contains(id))];
    }
}
