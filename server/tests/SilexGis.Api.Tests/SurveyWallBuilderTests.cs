// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Surveys;
using Therion.Blender;
using Therion.Blender.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// The walls built out of a line plot's own contents: the arithmetic of one ring and one tube, and
/// which legs of a survey are given a tube at all.
///
/// <para>
/// No database and no HTTP. Every fixture is stated in the test that uses it, in whole metres, so
/// each expected vertex can be worked out on paper from the four wall distances beside it.
/// </para>
/// </summary>
public class SurveyWallBuilderTests
{
    /// <summary>
    /// A survey in plain metres about its own zero: nothing is turned and nothing is scaled, so a
    /// vertex of the mesh is where the fixture put it.
    /// </summary>
    private static readonly SurveyPlacement PlainMetres = SurveyPlacement.Resolve(
        new ProjCoordinateProjector(),
        new SurveySourceDeclaration(SourceEpsg: null, 25.2, 45.5, OriginHeightM: 1200),
        () => (0, 0));

    // ---- one tube -----------------------------------------------------------

    [Fact]
    public void A_ring_passes_through_the_four_measured_points()
    {
        // Heading due east, so the right wall is to the south and the left wall to the north.
        var walls = new WallDistances(Left: 1, Right: 2, Up: 3, Down: 0.5);
        var mesh = Tube([Section(0, 0, 0, walls), Section(10, 0, 0, walls)]);

        foreach (var east in new[] { 0.0, 10.0 })
        {
            ShouldHaveVertex(mesh, east, -2, 0);
            ShouldHaveVertex(mesh, east, 1, 0);
            ShouldHaveVertex(mesh, east, 0, 3);
            ShouldHaveVertex(mesh, east, 0, -0.5);

            // The station itself, which is what a closed end is fanned about.
            ShouldHaveVertex(mesh, east, 0, 0);
        }

        // Two rings of eight and the two stations; sixteen faces round the leg and eight at each end.
        mesh.VertexCount.ShouldBe(18);
        mesh.TriangleCount.ShouldBe(32);
        PassageTubes.TriangleCount(sections: 2, sides: 8).ShouldBe(32);
    }

    [Fact]
    public void A_tube_is_closed_and_faces_outwards()
    {
        var walls = new WallDistances(Left: 1, Right: 2, Up: 3, Down: 0.5);
        var mesh = Tube([Section(0, 0, 0, walls), Section(10, 0, 0, walls)]);

        // The volume enclosed by a closed surface whose faces all point outwards is positive, and a
        // face wound the wrong way subtracts. The cross-section lies between the diamond through
        // the four measured points (half of 3 m by 3.5 m) and the box around them, ten metres long.
        var volume = Volume(mesh);
        volume.ShouldBeGreaterThan(52.5);
        volume.ShouldBeLessThan(105);
    }

    [Fact]
    public void A_distance_nobody_measured_is_drawn_as_no_distance_at_all()
    {
        // The left wall was not measured. The ring stops at the station on that side: it does not
        // stand off it by a metre, or by the same as the right, or by anything else made up.
        var walls = new WallDistances(Left: null, Right: 2, Up: 1, Down: 1);
        var mesh = Tube([Section(0, 0, 0, walls), Section(10, 0, 0, walls)]);

        Vertices(mesh).Max(v => v.North).ShouldBe(0, tolerance: 1e-12);
        Vertices(mesh).Min(v => v.North).ShouldBe(-2, tolerance: 1e-12);
    }

    [Fact]
    public void A_ring_at_a_bend_faces_half_way_round_it()
    {
        var walls = new WallDistances(Left: 1, Right: 1, Up: 1, Down: 1);
        var mesh = Tube(
        [
            Section(0, 0, 0, walls), Section(10, 0, 0, walls), Section(10, 10, 0, walls),
        ]);

        // East, then north: at the corner the passage is heading north-east, so its right wall is a
        // metre to the south-east of the station and its left wall a metre to the north-west.
        var step = Math.Sqrt(0.5);
        ShouldHaveVertex(mesh, 10 + step, -step, 0);
        ShouldHaveVertex(mesh, 10 - step, step, 0);

        // The ring at the corner is shared by the two stretches it joins, rather than each
        // stretch ending in a ring of its own with a gap or an overlap between them.
        mesh.TriangleCount.ShouldBe((int)PassageTubes.TriangleCount(sections: 3, sides: 8));
        mesh.VertexCount.ShouldBe((3 * 8) + 2);
    }

