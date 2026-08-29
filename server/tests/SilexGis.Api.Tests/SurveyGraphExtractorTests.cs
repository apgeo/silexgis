// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Surveys;
using Therion.Blender;
using Therion.Blender.Geometry;
using Therion.Blender.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// Reading a compiled survey into station and shot rows: what is stored, and what is admitted to
/// have been lost. No database and no request — the extraction is arithmetic over a parsed file,
/// and these tests are the arithmetic.
///
/// <para>
/// Most fixtures here are written as real files by the format's own writer and read back by the
/// format's own reader, rather than being handed to the extractor as objects. That round trip is
/// the point: the flags this whole area exists to carry are file bits, and a fixture that never
/// becomes bytes cannot show that the bits survive. The one committed fixture is a real export,
/// because there is no writer for the second format.
/// </para>
/// </summary>
public class SurveyGraphExtractorTests
{
    /// <summary>Where the hand-built fixtures say their zero point is, and how high it sits.</summary>
    private static readonly SurveySourceDeclaration Local = new(
        SourceEpsg: null, OriginLongitude: 25.0, OriginLatitude: 45.5, OriginHeightM: 700);

    private static readonly Guid ModelId = Guid.CreateVersion7();

    private static SurveyGraphExtractor Extractor => new(new ProjCoordinateProjector());

    [Fact]
    public void Every_station_and_leg_the_file_carries_becomes_a_row()
    {
        var extraction = Extractor.Extract(WrittenAndReadBack(DisagreeingCave()), ModelId, Local);

        extraction.Stations.Count.ShouldBe(8);
        extraction.Shots.Count.ShouldBe(7);

        // Splays are stored, not filtered. A statistic that wants only the traverse asks the flag;
        // one that wants the passage shape needs the wall shots to still be there to ask.
        extraction.Shots.Count(s => s.IsSplay).ShouldBe(1);
    }

    [Fact]
    public void A_station_is_named_by_its_label_and_the_survey_it_belongs_to()
    {
        var extraction = Extractor.Extract(WrittenAndReadBack(DisagreeingCave()), ModelId, Local);

        // This format names a station only within its own survey, and two surveys in one file
        // routinely both have a station called "1". The stored name carries the survey with it.
        extraction.Stations.Select(s => s.Name).ShouldBe(
            ["main.a", "main.b", "main.c", "main.d", "main.e", "main.x", "main.y", "main.z"],
            ignoreOrder: true);

        extraction.Stations.ShouldAllBe(s => s.SurveyName == "main");

        // The number the file wrote is kept as data and is not what anything keys on.
        extraction.Stations.ShouldAllBe(s => s.FileStationId != null);
    }

    [Fact]
    public void The_splay_stored_is_the_one_the_file_flagged_and_not_the_one_the_shape_suggests()
    {
        // This is the test the whole extraction exists for. Until a survey file's own flags could
        // be read, a wall shot could only be guessed at from the shape of the network: a shot to a
        // point nothing else touches, unless it is the only such shot at its station, which is how
        // a genuine dead-end passage tip looks. This cave is built so the two answers disagree in
        // both directions — a lone splay hangs off a station in the middle of the traverse, where
        // the guess reads it as a passage tip, and two real dead-end passages hang off one station
        // together, where the guess reads them as a fan of wall shots.
        var extraction = Extractor.Extract(WrittenAndReadBack(DisagreeingCave()), ModelId, Local);

        var splays = extraction.Shots.Where(s => s.IsSplay).ToList();
        splays.Count.ShouldBe(1);
        splays[0].FromStationName.ShouldBe("main.c");
        splays[0].ToStationName.ShouldBe("main.x");

        var heuristic = CenterlineSkeleton.Build(
            new MultiLineString([.. extraction.Shots.Select(s => s.Geom)]) { SRID = 4326 });
        var reached = heuristic.Coordinates.Select(c => (c.X, c.Y)).ToHashSet();

        // The guess keeps the leg the file calls a wall shot...
        reached.ShouldContain(PlanPositionOf(extraction, "main.x"));

        // ...and throws away two legs the file calls ordinary passage. Six of the seven legs are
        // real by the file's answer; the guess keeps four, and they are not the same four.
        reached.ShouldNotContain(PlanPositionOf(extraction, "main.y"));
        reached.ShouldNotContain(PlanPositionOf(extraction, "main.z"));
    }

