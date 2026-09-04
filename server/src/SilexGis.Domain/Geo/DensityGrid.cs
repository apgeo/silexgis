// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// The rules a grid that counts features per cell has to obey, in one place, because two of them
/// are the difference between a map of how thickly caves sit and a way of asking where one is.
///
/// <para>
/// <b>Why a floor under the cell size exists at all.</b> Aggregation is the reason a protected
/// location can be published in any form: a count over a wide cell says something about a district
/// and nothing about a cave. Shrink the cell and that stops being true — at the limit every cell
/// holds at most one feature and its position <i>is</i> the feature's position, so a caller who may
/// not see a coordinate can recover it by asking for a finer and finer grid and watching which cell
/// the count moves to. Coordinates the caller may not see exactly therefore enter the aggregate
/// already snapped to the protection grid, and, independently, no published cell may be finer than
/// that same grid. The two protections are deliberately not one: the snap is what makes the number
/// safe, and the floor is what keeps the answer from claiming a resolution the data behind it does
/// not have, however the aggregate is built.
/// </para>
/// <para>
/// <b>Why it refuses rather than widens.</b> Silently rounding a too-small cell up to the floor
/// answers a question the caller did not ask, at a resolution they believe they got, and every
/// number they compute from it afterwards is wrong by a factor nothing on the wire records. A
/// refusal costs one request and is impossible to misread.
/// </para>
/// </summary>
public static class DensityGrid
{
    /// <summary>
    /// The finest cell, in metres, that may be published for a given location-protection grid.
    ///
    /// <para>
    /// It is the protection grid itself: a snapped coordinate carries exactly that much
    /// information, so a cell narrower than it cannot be filled honestly, and a cell at least that
    /// wide can never separate two features the protection rule has already made indistinguishable.
    /// </para>
    /// </summary>
    public static double MinimumCellMeters(double protectionGridMeters) => protectionGridMeters;

    /// <summary>Whether a requested cell size may be served for this protection grid.</summary>
    public static bool IsCellPermitted(double cellMeters, double protectionGridMeters) =>
        double.IsFinite(cellMeters) && cellMeters >= MinimumCellMeters(protectionGridMeters);

    /// <summary>
    /// A cell size in degrees, taken through the same metres-to-degrees conversion the protection
    /// grid uses.
    ///
    /// <para>
    /// It has to be the same conversion and not an equivalent one: the floor compares a cell with
    /// the protection grid, and two conversions that disagree in the last bit would let a cell that
    /// is nominally at the floor come out fractionally finer than the lattice it is meant not to
    /// resolve.
    /// </para>
    /// </summary>
    public static double CellDegrees(double cellMeters) => LocationProtection.CellDegrees(cellMeters);

