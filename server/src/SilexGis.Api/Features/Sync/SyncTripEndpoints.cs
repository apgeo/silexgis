// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The trips other phones of the club recorded in the caves a set carries, read in pages like the
/// features themselves, each recording with its scans.
/// </summary>
/// <remarks>
/// <para>
/// A recording is carried when two things hold for this reader: its cave is one the feature
/// stream would hand them right now (inside the set, readable, and not withheld), and the
/// recording itself is readable under the trip rules. A recording in a withheld cave is absent,
/// not stubbed — a recording names its cave, and a row hanging on a withheld cave says where to
/// look for it.
/// </para>
/// <para>
/// A scan names a place, and the place is asked the same question: a scan of a place the set does
/// not hand this reader is left out of the recording, which is otherwise complete. Both questions
/// are put to the one answer every stream reads against, never decided here.
/// </para>
/// </remarks>
public static class SyncTripEndpoints
{
    /// <summary>
    /// A recording changed when it was itself last written, when any of its scans was — added,
    /// edited or removed — or when its cave was, whichever is latest. The scans' time is what
    /// tells a phone holding the recording that its list of scans moved, since the list is
    /// replaced wholesale. The cave's time carries a recording to a phone that already read past
    /// it when the cave comes into what the set hands this reader through a change to its own row
    /// alone — a location guard lifted, a move under one of the roots — exactly as it carries the
    /// cave's photographs.
    /// </summary>
    private static readonly SyncStreamOrder<SyncRecordingRow> ByChange = new(
        r => r.Recording.UpdatedAt > r.PointsChangedAt
            ? (r.Recording.UpdatedAt > r.CaveUpdatedAt ? r.Recording.UpdatedAt : r.CaveUpdatedAt)
            : (r.PointsChangedAt > r.CaveUpdatedAt ? r.PointsChangedAt : r.CaveUpdatedAt),
        r => r.Recording.Id);

    private static readonly SyncStreamOrder<SyncRecordingGone> ByGone = new(g => g.At, g => g.Id);

