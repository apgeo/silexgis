// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// What a caller may learn from a tracking row, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Three surfaces ask: the state read, the event log, and the published page a follower without
/// an account opens. They ask on different terms — two of them have a caller to evaluate rights
/// for and the third has nobody at all — but they ask the same question, so the fold and the
/// per-row test live here rather than once per surface. A second copy of this rule is how one
/// surface comes to answer a station name another withholds.
/// </para>
/// <para>
/// Everything here fails closed. A row that claims a place and has lost the cave it claimed it
/// in is withheld from everyone, because with nothing left to evaluate protection against the
/// only safe answer is no answer.
/// </para>
/// </remarks>
internal static class TrackingWithholding
{
    /// <summary>
    /// Which of the given cave snapshots this caller may see positions in. Per cave, because
    /// history can span models and every position row carries its own anchor.
    /// </summary>
    /// <remarks>
    /// The context is nullable and a null one denies everything below — the access walk refuses
    /// a caller it does not have — which is the right answer for this question and deliberately
    /// not the one a published page asks: publication is decided by
    /// <see cref="PublishableCaveIdsAsync"/>, which asks about the cave rather than about a
    /// caller who does not exist.
    /// </remarks>
    internal static async Task<HashSet<Guid>> OpenCaveIdsAsync(
        SilexGisDbContext db, IAccessService access, FeatureProtection protection, AccessContext? ctx,
        IReadOnlyCollection<Guid> caveIds, CancellationToken ct)
    {
        var open = new HashSet<Guid>();
        if (caveIds.Count == 0) return open;
        var caves = await db.Features.AsNoTracking()
            .Where(f => caveIds.Contains(f.Id) && f.Kind == FeatureKind.Cave)
            .ToListAsync(ct);
        foreach (var cave in caves)
        {
            if (await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct)) open.Add(cave.Id);
        }
        return open;
    }

    /// <summary>
    /// Which of the given caves may be published — shown, positions and survey drawing and all,
    /// to somebody carrying nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked of the cave, not of a caller: there is no caller. A published page hands over the
    /// party's stations and a URL for the survey model itself, which is the cave's position by
    /// another name, so the only cave that may carry one is a cave under no protection at all.
    /// There is no obfuscated or partial form of a followed trip — the answer is refuse, not
    /// degrade — and this predicate is the whole of that decision, asked again on every read
    /// because protection can be switched on after a link has been handed out.
    /// </para>
    /// <para>
    /// Two facts have to agree, and requiring both is the point. <c>IsProtectedEffective</c> is
    /// the quick one — a derived column, maintained by the write service rather than computed
    /// here. Beside it the protection service is asked the same question the long way, from the
    /// roots' own <c>location_protected</c> flags resolved through the ancestor arrays, with
    /// the derived column deliberately not consulted. The two cannot normally disagree; where
    /// they did, a derived column that had gone stale-false would otherwise publish a protected
    /// cave, and requiring both means such a disagreement refuses. Asking the caller-facing
    /// exact-view predicate instead would not be a second fact at all: it short-circuits on the
    /// same column, so a set already filtered by it would always come back whole.
    /// </para>
    /// <para>
    /// A cave that is missing, is soft-deleted, or is not a cave at all is in none of these sets
    /// and is therefore not publishable — the same fail-closed shape the per-row test takes.
    /// </para>
    /// </remarks>
    internal static Task<HashSet<Guid>> PublishableCaveIdsAsync(
        SilexGisDbContext db, FeatureProtection protection,
        IReadOnlyCollection<Guid> caveIds, CancellationToken ct) =>
        UnguardedIdsAsync(db, protection, caveIds, FeatureKind.Cave, ct);

    /// <summary>
    /// Which of the given features — of any kind — carry no location protection at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same question <see cref="PublishableCaveIdsAsync"/> asks, asked of anything a feature
    /// can be, and it exists because a published page now hangs photographs on the drawing. A
    /// picture is anchored by a link, and a link relates any number of things: the picture, the
    /// station, and whatever else somebody said belongs with them — an entrance, a spring, a GPS
    /// point, another cave. Publishing one member of such a link at a point in space gives the
    /// whole link a position, so a link naming something guarded must not have its picture
    /// published, and "something guarded" there is not restricted to caves.
    /// </para>
    /// <para>
    /// Deliberately the same two facts and the same fail-closed shape rather than a second
    /// predicate written beside them — a second copy is exactly what this class exists to stop.
    /// </para>
    /// </remarks>
    internal static Task<HashSet<Guid>> UnguardedFeatureIdsAsync(
        SilexGisDbContext db, FeatureProtection protection,
        IReadOnlyCollection<Guid> featureIds, CancellationToken ct) =>
        UnguardedIdsAsync(db, protection, featureIds, kind: null, ct);

    /// <summary>The two facts, in one place; <paramref name="kind"/> narrows what may qualify.</summary>
    private static async Task<HashSet<Guid>> UnguardedIdsAsync(
        SilexGisDbContext db, FeatureProtection protection,
        IReadOnlyCollection<Guid> featureIds, FeatureKind? kind, CancellationToken ct)
    {
        var open = new HashSet<Guid>();
        if (featureIds.Count == 0) return open;

        var ids = featureIds.Distinct().ToList();

        // Fact one, read under the ordinary filters: a live feature the derived column calls
        // unprotected. Fact two, over the same candidates: no protection root above it,
        // resolved without that column. A feature needs both.
        var candidates = db.Features.AsNoTracking()
            .Where(f => ids.Contains(f.Id) && !f.IsProtectedEffective);
        // Applied as a second Where rather than folded into the predicate, so the query the cave
        // question asks is the one it always asked — a nullable compared inside the expression
        // would put a parameter test in the SQL for every caller of both.
        if (kind is { } wanted)
        {
            candidates = candidates.Where(f => f.Kind == wanted);
        }

        var byColumn = await candidates.Select(f => f.Id).ToListAsync(ct);
        if (byColumn.Count == 0) return open;

        var byAncestry = await protection.UnprotectedByAncestryIdsAsync(ids, ct);
        foreach (var id in byColumn)
        {
            if (byAncestry.Contains(id)) open.Add(id);
        }
        return open;
    }

    /// <summary>A row that claims a place, however partially — anything here is location data.</summary>
    internal static bool HasPosition(TripPositionEvent e) =>
        e.ViewerStationName is not null || e.DepthEnteredM is not null || e.SurveyModelId is not null;

    /// <summary>
    /// Whether this caller may see the row's position, given the caves open to them. A position
    /// row whose cave snapshot is gone answers false for everyone: with nothing left to evaluate
    /// protection against, the only safe answer is no answer.
    /// </summary>
    internal static bool PositionOpen(TripPositionEvent e, HashSet<Guid> openCaves) =>
        !HasPosition(e) || (e.CaveFeatureId is not null && openCaves.Contains(e.CaveFeatureId.Value));
}
