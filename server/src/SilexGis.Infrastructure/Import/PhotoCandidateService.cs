// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>One picture of a candidate place, as the review shows it.</summary>
public sealed record PhotoMemberInfo(
    Guid FileId,
    string OriginalName,
    string MimeType,
    FileKind Kind,
    DateTimeOffset? CapturedAt,
    PhotoPositionSource PositionSource,
    double? AltitudeMeters,
    double? DirectionDegrees,
    bool DirectionIsMagnetic,
    double? Dop,
    bool HasOwnPosition);

/// <summary>An existing object these pictures might be, and how far away it is.</summary>
public sealed record NearbyFeatureInfo(
    Guid FeatureId,
    string? Name,
    FeatureKind Kind,
    double DistanceMeters,
    Guid? CaveFeatureId,
    string? CaveName);

/// <summary>
/// One place a drop of photographs proposes, with every picture of it.
/// </summary>
/// <param name="Key">
/// The lowest file id among the pictures. This is what a saved decision is stored under, and it
/// is derived rather than allocated so that the same drop reviewed twice names the same
/// candidates.
/// </param>
/// <param name="TrackMatchSecondsFromFix">
/// How far this place's position sits from a recorded fix, when it was placed by time rather
/// than by the camera. Shown, because a picture matched four seconds from a fix is placed and
/// one matched two minutes from a fix is a guess about somebody walking.
/// </param>
public sealed record PhotoCandidate(
    Guid Key,
    Point? Geom,
    PhotoPositionSource PositionSource,
    IReadOnlyList<PhotoMemberInfo> Members,
    string? ProposedName,
    double? AltitudeMeters,
    double? DirectionDegrees,
    bool DirectionIsMagnetic,
    double? Dop,
    double? TrackMatchSecondsFromFix,
    bool TrackMatchInterpolated,
    IReadOnlyList<NearbyFeatureInfo> Nearby);

/// <summary>
/// What one reading of a drop amounts to: the places, and what the chosen track had to say.
/// </summary>
/// <param name="TrackFixCount">
/// How many recorded positions the chosen track offered. Zero with a track chosen means the file
/// holds no times, which is a fact about the file rather than a failure of the review — and the
/// difference between "nothing matched" and "nothing could" is the whole of what a reviewer
/// staring at unplaced pictures needs to know.
/// </param>
public sealed record PhotoCandidateSet(
    IReadOnlyList<PhotoCandidate> Candidates,
    int TrackFixCount,
    DateTimeOffset? TrackFirstFixAt,
    DateTimeOffset? TrackLastFixAt);