    public static void MapSyncTripEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapGet("/{id:guid}/trips", TripsAsync)
            .WithName("syncTrips")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("One page of the trip recordings made in a sync set's caves, and the recordings that have gone.");
    }

    private static async Task<Results<Ok<SyncTripsPageDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        TripsAsync(
            Guid id,
            string? cursor,
            int? pageSize,
            SilexGisDbContext db,
            FeatureProtection protection,
            IAccessContextAccessor accessAccessor,
            IOptions<SyncOptions> options,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (opening, problem) = await SyncStreamPaging.OpenAsync(
            db, ctx, SyncStream.Trips, id, cursor, pageSize, options.Value, ct);
        if (opening is null)
        {
            return problem!;
        }

        if (opening.Roots.Count == 0)
        {
            return TypedResults.Ok(new SyncTripsPageDto(
                opening.Set.Revision, opening.Settings, [], [], opening.Cursor, false));
        }

        var handed = await SyncScope.HandedAsync(db, protection, ctx, opening.Roots, ct);

        // The scans' latest change is read over removed scans too — that a scan went is a change
        // to its recording — and over nothing else: a scan of a recording that went is hidden with
        // the recording.
        var live =
            from recording in db.TripRecordings.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs)
            join cave in handed on recording.CaveFeatureId equals cave.Id
            select new SyncRecordingRow
            {
                Recording = recording,
                CaveUpdatedAt = cave.UpdatedAt,
                PointsChangedAt = db.TripRecordingPoints
                    .Where(p => p.RecordingId == recording.Id)
                    .Max(p => (DateTimeOffset?)p.UpdatedAt) ?? recording.UpdatedAt,
            };
        live = SyncStreamPaging.After(live, ByChange, opening.From);
        var liveRows = await SyncStreamPaging.Probe(live, ByChange, opening.Size).ToListAsync(ct);

        var goneRows = SyncStreamPaging.Probe(
            SyncStreamPaging.After((await GoneAsync(db, ctx, handed, opening.From, ct)).AsQueryable(), ByGone, opening.From),
            ByGone,
            opening.Size).ToList();

        var cut = SyncStreamPaging.Cut(opening, liveRows.Select(ByChange.Entry), goneRows.Select(ByGone.Entry));
        var rows = cut.Rows.OfType<SyncRecordingRow>().ToList();
        var points = await PointsAsync(db, handed, [.. rows.Select(r => r.Recording.Id)], ct);

        return TypedResults.Ok(new SyncTripsPageDto(
            opening.Set.Revision,
            opening.Settings,
            [.. rows.Select(r => new SyncTripRecordingDto(
                r.Recording.Id,
                r.Recording.CaveFeatureId,
                r.Recording.Title,
                r.Recording.Description,
                r.Recording.Log,
                r.Recording.StartedAt,
                r.Recording.EndedAt,
                r.Recording.CreatedByDeviceUserId,
                r.Recording.UpdatedAt,
                points.GetValueOrDefault(r.Recording.Id) ?? []))],
            [.. cut.Rows.OfType<SyncRecordingGone>().Select(g => new SyncTombstoneDto(g.Id, g.At))],
            cut.NextCursor,
            cut.HasMore));
    }

    /// <summary>
    /// The recordings this reader may read that have gone after the position, in caves the set
    /// hands this reader, each stamped with the moment it went.
    /// </summary>
    /// <remarks>
    /// Asked in two statements, and the split is the point. Reading a removed recording means
    /// looking past the filter that hides removed rows, and that switch cannot be thrown for one
    /// table of a statement: it would lift the same filter off the features the recordings are
    /// restricted to, and a deleted cave would count as handed. So the removed recordings are read
    /// past the filter on their own, and only their caves are then asked about, under every filter.
    /// Both are small: a recording is removed rarely, and only those removed after the position are
    /// read.
    /// </remarks>
    private static async Task<List<SyncRecordingGone>> GoneAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IQueryable<Domain.Entities.Feature> handed,
        SyncCursor? from,
        CancellationToken ct)
    {
        var removed = db.TripRecordings.AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.DeletedAt != null)
            .VisibleTo(ctx, AccessDomain.TripLogs)
            .Select(r => new SyncRecordingGone { Id = r.Id, At = r.DeletedAt!.Value, CaveId = r.CaveFeatureId });
        var candidates = await SyncStreamPaging.After(removed, ByGone, from).ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return [];
        }

        var caveIds = candidates.Select(c => c.CaveId).Distinct().ToList();
        var handedCaves = await handed.Where(f => caveIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
        return [.. candidates.Where(c => handedCaves.Contains(c.CaveId))];
    }

    /// <summary>
    /// The live scans of the page's recordings that this reader may be given, per recording, in
    /// the order they were made. A scan of a place the set does not hand this reader is left out —
    /// asked of the same answer the recordings' caves were, for every place the page names at once.
    /// </summary>
    private static async Task<Dictionary<Guid, List<SyncTripPointDto>>> PointsAsync(
        SilexGisDbContext db,
        IQueryable<Domain.Entities.Feature> handed,
        IReadOnlyList<Guid> recordingIds,
        CancellationToken ct)
    {
        if (recordingIds.Count == 0)
        {
            return [];
        }

        var points = await db.TripRecordingPoints.AsNoTracking()
            .Where(p => recordingIds.Contains(p.RecordingId) && p.DeletedAt == null)
            .OrderBy(p => p.ScannedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var placeIds = points.Where(p => p.PlaceFeatureId != null).Select(p => p.PlaceFeatureId!.Value).Distinct().ToList();
        var handedPlaces = placeIds.Count == 0
            ? []
            : (await handed.Where(f => placeIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct)).ToHashSet();

        return points
            .Where(p => p.PlaceFeatureId is not { } placeId || handedPlaces.Contains(placeId))
            .GroupBy(p => p.RecordingId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => new SyncTripPointDto(
                    p.Id, p.PlaceFeatureId, p.ScannedAt, p.Notes, p.CreatedByDeviceUserId, p.UpdatedAt)).ToList());
    }
}

/// <summary>One recording of the trips stream before it is cut, with the times its order is made of.</summary>
internal sealed class SyncRecordingRow
{
    public required Domain.Entities.TripRecording Recording { get; init; }

    /// <summary>When the recording's cave last changed.</summary>
    public required DateTimeOffset CaveUpdatedAt { get; init; }

    /// <summary>When any of the recording's scans last changed, or the recording's own time if it has none.</summary>
    public required DateTimeOffset PointsChangedAt { get; init; }
}

/// <summary>A recording that has gone, as the trips stream tells it.</summary>
internal sealed class SyncRecordingGone
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }

    public Guid CaveId { get; init; }
}
