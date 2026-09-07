// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using MaxRev.Gdal.Core;
using Microsoft.Extensions.Logging.Abstractions;
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// What the elevation reader answers, proved against rasters made here whose every pixel is known
/// by construction.
/// </summary>
/// <remarks>
/// <para>
/// The inputs are ramps written in the test, so an expected height is arithmetic rather than a
/// number somebody once read off a tile and wrote down. A committed elevation fixture proves only
/// that the file which was committed still opens; it cannot be read to see what it claims, so a
/// reader that had drifted by a pixel would still agree with it.
/// </para>
/// <para>
/// Format handling is proved separately from arithmetic, and is proved on every run: the formats
/// published elevation actually ships in — a strip-organised single-precision GeoTIFF and a raw
/// headerless height file whose position is carried by its name — are written here and put through
/// the preparation step before being read. Real-world tiles appear in one further test, where the
/// machine has been pointed at a directory of them, and add only that files nobody here wrote
/// behave the same way. The arithmetic is never proved against them.
/// </para>
/// </remarks>
public sealed class DemSamplerTests : IDisposable
{
    /// <summary>The undulation measured over Piatra Craiului, used as a real correction size.</summary>
    private const double Undulation = 39.39;

    /// <summary>Where every raster in this class starts, and how big its pixels are.</summary>
    private const double West = 25.0;
    private const double North = 46.1;
    private const double Pixel = 0.001;

    private const int LongitudeLatitude = 4326;

    /// <summary>
    /// A directory of real published elevation rasters, if this machine has been pointed at one.
    /// </summary>
    /// <remarks>
    /// An environment variable rather than a path in the source, because such a directory is
    /// gigabytes of third-party data that belongs nowhere near a repository, and because the test
    /// is about handling whatever format a real publisher ships rather than about one named file.
    /// </remarks>
    private const string RealTileDirectoryVariable = "SILEXGIS_TEST_DEM_DIR";

    private readonly string root =
        Path.Combine(Path.GetTempPath(), $"silexgis-dem-{Guid.NewGuid():N}");

    private readonly GdalTerrainRasterPreparer preparer = new();

    private readonly GdalDemSampler sampler =
        new(NullLogger<GdalDemSampler>.Instance);