    [Fact]
    public void Roof_and_floor_are_plumb_on_a_passage_that_slopes()
    {
        // Thirty degrees down. Up and down are measured with the tape hanging, so the roof is two
        // metres straight above the station — not two metres square to the slope.
        var walls = new WallDistances(Left: 1, Right: 1, Up: 2, Down: 1);
        var run = 10 * Math.Cos(Math.PI / 6);
        var drop = -10 * Math.Sin(Math.PI / 6);
        var mesh = Tube([Section(0, 0, 0, walls), Section(run, 0, drop, walls)]);

        ShouldHaveVertex(mesh, 0, 0, 2);
        ShouldHaveVertex(mesh, 0, 0, -1);
        ShouldHaveVertex(mesh, run, 0, drop + 2);
    }

    [Fact]
    public void Down_a_pitch_the_ring_is_laid_across_the_shaft()
    {
        // Straight down. An upright ring would be edge-on to the leg and the shaft would be drawn
        // as a ribbon, so here the ring lies level: every corner of it is at the station's height.
        var walls = new WallDistances(Left: 1, Right: 1, Up: 2, Down: 2);
        var mesh = Tube([Section(0, 0, 0, walls), Section(0, 0, -20, walls)]);

        var top = Vertices(mesh).Where(v => v.Up > -10).ToList();
        top.ShouldAllBe(v => Math.Abs(v.Up) < 1e-9);
        top.Max(v => v.East).ShouldBe(1, tolerance: 1e-9);
        top.Min(v => v.East).ShouldBe(-1, tolerance: 1e-9);
        top.Max(v => v.North).ShouldBe(2, tolerance: 1e-9);
        top.Min(v => v.North).ShouldBe(-2, tolerance: 1e-9);
        Volume(mesh).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Four_sides_keep_every_measured_point_and_nothing_between()
    {
        var walls = new WallDistances(Left: 1, Right: 2, Up: 3, Down: 0.5);
        var mesh = Tube([Section(0, 0, 0, walls), Section(10, 0, 0, walls)], sides: 4);

        ShouldHaveVertex(mesh, 0, -2, 0);
        ShouldHaveVertex(mesh, 0, 1, 0);
        ShouldHaveVertex(mesh, 0, 0, 3);
        ShouldHaveVertex(mesh, 0, 0, -0.5);
        mesh.VertexCount.ShouldBe(10);
        mesh.TriangleCount.ShouldBe(16);
    }

    [Fact]
    public void Cross_sections_measured_as_nothing_but_zeros_leave_no_surface()
    {
        var onTheRock = new WallDistances(Left: 0, Right: 0, Up: 0, Down: 0);
        var assembler = new WallMeshAssembler();

        PassageTubes.Append(assembler, [Section(0, 0, 0, onTheRock), Section(10, 0, 0, onTheRock)], 8);

        assembler.ToMesh().ShouldBeNull();
    }

    [Fact]
    public void A_ring_needs_a_corner_at_each_measured_point()
    {
        var walls = new WallDistances(1, 1, 1, 1);

        Should.Throw<ArgumentOutOfRangeException>(() => PassageTubes.Append(
            new WallMeshAssembler(), [Section(0, 0, 0, walls), Section(10, 0, 0, walls)], sides: 6));
    }

    [Fact]
    public void A_reading_in_which_nothing_was_measured_is_not_a_cross_section()
    {
        WallDistances.Read(-1, -1, -1, -1).ShouldBeNull();
        WallDistances.Read(double.NaN, -1, -1, -1).ShouldBeNull();

        // A zero is a measurement: the station stands against that wall.
        WallDistances.Read(-1, 0, -1, -1).ShouldBe(new WallDistances(null, 0, null, null));
    }

    // ---- which legs get one -------------------------------------------------

    [Fact]
    public void Only_a_leg_measured_at_both_ends_is_given_a_tube()
    {
        const uint loxSplayBit = 16;
        var measured = new CaveLrud(Left: 1, Right: 1, Up: 1, Down: 1);
        var unmeasured = new CaveLrud(-1, -1, -1, -1);

        var walls = SurveyWallBuilder.Build(
            Lox(
                [(1, "A", 0, 0, 0), (2, "B", 10, 0, 0), (3, "C", 20, 0, 0), (4, "D", 30, 0, 0), (5, "W", 10, 5, 0)],
                [
                    Shot(1, 2, measured, measured),
                    // Measured where it starts and not where it ends: half a cross-section is not
                    // a passage, and closing it to a point at C would be a claim about C.
                    Shot(2, 3, measured, unmeasured),
                    Shot(3, 4, unmeasured, unmeasured),
                    // A shot at the wall, with dimensions written on it all the same. It is a
                    // measurement of the wall; a tube around it would run through the rock.
                    Shot(2, 5, measured, measured, rawFlags: loxSplayBit),
                ]),
            PlainMetres);

        walls.Source.ShouldBe(SurveyWallSource.PassageDimensions);
        walls.RingSides.ShouldBe(8);
        walls.WalledLegs.ShouldBe(1);
        walls.PassageLegs.ShouldBe(3);

        var mesh = walls.Mesh.ShouldNotBeNull();
        mesh.TriangleCount.ShouldBe(32);

        // Everything drawn is within a metre of the one measured leg, A to B along the east axis.
        Vertices(mesh).ShouldAllBe(v => v.East >= -1e-9 && v.East <= 10 + 1e-9);
        Vertices(mesh).ShouldAllBe(v => Math.Abs(v.North) <= 1 + 1e-9 && Math.Abs(v.Up) <= 1 + 1e-9);
    }

    [Fact]
    public void A_leg_flagged_as_carrying_no_wall_measurement_gets_no_tube_from_its_zeros()
    {
        const uint loxNotLrudBit = 8;
        var zeros = new CaveLrud(0, 0, 0, 0);
        var measured = new CaveLrud(1, 1, 1, 1);

        var walls = SurveyWallBuilder.Build(
            Lox(
                [(1, "A", 0, 0, 0), (2, "B", 10, 0, 0)],
                [Shot(1, 2, measured, zeros, rawFlags: loxNotLrudBit)]),
            PlainMetres);

        walls.Mesh.ShouldBeNull();
        walls.Source.ShouldBe(SurveyWallSource.None);
    }

    [Fact]
    public void The_compilers_own_wall_surfaces_are_used_as_they_stand()
    {
        var measured = new CaveLrud(1, 1, 1, 1);
        var model = Lox(
            [(1, "A", 0, 0, 0), (2, "B", 10, 0, 0)],
            [Shot(1, 2, measured, measured)],
            scraps:
            [
                new CaveScrap
                {
                    Id = 1,
                    Points =
                    [
                        new CaveVector3(0, 0, 0), new CaveVector3(4, 0, 0),
                        new CaveVector3(4, 3, 0), new CaveVector3(0, 3, 2),
                    ],
                    Triangles = [new CaveTriangle(0, 1, 2), new CaveTriangle(0, 2, 3)],
                },
            ]);

        var walls = SurveyWallBuilder.Build(model, PlainMetres);

        // The surfaces win outright. The compiler writes its own dimension-built walls into the
        // same place, so tubes added beside them would draw those passages twice.
        walls.Source.ShouldBe(SurveyWallSource.Scraps);
        walls.RingSides.ShouldBe(0);
        var mesh = walls.Mesh.ShouldNotBeNull();
        mesh.TriangleCount.ShouldBe(2);
        mesh.VertexCount.ShouldBe(4);
        ShouldHaveVertex(mesh, 4, 3, 0);
        ShouldHaveVertex(mesh, 0, 3, 2);
    }

    [Fact]
    public void Faces_of_a_wall_surface_that_carry_no_surface_are_not_written()
    {
        var model = Lox(
            [(1, "A", 0, 0, 0), (2, "B", 10, 0, 0)],
            [Shot(1, 2, null, null)],
            scraps:
            [
                new CaveScrap
                {
                    Id = 1,
                    Points =
                    [
                        new CaveVector3(0, 0, 0), new CaveVector3(4, 0, 0), new CaveVector3(4, 3, 0),
                        new CaveVector3(4, 3, 0), // the same place as the corner before it
                        new CaveVector3(9, 9, 9), // used by no face that survives
                    ],
                    Triangles =
                    [
                        new CaveTriangle(0, 1, 2),
                        new CaveTriangle(0, 1, 1), // two corners named the same
                        new CaveTriangle(1, 2, 3), // two corners at one place under different numbers
                        new CaveTriangle(4, 4, 4),
                    ],
                },
            ]);

        // Where a drawn passage pinches out the compiler still writes faces, and they have no
        // area. They would be counted, downloaded and drawn as nothing.
        var mesh = SurveyWallBuilder.Build(model, PlainMetres).Mesh.ShouldNotBeNull();
        mesh.TriangleCount.ShouldBe(1);
        mesh.VertexCount.ShouldBe(3);
    }

    [Fact]
    public void A_file_with_nothing_measured_gets_no_walls_and_says_why()
    {
        var walls = SurveyWallBuilder.Build(
            Lox(
                [(1, "A", 0, 0, 0), (2, "B", 10, 0, 0), (3, "C", 20, 0, 0)],
                [Shot(1, 2, null, null), Shot(2, 3, null, null)]),
            PlainMetres);

        walls.Mesh.ShouldBeNull();
        walls.Source.ShouldBe(SurveyWallSource.None);
        walls.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Cross_sections_keyed_by_station_become_tubes_of_their_own_dimensions()
    {
        // Half a metre to each wall and a quarter to roof and floor. The library that reads this
        // format has a mesher of its own which never looks at these numbers and draws a tube one
        // metre in every direction around each leg; these are the numbers that have to come out.
        var model = Survex(
            [new("a", 0, 0, 0), new("b", 10, 0, 0), new("c", 20, 0, 0)],
            [new((0, 0, 0), (10, 0, 0)), new((10, 0, 0), (20, 0, 0))],
            [[new("a", 0.5, 0.5, 0.25, 0.25), new("b", 0.5, 0.5, 0.25, 0.25), new("c", 0.5, 0.5, 0.25, 0.25)]]);

        var walls = SurveyWallBuilder.Build(model, PlainMetres);

        walls.Source.ShouldBe(SurveyWallSource.PassageDimensions);
        walls.WalledLegs.ShouldBe(2);
        var mesh = walls.Mesh.ShouldNotBeNull();
        mesh.TriangleCount.ShouldBe(48);

        Vertices(mesh).Max(v => Math.Abs(v.North)).ShouldBe(0.5, tolerance: 1e-9);
        Vertices(mesh).Max(v => Math.Abs(v.Up)).ShouldBe(0.25, tolerance: 1e-9);
        ShouldHaveVertex(mesh, 10, -0.5, 0);
        ShouldHaveVertex(mesh, 10, 0, 0.25);
    }

    [Fact]
    public void A_run_of_cross_sections_stops_where_nothing_was_measured()
    {
        var model = Survex(
            [new("a", 0, 0, 0), new("b", 10, 0, 0), new("c", 20, 0, 0), new("d", 30, 0, 0), new("e", 40, 0, 0)],
            [
                new((0, 0, 0), (10, 0, 0)), new((10, 0, 0), (20, 0, 0)),
                new((20, 0, 0), (30, 0, 0)), new((30, 0, 0), (40, 0, 0)),
            ],
            [[
                new("a", 1, 1, 1, 1), new("b", 1, 1, 1, 1),
                new("c", -1, -1, -1, -1), // on the traverse, walls never taken
                new("d", 1, 1, 1, 1), new("e", 1, 1, 1, 1),
            ]]);

        var walls = SurveyWallBuilder.Build(model, PlainMetres);

        // a-b and d-e. Nothing reaches c from either side: both legs touching it have an end
        // nobody measured.
        walls.WalledLegs.ShouldBe(2);
        walls.PassageLegs.ShouldBe(4);
        var mesh = walls.Mesh.ShouldNotBeNull();
        mesh.TriangleCount.ShouldBe(64);
        Vertices(mesh).ShouldAllBe(v => v.East <= 10 + 1e-9 || v.East >= 30 - 1e-9);
    }

    [Fact]
    public void Two_cross_sections_no_leg_joins_are_not_joined_by_a_tube()
    {
        // One run of four cross-sections whose middle step, b to c, is not a leg of the survey at
        // all — the end of the first passage was never marked, so the run carries on into the
        // second. Joined blindly that step is a tube through sixty metres of rock.
        var model = Survex(
            [new("a", 0, 0, 0), new("b", 10, 0, 0), new("c", 10, 60, 0), new("d", 20, 60, 0)],
            [new((0, 0, 0), (10, 0, 0)), new((10, 60, 0), (20, 60, 0))],
            [[new("a", 1, 1, 1, 1), new("b", 1, 1, 1, 1), new("c", 1, 1, 1, 1), new("d", 1, 1, 1, 1)]]);

        var walls = SurveyWallBuilder.Build(model, PlainMetres);

        walls.WalledLegs.ShouldBe(2);
        Vertices(walls.Mesh.ShouldNotBeNull()).ShouldAllBe(v => v.North <= 1 + 1e-9 || v.North >= 59 - 1e-9);
    }

    [Fact]
    public void A_shot_at_the_wall_does_not_join_two_cross_sections()
    {
        var model = Survex(
            [new("a", 0, 0, 0), new("b", 10, 0, 0)],
            [new((0, 0, 0), (10, 0, 0), Splay: true)],
            [[new("a", 1, 1, 1, 1), new("b", 1, 1, 1, 1)]]);

        SurveyWallBuilder.Build(model, PlainMetres).Mesh.ShouldBeNull();
    }

    [Fact]
    public void A_real_export_is_walled_only_as_far_out_as_it_was_measured()
    {
        // The public demonstration survey of the viewer this application embeds: a real cave on
        // the British national grid, with nine runs of cross-sections along part of its traverse.
        var model = CaveModelReader.Read(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P8_Master.3d")));
        var placement = SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(SurveyPlacement.EpsgFrom(model.CoordinateSystem), null, null, 0),
            () => (model.Stations.Average(s => s.Position.X), model.Stations.Average(s => s.Position.Y)));

        var walls = SurveyWallBuilder.Build(model, placement);

        walls.Source.ShouldBe(SurveyWallSource.PassageDimensions);
        walls.RingSides.ShouldBe(8);
        // Nine runs of 155 cross-sections make 146 steps from one to the next. Two carry no tube:
        // one joins two cross-sections standing at the same place, and one joins two stations that
        // no leg of the survey joins. The rest of the traverse — 232 legs in all — was never
        // measured for walls and has none.
        walls.WalledLegs.ShouldBe(144);
        walls.PassageLegs.ShouldBe(232);
        var mesh = walls.Mesh.ShouldNotBeNull();

        // No vertex stands further from the nearest station than the largest distance the file
        // measures anywhere. A tube of an invented size would not be bound by that; neither would a
        // tube joining two stations that no leg joins, whose rings are bound but whose misplaced
        // stations are not — every vertex here has a real station within reach.
        var largest = model.Passages.SelectMany(p => p.Stations)
            .Max(s => Math.Max(Math.Max(s.Left, s.Right), Math.Max(s.Up, s.Down)));
        var stations = model.Stations
            .Select(s => placement.ToLocal(s.Position.X, s.Position.Y, s.Position.Z))
            .ToList();
        var furthest = Vertices(mesh).Max(v => stations.Min(s =>
            Math.Sqrt(Math.Pow(v.East - s.East, 2) + Math.Pow(v.North - s.North, 2) + Math.Pow(v.Up - s.Up, 2))));
        furthest.ShouldBeLessThanOrEqualTo(largest + 1e-6);
    }

    // ---- how much is too much -----------------------------------------------

    [Fact]
    public void A_cave_too_large_for_eight_sides_is_drawn_with_four()
    {
        // 70,000 measured legs: 2.24 million triangles at eight sides, 1.12 million at four.
        var walls = SurveyWallBuilder.Build(MeasuredLegs(70_000), PlainMetres);

        walls.RingSides.ShouldBe(4);
        walls.WalledLegs.ShouldBe(70_000);
        walls.Mesh.ShouldNotBeNull().TriangleCount.ShouldBeLessThanOrEqualTo(SurveyWallBuilder.MaxTriangles);
    }

    [Fact]
    public void A_cave_too_large_even_for_four_sides_gets_no_mesh_and_says_why()
    {
        // 130,000 measured legs: 2.08 million triangles at four sides.
        var walls = SurveyWallBuilder.Build(MeasuredLegs(130_000), PlainMetres);

        walls.Mesh.ShouldBeNull();
        walls.Source.ShouldBe(SurveyWallSource.None);
        walls.Reason.ShouldNotBeNull().ShouldContain("2080000");
    }

    [Fact]
    public void Wall_surfaces_past_the_limit_are_not_thinned_and_not_drawn()
    {
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Scraps =
            [
                new CaveScrap
                {
                    Id = 1,
                    Points = [new CaveVector3(0, 0, 0), new CaveVector3(1, 0, 0), new CaveVector3(0, 1, 0)],
                    Triangles = Enumerable.Repeat(new CaveTriangle(0, 1, 2), SurveyWallBuilder.MaxTriangles + 1).ToArray(),
                },
            ],
        };

        var walls = SurveyWallBuilder.Build(model, PlainMetres);

        walls.Mesh.ShouldBeNull();
        walls.Reason.ShouldNotBeNull().ShouldContain("wall surfaces");
    }

    // ---- where the walls are ------------------------------------------------

    [Fact]
    public void Walls_of_a_survey_on_a_grid_stand_on_the_stations_that_grid_places()
    {
        // UTM zone 35N, well east of its central meridian: the grid is turned by more than a degree
        // from true north here and a grid metre is not quite a ground metre. Both corrections are
        // applied to the stations when they are stored, so they have to be applied to the walls.
        const int utm35N = 32635;
        var placement = SurveyPlacement.Resolve(
            new ProjCoordinateProjector(),
            new SurveySourceDeclaration(utm35N, null, null, OriginHeightM: 0),
            () => (360_000, 5_042_000));
        var measured = new CaveLrud(1, 1, 1, 1);

        var walls = SurveyWallBuilder.Build(
            Lox(
                [(1, "A", 360_000, 5_042_000, 900), (2, "B", 361_000, 5_042_000, 880)],
                [Shot(1, 2, measured, measured)]),
            placement);

        // The far station, a kilometre along the grid's east. Its tube is fanned about exactly the
        // point the placement gives that station — and that point is not a kilometre due east.
        var (east, north, up) = placement.ToLocal(361_000, 5_042_000, 880);
        Math.Abs(north).ShouldBeGreaterThan(10);
        ShouldHaveVertex(walls.Mesh.ShouldNotBeNull(), east, north, up);
    }

    // ---- fixtures -----------------------------------------------------------

    private static PassageSection Section(double east, double north, double up, WallDistances walls) =>
        new(new LocalPoint(east, north, up), walls);

    private static IndexedMesh Tube(PassageSection[] run, int sides = PassageTubes.FullSides)
    {
        var assembler = new WallMeshAssembler();
        PassageTubes.Append(assembler, run, sides);
        return assembler.ToMesh().ShouldNotBeNull();
    }

    private static List<(double East, double North, double Up)> Vertices(IndexedMesh mesh) =>
        [.. Enumerable.Range(0, mesh.VertexCount)
            .Select(i => (mesh.Positions[i * 3], mesh.Positions[(i * 3) + 1], mesh.Positions[(i * 3) + 2]))];

    private static void ShouldHaveVertex(IndexedMesh mesh, double east, double north, double up) =>
        Vertices(mesh)
            .Any(v => Math.Abs(v.East - east) < 1e-9 && Math.Abs(v.North - north) < 1e-9 && Math.Abs(v.Up - up) < 1e-9)
            .ShouldBeTrue($"no vertex at ({east}, {north}, {up})");

    /// <summary>The volume a closed mesh encloses, counting a face wound inwards as negative.</summary>
    private static double Volume(IndexedMesh mesh)
    {
        var vertices = Vertices(mesh);
        var sixfold = 0.0;
        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = vertices[mesh.Indices[i]];
            var b = vertices[mesh.Indices[i + 1]];
            var c = vertices[mesh.Indices[i + 2]];
            sixfold += (a.East * ((b.North * c.Up) - (b.Up * c.North)))
                - (a.North * ((b.East * c.Up) - (b.Up * c.East)))
                + (a.Up * ((b.East * c.North) - (b.North * c.East)));
        }

        return sixfold / 6;
    }

