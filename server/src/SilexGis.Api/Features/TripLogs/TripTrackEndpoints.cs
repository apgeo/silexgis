// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// Writes a recorded track onto a trip as the trip's own sketch, from a GPX file uploaded in
/// one request.
/// </summary>
/// <remarks>
/// <para>
/// The ordinary GPX path lands a line feature in the registry, which is right for a surface
/// survey and wrong for "where we walked on Saturday": that is a fact about the trip, drawn on
/// the trip's page and handed exactly to everyone who may read the trip, and it has a column of
/// its own. Nothing of the file is kept but the line — the upload is read on a scratch path the
/// server names and deleted before the response — so no registry row, no stored file and no
/// import batch is minted for what is one edit of one trip.
/// </para>
/// <para>
/// The file is read by the engine the geofile import reads it with, which hands back one
/// aggregate line per track or route, and every linear part is joined end to end in file order
/// into one line: a trip has one sketch, and a day's recording a unit split into segments where
/// it lost its fix is still one walk. Waypoints are ignored, since a point is not a track, and
/// a file with no linear part is refused rather than written as nothing.
/// </para>
/// <para>
/// Guarded as a trip write and answering like one: 404 for a trip the caller was never shown,
/// 403 for one they may read but not change. Saving moves the trip's row version, so an edit
/// form already open on the trip is refused on its next save and re-reads — the right outcome,
/// since the sketch it holds is no longer the trip's.
/// </para>
/// </remarks>
public static class TripTrackEndpoints
{
    public const string FileEmptyCode = "trip_track.file_empty";
    public const string FileTooLargeCode = "trip_track.file_too_large";
    public const string FormatUnsupportedCode = "trip_track.format_unsupported";
    public const string FileUnreadableCode = "trip_track.file_unreadable";
    public const string NoTrackCode = "trip_track.no_track";
    public const string TooManyPointsCode = "trip_track.too_many_points";

    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);

    public static RouteGroupBuilder MapTripTrackEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/trip-logs/{id:guid}/geometry/gpx", ImportAsync)
            .DisableAntiforgery() // bearer-token API; no cookie-form surface to forge
            .WithTags("TripLogs")
            .WithSummary("Sets the trip's own geometry from the track in an uploaded GPX file.")
            .WithDescription(
                "Multipart, one part named 'file'. Every track and route in the file is joined, in " +
                "file order, into one line that replaces the trip's sketch; waypoints are ignored. " +
                "Nothing of the file is kept. Needs Write on the trip.");
        return api;
    }

    private static async Task<Results<Ok<TripLogDto>, UnauthorizedHttpResult, ProblemHttpResult>> ImportAsync(
        Guid id,
        IFormFile file,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        FeatureProtection protection,
        IVectorIO vectorIO,
        IOptions<ImportLimitOptions> limits,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var trip = await db.TripLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (trip is null)
        {
            return ApiProblems.NotFound("trip_log.not_found");
        }

        // The same ladder the trip's own update climbs: a reader is refused, and somebody who
        // may not even read the trip is not told that it exists.
        if (!(await access.DecideAsync(ctx, AccessAction.Write, trip, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("trip_log.not_found");
        }

        // The upload's shape is refused before a byte of it is written anywhere.
        if (file.Length == 0)
        {
            return ApiProblems.BadRequest(FileEmptyCode, "The uploaded file is empty.");
        }

        if (file.Length > limits.Value.MaxTripTrackBytes)
        {
            return ApiProblems.BadRequest(
                FileTooLargeCode,
                $"The file is larger than the {limits.Value.MaxTripTrackBytes} bytes a trip's track may be.");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".gpx", StringComparison.OrdinalIgnoreCase))
        {
            return ApiProblems.BadRequest(FormatUnsupportedCode, "A trip takes its track from a GPX file.");
        }

        LineString track;
        try
        {
            track = await ReadTrackAsync(file, vectorIO, limits.Value.MaxTripTrackPoints, ct);
        }
        catch (TrackRefusal refusal)
        {
            return ApiProblems.BadRequest(refusal.Code, refusal.Message);
        }

        trip.Geom = track;

        // The people expecting to go on this trip are told it changed, the way the trip's own
        // update tells them: the sketch is one of the trip's facts, and a track written onto a
        // plan says where the party means to walk.
        await TripPlanNotifier.ChangedAsync(db, access, user, trip, [], ct);
        await db.SaveChangesAsync(ct);

        var items = await TripLogEndpoints.MapWithChildrenAsync(db, access, protection, ctx, user, [trip], ct);
        // The version this write produced, filed under the trip's own path. A page that reads a
        // track onto the trip and then saves the trip is saving over this version, and without
        // it that save would be refused until the trip had been read again.
        await Concurrency.EmitETagAsync(
            http, db, VersionedTable.TripLogs, trip.Id, ct, TripLogEndpoints.TripPath(trip.Id));
        return TypedResults.Ok(items[0]);
    }

    /// <summary>
    /// Reads the upload off a scratch file the server names and hands back the one line it
    /// holds. The scratch file is gone before this returns, whatever happened.
    /// </summary>
    private static async Task<LineString> ReadTrackAsync(
        IFormFile file, IVectorIO vectorIO, int maxPoints, CancellationToken ct)
    {
        // The engine reads from a path, and the uploaded name is never used to place anything.
        var scratch = Path.Combine(Path.GetTempPath(), $"silexgis-trip-track-{Guid.CreateVersion7():N}.gpx");
        try
        {
            await using (var content = file.OpenReadStream())
            await using (var target = new FileStream(scratch, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(target, ct);
            }

            VectorDataset dataset;
            try
            {
                dataset = vectorIO.Read(scratch, GeofileFormat.Gpx);
            }
            catch (VectorIOException e)
            {
                throw new TrackRefusal(FileUnreadableCode, e.Message);
            }

            return JoinTracks(dataset, maxPoints);
        }
        finally
        {
            try
            {
                File.Delete(scratch);
            }
            catch (IOException)
            {
                // A scratch file that would not go is a file the next cleanup takes; the trip is
                // not held hostage to it.
            }
        }
    }

    /// <summary>
    /// Every linear part of every feature, end to end in file order, as one planar line.
    /// </summary>
    private static LineString JoinTracks(VectorDataset dataset, int maxPoints)
    {
        var coordinates = new List<Coordinate>();
        foreach (var feature in dataset.Features)
        {
            foreach (var part in LinearParts(feature.Geom))
            {
                foreach (var coordinate in part.Coordinates)
                {
                    // Segments meet where the unit paused: the joint appears at the end of one and
                    // the start of the next, and once is enough. The elevation a track point
                    // carries is dropped — the sketch is drawn on a map, and a trip's depth has a
                    // field of its own.
                    var planar = new Coordinate(coordinate.X, coordinate.Y);
                    if (coordinates.Count > 0 && coordinates[^1].Equals2D(planar))
                    {
                        continue;
                    }

                    coordinates.Add(planar);
                    if (coordinates.Count > maxPoints)
                    {
                        throw new TrackRefusal(
                            TooManyPointsCode, $"The track has more than the {maxPoints} points a trip's sketch may carry.");
                    }
                }
            }
        }

        if (coordinates.Count < 2)
        {
            throw new TrackRefusal(NoTrackCode, "The file holds no track or route to write onto the trip.");
        }

        var line = Wgs84.CreateLineString([.. coordinates]);
        if (!line.IsValid)
        {
            throw new TrackRefusal(FileUnreadableCode, "The track does not make a valid line.");
        }

        return line;
    }

    /// <summary>
    /// The line strings inside a geometry, in order. A multi-line is a collection, so it is
    /// matched before the general collection arm; a point or a polygon yields nothing.
    /// </summary>
    private static IEnumerable<LineString> LinearParts(Geometry geometry) => geometry switch
    {
        LineString line => [line],
        MultiLineString lines => lines.Geometries.OfType<LineString>(),
        GeometryCollection collection => collection.Geometries.SelectMany(LinearParts),
        _ => [],
    };

    private sealed class TrackRefusal(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