    public DemSamplerTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        sampler.Dispose();

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temporary directory that will not go is litter, not a failing test.
        }
    }

    [Fact]
    public void A_pixel_centre_reads_back_the_height_that_pixel_was_written_with()
    {
        var coverage = Coverage(Ramp("ramp.tif"));

        // Pixel (3, 5) of a ramp that is 100 at its first pixel and rises by ten per column and one
        // per row: 100 + 30 + 5.
        var answer = One(coverage, Centre(3, 5));

        answer.Outcome.ShouldBe(DemSampleOutcome.Sampled);
        answer.ElevationM!.Value.ShouldBe(135, 0.001);
    }

    [Fact]
    public void A_point_between_two_pixel_centres_is_the_mean_of_the_two()
    {
        var coverage = Coverage(Ramp("ramp.tif"));

        // Exactly on the boundary between columns 3 and 4, at the centre of row 5: half of 135 and
        // half of 145. A reader that took the nearest pixel would answer one of them, not this.
        var answer = One(coverage, new DemSamplePoint(West + (4 * Pixel), Latitude(5)));

        answer.ElevationM!.Value.ShouldBe(140, 0.001);
    }

    [Fact]
    public void A_point_between_four_pixel_centres_is_the_mean_of_the_four()
    {
        var coverage = Coverage(Ramp("ramp.tif"));

        // The corner where columns 3 and 4 meet rows 5 and 6: 135, 145, 136 and 146.
        var answer = One(coverage, new DemSamplePoint(West + (4 * Pixel), North - (6 * Pixel)));

        answer.ElevationM!.Value.ShouldBe(140.5, 0.001);
    }

    [Fact]
    public void Ground_no_build_ever_reached_is_no_coverage_and_never_zero()
    {
        var coverage = Coverage(Ramp("ramp.tif"));

        // The positive half first, so a reader that refused everything could not pass here.
        One(coverage, Centre(3, 5)).Outcome.ShouldBe(DemSampleOutcome.Sampled);

        var beyond = One(coverage, new DemSamplePoint(West + 5, North - 5));

        beyond.Outcome.ShouldBe(DemSampleOutcome.OutsideCoverage);
        beyond.ElevationM.ShouldBeNull();
    }

    [Fact]
    public async Task A_coverage_holding_no_rasters_answers_no_coverage_for_everything()
    {
        var answers = await sampler.SampleAsync(
            TerrainCoverage.None, [Centre(3, 5), Centre(0, 0)], CancellationToken.None);

        answers.Count.ShouldBe(2);
        answers.ShouldAllBe(a => a.Outcome == DemSampleOutcome.OutsideCoverage);
        answers.ShouldAllBe(a => a.ElevationM == null);
    }

    [Fact]
    public void A_hole_in_the_data_is_no_data_and_never_zero()
    {
        // A ramp with the whole of column 8 marked as nothing-known.
        var coverage = Coverage(Raster(
            "holed.tif", (x, y) => x == 8 ? (float)TerrainRasterPreparation.VoidValue : Height(x, y)));

        One(coverage, Centre(3, 5)).Outcome.ShouldBe(DemSampleOutcome.Sampled);

        var hole = One(coverage, Centre(8, 5));

        hole.Outcome.ShouldBe(DemSampleOutcome.NoData);
        hole.ElevationM.ShouldBeNull();
    }

    [Fact]
    public void A_hole_next_door_is_not_interpolated_into_a_real_height()
    {
        var coverage = Coverage(Raster(
            "holed.tif", (x, y) => x == 8 ? (float)TerrainRasterPreparation.VoidValue : Height(x, y)));

        // On the boundary between column 7, which has data, and column 8, which has none. Blending
        // the void marker in would answer roughly minus five thousand metres and look like a number.
        var answer = One(coverage, new DemSamplePoint(West + (8 * Pixel), Latitude(5)));

        answer.Outcome.ShouldBe(DemSampleOutcome.NoData);
        answer.ElevationM.ShouldBeNull();
    }

    [Fact]
    public void The_finer_raster_answers_where_two_cover_the_same_ground()
    {
        var fine = Describe(Ramp("fine.tif"));
        var coarse = Describe(Raster("coarse.tif", (_, _) => 999f, pixel: Pixel * 4, size: 8));

        var coverage = new TerrainCoverage(
            [coarse, fine], TerrainHeightDatum.Orthometric, 0);

        // Both cover the point; the fine one holds detail the coarse one cannot, so it is the
        // answer. The order of the list is deliberately coarse-first, so an implementation that
        // simply took the first match would answer 999.
        One(coverage, Centre(3, 5)).ElevationM!.Value.ShouldBe(135, 0.001);
    }

    [Fact]
    public void A_hole_in_the_fine_raster_falls_through_to_the_coarse_one_beneath_it()
    {
        var fine = Describe(Raster(
            "fine.tif", (x, y) => x == 8 ? (float)TerrainRasterPreparation.VoidValue : Height(x, y)));
        var coarse = Describe(Raster("coarse.tif", (_, _) => 999f, pixel: Pixel * 4, size: 8));

        var coverage = new TerrainCoverage([coarse, fine], TerrainHeightDatum.Orthometric, 0);

        One(coverage, Centre(3, 5)).ElevationM!.Value.ShouldBe(135, 0.001);
        One(coverage, Centre(8, 5)).ElevationM!.Value.ShouldBe(999, 0.001);
    }

    [Fact]
    public void An_ellipsoidal_source_is_lowered_by_the_undulation_exactly_once()
    {
        var raster = Describe(Ramp("ramp.tif"));

        var asSurveyed = new TerrainCoverage(
            [raster], TerrainHeightDatum.Orthometric, Undulation);
        var fromTheEllipsoid = new TerrainCoverage(
            [raster], TerrainHeightDatum.Ellipsoidal, Undulation);

        var orthometric = One(asSurveyed, Centre(3, 5)).ElevationM!.Value;
        var ellipsoidal = One(fromTheEllipsoid, Centre(3, 5)).ElevationM!.Value;

        // An orthometric source is already the kind of number a survey carries: the undulation
        // recorded beside it changes nothing.
        orthometric.ShouldBe(135, 0.001);

        // And the ellipsoidal one is corrected by exactly one undulation — not none, and not two,
        // which is the failure that looks like a plausible altitude eighty metres out.
        ellipsoidal.ShouldBe(GeoidOffset.OrthometricFromEllipsoidal(135, Undulation), 0.001);
        (orthometric - ellipsoidal).ShouldBe(Undulation, 0.001);
    }

    [Fact]
    public async Task Every_point_asked_about_is_answered_in_the_order_it_was_asked()
    {
        var coverage = Coverage(Ramp("ramp.tif"));

        var points = new[] { Centre(0, 0), new DemSamplePoint(West + 5, North - 5), Centre(9, 2) };
        var answers = await sampler.SampleAsync(coverage, points, CancellationToken.None);

        answers.Count.ShouldBe(points.Length);
        answers[0].Point.ShouldBe(points[0]);
        answers[0].ElevationM!.Value.ShouldBe(100, 0.001);
        answers[1].Outcome.ShouldBe(DemSampleOutcome.OutsideCoverage);
        answers[2].ElevationM!.Value.ShouldBe(192, 0.001);
    }

    /// <summary>
    /// Several callers reading ground at the same moment, each getting the height they asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every route that reads elevation is served from a thread pool, so unlike the rest of this
    /// application's raster work — which runs on a worker taking one job at a time — the reader is
    /// genuinely entered from several threads at once. It answers that by letting exactly one
    /// caller inside at a time, and this is the test that fails if that ever stops being true.
    /// </para>
    /// <para>
    /// What it can prove is the half that fails loudly. The worse half cannot be asserted at all:
    /// two threads inside the native library on one dataset abort the process rather than raising
    /// something a test could catch, and a test run that has been aborted reports nothing. But the
    /// handles the reader keeps open are recorded in ordinary managed collections, and reading from
    /// several threads without the gate corrupts those or answers out of a handle another caller
    /// has just closed — which surfaces here as a wrong height or a raised exception, because every
    /// expected value below is arithmetic rather than a number read back out of the file.
    /// </para>
    /// </remarks>
    [Fact]
    public void Callers_reading_ground_at_the_same_moment_each_get_the_height_they_asked_for()
    {
        var coverage = Coverage(Ramp("ramp.tif"));

        const int callers = 8;
        const int readsEach = 40;
        var wrong = new ConcurrentBag<string>();

        Parallel.For(0, callers, caller =>
        {
            for (var i = 0; i < readsEach; i++)
            {
                // Deliberately overlapping rather than disjoint: the callers are meant to be
                // asking for the same pixels at the same time, not politely taking turns.
                var x = (caller + i) % 16;
                var y = ((caller * 3) + i) % 16;

                var answer = One(coverage, Centre(x, y));
                var expected = Height(x, y);

                if (answer.Outcome != DemSampleOutcome.Sampled
                    || Math.Abs(answer.ElevationM!.Value - expected) > 0.001)
                {
                    wrong.Add(
                        $"({x}, {y}) answered {answer.Outcome} {answer.ElevationM}, expected {expected}");
                }
            }
        });

        wrong.ShouldBeEmpty();
    }

    /// <summary>
    /// More separate rasters than the reader holds open at once, each still answering its own
    /// ground — including the first one, long since closed to make room for the rest.
    /// </summary>
    /// <remarks>
    /// The reader keeps finished datasets open because a profile asks for hundreds of heights out
    /// of the same one or two files, and reopening one per point would spend the whole request in
    /// header reads. It holds a couple of dozen and closes the least recently used beyond that, so
    /// this walks a row of rasters wider than it will hold and then comes back to the start. A
    /// raster closed and opened again has to say what it always said; a handle closed while still
    /// recorded as open would answer nothing at all, and a stale one would answer a neighbour's
    /// ground. The count is deliberately above the reader's limit — if that limit ever rises past
    /// it, this number rises with it or the test quietly stops proving anything.
    /// </remarks>
    [Fact]
    public void A_raster_closed_to_make_room_for_others_answers_the_same_when_it_is_opened_again()
    {
        const int rasters = 40;
        const int size = 16;
        const double width = size * Pixel;

        // A row of rasters side by side, each lifted a clear kilometre above its neighbour so that
        // an answer coming out of the wrong file is a wrong number rather than a near miss.
        var coverage = new TerrainCoverage(
            [
                .. Enumerable.Range(0, rasters).Select(i => Describe(Raster(
                    $"strip-{i}.tif",
                    (x, y) => Height(x, y) + (i * 1000f),
                    west: West + (i * width)))),
            ],
            TerrainHeightDatum.Orthometric,
            0);

        for (var i = 0; i < rasters; i++)
        {
            One(coverage, Centre(4, 6, West + (i * width)))
                .ElevationM!.Value.ShouldBe(Height(4, 6) + (i * 1000f), 0.001);
        }

        One(coverage, Centre(4, 6, West))
            .ElevationM!.Value.ShouldBe(Height(4, 6), 0.001);
    }

    /// <summary>
    /// A description naming a file that is not there is not read as a hole in the ground.
    /// </summary>
    /// <remarks>
    /// The description of a build's rasters outlives the files it names: a serving host pointed at
    /// a different disk, a prepared directory cleared to reclaim space. Reported as no data it
    /// would say "a build looked here and found nothing", which is a statement about the hillside
    /// and would be drawn as one. What is actually true is that nothing this server can still read
    /// covers the point, and that is what no coverage means. The positive half is here so that a
    /// reader answering nothing at all could not pass: the same footprint, differing only in
    /// whether the path it names is a file.
    /// </remarks>
    [Fact]
    public void Ground_whose_only_raster_is_not_on_disk_is_no_coverage_rather_than_a_hole()
    {
        var described = Describe(Ramp("ramp.tif"));

        One(new TerrainCoverage([described], TerrainHeightDatum.Orthometric, 0), Centre(3, 5))
            .Outcome.ShouldBe(DemSampleOutcome.Sampled);

        // The same ground, described in the same words, named at a path nothing wrote.
        var gone = described with { Path = Path.Combine(root, "gone.tif") };

        var answer = One(
            new TerrainCoverage([gone], TerrainHeightDatum.Orthometric, 0), Centre(3, 5));

        answer.Outcome.ShouldBe(DemSampleOutcome.OutsideCoverage);
        answer.ElevationM.ShouldBeNull();
    }

    /// <summary>
    /// A raster that is not on disk is passed over rather than believed, so a coarser one that is
    /// answers for the ground both of them describe.
    /// </summary>
    /// <remarks>
    /// A hole in the fine raster also falls through to the coarse one, so this alone would pass
    /// even if a missing file were read as a hole; what it adds to the test above is that the
    /// fall-through still happens when the reason is the file rather than the pixel.
    /// </remarks>
    [Fact]
    public void A_raster_that_is_not_on_disk_falls_through_to_a_coarser_one_that_is()
    {
        var fine = Describe(Ramp("fine.tif"));
        var coarse = Describe(Raster("coarse.tif", (_, _) => 999f, pixel: Pixel * 4, size: 8));

        One(new TerrainCoverage([coarse, fine], TerrainHeightDatum.Orthometric, 0), Centre(3, 5))
            .ElevationM!.Value.ShouldBe(135, 0.001);

        var gone = fine with { Path = Path.Combine(root, "gone.tif") };

        One(new TerrainCoverage([coarse, gone], TerrainHeightDatum.Orthometric, 0), Centre(3, 5))
            .ElevationM!.Value.ShouldBe(999, 0.001);
    }

    /// <summary>
    /// A caller who has already given up is not admitted to the queue for the reader.
    /// </summary>
    /// <remarks>
    /// Only one caller is inside the reader at a time, so a request that has been abandoned would
    /// otherwise wait its turn for files nobody is going to be told about, holding a place in front
    /// of callers who are still listening.
    /// </remarks>
    [Fact]
    public async Task A_caller_who_has_given_up_does_not_take_a_turn_at_the_files()
    {
        var coverage = Coverage(Ramp("ramp.tif"));

        using var abandoned = new CancellationTokenSource();
        await abandoned.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await sampler.SampleAsync(coverage, [Centre(0, 0)], abandoned.Token));
    }

    /// <summary>
    /// A strip-organised single-precision GeoTIFF — the shape a great many published elevation
    /// tiles ship in — prepared and then read.
    /// </summary>
    /// <remarks>
    /// Prepared first, deliberately. Everything above hands the reader a file the test wrote and
    /// the describing step measured; nothing above puts a file through the conversion the pipeline
    /// actually performs on what an operator supplies. A regression in that conversion — a tile
    /// with no internal tiling refused, a footprint measured off by a pixel — would otherwise ship
    /// with every test in this file green. Written here rather than committed, so the expected
    /// height is arithmetic and not a number somebody once read off a file.
    /// </remarks>
    [Fact]
    public void A_strip_organised_single_precision_tile_survives_being_prepared_and_read()
    {
        var source = Ramp("stripped.tif");

        var prepared = preparer.Prepare(
            new TerrainRasterPrepareRequest([source], Path.Combine(root, "stripped-out")),
            CancellationToken.None);

        prepared.Count.ShouldBe(1);

        var coverage = new TerrainCoverage(prepared, TerrainHeightDatum.Orthometric, 0);
        var answer = One(coverage, Centre(3, 5));

        answer.Outcome.ShouldBe(DemSampleOutcome.Sampled);

        // The conversion is longitude/latitude to longitude/latitude on the same grid, so the pixel
        // written as 135 is still 135 — with room for the resampling to move it by a hair.
        answer.ElevationM!.Value.ShouldBe(Height(3, 5), 0.01);
    }

    /// <summary>
    /// A raw height file of the kind the shuttle survey is published as, prepared and then read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This format carries no header at all: the whole file is two-byte heights, most significant
    /// byte first, in rows running north to south, and where on Earth it sits is carried by its
    /// name. Nothing else this pipeline accepts works that way, so it is the one input whose
    /// handling cannot be inferred from the GeoTIFF path working — a byte order read the wrong way
    /// round produces heights in the tens of thousands rather than an error, and a row order read
    /// the wrong way round produces a hillside upside down.
    /// </para>
    /// <para>
    /// Written here rather than committed: it is a few megabytes of arithmetic, and the whole
    /// point is that what it holds is known by construction. Its own low resolution is the reason
    /// the tolerance below is loose — the conversion resamples a three-arc-second grid.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_raw_shuttle_height_file_survives_being_prepared_and_read()
    {
        const int side = 1201;
        const double degree = 1.0 / (side - 1);

        // One degree square whose south-west corner is where the name says: 45 north, 25 east.
        var source = Path.Combine(root, "N45E025.hgt");

        // A plane rising to the east and falling to the north, so that reading rows or bytes the
        // wrong way round answers a different number rather than the same one.
        static short At(int column, int row) => (short)(500 + (column / 4) - (row / 8));

        var bytes = new byte[side * side * 2];
        for (var row = 0; row < side; row++)
        {
            for (var column = 0; column < side; column++)
            {
                var value = At(column, row);
                var offset = ((row * side) + column) * 2;
                bytes[offset] = (byte)((value >> 8) & 0xFF);
                bytes[offset + 1] = (byte)(value & 0xFF);
            }
        }

        File.WriteAllBytes(source, bytes);

        var prepared = preparer.Prepare(
            new TerrainRasterPrepareRequest([source], Path.Combine(root, "hgt-out")),
            CancellationToken.None);

        prepared.Count.ShouldBe(1);

        var coverage = new TerrainCoverage(prepared, TerrainHeightDatum.Orthometric, 0);

        // A quarter of the way in from the west edge and a quarter down from the north edge, which
        // is column 300 of row 300 of the source grid.
        var point = new DemSamplePoint(25 + (300 * degree), 46 - (300 * degree));
        var answer = One(coverage, point);

        answer.Outcome.ShouldBe(DemSampleOutcome.Sampled);
        answer.ElevationM!.Value.ShouldBe(At(300, 300), 2);
    }

    /// <summary>
    /// A real published elevation tile, prepared and then read, to prove the formats such datasets
    /// actually ship in survive the round trip.
    /// </summary>
    /// <remarks>
    /// Format handling only. What the tile says is never asserted — the numbers in it are nobody's
    /// to verify here — so what is checked is that a point in the middle of it comes back as a
    /// height at all, in a range that rules out the void marker and sea level standing in for one.
    /// Runs only where the machine has been pointed at such a directory; where it has been, an
    /// unusable directory fails rather than passes quietly.
    /// </remarks>
    [Fact]
    public void A_real_published_tile_can_be_prepared_and_read()
    {
        var directory = Environment.GetEnvironmentVariable(RealTileDirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.Exists(directory).ShouldBeTrue(
            $"{RealTileDirectoryVariable} names a directory that is not there: {directory}");

        // One per distinct format rather than whichever name sorts first: a directory of published
        // tiles holds several, and taking one of them would leave the others unexercised while
        // reading as though the obligation had been met.
        var tiles = Directory.EnumerateFiles(directory)
            .Where(TerrainRasterFiles.IsRaster)
            .Order(StringComparer.Ordinal)
            .GroupBy(f => Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        tiles.ShouldNotBeEmpty(
            $"{RealTileDirectoryVariable} names a directory holding no elevation raster: {directory}");

        for (var i = 0; i < tiles.Count; i++)
        {
            var output = Path.Combine(root, $"real-{i}");
            var prepared = preparer.Prepare(
                new TerrainRasterPrepareRequest([tiles[i]], output), CancellationToken.None);

            prepared.Count.ShouldBe(1, tiles[i]);

            var raster = prepared[0];
            var coverage = new TerrainCoverage([raster], TerrainHeightDatum.Orthometric, 0);
            var middle = new DemSamplePoint(
                (raster.West + raster.East) / 2, (raster.South + raster.North) / 2);

            var answer = One(coverage, middle);

            answer.Outcome.ShouldBe(DemSampleOutcome.Sampled, tiles[i]);

            // Bounded rather than exact: anywhere on land is above the Dead Sea shore and below
            // Everest, and both bounds are far from the void marker and from the sea level a lost
            // value would otherwise be mistaken for.
            answer.ElevationM!.Value.ShouldBeInRange(-500, 9000, tiles[i]);
        }
    }

    /// <summary>One height, read by waiting for the reader on this thread.</summary>
    /// <remarks>
    /// Deliberately blocking rather than awaited. The reader's own entry point is asynchronous
    /// because a request thread must not be held while queueing for it, but what is being proved
    /// here is arithmetic and, in one test, what happens when several real threads are inside the
    /// reader at once — which needs threads that block, not continuations.
    /// </remarks>
    private DemSample One(TerrainCoverage coverage, DemSamplePoint point) =>
        sampler.SampleAsync(coverage, [point], CancellationToken.None)
            .GetAwaiter().GetResult().Single();

    private TerrainCoverage Coverage(string path) =>
        new([Describe(path)], TerrainHeightDatum.Orthometric, 0);

    /// <summary>
    /// What the raster chain says is on disk, which is what the reader is given in production.
    /// </summary>
    private PreparedTerrainRaster Describe(string path) =>
        preparer.Describe(path)
        ?? throw new InvalidOperationException($"The test raster is not a prepared raster: {path}");

    /// <summary>The height this ramp holds at one pixel: known by construction, never read back.</summary>
    private static float Height(int x, int y) => 100f + (x * 10f) + y;

    private string Ramp(string name) => Raster(name, Height);

    /// <summary>The centre of one pixel of a 0.001-degree raster, by default at the shared origin.</summary>
    private static DemSamplePoint Centre(int x, int y, double? west = null) =>
        new((west ?? West) + ((x + 0.5) * Pixel), Latitude(y));

    private static double Latitude(int y) => North - ((y + 0.5) * Pixel);

    /// <summary>
    /// Writes a raster in the form the preparation step produces: longitude and latitude on the
    /// world datum, one band of single-precision heights, and the one value this pipeline uses for
    /// a hole.
    /// </summary>
    private string Raster(
        string name,
        Func<int, int, float> sample,
        double pixel = Pixel,
        int size = 16,
        double? west = null)
    {
        GdalBase.ConfigureAll();
        var path = Path.Combine(root, name);
        var driver = Gdal.GetDriverByName("GTiff");

        using (var dataset = driver.Create(path, size, size, 1, DataType.GDT_Float32, null))
        {
            dataset.SetGeoTransform([west ?? West, pixel, 0, North, 0, -pixel]);

            using (var reference = new SpatialReference(""))
            {
                reference.ImportFromEPSG(LongitudeLatitude);
                reference.ExportToWkt(out var wkt, null);
                dataset.SetProjection(wkt);
            }

            var band = dataset.GetRasterBand(1);
            band.SetNoDataValue(TerrainRasterPreparation.VoidValue);

            var pixels = new float[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    pixels[(y * size) + x] = sample(x, y);
                }
            }

            band.WriteRaster(0, 0, size, size, pixels, size, size, 0, 0);
            dataset.FlushCache();
        }

        return path;
    }
}
