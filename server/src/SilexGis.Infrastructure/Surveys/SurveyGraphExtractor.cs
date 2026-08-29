// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using Therion.Blender;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The station, shot and passage-dimension rows read out of one compiled survey, where the file sits
/// in the world, and what reading it could not keep.
/// </summary>
/// <param name="Stations">Every station the file names, in file order.</param>
/// <param name="Shots">Every leg the file carries, in file order — splays included.</param>
/// <param name="Lrud">
/// Every wall measurement the file states, keyed by the station it was taken at, whichever of the
/// two shapes the file used to say it. A reading in which the surveyor measured nothing is not a
/// reading and is not here.
/// </param>
/// <param name="DroppedShotCount">
/// Legs of the traverse whose endpoints matched no station, or matched the same one twice, and so
/// join the station network nowhere. Splay, surface and duplicate legs are not counted: they are
/// stored, but they were never part of the network to begin with.
/// </param>
/// <param name="MergedStationCount">
/// Stations that stood at a position an earlier station already held, and so became one node.
/// </param>
public sealed record SurveyGraphExtraction(
    IReadOnlyList<SurveyStation> Stations,
    IReadOnlyList<SurveyShot> Shots,
    IReadOnlyList<SurveyLrud> Lrud,
    int DroppedShotCount,
    int MergedStationCount,
    double AnchorLongitude,
    double AnchorLatitude,
    double AnchorHeightM,
    double AppliedRotationDeg);

