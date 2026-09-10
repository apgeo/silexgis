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
    public void Walls_measured_along_a_leg_are_stored_at_the_station_they_were_measured_from()
    {
        var extraction = Extractor.Extract(WrittenAndReadBack(MeasuredCave()), ModelId, Local);

        var reading = extraction.Lrud.ShouldHaveSingleItem();
        reading.StationName.ShouldBe("main.a");

        // The leg travels with the reading in this format, because this format is the one that has
        // it to give: the walls were measured facing along a particular leg, and which leg that was
        // is not recoverable from the station alone.
        reading.Shot.ShouldBeSameAs(extraction.Shots.Single());
        reading.Section.ShouldBe(SurveySectionShape.Oval);

        reading.LeftM.ShouldBe(1.5);
        reading.RightM.ShouldBe(2.0);

        // A dimension the surveyor did not measure is nothing, and a dimension measured as zero is
        // zero. Both formats say "not measured" with a negative number, so storing that number
        // would make a passage with a wall a metre behind the station — and a substituted default
        // would invent a passage nobody measured. These are three different facts and only two of
        // them are numbers.
        reading.UpM.ShouldBeNull();
        reading.DownM.ShouldBe(0);

        // Nothing at all was measured at the far end, and a row stating nothing is not a reading.
        // The formats emit all four distances whenever they emit a leg, filled in or not, so
        // keeping the empty ones would put a row on nearly every station in the cave.
        extraction.Lrud.ShouldNotContain(r => r.StationName == "main.b");
    }

    [Fact]
    public void A_leg_the_file_says_carries_no_wall_measurement_produces_no_reading()
    {
        // The format that hangs walls off legs writes all eight distances for every leg, filled or
        // not, and has a flag for saying that a leg's are not wall measurements at all. A leg
        // carrying that flag with zeros in those fields is the case that matters: zero is a real
        // measurement everywhere else — a station standing against the wall — so the sign rule that
        // catches the "not measured" sentinel cannot catch this one, and the readings would be
        // stored as a passage of no width and no height at a real station.
        var extraction = Extractor.Extract(WrittenAndReadBack(UnmeasuredCave()), ModelId, Local);

        var reading = extraction.Lrud.ShouldHaveSingleItem();
        reading.StationName.ShouldBe("main.a");
        reading.LeftM.ShouldBe(1.5);

        // Nothing from either end of the flagged leg — not from the station it shares with the leg
        // that was measured either.
        extraction.Lrud.ShouldNotContain(r => r.StationName == "main.c");
    }

    /// <summary>
    /// The same cave, described by both compiled formats, storing the same wall measurements.
    ///
    /// <para>
    /// This is the test the normalisation exists to pass. The two formats do not merely spell the
    /// same idea differently — one hangs measurements off the legs they were taken along and the
    /// other emits ordered runs of cross-sections keyed by station name, with no leg anywhere in it.
    /// Either shape can be written down without the other ever agreeing with it, so the only thing
    /// that shows they were normalised rather than merely stored is reading one cave through both
    /// paths and getting one table.
    /// </para>
    ///
    /// <para>
    /// The readings come from the committed export, because there is no writer for that format and a
    /// cave invented here would only prove that this test can invent the same numbers twice. They
    /// are then restated as the other format states them — as a chained traverse, which is how that
    /// format really carries a run of cross-sections — and written as real bytes by its own writer,
    /// so both sides of the comparison came out of a file.
    /// </para>
    ///
    /// <para>
    /// The two tables are therefore not the same size, and that is the point of the comparison
    /// rather than a flaw in it. An interior station of a traverse is the far end of one leg and the
    /// near end of the next, so the leg-based format states its walls twice while the run-based
    /// format states them once. What has to agree is the set of facts — which station, which four
    /// distances — and the extra rows have to be restatements of a fact the other format already
    /// carries rather than readings of their own.
    /// </para>
    /// </summary>
    /// <summary>
    /// A file that is an extended elevation is refused at ingest rather than measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An extended elevation unrolls a cave onto one vertical plane so it can be printed side-on.
    /// Its stations are positions in that drawing: the horizontal distance between two of them is
    /// the distance along the unrolled path, and the bearing between them means nothing. Length,
    /// the direction rose, hypsometry, the passage network and closest approach would all be
    /// computed from it without complaint, and none of the answers would look wrong on a cave's
    /// page — they would be measurements of a shape the cave does not have.
    /// </para>
    /// <para>
    /// The variant is derived from the plan-view fixture rather than committed beside it, because
    /// the two must not drift into being two different caves: the same bytes, with the one bit the
    /// format uses to say so. In a v8 file that is bit 0x80 of the file-wide flags byte, which sits
    /// immediately after the fourth newline — the magic, version, title and datestamp lines. The
    /// assertions below pin that arithmetic, so a fixture regenerated in another format version
    /// fails here rather than silently testing a plan-view file against itself.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_extended_elevation_is_refused_rather_than_measured()
    {
        var planView = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P8_Master.3d"));

        // The plan-view fixture is accepted — without this the test would pass against a rule that
        // refused everything.
        SurveySourceRules.EnsureIsPlanView(CaveModelReader.Read(planView));

        var flagsOffset = FileWideFlagsOffset(planView);
        (planView[flagsOffset] & 0x80).ShouldBe(0, "the fixture is supposed to be a plan view");

        var extended = (byte[])planView.Clone();
        extended[flagsOffset] |= 0x80;

        var model = CaveModelReader.Read(extended);
        model.IsExtendedElevation.ShouldBeTrue("flipping the flag bit did not produce an extended elevation");

        // Everything else about the file is unchanged, so what is refused below is the kind of
        // file and not a corrupted one.
        model.Stations.Count.ShouldBe(CaveModelReader.Read(planView).Stations.Count);

        Should.Throw<SurveySourceException>(() => SurveySourceRules.EnsureIsPlanView(model))
            .Message.ShouldContain("extended elevation");
    }

    /// <summary>
    /// Where a v8 file keeps the flags byte carrying the extended-elevation bit: straight after
    /// the magic, version, title and datestamp lines.
    /// </summary>
    private static int FileWideFlagsOffset(byte[] file)
    {
        var offset = -1;
        for (var line = 0; line < 4; line++)
        {
            offset = Array.IndexOf(file, (byte)'\n', offset + 1);
            offset.ShouldBeGreaterThan(0, $"the header ended before line {line + 1}");
        }

        return offset + 1;
    }

    [Fact]
    public void The_same_cave_read_through_either_format_yields_the_same_wall_measurements()
    {
        var survex = CaveModelReader.Read(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P8_Master.3d")));

        var fromRuns = Extractor.Extract(
            survex, ModelId, new SurveySourceDeclaration(SourceEpsg: 27700, null, null, OriginHeightM: 0)).Lrud;

        var fromLegs = Extractor.Extract(
            WrittenAndReadBack(SameReadingsAsChainedLegs(survex)), ModelId, Local).Lrud;

        // A file with no cross-sections would pass every assertion below by having nothing to
        // disagree about, so what the export actually carries is stated rather than assumed: nine
        // runs of cross-sections, 155 stations between them with something measured at each.
        fromRuns.Count.ShouldBe(155);

        // The shapes really do differ, so the agreement below is between two different tables and
        // not between one table and itself. If this ever stops holding, the fixture has been flattened
        // into a one-reading-per-station shape no real file of that format has.
        fromLegs.Count.ShouldBeGreaterThan(fromRuns.Count);

        // The same facts, from both paths — this is what normalisation means here.
        Walls(fromLegs).Distinct().ShouldBe(Walls(fromRuns).Distinct(), ignoreOrder: true);

        // And every extra row the leg-based shape produced is one of those facts again, at the
        // station that already stated it, rather than a reading the other format never mentioned.
        var stated = Walls(fromRuns).ToHashSet();
        Walls(fromLegs).ShouldAllBe(w => stated.Contains(w));

        // And the one thing that legitimately differs, which is why the column is nullable: the
        // format that measures along a leg names it, and the format that does not, does not.
        fromLegs.ShouldAllBe(r => r.Shot != null);
        fromRuns.ShouldAllBe(r => r.Shot == null);

        // No section shape either — that format has no word for one, and inventing this
        // application's default would record a shape the surveyor never wrote down.
        fromRuns.ShouldAllBe(r => r.Section == null);
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

    // "This leg's wall distances are not wall measurements", in this format's own bits. The other
    // format has no way of saying it, which is why it is stated as a bit and not as a value.
    private const uint LoxNotLrud = 8;

    private static CaveStation Station(uint id, string name, CaveVector3 position, uint raw) => new()
    {
        Id = id,
        SurveyId = 1,
        Name = name,
        Position = position,
        Flags = MapLoxStationFlags(raw),
        RawFlags = raw,
    };

    /// <summary>
    /// What a reading says, with the leg it came along left out — that is the one thing the two
    /// formats are not expected to agree about.
    /// </summary>
    private static List<(string Station, double? Left, double? Right, double? Up, double? Down)> Walls(
        IEnumerable<SurveyLrud> readings) =>
        [.. readings.Select(r => (r.StationName, r.LeftM, r.RightM, r.UpM, r.DownM))];

    /// <summary>
    /// Every cross-section of <paramref name="survex"/>, restated the way the other format states
    /// one: as wall distances at the two ends of the legs of a traverse.
    ///
    /// <para>
    /// Each run of cross-sections becomes a chain of legs along the stations it names, so an
    /// interior station carries its walls twice — as the far end of the leg that arrives and the
    /// near end of the leg that leaves — which is what a real file of that format looks like and is
    /// exactly the structural difference the comparison has to survive. Giving every reading a leg
    /// of its own instead would engineer that difference away and leave both sides of the comparison
    /// coming out of one code path.
    /// </para>
    ///
    /// <para>
    /// A run naming a single station has no leg to hang anything on, so it gets one to a shared far
    /// end whose own walls were never measured — nothing rides along from that end.
    /// </para>
    /// </summary>
    private static CaveModel SameReadingsAsChainedLegs(CaveModel survex)
    {
        var idByName = new Dictionary<string, uint>();
        var stations = new List<CaveStation>
        {
            // Allocated first so a run of one station has somewhere to point; its own walls were
            // never measured, so it contributes no reading of its own.
            new() { Id = 1, Name = "far-end", Position = new CaveVector3(0, 1000, 0) },
        };

        const uint farEnd = 1;

        uint IdOf(string name)
        {
            if (idByName.TryGetValue(name, out var known))
            {
                return known;
            }

            var id = (uint)(stations.Count + 1);
            idByName[name] = id;
            stations.Add(new CaveStation
            {
                Id = id,
                Name = name,

                // Positions this format needs but this test does not: spread out so that no two
                // stations land on one point and get read as one node.
                Position = new CaveVector3(id * 10, 0, 0),
            });
            return id;
        }

        static CaveLrud WallsOf(CavePassageStation section) =>
            new(section.Left, section.Right, section.Up, section.Down);

        var shots = new List<CaveShot>();

        foreach (var passage in survex.Passages)
        {
            var run = passage.Stations;
            for (var i = 0; i + 1 < run.Count; i++)
            {
                shots.Add(new CaveShot
                {
                    FromStationId = IdOf(run[i].StationName),
                    ToStationId = IdOf(run[i + 1].StationName),
                    FromLrud = WallsOf(run[i]),
                    ToLrud = WallsOf(run[i + 1]),
                });
            }

            if (run.Count == 1)
            {
                shots.Add(new CaveShot
                {
                    FromStationId = IdOf(run[0].StationName),
                    ToStationId = farEnd,
                    FromLrud = WallsOf(run[0]),
                });
            }
        }

        return new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations = stations,
            Shots = shots,
        };
    }

    /// <summary>
    /// One leg with its walls measured at the near end and nothing measured at the far end. The near
    /// end has all three cases in it at once: two distances measured, one measured as zero because
    /// the station stands against the wall, and one the surveyor never took.
    /// </summary>
    private static CaveModel MeasuredCave() => new()
    {
        SourceFormat = CaveSourceFormat.Lox,
        Surveys = [new CaveSurvey(Id: 1, ParentId: 1, Name: "main", Title: null)],
        Stations =
        [
            Station(1, "a", new CaveVector3(0, 0, 0), raw: 0),
            Station(2, "b", new CaveVector3(10, 0, 0), raw: 0),
        ],
        Shots =
        [
            new CaveShot
            {
                FromStationId = 1,
                ToStationId = 2,
                SurveyId = 1,
                SectionType = CaveShotSection.Oval,

                // -1 is how this format writes "not measured"; the other format writes a different
                // negative number for the same statement, which is why the rule that reads it is
                // about the sign and not about the value.
                FromLrud = new CaveLrud(Left: 1.5, Right: 2.0, Up: -1, Down: 0),
                ToLrud = new CaveLrud(-1, -1, -1, -1),
            },
        ],
    };

    /// <summary>
    /// Two legs of one traverse: one whose walls were measured, and one the file flags as carrying
    /// no wall measurement while still filling its eight distance fields — with zeros, which is what
    /// makes the flag the only thing that can tell them apart from a real measurement.
    /// </summary>
    private static CaveModel UnmeasuredCave() => new()
    {
        SourceFormat = CaveSourceFormat.Lox,
        Surveys = [new CaveSurvey(Id: 1, ParentId: 1, Name: "main", Title: null)],
        Stations =
        [
            Station(1, "a", new CaveVector3(0, 0, 0), raw: 0),
            Station(2, "b", new CaveVector3(10, 0, 0), raw: 0),
            Station(3, "c", new CaveVector3(20, 0, 0), raw: 0),
        ],
        Shots =
        [
            new CaveShot
            {
                FromStationId = 1,
                ToStationId = 2,
                SurveyId = 1,
                FromLrud = new CaveLrud(Left: 1.5, Right: 2.0, Up: 3.0, Down: 0),
                ToLrud = new CaveLrud(-1, -1, -1, -1),
            },
            new CaveShot
            {
                FromStationId = 2,
                ToStationId = 3,
                SurveyId = 1,
                Flags = MapLoxShotFlags(LoxNotLrud),
                RawFlags = LoxNotLrud,
                FromLrud = new CaveLrud(0, 0, 0, 0),
                ToLrud = new CaveLrud(0, 0, 0, 0),
            },
        ],
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

    private static CaveShotFlags MapLoxShotFlags(uint raw)
    {
        var flags = CaveShotFlags.None;
        if ((raw & 8) != 0)
        {
            flags |= CaveShotFlags.NotLrud;
        }

        if ((raw & 16) != 0)
        {
            flags |= CaveShotFlags.Splay;
        }

        return flags;
    }
}