    [Fact]
    public void A_clean_file_admits_to_losing_nothing()
    {
        var extraction = Extractor.Extract(WrittenAndReadBack(DisagreeingCave()), ModelId, Local);

        extraction.DroppedShotCount.ShouldBe(0);
        extraction.MergedStationCount.ShouldBe(0);

        // Every leg of the traverse found both its ends; the wall shot found both of its own too,
        // because this format writes wall shots between stations it names.
        extraction.Shots.ShouldAllBe(s => s.FromStationName != null && s.ToStationName != null);
    }

    [Fact]
    public void A_leg_that_stands_at_no_station_is_stored_and_counted_as_lost()
    {
        var model = DisagreeingCave();
        var broken = new CaveModel
        {
            SourceFormat = model.SourceFormat,
            Surveys = model.Surveys,
            Stations = model.Stations,

            // A leg pointing at a station the file never wrote. The reader leaves such an endpoint
            // at the file's own zero, which stands at no station here, so the leg joins the network
            // nowhere — silently, which is exactly why it is counted.
            Shots = [.. model.Shots, Leg(1, 99, raw: 0)],
        };

        var extraction = Extractor.Extract(WrittenAndReadBack(broken), ModelId, Local);

        extraction.Shots.Count.ShouldBe(8);
        extraction.DroppedShotCount.ShouldBe(1);

        // Stored all the same. Throwing the leg away would lose the flags the row exists to carry
        // and would make the loss unrecoverable as well as invisible.
        extraction.Shots.Count(s => s.ToStationName is null).ShouldBe(1);
    }

    [Fact]
    public void Two_stations_at_one_position_become_one_node_and_the_merge_is_counted()
    {
        var model = DisagreeingCave();
        var shared = model.Stations[1].Position;
        var merged = new CaveModel
        {
            SourceFormat = model.SourceFormat,
            Surveys = model.Surveys,
            Stations = [.. model.Stations, Station(99, "b_again", shared, raw: 0)],
            Shots = model.Shots,
        };

        var extraction = Extractor.Extract(WrittenAndReadBack(merged), ModelId, Local);

        // Both rows are kept — they are distinct stations of the survey and the file named both.
        extraction.Stations.Count.ShouldBe(9);

        // But the network has one node there, and every leg that pointed at the position now points
        // at whichever station reached it first. That is what the count records.
        extraction.MergedStationCount.ShouldBe(1);
        extraction.Shots.ShouldAllBe(s => s.FromStationName != "main.b_again");
    }

    [Fact]
    public void What_is_counted_as_lost_is_what_the_network_reconstruction_loses()
    {
        // The counters are only worth anything if they measure the same losses the network build
        // suffers. Both match endpoints to stations by exact coordinate equality with the first
        // station at a position winning; this pins the two together, so a change to either rule
        // that leaves the other behind fails here rather than in a topology number a year later.
        var model = DisagreeingCave();
        var strained = new CaveModel
        {
            SourceFormat = model.SourceFormat,
            Surveys = model.Surveys,
            Stations = [.. model.Stations, Station(99, "b_again", model.Stations[1].Position, raw: 0)],
            Shots = [.. model.Shots, Leg(1, 98, raw: 0), Leg(2, 99, raw: 0)],
        };

        var parsed = WrittenAndReadBack(strained);
        var extraction = Extractor.Extract(parsed, ModelId, Local);

        const CaveShotFlags notNetwork =
            CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate;
        var network = parsed.Shots.Count(s => (s.Flags & notNetwork) == 0);

        extraction.DroppedShotCount.ShouldBe(network - CenterlineGraph.Build(parsed).Edges.Count);
    }