/// <summary>
/// Reads a parsed compiled survey into this application's own station and shot rows.
///
/// <para>
/// Pure by design: a parsed model in, rows out, nothing read from disk and nothing written to the
/// database. Reading the bytes and storing the rows belong to the job that calls this; keeping them
/// out means the rules below — which name a station carries, which legs count as network, what a
/// position means — can be stated as arithmetic and tested as arithmetic.
/// </para>
///
/// <para>
/// Two losses are counted rather than ignored. Legs name no endpoints in one of the two formats, so
/// endpoints are matched to stations by exact coordinate equality; a leg that matches nothing is
/// stored but joins nothing, and two stations at one position become one node. Both losses are
/// silent, and a network missing a tenth of its legs still produces connectivity numbers that look
/// entirely reasonable — the counts are the only thing that says otherwise.
/// </para>
///
/// <para>
/// Passage dimensions are the one thing the two formats describe in genuinely incompatible shapes,
/// and both are flattened here into one station-keyed row. One format measures the walls at each end
/// of a leg and names a cross-section shape, so its readings carry the leg they were taken along;
/// the other emits runs of cross-sections keyed by station name alone, with no leg and no shape.
/// Whichever the file used, what comes out is a reading at a named station, with the leg and the
/// shape filled in only where that format has them to give.
/// </para>
/// </summary>
public sealed class SurveyGraphExtractor(ICoordinateProjector projector)
{
    /// <summary>
    /// Reads <paramref name="model"/> into rows belonging to <paramref name="surveyModelId"/>,
    /// placed in the world by what <paramref name="declaration"/> says about the file.
    /// </summary>
    /// <exception cref="SurveySourceException">
    /// The file cannot be placed: a local file with no position for its zero point, a coordinate
    /// system this installation cannot resolve, or a projected file with no coordinates at all.
    /// </exception>
    public SurveyGraphExtraction Extract(
        CaveModel model, Guid surveyModelId, SurveySourceDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(declaration);

        var placement = SurveyPlacement.Resolve(projector, declaration, () => FootprintCentre(model));
        var surveyPaths = SurveyPaths(model);

        // The name standing at each position, first station there winning — the same rule the
        // network reconstruction uses, because these names are what the legs will point at. Exact
        // double equality on all three axes: the altitude is not decoration, since a deep cave has
        // distinct stations sharing a plan position and treating two of them as one node has been
        // measured inflating a reduced network to 143% of the length actually surveyed.
        var nameAtPosition = new Dictionary<CaveVector3, string>(model.Stations.Count);

        // The name of each station by the number the file wrote it at. Only the legs need this, and
        // only in the format whose legs reference stations by that number rather than by position:
        // a wall measurement taken at a station that shares a position with another must be stored
        // against the station that was actually measured, and the position lookup above deliberately
        // answers with whichever of the two got there first.
        var nameByFileId = new Dictionary<uint, string>(model.Stations.Count);

        var stations = new List<SurveyStation>(model.Stations.Count);
        var mergedStations = 0;

        foreach (var station in model.Stations)
        {
            var surveyName = SurveyNameOf(station.SurveyId, surveyPaths);
            var name = StationName(station, surveyName, model.SeparatorChar);
            var (longitude, latitude, altitude) =
                placement.ToWorld(station.Position.X, station.Position.Y, station.Position.Z);

            stations.Add(new SurveyStation
            {
                SurveyModelId = surveyModelId,
                Name = name,
                SurveyName = surveyName,
                FileStationId = station.Id,
                Position = new Point(new CoordinateZ(longitude, latitude, altitude)) { SRID = 4326 },
                Flags = MapStationFlags(station.Flags),
                RawFlags = station.RawFlags,
            });

            nameByFileId[station.Id] = name;

            if (!nameAtPosition.TryAdd(station.Position, name))
            {
                mergedStations++;
            }
        }

        string? StationOf(uint? fileStationId) =>
            fileStationId is { } id && nameByFileId.TryGetValue(id, out var known) ? known : null;

        var shots = new List<SurveyShot>(model.Shots.Count);
        var readings = new List<SurveyLrud>();
        var droppedShots = 0;

        foreach (var shot in model.Shots)
        {
            var from = nameAtPosition.GetValueOrDefault(shot.FromPosition);
            var to = nameAtPosition.GetValueOrDefault(shot.ToPosition);
            var flags = MapShotFlags(shot.Flags);

            var (fromLongitude, fromLatitude, fromAltitude) =
                placement.ToWorld(shot.FromPosition.X, shot.FromPosition.Y, shot.FromPosition.Z);
            var (toLongitude, toLatitude, toAltitude) =
                placement.ToWorld(shot.ToPosition.X, shot.ToPosition.Y, shot.ToPosition.Z);

            var leg = new SurveyShot
            {
                SurveyModelId = surveyModelId,
                FromStationName = from,
                ToStationName = to,
                SurveyName = shot.SurveyName ?? SurveyNameOf(shot.SurveyId, surveyPaths),
                Geom = new LineString(
                    [
                        new CoordinateZ(fromLongitude, fromLatitude, fromAltitude),
                        new CoordinateZ(toLongitude, toLatitude, toAltitude),
                    ])
                {
                    SRID = 4326,
                },

                // The length the file states, kept as it was surveyed. Measuring it back off the
                // stored longitude and latitude would answer a slightly different question and
                // would answer it approximately.
                LengthM = (shot.ToPosition - shot.FromPosition).Length,
                Flags = flags,
                RawFlags = shot.RawFlags,
            };

            shots.Add(leg);

            // The wall measurements this leg carries, one at each of its ends, in the format that
            // states them that way. The leg travels with them: it is the only thing that says which
            // way the surveyor was facing when the walls were measured, and it is exactly what the
            // other format cannot say.
            var section = MapSection(shot.SectionType);
            AddReading(readings, surveyModelId, StationOf(shot.FromStationId) ?? from, shot.FromLrud, section, leg);
            AddReading(readings, surveyModelId, StationOf(shot.ToStationId) ?? to, shot.ToLrud, section, leg);

            // A splay is a shot at the wall, and its far end is routinely a point the file names no
            // station for; a surface or duplicate leg is not passage to be counted either. None of
            // those is a loss, so counting them as one would bury the losses that matter.
            const SurveyShotFlags notNetwork =
                SurveyShotFlags.Splay | SurveyShotFlags.Surface | SurveyShotFlags.Duplicate;
            if ((flags & notNetwork) == 0 && (from is null || to is null || from == to))
            {
                droppedShots++;
            }
        }

        // The same measurements as the block above, in the shape the other format states them:
        // ordered runs of cross-sections that name their station and nothing else. There is no leg
        // to record and no shape to record, and inventing either — picking one of the station's legs
        // to blame the reading on — would be storing a guess as if the file had said it.
        foreach (var passage in model.Passages)
        {
            foreach (var section in passage.Stations)
            {
                AddReading(
                    readings,
                    surveyModelId,
                    section.StationName,
                    new CaveLrud(section.Left, section.Right, section.Up, section.Down),
                    shape: null,
                    leg: null);
            }
        }

        return new SurveyGraphExtraction(
            stations,
            shots,
            readings,
            droppedShots,
            mergedStations,
            placement.Anchor.Longitude,
            placement.Anchor.Latitude,
            placement.OriginHeightM,
            placement.AppliedRotationDeg);
    }

