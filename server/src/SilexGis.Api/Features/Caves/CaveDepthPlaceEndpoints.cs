// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// What a cave's depths mean: the station each declared depth stands for, and the word people use
/// for the place.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a cave declares this at all.</b> A watch turns a reported depth into a station by
/// measuring — the nearest station to the datum under the trip's filter — and measurement answers
/// the wrong question wherever the cave's shape disagrees with its arithmetic: parallel shafts
/// share a depth, a meander wanders up and down, and the station nearest a round number may be
/// somewhere nobody goes. A declaration is the cave's own answer, written once by somebody who
/// knows it, and read before any measuring is attempted.
/// </para>
/// <para>
/// <b>Read with the cave, written with the cave.</b> Reading takes Read on the cave and writing
/// takes Write, with no separate right of its own: a declaration is a fact about the cave's shape,
/// of exactly the same kind as its name or its length, and a reader who may be told the one may be
/// told the other. A cave nobody may read answers the same not-found an absent cave answers.
/// </para>
/// <para>
/// <b>Location protection does not reach the rows, and the reason is worth stating.</b> These rows
/// carry no coordinate: a depth is a distance below an entrance and a station name is a label
/// inside a survey, neither of which places a cave on the earth. What protects a survey's geometry
/// is the gate on the survey model itself, which is untouched here.
/// </para>
/// <para>
/// <b>It does reach the one thing said here about the survey.</b> Each row is sent with whether the
/// cave's survey holds its station, and that is not a fact about the declaration: it says the cave
/// has a survey that has been read, and which names are and are not in it. So it is answered on
/// the survey's terms — Read on the cave and its exact position open — and is null for everybody
/// else, exactly as it is for a cave with no survey at all.
/// </para>
/// </remarks>
public static class CaveDepthPlaceEndpoints
{
    public static RouteGroupBuilder MapCaveDepthPlaceEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("CaveDepthPlaces");

        caves.MapGet("/{caveId:guid}/depth-places", ListAsync)
            .WithSummary("The depths this cave has declared the meaning of, shallowest first.");

        caves.MapPut("/{caveId:guid}/depth-places", WriteAsync)
            .WithValidation<CaveDepthPlaceWriteRequest>()
            .WithSummary(
                "Declares what one depth of this cave means, replacing the declaration for that "
                + "depth where there is one (Write on the cave).");

        caves.MapDelete("/{caveId:guid}/depth-places/{id:guid}", DeleteAsync)
            .WithSummary("Withdraws one declaration (Write on the cave).");