    private static CaveShot Shot(uint from, uint to, CaveLrud? atStart, CaveLrud? atEnd, uint rawFlags = 0) =>
        new()
        {
            FromStationId = from,
            ToStationId = to,
            FromLrud = atStart,
            ToLrud = atEnd,
            RawFlags = rawFlags,
            SectionType = CaveShotSection.Oval,
        };

    /// <summary>
    /// A compiled Therion survey, written by the format's own writer and read back by its own
    /// reader, so that what reaches the builder is what a real file would give it — positions
    /// resolved from station numbers, flags decoded from the file's bits.
    /// </summary>
    private static CaveModel Lox(
        (uint Id, string Name, double X, double Y, double Z)[] stations,
        CaveShot[] shots,
        CaveScrap[]? scraps = null) =>
        CaveModelReader.Read(
            LoxWriter.Write(new CaveModel
            {
                SourceFormat = CaveSourceFormat.Lox,
                Stations =
                [
                    .. stations.Select(s => new CaveStation
                    {
                        Id = s.Id,
                        Name = s.Name,
                        Position = new CaveVector3(s.X, s.Y, s.Z),
                    }),
                ],
                Shots = shots,
                Scraps = scraps ?? [],
            }),
            "invented.lox");

    private static CaveModel Survex(
        SurveyFileSamples.Station[] stations,
        SurveyFileSamples.Leg[] legs,
        SurveyFileSamples.CrossSection[][] passages) =>
        CaveModelReader.Read(SurveyFileSamples.Survex3d(stations, legs, passages), "invented.3d");

    /// <summary>
    /// That many legs end to end, every one measured at both ends. Built in memory rather than
    /// through a file: what is under test is a count, and a count does not need a megabyte written
    /// and read back to be the same count.
    /// </summary>
    private static CaveModel MeasuredLegs(int count)
    {
        var measured = new CaveLrud(1, 1, 1, 1);
        var shots = new CaveShot[count];
        for (var i = 0; i < count; i++)
        {
            shots[i] = new CaveShot
            {
                FromStationId = (uint)i,
                ToStationId = (uint)i + 1,
                FromPosition = new CaveVector3(i * 5.0, 0, 0),
                ToPosition = new CaveVector3((i + 1) * 5.0, 0, 0),
                FromLrud = measured,
                ToLrud = measured,
            };
        }

        return new CaveModel { SourceFormat = CaveSourceFormat.Lox, Shots = shots };
    }
}