    /// <summary>
    /// Records one set of wall distances at one named station, if there is anything there to record.
    ///
    /// <para>
    /// Two things are dropped rather than stored. A reading whose station could not be named cannot
    /// be filed against anything — the station name is what identifies a reading, so a nameless one
    /// is a row nothing could ever ask for. And a reading in which the surveyor measured nothing is
    /// not a reading: both formats emit all four distances whenever they emit a leg or a
    /// cross-section at all, filled or not, so keeping the empty ones would put a row on almost
    /// every station and make "how many stations have passage dimensions" answer with the size of
    /// the survey.
    /// </para>
    /// </summary>
    private static void AddReading(
        List<SurveyLrud> readings,
        Guid surveyModelId,
        string? stationName,
        CaveLrud? lrud,
        SurveySectionShape? shape,
        SurveyShot? leg)
    {
        if (stationName is null || lrud is not { } walls)
        {
            return;
        }

        var left = SurveyDimensions.Measured(walls.Left);
        var right = SurveyDimensions.Measured(walls.Right);
        var up = SurveyDimensions.Measured(walls.Up);
        var down = SurveyDimensions.Measured(walls.Down);

        if (!SurveyDimensions.AnyMeasured(left, right, up, down))
        {
            return;
        }

        readings.Add(new SurveyLrud
        {
            SurveyModelId = surveyModelId,
            StationName = stationName,
            Shot = leg,
            Section = shape,
            LeftM = left,
            RightM = right,
            UpM = up,
            DownM = down,
        });
    }

    /// <summary>
    /// Translates the cross-section shape one of the two formats names into this application's own
    /// word for it. Written out rather than cast, for the same reason the flags are: these numbers
    /// are stored, and a reader that renumbers its own vocabulary must not be able to change what a
    /// row already in the database means.
    ///
    /// <para>
    /// The answer is null where the file said nothing, which covers both the format that has no such
    /// field and the format whose field was left at its own "unstated" value. Those are the same
    /// fact — the file did not say — and they are stored the same way.
    /// </para>
    /// </summary>
    private static SurveySectionShape? MapSection(CaveShotSection section) => section switch
    {
        CaveShotSection.Oval => SurveySectionShape.Oval,
        CaveShotSection.Square => SurveySectionShape.Square,
        CaveShotSection.Diamond => SurveySectionShape.Diamond,
        CaveShotSection.Tunnel => SurveySectionShape.Tunnel,
        _ => null,
    };

    /// <summary>
    /// The middle of everything the file draws, in the file's own coordinates. Shot endpoints are
    /// included and not only stations, because one of the two formats fires splays at points it
    /// gives no station for, and they are part of the extent all the same.
    /// </summary>
    private static (double X, double Y) FootprintCentre(CaveModel model)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        void Include(CaveVector3 point)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        foreach (var station in model.Stations)
        {
            Include(station.Position);
        }

        foreach (var shot in model.Shots)
        {
            Include(shot.FromPosition);
            Include(shot.ToPosition);
        }

        if (minX > maxX)
        {
            // A projected file is placed from its own coordinates, and this one has none. Saying so
            // is better than placing an empty cave at whatever the grid's zero happens to be.
            throw new SurveySourceException(
                "This file carries no survey stations or legs, so there is nothing to place.");
        }

