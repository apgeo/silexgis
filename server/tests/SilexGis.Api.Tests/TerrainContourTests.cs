// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;
using OSGeo.OSR;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Api.Tests;

/// <summary>
/// Contour lines drawn as a picture, checked against a hill whose contours are known before
/// anything is traced.
/// </summary>
/// <remarks>
/// <para>
/// The hill is a cone: its height falls away evenly with distance from the summit, measured across
/// the ground. Every contour of a cone is a circle about the summit, and its radius is a division —
/// so where each line belongs, how many there are between the summit and any edge, and which of
/// them are the heavier ones are all arithmetic, and a picture that merely has plausible-looking
/// lines on it does not pass.
/// </para>
/// <para>
/// On a raster in longitude and latitude at 46° north a circle on the ground is an ellipse on the
/// grid, half as wide again as it is tall. That is deliberate: lines traced and drawn correctly
/// follow the ground, and a picture stretched, flipped or drawn against the wrong corner puts them
/// several cells from where the arithmetic says they are.
/// </para>
/// </remarks>
[Collection(RasterScratchCollection.Name)]
public sealed class TerrainContourTests : IDisposable
{
    private const double West = 25.0;
    private const double Latitude = 46d;

    /// <summary>One arc second: about thirty metres north to south.</summary>
    private const double Cell = 1d / 3600d;

    private const int Size = 120;

    /// <summary>The summit's height, deliberately not on a line.</summary>
    private const double Summit = 493d;

    /// <summary>Metres of height lost for every metre walked away from the summit.</summary>
    private const double Fall = 0.2d;

    private readonly string root = Path.Combine(
        TestScratch.Root, "silexgis-contours-" + Guid.NewGuid().ToString("N"));

    private readonly GdalDemDerivatives derivatives = new();

    public TerrainContourTests()
    {
        Directory.CreateDirectory(root);
        GdalRuntime.Configure();
    }