/// <summary>
/// Turns a drop of photographs into the places they show.
///
/// <para>
/// Nothing here writes, and nothing here decides who may see a picture — the files arrive
/// already authorised, because that walk belongs to the document rules and asking it again here
/// would be a second copy of it. What this does is the part that is about photographs: work out
/// where each one was taken, group the ones that show the same place, and say what is already in
/// the registry near it.
/// </para>
/// <para>
/// A picture is placed by what the camera recorded, or — when it recorded nothing — by matching
/// its capture time against a track somebody walked. A picture that neither places is still a
/// candidate, on its own, waiting to be dragged onto the map; it is not silently dropped,
/// because a photograph nobody can place is exactly the one worth showing a reviewer.
/// </para>
/// </summary>
public sealed class PhotoCandidateService(
    SilexGisDbContext db,
    VisibleProximitySearch proximity,
    IVectorIO vectorIo,
    IFileStore fileStore)
{
    /// <summary>
    /// How many objects the proximity list offers per candidate. Enough that "three objects
    /// within eighty metres" is a list somebody reads rather than a page they scroll.
    /// </summary>
    public const int MaxNearbyPerCandidate = 5;

    public async Task<PhotoCandidateSet> BuildAsync(
        IReadOnlyList<StoredFile> files,
        PhotoImportOptions options,
        IReadOnlyDictionary<Guid, PhotoDecision> decisions,
        AccessContext ctx,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
        {
            return new PhotoCandidateSet([], 0, null, null);
        }

        var byId = files.ToDictionary(f => f.Id);
        var track = await TrackIndexAsync(options, ct);

        var placed = new List<PhotoFix>();
        var matched = new Dictionary<Guid, TrackMatch>();
        var unplaced = new List<StoredFile>();

        foreach (var file in files)
        {
            if (file.Geom is { } point)
            {
                placed.Add(new PhotoFix(file.Id, point.X, point.Y, file.ContentCreatedAt));
                continue;
            }

            // Only now is the track consulted: a picture that carries its own fix keeps it, and
            // a track match never overrides what the camera actually recorded.
            if (track is not null
                && file.ContentCreatedAt is { } takenAt
                && track.Match(takenAt, options.CameraClockOffsetSeconds, options.TrackMatchToleranceSeconds)
                    is { } hit)
            {
                matched[file.Id] = hit;
                placed.Add(new PhotoFix(file.Id, hit.Longitude, hit.Latitude, takenAt));
                continue;
            }

            unplaced.Add(file);
        }

        var candidates = PhotoClustering.Group(placed, options.ClusterRadiusMeters)
            .Select(cluster => Build(cluster, byId, matched, decisions))
            .Concat(unplaced.Select(file => BuildUnplaced(file, decisions)))
            .ToList();

        // The track's own facts travel back with the candidates rather than being fetched again
        // by whoever wants to show them. A day's track is tens of thousands of points, and
        // parsing it twice to answer one screen is a cost nobody would find by reading either
        // caller on its own.
        return new PhotoCandidateSet(
            await WithNearbyAsync(candidates, options, ctx, ct),
            track?.Count ?? 0,
            track?.FirstTime,
            track?.LastTime);
    }

    // ---------- one candidate ----------

    private static PhotoCandidate Build(
        PhotoCluster cluster,
        IReadOnlyDictionary<Guid, StoredFile> byId,
        IReadOnlyDictionary<Guid, TrackMatch> matched,
        IReadOnlyDictionary<Guid, PhotoDecision> decisions)
    {
        var members = cluster.FileIds.Select(id => Member(byId[id])).ToList();
        var files = cluster.FileIds.Select(id => byId[id]).ToList();
        var decision = decisions.GetValueOrDefault(cluster.Key);

        // A position the reviewer gave by hand beats everything the files say: it is the only
        // one with a person behind it.
        var manual = PointOf(decision?.Position);
        var geom = manual ?? new Point(cluster.Longitude, cluster.Latitude) { SRID = 4326 };

        var match = cluster.FileIds.Select(matched.GetValueOrDefault).FirstOrDefault(m => m is not null);
        var source = manual is not null ? PhotoPositionSource.Manual
            : files.Any(f => f.Geom is not null) ? files.First(f => f.Geom is not null).PositionSource
            : PhotoPositionSource.TrackMatch;

        var withDirection = files.FirstOrDefault(f => f.DirectionDegrees is not null);
        return new PhotoCandidate(
            cluster.Key,
            geom,
            source,
            members,
            PhotoNameProposal.From(files.Select(f => f.OriginalName)),
            // The first picture that recorded one. Averaging altitudes across a group would
            // invent a number none of the cameras reported.
            files.FirstOrDefault(f => f.AltitudeMeters is not null)?.AltitudeMeters,
            withDirection?.DirectionDegrees,
            withDirection?.DirectionIsMagnetic ?? false,
            files.Where(f => f.PositionDop is not null).Select(f => f.PositionDop).Min(),
            manual is null ? match?.SecondsFromFix : null,
            manual is null && (match?.Interpolated ?? false),
            []);
    }

    /// <summary>
    /// A picture nothing could place: its own candidate, with no position, waiting for somebody
    /// to drag it onto the map. Pictures like this are never grouped with each other — without
    /// positions there is nothing to group them by, and guessing from capture times would make
    /// two shafts photographed a minute apart into one place.
    /// </summary>
    private static PhotoCandidate BuildUnplaced(
        StoredFile file, IReadOnlyDictionary<Guid, PhotoDecision> decisions)
    {
        var manual = PointOf(decisions.GetValueOrDefault(file.Id)?.Position);
        return new PhotoCandidate(
            file.Id,
            manual,
            manual is null ? PhotoPositionSource.None : PhotoPositionSource.Manual,
            [Member(file)],
            PhotoNameProposal.From(file.OriginalName),
            file.AltitudeMeters,
            file.DirectionDegrees,
            file.DirectionIsMagnetic,
            file.PositionDop,
            null,
            false,
            []);
    }

    private static PhotoMemberInfo Member(StoredFile file) => new(
        file.Id,
        file.OriginalName,
        file.MimeType,
        file.Kind,
        file.ContentCreatedAt,
        file.PositionSource,
        file.AltitudeMeters,
        file.DirectionDegrees,
        file.DirectionIsMagnetic,
        file.PositionDop,
        file.Geom is not null);

    private static Point? PointOf(IReadOnlyList<double>? position) =>
        position is { Count: 2 }
            && double.IsFinite(position[0]) && double.IsFinite(position[1])
            && position[0] is >= -180 and <= 180 && position[1] is >= -90 and <= 90
            ? new Point(position[0], position[1]) { SRID = 4326 }
            : null;

    // ---------- what is already there ----------

    /// <summary>
    /// The proximity list on every placed candidate: the few nearest objects the caller may both
    /// read and place, nearest first. This is the "three objects within eighty metres" that turns
    /// filing a trip's photographs into one press per place instead of map work.
    /// </summary>
    private async Task<IReadOnlyList<PhotoCandidate>> WithNearbyAsync(
        IReadOnlyList<PhotoCandidate> candidates,
        PhotoImportOptions options,
        AccessContext ctx,
        CancellationToken ct)
    {
        var points = candidates.Where(c => c.Geom is not null).Select(c => c.Geom!).ToList();
        var radius = Math.Min(options.ProximityRadiusMeters, PhotoImportOptions.MaxProximityRadiusMeters);
        var comparable = await proximity.NearAsync(points, radius, ctx, ct);
        if (comparable.Count == 0)
        {
            return candidates;
        }

        return
        [
            .. candidates.Select(candidate => candidate.Geom is not { } point
                ? candidate
                : candidate with
                {
                    Nearby =
                    [
                        .. comparable
                            .Select(hit => new NearbyFeatureInfo(
                                hit.FeatureId,
                                hit.Name,
                                hit.Kind,
                                Geodesy.DistanceMeters(point.Coordinate, hit.Geom.Coordinate),
                                hit.CaveFeatureId,
                                hit.CaveName))
                            .Where(hit => hit.DistanceMeters <= radius)
                            .OrderBy(hit => hit.DistanceMeters)
                            .Take(MaxNearbyPerCandidate)
                    ],
                })
        ];
    }

    // ---------- the track a picture is placed against ----------

    /// <summary>
    /// The chosen track's recorded fixes, or null when no track was chosen or it holds none.
    /// Read from the stored file each time rather than kept: a review is a handful of requests
    /// over an afternoon, and a cache of parsed tracks would be a second thing to invalidate
    /// when somebody re-uploads the file.
    /// </summary>
    private async Task<TrackFixIndex?> TrackIndexAsync(PhotoImportOptions options, CancellationToken ct)
    {
        if (options.TrackGeofileId is not { } geofileId)
        {
            return null;
        }

        var geofile = await db.Geofiles.AsNoTracking()
            .Where(g => g.Id == geofileId)
            .Select(g => new { g.FileId, g.Format })
            .FirstOrDefaultAsync(ct);
        if (geofile is null)
        {
            return null;
        }

        var storagePath = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == geofile.FileId)
            .Select(f => f.StoragePath)
            .FirstOrDefaultAsync(ct);
        if (storagePath is null)
        {
            return null;
        }

        try
        {
            var fixes = vectorIo.ReadTrackFixes(fileStore.GetAbsolutePath(storagePath), geofile.Format);
            return fixes.Count == 0 ? null : TrackFixIndex.Of(fixes);
        }
        catch (VectorIOException)
        {
            // A track that will not open places nothing. The review still works — every picture
            // that carries its own fix is unaffected — and saying so is the endpoint's job.
            return null;
        }
    }
}