    [Fact]
    public void A_local_file_is_placed_about_the_position_given_for_its_zero_point()
    {
        var extraction = Extractor.Extract(WrittenAndReadBack(DisagreeingCave()), ModelId, Local);

        var stations = extraction.Stations.ToDictionary(s => s.Name);

        // The cave is fifty metres across, so every station sits well inside a thousandth of a
        // degree of the declared zero point. A file read as something other than metres, or placed
        // by something other than the declaration, lands nowhere near this.
        extraction.Stations.ShouldAllBe(s =>
            Math.Abs(s.Position.X - 25.0) < 0.001 && Math.Abs(s.Position.Y - 45.5) < 0.001);

        // Altitude is the file's Z above the plane the uploader said its zero sits on. The file
        // measures depth from a station fixed at zero height, so this is where the number comes in.
        stations["main.e"].Position.Z.ShouldBe(696, tolerance: 0.001);
        stations["main.a"].Position.Z.ShouldBe(700, tolerance: 0.001);

        // Forty metres east in the file is forty metres east in the world, to within a metre. Not a
        // second copy of the placement arithmetic: a crude metres-per-degree is used here only to
        // size the tolerance, which is what makes a units mistake fail and rounding not.
        var spanDegrees = stations["main.e"].Position.X - stations["main.a"].Position.X;
        (spanDegrees * 111_320 * Math.Cos(45.5 * Math.PI / 180)).ShouldBe(40, tolerance: 1);

        // North is north: the two dead-end tips straddle the station they hang off.
        stations["main.y"].Position.Y.ShouldBeGreaterThan(stations["main.b"].Position.Y);
        stations["main.z"].Position.Y.ShouldBeLessThan(stations["main.b"].Position.Y);
    }

    [Fact]
    public void A_local_file_with_no_position_is_refused_rather_than_placed_somewhere()
    {
        var thrown = Should.Throw<SurveySourceException>(() => Extractor.Extract(
            WrittenAndReadBack(DisagreeingCave()),
            ModelId,
            new SurveySourceDeclaration(SourceEpsg: null, null, null, OriginHeightM: 700)));

        thrown.Message.ShouldContain("zero point");
    }

    [Fact]
    public void A_coordinate_system_this_installation_cannot_resolve_is_refused()
    {
        Should.Throw<SurveySourceException>(() => Extractor.Extract(
            WrittenAndReadBack(DisagreeingCave()),
            ModelId,
            new SurveySourceDeclaration(SourceEpsg: 999999, null, null, OriginHeightM: 700)));
    }