    public void Dispose()
    {
        Gdal.SetConfigOption("CPL_TMPDIR", TestScratch.Root);

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Litter, not a failing test.
        }
    }

    /// <summary>
    /// The picture is the elevation's own footprint, twice as fine, in colour with transparency.
    /// </summary>
    [Fact]
    public void Contours_are_a_picture_on_the_footprint_of_the_elevation_they_were_traced_from()
    {
        var picture = Draw(Hill("placed.tif"), "placed-contours.tif", interval: 20d);

        picture.Width.ShouldBe(Size * 2);
        picture.Height.ShouldBe(Size * 2);
        picture.PixelSizeDegrees.ShouldBe(Cell / 2d, 1e-12);
        picture.West.ShouldBe(West, 1e-9);
        picture.East.ShouldBe(West + (Size * Cell), 1e-9);
        picture.North.ShouldBe(Latitude + (Size * Cell / 2d), 1e-9);
        picture.South.ShouldBe(Latitude - (Size * Cell / 2d), 1e-9);

        using var dataset = Gdal.Open(picture.Path, Access.GA_ReadOnly);
        dataset.RasterCount.ShouldBe(4);
        for (var index = 1; index <= 4; index++)
        {
            using var band = dataset.GetRasterBand(index);
            band.DataType.ShouldBe(DataType.GDT_Byte);
        }

        using (var transparency = dataset.GetRasterBand(4))
        {
            transparency.GetRasterColorInterpretation().ShouldBe(ColorInterp.GCI_AlphaBand);
        }

        // Nothing else carrying the picture's name is left beside it.
        Directory.GetFiles(root).Select(Path.GetFileName)
            .Where(name => name!.StartsWith("placed-contours.tif", StringComparison.Ordinal))
            .ShouldBe(["placed-contours.tif"]);
    }

    /// <summary>
    /// Every cell drawn lies on a circle where the height is a multiple of the spacing, and
    /// everything that is not a line is clear.
    /// </summary>
    [Fact]
    public void Every_line_drawn_lies_where_the_height_is_a_multiple_of_the_spacing()
    {
        const double interval = 20d;
        var picture = Read(Draw(Hill("rings.tif"), "rings-contours.tif", interval));

        var drawn = 0;
        var worstOrdinary = 0d;
        var worstIndex = 0d;

        for (var row = 0; row < picture.Height; row++)
        {
            for (var column = 0; column < picture.Width; column++)
            {
                var alpha = picture.Alpha(column, row);
                if (alpha == 0)
                {
                    continue;
                }

                // A line is there or it is not: nothing in between at the picture's own fineness.
                alpha.ShouldBe((byte)255);
                drawn++;

                if (picture.IsHeavy(column, row))
                {
                    // Against the heights every fifth line stands at, and no others: a heavier
                    // line anywhere else is the wrong line drawn heavy.
                    worstIndex = Math.Max(
                        worstIndex,
                        picture.CellsFromHeavyLine(column, row, interval * TerrainContourLines.IndexEvery));
                }
                else
                {
                    // And the converse: no part of a fifth line is left drawn as an ordinary one.
                    var level = picture.NearestLevel(column, row, interval);
                    Math.Abs(Math.IEEERemainder(level, interval * TerrainContourLines.IndexEvery))
                        .ShouldBeGreaterThan(1d, $"an ordinary cell on the {level} m line");

                    worstOrdinary = Math.Max(
                        worstOrdinary, picture.CellsFromNearestLine(column, row, interval));
                }
            }
        }

        // Within a cell and three quarters of the picture, which is under one cell of the
        // elevation the line was traced through. Half a cell of that is drawing a line through
        // whole cells; the rest is that the line is traced as short straight pieces between
        // heights a cell apart, and each piece is drawn from the cell it starts in to the cell it
        // ends in. A heavier line is judged by the cell it was thickened from.
        worstOrdinary.ShouldBeLessThan(1.75d);
        worstIndex.ShouldBeLessThan(1.75d);

        // Lines, and not a filled picture: most of it is clear.
        drawn.ShouldBeGreaterThan(picture.Width * 4);
        drawn.ShouldBeLessThan(picture.Width * picture.Height / 4);
    }

    /// <summary>
    /// Every circle that should be there is there, all the way round.
    /// </summary>
    /// <remarks>
    /// The converse of the test above, which a blank picture would pass. Each height that is a
    /// multiple of the spacing is walked round at five-degree steps, and wherever that point is
    /// inside the picture a drawn cell has to be within two cells of it.
    /// </remarks>
    [Fact]
    public void Every_height_that_is_a_multiple_of_the_spacing_has_its_line_all_the_way_round()
    {
        const double interval = 20d;
        var picture = Read(Draw(Hill("whole.tif"), "whole-contours.tif", interval));

        var looked = 0;
        for (var level = interval; level < Summit; level += interval)
        {
            var radius = (Summit - level) / Fall;
            for (var bearing = 0d; bearing < 360d; bearing += 5d)
            {
                var (column, row) = picture.CellAt(
                    radius * Math.Sin(Radians(bearing)), radius * Math.Cos(Radians(bearing)));

                if (column < 3 || row < 3 || column >= picture.Width - 3 || row >= picture.Height - 3)
                {
                    continue;
                }

                looked++;
                picture.DrawnWithin(column, row, reach: 2)
                    .ShouldBeTrue($"the {level} m line at bearing {bearing}");
            }
        }

        // Enough of them fall inside the picture for this to have been a search and not a formality.
        looked.ShouldBeGreaterThan(500);
    }

    /// <summary>
    /// Walking from the summit to an edge crosses as many lines as there are multiples of the
    /// spacing between the two heights, and every fifth is the heavier one.
    /// </summary>
    /// <remarks>
    /// Walked east and walked north, which on this grid are different distances for the same number
    /// of cells — so the two counts differ, and a picture whose lines followed the grid rather than
    /// the ground would give the same count both ways.
    /// </remarks>
    [Theory]
    [InlineData(20d)]
    [InlineData(50d)]
    public void A_walk_from_the_summit_crosses_the_right_number_of_lines_and_every_fifth_is_heavier(
        double interval)
    {
        var picture = Read(Draw(Hill("walk.tif"), "walk-contours.tif", interval));

        var eastward = picture.CrossingsEastOfSummit();
        var northward = picture.CrossingsNorthOfSummit();

        var expectedEast = Levels(picture.HeightAtEasternEdge(), interval);
        var expectedNorth = Levels(picture.HeightAtNorthernEdge(), interval);

        eastward.Select(crossing => crossing.Level(interval)).ShouldBe(expectedEast);
        northward.Select(crossing => crossing.Level(interval)).ShouldBe(expectedNorth);
        expectedNorth.Count.ShouldBeGreaterThan(expectedEast.Count);

        foreach (var crossing in eastward.Concat(northward))
        {
            var level = crossing.Level(interval);
            var index = Math.Abs(Math.IEEERemainder(level, interval * TerrainContourLines.IndexEvery)) < 1e-6;

            crossing.Heavy.ShouldBe(index, $"the {level} m line");
            crossing.Cells.ShouldBe(index ? 2 : 1, $"the {level} m line");
        }

        // Both kinds were met, so neither half of the assertion above was vacuous.
        eastward.Concat(northward).Any(crossing => crossing.Heavy).ShouldBeTrue();
        eastward.Concat(northward).Any(crossing => !crossing.Heavy).ShouldBeTrue();
    }

    /// <summary>
    /// The two kinds of line are two browns, the heavier one the darker.
    /// </summary>
    [Fact]
    public void Ordinary_lines_are_a_mid_brown_and_the_heavier_ones_a_darker_one()
    {
        var picture = Read(Draw(Hill("colours.tif"), "colours-contours.tif", interval: 20d));
        var crossings = picture.CrossingsNorthOfSummit();

        var ordinary = crossings.First(crossing => !crossing.Heavy).Colour;
        var heavy = crossings.First(crossing => crossing.Heavy).Colour;

        // Brown: more red than green, more green than blue. And neither pale nor black.
        foreach (var colour in new[] { ordinary, heavy })
        {
            colour.Red.ShouldBeGreaterThan(colour.Green);
            colour.Green.ShouldBeGreaterThan(colour.Blue);
            (colour.Red + colour.Green + colour.Blue).ShouldBeInRange(120, 480);
        }

        (heavy.Red + heavy.Green + heavy.Blue)
            .ShouldBeLessThan(ordinary.Red + ordinary.Green + ordinary.Blue - 90);

        // Exactly two colours in the whole picture, so a line cannot be mistaken for a third kind.
        picture.Colours().Count.ShouldBe(2);
    }

    /// <summary>
    /// A gap in the elevation is left alone, rather than being ringed by every line between the
    /// ground and the value that stands for a gap.
    /// </summary>
    /// <remarks>
    /// The gap is placed between two lines, where the hill itself draws nothing. Told that the
    /// marker is a height like any other, the tracer finds the ground falling ten thousand metres
    /// in the width of a cell and draws five hundred lines round the edge of the hole.
    /// </remarks>
    [Fact]
    public void A_gap_in_the_elevation_is_not_ringed_with_lines()
    {
        // Nine cells east of the summit the hill stands near 452 m: the 460 line passes a cell and
        // a half short of the gap and the 440 line three cells beyond it, so the hill itself
        // draws nothing in the gap or in the ring of cells around it.
        const int column = (Size / 2) + 9;
        const int row = Size / 2;
        var source = Hill("gap.tif", hole: (column, row));

        var picture = Read(Draw(source, "gap-contours.tif", interval: 20d));

        // The gap's own cell and the ring of cells around it, in the picture's finer cells.
        for (var y = (row - 1) * 2; y < (row + 2) * 2; y++)
        {
            for (var x = (column - 1) * 2; x < (column + 2) * 2; x++)
            {
                picture.Alpha(x, y).ShouldBe((byte)0, $"cell {x},{y}");
            }
        }
    }

    /// <summary>
    /// A line that runs from one run of elevation rows into the next is one unbroken line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A large raster is traced a run of rows at a time, and each run draws only its own rows of
    /// the picture. This one is one point two million cells, against the half million traced at
    /// a time, so it is drawn in three runs and has two joins.
    /// </para>
    /// <para>
    /// Its heights fall away evenly to the east and do not change from north to south at all, so
    /// every contour is a straight line from the top of the picture to the bottom and every row
    /// of the picture is the same row. A line broken at a join is a row with a cell missing, and
    /// a line drawn twice there, or continued past the last real height instead of traced, is a
    /// row with a cell too many.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_line_crossing_from_one_run_of_rows_into_the_next_is_unbroken()
    {
        const int columns = 100;
        const int rows = 12_000;

        // From 996 m at the western edge to 274 m at the eastern one. The fall per column is
        // chosen so that no line lies along the boundary between two cells of the picture, where
        // which of the two is drawn would be decided by the last digit of a float.
        var source = Surface(
            "north-south.tif", columns, rows, 0.0001d, (column, _) => 1000d - (7.3d * (column + 0.5d)));
        var picture = Read(Draw(source, "north-south-contours.tif", interval: 20d));

        picture.Height.ShouldBe(rows * 2);

        // A line for every twenty metres from 980 down to 280, the seven hundreds among them
        // heavier.
        var middle = picture.Height / 2;
        var crossed = picture.Runs(middle);
        crossed.Count.ShouldBe(36);
        crossed.Count(run => run.Cells == 2).ShouldBe(7);
        crossed.Count(run => run.Cells == 1).ShouldBe(29);

        for (var row = 1; row < picture.Height - 1; row++)
        {
            picture.SameRow(row, middle).ShouldBeTrue($"row {row} of {picture.Height}");
        }
    }

    /// <summary>
    /// A line lying along a join between two runs is drawn once, on the row it belongs to.
    /// </summary>
    /// <remarks>
    /// The other way round from the test above: heights that fall away evenly to the south, so
    /// every contour is a straight line across the picture on one row, and the row is arithmetic.
    /// The summit height is chosen so that one line falls four tenths of an elevation row beyond
    /// the first join in one case and four tenths short of it in the other — inside the strip of
    /// rows both runs read, where a run that drew past its own rows would draw the line a second
    /// time and a run placed a row out would draw it on the wrong one.
    /// </remarks>
    [Theory]
    [InlineData(1010.02d)]
    [InlineData(1009.98d)]
    public void A_line_lying_along_a_join_between_runs_is_drawn_once_on_its_own_row(double top)
    {
        const int columns = 100;
        const int rows = 12_000;
        const double fall = 0.05d;
        const double interval = 20d;

        var source = Surface(
            "east-west.tif", columns, rows, 0.0001d, (_, row) => top - (fall * (row + 0.5d)));
        var picture = Read(Draw(source, "east-west-contours.tif", interval));

        // Which rows of the picture hold a line, and whether it is one of the heavier ones.
        var expected = new Dictionary<int, bool>();
        for (var level = Math.Floor(top / interval) * interval;
             level > top - (fall * (rows - 0.5d));
             level -= interval)
        {
            var rowsSouth = (top - level) / fall;
            var heavy = Math.Abs(Math.IEEERemainder(level, interval * TerrainContourLines.IndexEvery)) < 1e-6;
            expected.Add((int)Math.Floor(rowsSouth * picture.Height / rows), heavy);
        }

        expected.Count.ShouldBe(30);
        expected.Keys.ShouldContain(top > 1010d ? 10_000 : 9_999);

        for (var row = 0; row < picture.Height; row++)
        {
            var line = expected.ContainsKey(row)
                // The second row of a heavier line, which is thickened downwards.
                || (expected.TryGetValue(row - 1, out var above) && above);

            if (line)
            {
                picture.DrawnBetween(row, 2, picture.Width - 2).ShouldBe(picture.Width - 4, $"row {row}");
            }
            else
            {
                picture.DrawnBetween(row, 0, picture.Width).ShouldBe(0, $"row {row}");
            }
        }
    }

    private ComputedTerrainRaster Draw(string source, string name, double interval) =>
        derivatives.Compute(
            new TerrainDerivativeRequest(
                source,
                Path.Combine(root, name),
                new TerrainDerivativeSettings
                {
                    Derivative = TerrainDerivative.Contours,
                    ContourIntervalMetres = interval,
                }),
            CancellationToken.None);

    /// <summary>The multiples of the spacing between a height and the summit, highest first.</summary>
    private static List<double> Levels(double lowest, double interval)
    {
        var levels = new List<double>();
        for (var level = Math.Floor(Summit / interval) * interval; level > lowest; level -= interval)
        {
            levels.Add(level);
        }

        return levels;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180d;

    // The ellipsoid the rasters are placed on, by its two axes.
    private const double EquatorialRadius = 6_378_137d;
    private const double PolarRadius = 6_356_752.314245d;

    private static double MetresPerDegreeNorth(double latitude)
    {
        var across = EquatorialRadius * Math.Cos(Radians(latitude));
        var along = PolarRadius * Math.Sin(Radians(latitude));
        return Math.Pow(EquatorialRadius * PolarRadius, 2d)
            / Math.Pow((across * across) + (along * along), 1.5d) * Math.PI / 180d;
    }

    private static double MetresPerDegreeEast(double latitude)
    {
        var across = EquatorialRadius * Math.Cos(Radians(latitude));
        var along = PolarRadius * Math.Sin(Radians(latitude));
        return EquatorialRadius * EquatorialRadius / Math.Sqrt((across * across) + (along * along))
            * Math.Cos(Radians(latitude)) * Math.PI / 180d;
    }

    /// <summary>
    /// A cone in longitude and latitude: the summit at the middle of the raster, and the height
    /// falling away evenly with distance across the ground in every direction.
    /// </summary>
    /// <param name="hole">A cell to mark as holding no height, if any.</param>
    private string Hill(string name, (int Column, int Row)? hole = null)
    {
        var east = Cell * MetresPerDegreeEast(Latitude);
        var north = Cell * MetresPerDegreeNorth(Latitude);

        return Surface(
            name,
            Size,
            Size,
            Cell,
            (column, row) =>
            {
                var easting = (column + 0.5d - (Size / 2d)) * east;
                var northing = ((Size / 2d) - (row + 0.5d)) * north;
                return Summit - (Fall * Math.Sqrt((easting * easting) + (northing * northing)));
            },
            hole);
    }

    /// <summary>
    /// An elevation raster in longitude and latitude, centred on the latitude these tests are at,
    /// whose heights are given by a formula over the column and the row of each cell.
    /// </summary>
    private string Surface(
        string name,
        int columns,
        int rows,
        double cell,
        Func<int, int, double> height,
        (int Column, int Row)? hole = null)
    {
        var path = Path.Combine(root, name);

        using var dataset = Gdal.GetDriverByName("GTiff")
            .Create(path, columns, rows, 1, DataType.GDT_Float32, null);
        dataset.SetGeoTransform([West, cell, 0, Latitude + (rows * cell / 2d), 0, -cell]);

        using (var reference = new SpatialReference(null))
        {
            reference.ImportFromEPSG(TerrainRasterPreparation.TargetEpsg);
            reference.ExportToWkt(out var wkt, null);
            dataset.SetProjection(wkt);
        }

        var heights = new float[columns * rows];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                heights[(row * columns) + column] = (float)height(column, row);
            }
        }

        if (hole is { } gap)
        {
            heights[(gap.Row * columns) + gap.Column] = (float)TerrainRasterPreparation.VoidValue;
        }

        using (var band = dataset.GetRasterBand(1))
        {
            band.SetNoDataValue(TerrainRasterPreparation.VoidValue);
            band.WriteRaster(0, 0, columns, rows, heights, columns, rows, 0, 0);
        }

        dataset.FlushCache();
        return path;
    }

    private static Picture Read(ComputedTerrainRaster raster)
    {
        using var dataset = Gdal.Open(raster.Path, Access.GA_ReadOnly);
        var bands = new byte[4][];
        for (var index = 0; index < 4; index++)
        {
            using var band = dataset.GetRasterBand(index + 1);
            bands[index] = new byte[raster.Width * raster.Height];
            band.ReadRaster(
                0, 0, raster.Width, raster.Height, bands[index], raster.Width, raster.Height, 0, 0);
        }

        return new Picture(raster, bands);
    }

    /// <summary>One line met on a walk away from the summit.</summary>
    /// <param name="Metres">How far from the summit the middle of the line is, across the ground.</param>
    /// <param name="Cells">How many cells of the picture the line is wide, along the walk.</param>
    private sealed record Crossing(double Metres, int Cells, bool Heavy, (int Red, int Green, int Blue) Colour)
    {
        /// <summary>The multiple of the spacing this line is nearest to.</summary>
        public double Level(double interval) =>
            Math.Round((Summit - (Fall * Metres)) / interval) * interval;
    }

    /// <summary>A drawn picture of contours, with the hill's own arithmetic to judge it by.</summary>
    private sealed class Picture(ComputedTerrainRaster raster, byte[][] bands)
    {
        private readonly double cellEast = raster.PixelSizeDegrees * MetresPerDegreeEast(Latitude);
        private readonly double cellNorth = raster.PixelSizeDegrees * MetresPerDegreeNorth(Latitude);
        private int? midway;

        public int Width => raster.Width;

        public int Height => raster.Height;

        public byte Alpha(int column, int row) => bands[3][(row * Width) + column];

        /// <summary>
        /// Whether a drawn cell is one of the heavier lines, told by its colour: the darker of
        /// the two.
        /// </summary>
        public bool IsHeavy(int column, int row)
        {
            if (midway is null)
            {
                var colours = Colours();
                midway = (colours.Min(Brightness) + colours.Max(Brightness)) / 2;
            }

            return Sum(column, row) < midway;
        }

        public HashSet<(int Red, int Green, int Blue)> Colours()
        {
            var colours = new HashSet<(int, int, int)>();
            for (var index = 0; index < Width * Height; index++)
            {
                if (bands[3][index] != 0)
                {
                    colours.Add((bands[0][index], bands[1][index], bands[2][index]));
                }
            }

            return colours;
        }

        /// <summary>The cell holding a point so many metres east and north of the summit.</summary>
        public (int Column, int Row) CellAt(double east, double north) => (
            (int)Math.Floor((Width / 2d) + (east / cellEast)),
            (int)Math.Floor((Height / 2d) - (north / cellNorth)));

        public bool DrawnWithin(int column, int row, int reach)
        {
            for (var y = row - reach; y <= row + reach; y++)
            {
                for (var x = column - reach; x <= column + reach; x++)
                {
                    if (Alpha(x, y) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// How far the middle of a cell is from the nearest circle where the height is a multiple
        /// of the spacing, in cells of the picture.
        /// </summary>
        /// <remarks>
        /// Measured the way the picture is laid out rather than across the ground: the step from
        /// the cell to the circle is taken along the line to the summit and then counted in cells,
        /// which are narrower east to west than north to south.
        /// </remarks>
        public double CellsFromNearestLine(int column, int row, double interval)
        {
            var east = (column + 0.5d - (Width / 2d)) * cellEast;
            var north = ((Height / 2d) - (row + 0.5d)) * cellNorth;
            var distance = Math.Sqrt((east * east) + (north * north));

            var level = Math.Round((Summit - (Fall * distance)) / interval) * interval;
            var beyond = distance - ((Summit - level) / Fall);

            return Math.Sqrt(
                Math.Pow(beyond * east / distance / cellEast, 2d)
                + Math.Pow(beyond * north / distance / cellNorth, 2d));
        }

        /// <summary>Whether two rows of the picture are the same, cell for cell and band for band.</summary>
        public bool SameRow(int one, int other)
        {
            foreach (var band in bands)
            {
                if (!band.AsSpan(one * Width, Width).SequenceEqual(band.AsSpan(other * Width, Width)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>How many cells of one row are drawn, between two columns.</summary>
        public int DrawnBetween(int row, int fromColumn, int toColumn)
        {
            var drawn = 0;
            for (var column = fromColumn; column < toColumn; column++)
            {
                if (Alpha(column, row) != 0)
                {
                    drawn++;
                }
            }

            return drawn;
        }

        /// <summary>The lines met walking one row of the picture from west to east.</summary>
        public List<Crossing> Runs(int row) => Walk(Width, step => (step, row), cellEast);

        /// <summary>The multiple of the spacing nearest the height the hill has at a cell.</summary>
        public double NearestLevel(int column, int row, double interval)
        {
            var east = (column + 0.5d - (Width / 2d)) * cellEast;
            var north = ((Height / 2d) - (row + 0.5d)) * cellNorth;
            var distance = Math.Sqrt((east * east) + (north * north));
            return Math.Round((Summit - (Fall * distance)) / interval) * interval;
        }

        /// <summary>
        /// How far a cell of a heavier line is from where that line belongs, judged by the cell
        /// it was thickened from.
        /// </summary>
        /// <remarks>
        /// A heavier line is its traced cells and, for each of them, the cell to its right, the
        /// cell below it and the cell below that — so every cell of one is the corner of a square
        /// of four whose opposite corner was traced. The nearest of the heavy cells it could have
        /// been grown from is the one measured.
        /// </remarks>
        public double CellsFromHeavyLine(int column, int row, double interval)
        {
            var nearest = double.MaxValue;
            for (var up = 0; up <= 1; up++)
            {
                for (var left = 0; left <= 1; left++)
                {
                    var x = column - left;
                    var y = row - up;
                    if (x >= 0 && y >= 0 && Alpha(x, y) != 0 && IsHeavy(x, y))
                    {
                        nearest = Math.Min(nearest, CellsFromNearestLine(x, y, interval));
                    }
                }
            }

            return nearest;
        }

        public double HeightAtEasternEdge() => Summit - (Fall * (Width / 2d) * cellEast);

        public double HeightAtNorthernEdge() => Summit - (Fall * (Height / 2d) * cellNorth);

        /// <summary>The lines met walking east from the summit along the row just south of it.</summary>
        public List<Crossing> CrossingsEastOfSummit() =>
            Walk(Width - (Width / 2), step => ((Width / 2) + step, Height / 2), cellEast);

        /// <summary>The lines met walking north from the summit along the column just east of it.</summary>
        public List<Crossing> CrossingsNorthOfSummit() =>
            Walk(Height / 2, step => (Width / 2, (Height / 2) - 1 - step), cellNorth);

        private List<Crossing> Walk(int steps, Func<int, (int Column, int Row)> at, double stride)
        {
            var crossings = new List<Crossing>();
            var entered = -1;

            for (var step = 0; step <= steps; step++)
            {
                var drawn = false;
                if (step < steps)
                {
                    var (column, row) = at(step);
                    drawn = Alpha(column, row) != 0;
                }

                if (drawn && entered < 0)
                {
                    entered = step;
                }
                else if (!drawn && entered >= 0)
                {
                    var (column, row) = at(entered);
                    crossings.Add(new Crossing(
                        ((entered + step) / 2d) * stride,
                        step - entered,
                        IsHeavy(column, row),
                        (bands[0][(row * Width) + column], bands[1][(row * Width) + column], bands[2][(row * Width) + column])));
                    entered = -1;
                }
            }

            return crossings;
        }

        private int Sum(int column, int row)
        {
            var index = (row * Width) + column;
            return bands[0][index] + bands[1][index] + bands[2][index];
        }

        private static int Brightness((int Red, int Green, int Blue) colour) =>
            colour.Red + colour.Green + colour.Blue;
    }
}
