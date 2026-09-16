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
/// reading and is not here, whether the file said so by writing its "not measured" number in every
/// field or by flagging the whole leg as carrying no wall measurement.
/// </param>
/// <param name="DroppedShotCount">
/// Legs of the traverse whose endpoints matched no station, or matched the same one twice, and so
/// join the station network nowhere. Splay, surface and duplicate legs are not counted: they are
/// stored, but they were never part of the network to begin with.
/// </param>
/// <param name="MergedStationCount">
/// Stations that stood at a position an earlier station already held, and so became one node.
/// </param>
/// <param name="AnonymousStationCount">
/// Station records the file wrote as its "there is no station here" placeholder — the far end of a
/// shot at the passage wall — and which are therefore not stations and have no row. Counted rather
/// than passed over in silence: on a whole-system export these outnumber the real stations ten to
/// one, and a station count that shrank by that much with nothing saying why would look like the
/// reading having lost most of the cave.
/// </param>
/// <param name="RootSurveyName">
/// The name of the file's root survey, where it has a survey tree with exactly one named root;
/// null for a format that carries no tree, for an unnamed root, and for the malformed case of
/// several surveys each claiming to be their own parent — in every one of those the name below is
/// no longer the single component that would tell a stored station name from the way the survey
/// viewer addresses the same station, so answering with one of them would be a guess.
/// </param>
public sealed record SurveyGraphExtraction(
    IReadOnlyList<SurveyStation> Stations,
    IReadOnlyList<SurveyShot> Shots,
    IReadOnlyList<SurveyLrud> Lrud,
    int DroppedShotCount,
    int MergedStationCount,
    int AnonymousStationCount,
    string? RootSurveyName,
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
    /// system this installation cannot resolve, or a projected file with no coordinates at all. Or
    /// it names two of its own stations identically inside one survey, so the two cannot be told
    /// apart — see <see cref="RefuseCollidingNames"/>.
    /// </exception>
    public SurveyGraphExtraction Extract(
        CaveModel source, Guid surveyModelId, SurveySourceDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(declaration);

        // Read here and not left to the caller, so that every path into this — the job, a test,
        // anything later — sees the same legs. A file whose exporter wrote no wall-shot flags is
        // otherwise read as a cave made entirely of passage, and the rows below would say so.
        var model = SurveyWallShots.Flagged(source);
        var anonymousPointIds = SurveyWallShots.AnonymousPointIds(model);

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
        var anonymousStations = 0;

        foreach (var station in model.Stations)
        {
            // Not a station: the survey language's own token for the far end of a shot at the wall,
            // kept by the compiler as a record so the leg has something to reference. It is a point
            // on the rock, not a place anybody named, and the one thing this table's rows have to
            // be is addressable — by a person typing a name, by a tracking position, by a resource
            // link, and by the viewer, whose reader of this same format gives these points no
            // addressable name at all. Every one of them in a survey carries the identical spelling,
            // so admitting them would also be storing one name thousands of times over.
            //
            // Left out of the position index as well, and deliberately. That index answers "which
            // station stands here" for the legs, first record at a position winning; a placeholder
            // that happens to coincide with a real station used to win that race and hand the legs
            // a name nobody could resolve.
            if (anonymousPointIds.Contains(station.Id))
            {
                anonymousStations++;
                continue;
            }

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

        RefuseCollidingNames(stations);

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
            //
            // Unless the file has already said those numbers mean nothing. That format gives every
            // leg its eight wall distances whether or not any were taken, and states "this leg
            // carries no usable wall measurement" with a flag of its own rather than by leaving the
            // fields empty — so a flagged leg holding four zeros is not a passage of zero width at
            // a station standing against the wall, it is a leg nobody measured. The negative
            // sentinel does not catch it, because zero is a real measurement everywhere else.
            //
            // Splay, surface and duplicate legs are deliberately not excluded here. What those
            // flags say is that the leg is not ordinary passage to be counted, not that the
            // distances recorded at its ends are meaningless: those ends are stations like any
            // other, and a wall distance measured at one is a measurement. Only the flag that
            // speaks about the wall distances themselves decides whether they are kept.
            if ((flags & SurveyShotFlags.NotLrud) == 0)
            {
                var section = MapSection(shot.SectionType);
                AddReading(readings, surveyModelId, StationOf(shot.FromStationId) ?? from, shot.FromLrud, section, leg);
                AddReading(readings, surveyModelId, StationOf(shot.ToStationId) ?? to, shot.ToLrud, section, leg);
            }

            // A splay is a shot at the wall, and its far end is routinely a point the file names no
            // station for; a surface or duplicate leg is not passage to be counted either. None of
            // those is a loss, so counting them as one would bury the losses that matter.
            //
            // This is why the wall-shot flag is settled before any of this runs rather than taken
            // from the file as it stands. On a survey whose exporter flags no wall shots, the wall
            // shots' far ends are exactly the points that are no longer station rows, so every one
            // of them would land here as a leg that joins nothing: measured on one such file, this
            // count goes from 29 real losses to 15,977 — a number that says the reading lost two
            // thirds of the cave, about a reading that lost nothing.
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
            anonymousStations,
            RootSurveyName(model),
            placement.Anchor.Longitude,
            placement.Anchor.Latitude,
            placement.OriginHeightM,
            placement.AppliedRotationDeg);
    }

    /// <summary>
    /// Refuses a file that names two of its own stations identically, in words its uploader can act
    /// on, before a single row of it is offered to the database.
    ///
    /// <para>
    /// <b>Why it is said here and not left to the database.</b> A station's name is its identity
    /// within a model, and the table says so with a unique index, so a file like this was already
    /// refused — but by a constraint violation arriving out of the save that writes tens of
    /// thousands of rows at once, long after the reading looked like it had succeeded. What the
    /// uploader was shown for it was "the survey could not be read", which is true and useless: it
    /// is the sentence kept for faults that are ours, and there is nothing in it to act on. Asked
    /// here, the question is answered by arithmetic over the rows in hand, and the answer names the
    /// thing that is wrong with their file.
    /// </para>
    ///
    /// <para>
    /// <b>Counts, never the names themselves.</b> The reason travels onto the survey model and is
    /// shown wherever that record is, and a station name on a location-protected cave is part of
    /// what this application exists to keep. How many is enough to act on; which ones is the
    /// uploader's own file to look in.
    /// </para>
    ///
    /// <para>
    /// This is genuinely rare, and it is not what made whole files unreadable: of eighteen compiled
    /// surveys measured, carrying some fifty thousand named stations between them, not one had a
    /// real collision. What they had was the wall-shot placeholder admitted as a station, which is
    /// no longer a row at all.
    /// </para>
    /// </summary>
    /// <exception cref="SurveySourceException">Two or more stations share one qualified name.</exception>
    private static void RefuseCollidingNames(IReadOnlyList<SurveyStation> stations)
    {
        var rowsPerName = new Dictionary<string, int>(stations.Count, StringComparer.Ordinal);
        foreach (var station in stations)
        {
            rowsPerName[station.Name] = rowsPerName.GetValueOrDefault(station.Name) + 1;
        }

        var repeatedNames = 0;
        var affectedRows = 0;
        foreach (var (_, rows) in rowsPerName)
        {
            if (rows > 1)
            {
                repeatedNames++;
                affectedRows += rows;
            }
        }

        if (repeatedNames == 0)
        {
            return;
        }

        var names = repeatedNames == 1 ? "one station name" : $"{repeatedNames} station names";
        throw new SurveySourceException(
            $"This survey uses {names} for more than one station ({affectedRows} stations in all), "
                + "so those stations cannot be told apart and the survey cannot be stored. Station "
                + "names have to be unique within the survey they belong to: re-export with the "
                + "duplicates renamed, or check whether one survey has been included twice.");
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

    /// <summary>
    /// The name of the file's root survey — the one survey that is its own parent — or null where
    /// the file has no survey tree, where that root has no name, or where more than one survey
    /// claims to be its own parent.
    ///
    /// <para>
    /// Surfaced rather than kept private because it is the single component by which a stored
    /// station name differs from the way the survey viewer addresses the same station: the path
    /// built above starts at the root, and the viewer's reader of this format leaves the root out
    /// of its tree. Nothing else in the file says which of a station name's components that is, and
    /// the station names alone cannot say — a root called <c>a</c> and an unnamed root with one
    /// sub-survey called <c>a</c> produce the same names and want different answers.
    /// </para>
    /// </summary>
    private static string? RootSurveyName(CaveModel model)
    {
        string? root = null;
        foreach (var survey in model.Surveys)
        {
            if (survey.ParentId != survey.Id)
            {
                continue;
            }

            if (root is not null)
            {
                return null;
            }

            root = survey.Name;
        }

        return string.IsNullOrEmpty(root) ? null : root;
    }

    private static string? SurveyNameOf(uint? surveyId, IReadOnlyDictionary<uint, string> paths) =>
        surveyId is { } id && paths.TryGetValue(id, out var path) && path.Length > 0 ? path : null;

    /// <summary>
    /// What identifies a station: the name the file gives it, qualified by the survey it belongs to
    /// where the format names stations only within their own survey. Two surveys in one file
    /// routinely both have a station called "1", and storing them under one name would store two
    /// stations as one.
    ///
    /// <para>
    /// Nothing reaching here is one of the compiled Therion format's wall-shot placeholders: those
    /// are not stations and were left out before this. The fallback below is a different case and
    /// belongs to the other format — see the comment on it, and do not be tempted to widen it into
    /// a way of telling the placeholders apart, which is the one thing it must not become.
    /// </para>
    /// </summary>
    private static string StationName(CaveStation station, string? surveyName, char separator)
    {
        // A station with no name at all, which the other of the two formats does emit. The number
        // the file wrote it at is then the only handle there is. That number is assigned by file
        // order and is reassigned by every re-export, which is exactly why it is not used for
        // stations that do have names — such a station simply cannot be followed across two
        // exports, and pretending otherwise would be worse than saying so.
        //
        // It is also why this is not the answer for the compiled Therion format's wall-shot
        // placeholders, which do have a name — one character of it — and would fall straight
        // through here if the test were widened to catch them. Numbering them would remove the
        // collision and store tens of thousands of rows under names nobody can type and the viewer
        // can never resolve, which is a worse answer wearing the look of a fix.
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