    /// <summary>
    /// Which cell a coordinate falls in, as a whole-number index on an absolute lattice anchored at
    /// zero rather than at the requested window.
    ///
    /// <para>
    /// Anchoring to the window would make the same feature land in a different cell depending on
    /// where the caller happened to look, so two overlapping requests could not be compared and a
    /// caller could shift the window to split a cell and read a finer position out of the
    /// difference. Anchoring to zero makes the answer a property of the data.
    /// </para>
    /// <para>
    /// Rounding is away from zero at the halfway point, matching the location-protection rule's own
    /// rounding. In SQL that is <c>round((value / cell)::numeric)</c> and not
    /// <c>round(value / cell)</c>: the double-precision form rounds half to <i>even</i>, and the
    /// two must stay identical, or a cell index computed here and one computed in a query would
    /// disagree at exactly the boundaries every snapped coordinate lands on.
    /// </para>
    /// </summary>
    public static long CellIndex(double value, double cellDegrees) =>
        (long)Math.Round(value / cellDegrees, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The inclusive range of cell indices covering <paramref name="min"/>..<paramref name="max"/>
    /// on one axis. A cell with index <c>i</c> spans <c>(i - 0.5) * cell</c> to
    /// <c>(i + 0.5) * cell</c>, so the ends of the window fall inside their own cells rather than
    /// on a boundary — the same convention as <see cref="CellIndex"/>.
    /// </summary>
    public static (long First, long Last) CellRange(double min, double max, double cellDegrees)
    {
        var first = (long)Math.Ceiling((min / cellDegrees) - 0.5);
        var last = (long)Math.Floor((max / cellDegrees) + 0.5);

        return (first, last < first ? first : last);
    }

    /// <summary>
    /// How many cells a window would produce, computed before any of them are built so an
    /// over-large request is refused rather than served slowly.
    ///
    /// <para>
    /// The multiplication is done in floating point and only then brought back to a whole number,
    /// because the two spans are each large enough that their product can exceed what a 64-bit
    /// integer holds. That mattered more than it sounds: an integer product that wraps comes back
    /// <i>negative</i>, so the guard that compares this against a ceiling passes, and the caller
    /// gets the enormous query the guard exists to refuse. Saturating at
    /// <see cref="long.MaxValue"/> instead means an impossible window reads as impossibly large,
    /// which is the answer every caller of this already knows what to do with.
    /// </para>
    /// </summary>
    public static long CellCount(
        double west, double south, double east, double north, double cellDegrees)
    {
        var (firstX, lastX) = CellRange(west, east, cellDegrees);
        var (firstY, lastY) = CellRange(south, north, cellDegrees);

        var product = ((double)lastX - firstX + 1d) * ((double)lastY - firstY + 1d);

        return product >= long.MaxValue ? long.MaxValue : (long)product;
    }

    /// <summary>
    /// The narrowest kernel bandwidth that may be published, in metres. It is the cell the counts
    /// were binned into: a kernel cannot recover detail the binning has already thrown away, so a
    /// bandwidth finer than the cell does not sharpen the surface, it only draws each cell as an
    /// isolated blob and invites the reader to believe the peaks are cave positions.
    /// </summary>
    public static double MinimumBandwidthMeters(double cellMeters) => cellMeters;

    /// <summary>
    /// The bandwidth used when the caller does not name one: twice the cell, which is wide enough
    /// that the surface reads as a surface rather than as the grid it came from, and narrow enough
    /// that a real cluster still shows as one.
    /// </summary>
    public static double DefaultBandwidthMeters(double cellMeters) => cellMeters * 2d;

    /// <summary>
    /// Beyond this many bandwidths a Gaussian contributes less than a ten-thousandth of its peak,
    /// which is below the precision anything downstream reads the surface to. Truncating there
    /// turns an all-pairs sum over the window into a local one.
    /// </summary>
    private const double KernelRadiusInBandwidths = 3d;

    /// <summary>
    /// A smoothed intensity surface over cells that have already been counted — the kernel half of
    /// a density reading, evaluated at each cell's centre.
    ///
    /// <para>
    /// <b>Why it is computed from the cells and not from the features.</b> Every count that reaches
    /// here has already had protected coordinates rounded onto the protection lattice and has
    /// already been aggregated into cells no finer than that lattice. Smoothing those counts can
    /// only ever spread information out, never concentrate it, so the surface inherits the grid's
    /// protection instead of needing its own — whereas a kernel evaluated over raw feature
    /// positions would put a peak exactly where each cave is, which is the one thing this whole
    /// file exists to prevent.
    /// </para>
    /// <para>
    /// <b>What the study area does and does not do to it.</b> An outline decides which features are
    /// counted and which cells exist; it is deliberately <i>not</i> applied again as a per-cell
    /// divisor. A kernel value is already an intensity per unit ground, so dividing it a second
    /// time by the sliver of a cell that lies inside the outline would inflate the edge of every
    /// study area by however thin that sliver happened to be — the same mistake as dividing a whole
    /// cell's count by part of a cell.
    /// </para>
    /// </summary>
    /// <param name="cells">
    /// Cell centres in degrees with the count each holds, in any order.
    /// </param>
    /// <param name="bandwidthMeters">The Gaussian's standard deviation, in metres on the ground.</param>
    /// <returns>
    /// Intensity in features per square kilometre at each cell's centre, in the order the cells
    /// were given.
    /// </returns>
    public static double[] KernelSurface(
        IReadOnlyList<(double LonDegrees, double LatDegrees, int Count)> cells,
        double bandwidthMeters)
    {
        var surface = new double[cells.Count];
        if (cells.Count == 0 || !(bandwidthMeters > 0d))
        {
            return surface;
        }

        var bandwidthDegrees = CellDegrees(bandwidthMeters);
        var cutoffDegrees = bandwidthDegrees * KernelRadiusInBandwidths;

        // Per square metre, before being reported per square kilometre. The 2-pi-h-squared is the
        // Gaussian's own normalisation, which is what makes the sum an intensity rather than a
        // weighted count.
        var peak = 1_000_000d / (2d * Math.PI * bandwidthMeters * bandwidthMeters);
        var twoBandwidthSquared = 2d * bandwidthDegrees * bandwidthDegrees;

        for (var i = 0; i < cells.Count; i++)
        {
            // A degree of longitude is shorter than a degree of latitude everywhere but the
            // equator, and by a fifth at the latitude of the Carpathians. Without this the kernel
            // would be an ellipse stretched east-west and every cluster would read as a band.
            var cosLat = Math.Max(Math.Cos(cells[i].LatDegrees * Math.PI / 180d), 1e-6d);
            var total = 0d;

            for (var j = 0; j < cells.Count; j++)
            {
                if (cells[j].Count == 0)
                {
                    continue;
                }

                var dy = cells[i].LatDegrees - cells[j].LatDegrees;
                if (Math.Abs(dy) > cutoffDegrees)
                {
                    continue;
                }

                var dx = (cells[i].LonDegrees - cells[j].LonDegrees) * cosLat;
                var d2 = (dx * dx) + (dy * dy);
                if (d2 > cutoffDegrees * cutoffDegrees)
                {
                    continue;
                }

                total += cells[j].Count * Math.Exp(-d2 / twoBandwidthSquared);
            }

            surface[i] = total * peak;
        }

        return surface;
    }
}
