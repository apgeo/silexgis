// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
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
    /// to somebody carrying nothing. There is no obfuscated or partial form of a followed trip;
    /// the rule, its two facts and its fail-closed shape are the one every public page shares.
    /// </summary>
    internal static Task<HashSet<Guid>> PublishableCaveIdsAsync(
        SilexGisDbContext db, FeatureProtection protection,
        IReadOnlyCollection<Guid> caveIds, CancellationToken ct) =>
        FeaturePublication.PublishableCaveIdsAsync(db, protection, caveIds, ct);

    /// <summary>
    /// Which of the given features — of any kind — carry no location protection at all.
    /// </summary>
    /// <remarks>
    /// A published page hangs photographs on the drawing, anchored by links that can relate the
    /// picture to an entrance, a spring, a GPS point or another cave. Publishing one member of such
    /// a link at a point in space gives the whole link a position, so a link naming anything
    /// guarded must not have its picture published — the same shared rule, asked of any kind.
    /// </remarks>
    internal static Task<HashSet<Guid>> UnguardedFeatureIdsAsync(
        SilexGisDbContext db, FeatureProtection protection,
        IReadOnlyCollection<Guid> featureIds, CancellationToken ct) =>
        FeaturePublication.UnguardedFeatureIdsAsync(db, protection, featureIds, ct);

    /// <summary>A row that claims a place, however partially — anything here is location data.</summary>
    internal static bool HasPosition(TripPositionEvent e) =>
        e.ViewerStationName is not null || e.ViewerToStationName is not null
        || e.DepthEnteredM is not null || e.SurveyModelId is not null;

    /// <summary>
    /// Whether this caller may see the row's position, given the caves open to them. A position
    /// row whose cave snapshot is gone answers false for everyone: with nothing left to evaluate
    /// protection against, the only safe answer is no answer.
    /// </summary>
    internal static bool PositionOpen(TripPositionEvent e, HashSet<Guid> openCaves) =>
        !HasPosition(e) || (e.CaveFeatureId is not null && openCaves.Contains(e.CaveFeatureId.Value));

    /// <summary>
    /// The caves these stored reports were placed in that this caller may see positions in.
    /// </summary>
    /// <remarks>
    /// Asked of the rows' own anchors and not of the watch's present survey: a log can span
    /// surveys, and a report stays anchored to the cave it was placed in after the watch has moved.
    /// </remarks>
    internal static Task<HashSet<Guid>> OpenCavesOfAsync(
        SilexGisDbContext db, IAccessService access, FeatureProtection protection, AccessContext? ctx,
        IEnumerable<TripPositionEvent> rows, CancellationToken ct) =>
        OpenCaveIdsAsync(
            db, access, protection, ctx,
            [.. rows.Where(e => e.CaveFeatureId is not null).Select(e => e.CaveFeatureId!.Value).Distinct()],
            ct);

    /// <summary>
    /// A stored report as this caller may be shown it: whole, or with its place taken out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place a stored report is turned into what a signed-in caller reads. The log's list
    /// and the sheet import's "what is there now" both answer through it, so a station the list
    /// withholds from somebody cannot reach them as the "before" of a row they are about to
    /// replace. What is taken out is the place and only the place — the station, the far end of a
    /// stretch from it, the depth and the survey it was read on; that the report exists, what kind it is, its note and its moment are
    /// the trip's, and the caller reads the trip.
    /// </para>
    /// <para>
    /// How a depth became its station is not carried: a stored report does not keep it.
    /// </para>
    /// <para>
    /// Whether the place lies outside the parts the watch declared is said on the same branch as
    /// the place and never beside one taken out. A caller that does not pass the comparison — the
    /// sheet import's "what is there now" — says nothing of it, which reads as "not marked".
    /// </para>
    /// </remarks>
    internal static TrackingEventDto Shown(
        TripPositionEvent e, HashSet<Guid> openCaves, TrackingDeclaredParts? declaredParts = null)
    {
        var open = PositionOpen(e, openCaves);
        return new TrackingEventDto(
            e.Id, e.CaverId, e.TeamId, e.Kind,
            open ? e.SurveyModelId : null,
            open ? e.ViewerStationName : null,
            // The far end of a stretch is place, and leaves with the rest of it.
            open ? e.ViewerToStationName : null,
            open ? e.DepthEnteredM : null,
            e.Note, e.RecordedAt,
            TripTrackingRules.ChangedSinceWritten(e.CreatedAt, e.UpdatedAt),
            open && declaredParts is not null && declaredParts.Outside(e),
            // Carried whether or not the place is, exactly as the note beside it is. What the
            // party was doing is not where they were: a reader kept from the station names still
            // learns nothing about the cave from being told somebody was asleep or rigging.
            Activity: e.Activity);
    }
}