    [Fact]
    public void A_real_export_is_read_whole_and_placed_from_its_own_grid()
    {
        var parsed = CaveModelReader.Read(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P8_Master.3d")));

        var extraction = Extractor.Extract(
            parsed, ModelId, new SurveySourceDeclaration(SourceEpsg: 27700, null, null, OriginHeightM: 0));

        extraction.Stations.Count.ShouldBe(393);
        extraction.Shots.Count.ShouldBe(389);

        // This format writes the whole survey path into every label, so the names are already
        // unique and nothing had to be qualified. Uniqueness is not decoration — it is the identity
        // the stored rows are keyed on.
        extraction.Stations.Select(s => s.Name).Distinct().Count().ShouldBe(393);

        // Read as the British national grid, which is what the file itself declares, the cave lands
        // in Great Britain. A grid confusion puts it in the sea, silently.
        extraction.Stations.ShouldAllBe(s =>
            s.Position.X > -8 && s.Position.X < 2 && s.Position.Y > 49 && s.Position.Y < 61);

        // The turn from grid north to true north is applied once, here, and recorded as already
        // applied. Anything drawing these rows that applied it again would move the far end of a
        // long cave by tens of metres with nothing on screen to explain it.
        extraction.AppliedRotationDeg.ShouldNotBe(0);

        const CaveShotFlags notNetwork =
            CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate;
        var network = parsed.Shots.Count(s => (s.Flags & notNetwork) == 0);
        extraction.DroppedShotCount.ShouldBe(network - CenterlineGraph.Build(parsed).Edges.Count);
    }

    private static (double X, double Y) PlanPositionOf(SurveyGraphExtraction extraction, string name)
    {
        var station = extraction.Stations.Single(s => s.Name == name);
        return (station.Position.X, station.Position.Y);
    }

    /// <summary>
    /// Writes the model as real bytes of its format and reads them back, so that what the extractor
    /// sees came out of a file rather than out of this test.
    /// </summary>
    private static CaveModel WrittenAndReadBack(CaveModel model) =>
        CaveModelReader.Read(LoxWriter.Write(model));

    /// <summary>
    /// A small cave laid out so that the file's own answer about which legs are wall shots and the
    /// answer a reader could infer from the network's shape are different answers.
    ///
    /// <para>
    /// The traverse runs a–b–c–d–e. One wall shot, c–x, hangs off a station in the middle of it,
    /// where a shape-based reading sees the only loose shot at its station and calls it the tip of
    /// a dead-end passage. Two real dead-end passages, b–y and b–z, hang off one station together,
    /// where a shape-based reading sees a fan and calls both wall shots. Neither reading is
    /// unreasonable; only one of them is what the surveyor wrote down.
    /// </para>
    /// </summary>
    private static CaveModel DisagreeingCave() => new()
    {
        SourceFormat = CaveSourceFormat.Lox,
        Surveys = [new CaveSurvey(Id: 1, ParentId: 1, Name: "main", Title: null)],
        Stations =
        [
            Station(1, "a", new CaveVector3(10, 0, 0), raw: LoxEntrance),
            Station(2, "b", new CaveVector3(20, 0, -1), raw: 0),
            Station(3, "c", new CaveVector3(30, 0, -2), raw: 0),
            Station(4, "d", new CaveVector3(40, 0, -3), raw: 0),
            Station(5, "e", new CaveVector3(50, 0, -4), raw: 0),
            Station(6, "x", new CaveVector3(30, 4, -2), raw: 0),
            Station(7, "y", new CaveVector3(20, 4, -1), raw: 0),
            Station(8, "z", new CaveVector3(20, -4, -1), raw: 0),
        ],
        Shots =
        [
            Leg(1, 2, raw: 0),
            Leg(2, 3, raw: 0),
            Leg(3, 4, raw: 0),
            Leg(4, 5, raw: 0),
            Leg(3, 6, raw: LoxSplay),
            Leg(2, 7, raw: 0),
            Leg(2, 8, raw: 0),
        ],
    };

    // The bit values this format uses, which are not the bit values the other one uses and are not
    // the bit values stored: a splay is bit 16 here and bit 4 in the other format. Written as file
    // bits because that is what a file carries and what the reader has to translate.
    private const uint LoxEntrance = 2;
    private const uint LoxSplay = 16;

    private static CaveStation Station(uint id, string name, CaveVector3 position, uint raw) => new()
    {
        Id = id,
        SurveyId = 1,
        Name = name,
        Position = position,
        Flags = MapLoxStationFlags(raw),
        RawFlags = raw,
    };

    private static CaveShot Leg(uint from, uint to, uint raw) => new()
    {
        FromStationId = from,
        ToStationId = to,
        SurveyId = 1,
        Flags = MapLoxShotFlags(raw),
        RawFlags = raw,
    };

    // Only the raw bits survive the writer; the reader is what produces the typed flags, and these
    // keep the model handed to the writer self-consistent for the tests that read it directly.
    private static CaveStationFlags MapLoxStationFlags(uint raw) =>
        (raw & 2) != 0 ? CaveStationFlags.Entrance : CaveStationFlags.None;

    private static CaveShotFlags MapLoxShotFlags(uint raw) =>
        (raw & 16) != 0 ? CaveShotFlags.Splay : CaveShotFlags.None;
}