        return api;
    }

    private static async Task<Results<Ok<List<CaveDepthPlaceDto>>, ProblemHttpResult>> ListAsync(
        Guid caveId, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (await CaveAsync(db, access, ctx, caveId, AccessAction.Read, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var rows = await db.CaveDepthPlaces.AsNoTracking()
            .Where(x => x.CaveFeatureId == caveId)
            // Shallowest first: the order the cave is described in, and the order a chooser has to
            // offer, because somebody picking where a party is thinks downwards from the entrance.
            .OrderBy(x => x.DepthM)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var inSurvey = await InSurveyAsync(db, ctx, caveId, [.. rows.Select(x => x.ViewerStationName)], ct);
        return TypedResults.Ok(rows
            .Select(x => new CaveDepthPlaceDto(
                x.Id, x.DepthM, x.ViewerStationName, x.PlaceLabel, inSurvey(x.ViewerStationName)))
            .ToList());
    }

    private static async Task<Results<Ok<CaveDepthPlaceDto>, ProblemHttpResult>> WriteAsync(
        Guid caveId, CaveDepthPlaceWriteRequest request, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (await CaveAsync(db, access, ctx, caveId, AccessAction.Write, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // Keyed the way the table keys it — magnitude, one decimal — before it is looked up or
        // written. The request accepts the sign field notes use and any decimals a person types;
        // the column holds neither. Looking up the raw value would find nothing beside the row at
        // 120 for a write of 120.04 or −120, and the insert that followed would then be refused by
        // the unique index for the very row it failed to find.
        var depth = DeclaredDepthPlaces.Key(request.DepthM!.Value);
        var station = request.StationName!.Trim();
        var label = string.IsNullOrWhiteSpace(request.PlaceLabel) ? null : request.PlaceLabel.Trim();

        // The depth is the key, so a second write at the same depth is a correction rather than a
        // second answer. Done by reading first rather than by catching the unique index: the index
        // is there to keep the table honest if this is ever bypassed, and an upsert that relied on
        // it would report a conflict for the ordinary act of changing one's mind.
        var existing = await db.CaveDepthPlaces
            .FirstOrDefaultAsync(x => x.CaveFeatureId == caveId && x.DepthM == depth, ct);

        if (existing is null)
        {
            existing = new CaveDepthPlace
            {
                CaveFeatureId = caveId,
                DepthM = depth,
                ViewerStationName = station,
                PlaceLabel = label,
            };
            db.CaveDepthPlaces.Add(existing);
        }
        else
        {
            existing.ViewerStationName = station;
            existing.PlaceLabel = label;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (IsRaceOnDepth(e))
        {
            // With the key normalised above, the index can only fire when two first-time writes
            // at one depth cross between the read and this save. That is a conflict worth its
            // name rather than an unexplained failure: the caller reads the list and writes again.
            return ApiProblems.Conflict("cave_depth_place.concurrent_write",
                "That depth was declared by somebody else while this was being written. Read the list and write again.");
        }

        // Said on the answer to a write as it is on the list, under the same terms: the person
        // who has just typed a station name is the one who can act on being told the survey does
        // not have it, and somebody who may write the cave but not place it is told nothing.
        var inSurvey = await InSurveyAsync(db, ctx, caveId, [existing.ViewerStationName], ct);
        return TypedResults.Ok(new CaveDepthPlaceDto(
            existing.Id, existing.DepthM, existing.ViewerStationName, existing.PlaceLabel,
            inSurvey(existing.ViewerStationName)));
    }

    /// <summary>
    /// For each of these station names, whether the cave's current survey holds it — or null for
    /// all of them where that may not be said to this caller, or there is no such survey.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Current" is the survey carrying the cave's mark, once it has been read, and nothing else:
    /// a survey that merely stands in for an unreadable marked one answers for the cave's figures
    /// but not here. The page that shows these answers offers station names out of the marked
    /// survey and tells its reader to check which survey is the current one, so an answer judged
    /// against any other upload would contradict both.
    /// </para>
    /// <para>
    /// The survey and the caller's right to hear about it are asked together, in one statement,
    /// so there is no path through here on which the names are compared for somebody who was not
    /// first found entitled to the survey.
    /// </para>
    /// <para>
    /// The comparison itself is the one a watch makes before it honours a declaration, asked of
    /// the same function: a declaration keeps the name the drawing shows, a station row may hold
    /// it under the file's root survey, and "is it there" has to mean the same thing on the cave's
    /// page as it does at the moment a report is placed. Only the rows either reading could mean
    /// are fetched — a cave declares a handful of places and its survey holds tens of thousands
    /// of stations.
    /// </para>
    /// </remarks>
    private static async Task<Func<string, bool?>> InSurveyAsync(
        SilexGisDbContext db, AccessContext? ctx, Guid caveId, IReadOnlyList<string> viewerNames,
        CancellationToken ct)
    {
        if (ctx is null || viewerNames.Count == 0)
        {
            return _ => null;
        }

        var survey = await ChosenSurveyModelSql.CurrentForCaveAsync(db, ctx, caveId, ct);
        if (survey is null)
        {
            return _ => null;
        }

        var readings = viewerNames
            .SelectMany(name => SurveyStationNames.StoredCandidates(survey.Format, survey.RootSurveyName, name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var held = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == survey.Id && readings.Contains(s.Name))
            .Select(s => new { s.Name, s.SurveyName })
            .ToListAsync(ct);

        // Altitude and the entrance flag play no part in whether a name is there, so neither is read.
        var stations = held
            .Select(s => TrackingDepthResolver.Station.Of(
                survey.Format, survey.RootSurveyName, s.Name, s.SurveyName, z: 0, isEntrance: false))
            .ToList();

        return name => TrackingDepthPlacements.NamesAStationOf(
            stations, new DeclaredDepthPlaces.Declared(0, name, null));
    }

    /// <summary>The unique index on (cave, depth) refused the write, and nothing else did.</summary>
    private static bool IsRaceOnDepth(DbUpdateException e) =>
        e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_cave_depth_places_cave_depth",
        };

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid caveId, Guid id, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (await CaveAsync(db, access, ctx, caveId, AccessAction.Write, ct) is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var row = await db.CaveDepthPlaces
            .FirstOrDefaultAsync(x => x.Id == id && x.CaveFeatureId == caveId, ct);
        if (row is null)
        {
            return ApiProblems.NotFound("cave_depth_place.not_found");
        }

        db.CaveDepthPlaces.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The cave, when this caller may act on it in the way asked. Missing and refused answer alike
    /// — one shape, so reaching for a cave nobody may see learns nothing about whether it exists.
    /// </summary>
    private static async Task<Feature?> CaveAsync(
        SilexGisDbContext db, IAccessService access, AccessContext? ctx, Guid caveId,
        AccessAction action, CancellationToken ct)
    {
        var cave = await db.Features.FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        return cave is not null && (await access.DecideAsync(ctx, action, cave, ct)).Allowed ? cave : null;
    }
}