        return ((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>
    /// The full survey path of every survey in the file, by id. One of the two formats carries an
    /// explicit survey tree and names its stations only within their own survey; the other has no
    /// tree at all and writes the whole path into each label already.
    /// </summary>
    private static Dictionary<uint, string> SurveyPaths(CaveModel model)
    {
        var byId = new Dictionary<uint, CaveSurvey>(model.Surveys.Count);
        foreach (var survey in model.Surveys)
        {
            byId[survey.Id] = survey;
        }

        var paths = new Dictionary<uint, string>(model.Surveys.Count);

        string Resolve(uint id, HashSet<uint> walked)
        {
            if (paths.TryGetValue(id, out var known))
            {
                return known;
            }

            // The root survey is its own parent, and a malformed file could name a longer cycle;
            // either way the walk stops rather than running out of stack.
            if (!byId.TryGetValue(id, out var survey) || !walked.Add(id))
            {
                return string.Empty;
            }

            var parent = survey.ParentId == id ? string.Empty : Resolve(survey.ParentId, walked);
            var path = survey.Name.Length == 0
                ? parent
                : parent.Length == 0 ? survey.Name : parent + model.SeparatorChar + survey.Name;

            paths[id] = path;
            return path;
        }

        foreach (var survey in model.Surveys)
        {
            Resolve(survey.Id, []);
        }

        return paths;
    }

    private static string? SurveyNameOf(uint? surveyId, IReadOnlyDictionary<uint, string> paths) =>
        surveyId is { } id && paths.TryGetValue(id, out var path) && path.Length > 0 ? path : null;

    /// <summary>
    /// What identifies a station: the name the file gives it, qualified by the survey it belongs to
    /// where the format names stations only within their own survey. Two surveys in one file
    /// routinely both have a station called "1", and storing them under one name would store two
    /// stations as one.
    /// </summary>
    private static string StationName(CaveStation station, string? surveyName, char separator)
    {
        // An anonymous station has no name of its own anywhere in the file, so the number the file
        // wrote it at is the only handle there is. That number is assigned by file order in one of
        // the formats and is reassigned by every re-export, which is exactly why it is not used for
        // stations that do have names — an anonymous station simply cannot be followed across two
        // exports, and pretending otherwise would be worse than saying so.
        var name = station.Name.Length > 0
            ? station.Name
            : "#" + station.Id.ToString(CultureInfo.InvariantCulture);

        return surveyName is null ? name : surveyName + separator + name;
    }

    /// <summary>
    /// Translates the reader's flag vocabulary into this application's own, one flag at a time.
    ///
    /// <para>
    /// Written out rather than cast, even though the two numberings happen to agree today. These
    /// are stored bit sets: a row means whatever this application's enum meant on the day it was
    /// written, and a reader that renumbers its own vocabulary in some future version must not be
    /// able to reclassify rows already in the database. A cast would let it.
    /// </para>
    /// </summary>
    private static SurveyStationFlags MapStationFlags(CaveStationFlags flags)
    {
        var mapped = SurveyStationFlags.None;
        if ((flags & CaveStationFlags.Surface) != 0)
        {
            mapped |= SurveyStationFlags.Surface;
        }

        if ((flags & CaveStationFlags.Underground) != 0)
        {
            mapped |= SurveyStationFlags.Underground;
        }

        if ((flags & CaveStationFlags.Entrance) != 0)
        {
            mapped |= SurveyStationFlags.Entrance;
        }

        if ((flags & CaveStationFlags.Exported) != 0)
        {
            mapped |= SurveyStationFlags.Exported;
        }

        if ((flags & CaveStationFlags.Fixed) != 0)
        {
            mapped |= SurveyStationFlags.Fixed;
        }

        if ((flags & CaveStationFlags.Continuation) != 0)
        {
            mapped |= SurveyStationFlags.Continuation;
        }

        if ((flags & CaveStationFlags.HasWalls) != 0)
        {
            mapped |= SurveyStationFlags.HasWalls;
        }

        if ((flags & CaveStationFlags.Anonymous) != 0)
        {
            mapped |= SurveyStationFlags.Anonymous;
        }

        if ((flags & CaveStationFlags.Wall) != 0)
        {
            mapped |= SurveyStationFlags.Wall;
        }

        return mapped;
    }

    /// <summary>
    /// Translates the reader's leg flags into this application's own. Same reasoning as the station
    /// flags, and more load-bearing: splay and duplicate sit in adjacent bits and mean opposite
    /// things about whether the leg is passage at all.
    /// </summary>
    private static SurveyShotFlags MapShotFlags(CaveShotFlags flags)
    {
        var mapped = SurveyShotFlags.None;
        if ((flags & CaveShotFlags.Surface) != 0)
        {
            mapped |= SurveyShotFlags.Surface;
        }

        if ((flags & CaveShotFlags.Duplicate) != 0)
        {
            mapped |= SurveyShotFlags.Duplicate;
        }

        if ((flags & CaveShotFlags.Splay) != 0)
        {
            mapped |= SurveyShotFlags.Splay;
        }

        if ((flags & CaveShotFlags.NotVisible) != 0)
        {
            mapped |= SurveyShotFlags.NotVisible;
        }

        if ((flags & CaveShotFlags.NotLrud) != 0)
        {
            mapped |= SurveyShotFlags.NotLrud;
        }

        return mapped;
    }
}
