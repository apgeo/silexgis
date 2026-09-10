// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Statistics;

namespace SilexGis.Domain.Geo;

/// <summary>One cell of a grid as an autocorrelation statistic sees it: where it sits on the
/// lattice, and the one number being read off it.</summary>
/// <param name="X">Column index on the absolute lattice the grid was built on.</param>
/// <param name="Y">Row index on the same lattice.</param>
/// <param name="Value">
/// The quantity whose spatial arrangement is being tested. Which quantity that is belongs to the
/// caller, not here — the arithmetic is the same for a count and for a density, and the two are
/// not the same reading of the same grid.
/// </param>
public readonly record struct GridCellValue(long X, long Y, double Value);

/// <summary>What the arrangement of a grid's values reads as, once the departure from chance has
/// been measured.</summary>
public enum SpatialPatternKind : short
{
    /// <summary>The question could not be asked: too few cells, no cell with a neighbour, or every
    /// cell carrying the same value so there is no arrangement to test. Distinct from
    /// <see cref="Random"/>, which is the answer to a question that was asked.</summary>
    Undetermined = 0,

    /// <summary>The arrangement is indistinguishable from a chance one at the conventional
    /// threshold. This is a failure to detect structure, not a demonstration that there is
    /// none.</summary>
    Random = 1,

    /// <summary>Like values sit beside like ones more than chance would put them: full cells next
    /// to full cells and empty next to empty.</summary>
    Clustered = 2,

    /// <summary>Unlike values sit beside each other more than chance would put them — a
    /// chequerboard rather than a patch.</summary>
    Dispersed = 3,
}

/// <summary>
/// A global autocorrelation reading over a whole grid.
/// </summary>
/// <param name="CellCount">The n every figure here was computed over: how many cells took part.</param>
/// <param name="NeighbourPairCount">
/// How many ordered cell-to-cell adjacencies the lattice held. Published because it is the other
/// half of the sample size — a grid of many cells that are nearly all isolated carries far less
/// evidence than the cell count suggests.
/// </param>
/// <param name="Index">
/// Moran's I. Null when the statistic is undefined, never zero: zero is the reading for an
/// arrangement that was tested and came out like chance, which is a different statement from
/// "nothing was tested".
/// </param>
/// <param name="ExpectedIndex">
/// What Moran's I averages to when the same values are shuffled at random over the same cells:
/// <c>-1/(n-1)</c>, which is slightly below zero rather than at it. Published because a reader
/// comparing a small positive I against zero rather than against this over-reads it.
/// </param>
/// <param name="Variance">The variance of I under that same randomisation, on the normality assumption.</param>
/// <param name="ZScore">How many standard errors the observed I sits above or below its expectation.</param>
/// <param name="PValue">Two-sided probability of a departure at least this large arising by chance.</param>
/// <param name="Pattern">The reading, at the conventional two-sided five per cent threshold.</param>
public sealed record GlobalAutocorrelation(
    int CellCount,
    int NeighbourPairCount,
    double? Index,
    double? ExpectedIndex,
    double? Variance,
    double? ZScore,
    double? PValue,
    SpatialPatternKind Pattern);

/// <summary>
/// A global reading and the per-cell one, computed together over one neighbour list.
/// </summary>
/// <param name="Global">Moran's I over the whole grid.</param>
/// <param name="HotSpotZScores">
/// The Getis–Ord Gi* z-score of each cell, in the order the cells were given, so a caller can zip
/// it back onto the rows it already holds. An entry is null where the statistic is undefined for
/// that cell or for the grid.
/// </param>
public sealed record AutocorrelationResult(
    GlobalAutocorrelation Global,
    IReadOnlyList<double?> HotSpotZScores);

/// <summary>
/// Whether the values on a grid are arranged by more than chance: Moran's I over the whole grid,
/// and Getis–Ord Gi* cell by cell.
///
/// <para>
/// <b>These statistics say nothing a grid does not already say.</b> Both are arithmetic over the
/// cells that were published and their adjacencies — one number per existing cell, and one number
/// for the grid. Nothing here subdivides a cell, interpolates between cells, or reads a position
/// finer than the grid it was handed, so a hot-spot surface built from this carries exactly the
/// resolution of the density surface underneath it and inherits its floor by construction rather
/// than by a second rule that could be forgotten. There is deliberately no cell size in this file
/// to get wrong.
/// </para>
/// <para>
/// <b>Neighbours are lattice adjacency, taken from the indices the grid already carries.</b> Two
/// cells are neighbours when their column and row indices differ by at most one — the eight
/// surrounding cells, corners included, since a hot patch that touches at a corner is one patch
/// and calling it two would be an artefact of the lattice's orientation. The indices are used as
/// given rather than re-derived from the cells' corner coordinates: those indices came from a
/// rounding rule that has one home, and a second rounding of a coordinate that was already rounded
/// is how two parts of one answer come to disagree about which cell something is in. A cell absent
/// from the list — outside the study outline, say — is not a neighbour of anything; it is not
/// treated as an empty cell, because "no ground here to count over" and "ground here with nothing
/// on it" are different statements.
/// </para>
/// <para>
/// <b>What these figures do not establish.</b> A significant I says the arrangement is unlike a
/// random shuffle of the same values over the same cells; it does not say why, and on a window
/// mostly outside the karst it will mostly be detecting where the limestone is. Gi* z-scores are
/// not corrected for having been computed at every cell at once, so a scattering of individually
/// "significant" cells over a large grid is expected under chance alone and the per-cell figure is
/// a shading, not a test. Both are stated here because they are properties of the method, not of
/// any particular registry.
/// </para>
/// </summary>
public static class SpatialAutocorrelation
{
    /// <summary>
    /// The two-sided threshold, in standard errors, at which a departure is called something other
    /// than chance. The conventional five per cent, and it is a convention rather than a
    /// measurement — which is why the z-score and the p-value are published beside the reading
    /// rather than only the reading.
    /// </summary>
    public const double SignificanceZ = 1.96d;

    /// <summary>
    /// The fewest cells a global reading is attempted over. Below four the variance under
    /// randomisation is dominated by the handful of arrangements that exist at all, and a z-score
    /// computed from it is a number without a meaning.
    /// </summary>
    public const int MinimumCells = 4;

    /// <summary>
    /// Reads a grid's arrangement: Moran's I over all of it, and Gi* for each cell.
    /// </summary>
    /// <remarks>
    /// Undefined rather than zero wherever the arithmetic has nothing to work on — a grid of fewer
    /// than <see cref="MinimumCells"/> cells, a grid where no cell touches another, or a grid whose
    /// cells all carry the same value. A constant surface has no arrangement: reporting it as
    /// "random" would claim a test was run on it and passed.
    /// </remarks>
    /// <exception cref="ArgumentException">Two cells carry the same lattice position.</exception>
    public static AutocorrelationResult Analyse(IReadOnlyList<GridCellValue> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var n = cells.Count;
        var neighbours = BuildNeighbours(cells);
        var pairCount = neighbours.Sum(list => list.Length);

        var undefinedGlobal = new GlobalAutocorrelation(
            n, pairCount, null, null, null, null, null, SpatialPatternKind.Undetermined);
        var noHotSpots = new double?[n];

        if (n < MinimumCells || pairCount == 0)
        {
            return new AutocorrelationResult(undefinedGlobal, noHotSpots);
        }

        var mean = cells.Sum(c => c.Value) / n;
        var deviations = new double[n];
        var sumSquaredDeviations = 0d;
        for (var i = 0; i < n; i++)
        {
            deviations[i] = cells[i].Value - mean;
            sumSquaredDeviations += deviations[i] * deviations[i];
        }

        if (sumSquaredDeviations <= 0d)
        {
            return new AutocorrelationResult(undefinedGlobal, noHotSpots);
        }

        return new AutocorrelationResult(
            Moran(cells, neighbours, deviations, sumSquaredDeviations, pairCount),
            HotSpots(cells, neighbours, mean, sumSquaredDeviations));
    }

    /// <summary>
    /// Moran's I with binary adjacency weights, and its z-score under the normality assumption.
    /// </summary>
    /// <remarks>
    /// The weights are left binary rather than row-standardised. Row standardisation makes each
    /// cell contribute equally whatever its number of neighbours, which is the right choice when
    /// the neighbourhoods are irregular administrative units; here they are a regular lattice
    /// whose only irregularity is its edge, and standardising would give a corner cell with three
    /// neighbours the same say as an interior cell with eight.
    /// </remarks>
    private static GlobalAutocorrelation Moran(
        IReadOnlyList<GridCellValue> cells,
        int[][] neighbours,
        double[] deviations,
        double sumSquaredDeviations,
        int pairCount)
    {
        var n = cells.Count;

        // S0 is the sum of every weight; with binary weights that is the number of ordered
        // adjacent pairs, which is what makes the pair count worth publishing beside n.
        double s0 = pairCount;

        var cross = 0d;
        var sumSquaredDegrees = 0d;
        for (var i = 0; i < n; i++)
        {
            foreach (var j in neighbours[i])
            {
                cross += deviations[i] * deviations[j];
            }

            sumSquaredDegrees += (double)neighbours[i].Length * neighbours[i].Length;
        }

        var index = n / s0 * (cross / sumSquaredDeviations);
        var expected = -1d / (n - 1d);

        // The two weight moments the variance under randomisation needs. Adjacency is symmetric
        // here, so S1 reduces to twice S0 and S2 to four times the sum of squared neighbour counts;
        // both are written as the reduction rather than as a double loop, because the reduction is
        // exact for a symmetric binary matrix and the loop would be the same arithmetic slower.
        var s1 = 2d * s0;
        var s2 = 4d * sumSquaredDegrees;

        var nd = (double)n;
        var variance = (((nd * nd * s1) - (nd * s2) + (3d * s0 * s0))
            / (s0 * s0 * ((nd * nd) - 1d))) - (expected * expected);

        // The variance can come out at exactly zero rather than merely small, and the smallest
        // window this method accepts is where it does: four cells in a two-by-two block are all
        // mutually adjacent, and the weight moments then cancel the expectation term exactly. The
        // index is still arithmetically defined there, but publishing it while the z-score and the
        // p-value are absent invites a reader - and any client keying its "could not be asked"
        // branch off the index - to take a reading with no test behind it for a tested one. So
        // nothing is published: an undetermined window carries no index, which is what an absent
        // index is documented to mean.
        if (!(variance > 0d) || !double.IsFinite(index))
        {
            return new GlobalAutocorrelation(
                n, pairCount, null, expected, null, null, null,
                SpatialPatternKind.Undetermined);
        }

        var z = (index - expected) / Math.Sqrt(variance);
        var p = NormalTail.TwoSided(z);

        var pattern = z > SignificanceZ ? SpatialPatternKind.Clustered
            : z < -SignificanceZ ? SpatialPatternKind.Dispersed
            : SpatialPatternKind.Random;

        return new GlobalAutocorrelation(n, pairCount, index, expected, variance, z, p, pattern);
    }

    /// <summary>
    /// Getis–Ord Gi*, as a z-score per cell.
    /// </summary>
    /// <remarks>
    /// The star form: a cell is part of its own neighbourhood, so a lone full cell in empty ground
    /// reads as warm rather than as nothing. The denominator uses the population standard
    /// deviation of all the cells' values, so the figure says how unusual this neighbourhood's
    /// total is against the grid it sits in — which is why the same cave pattern reads differently
    /// against a wider window, and why the window is part of the answer.
    /// </remarks>
    private static double?[] HotSpots(
        IReadOnlyList<GridCellValue> cells,
        int[][] neighbours,
        double mean,
        double sumSquaredDeviations)
    {
        var n = cells.Count;
        var scores = new double?[n];
        var sd = Math.Sqrt(sumSquaredDeviations / n);
        if (!(sd > 0d))
        {
            return scores;
        }

        for (var i = 0; i < n; i++)
        {
            // The neighbourhood is the cell plus its neighbours, so its weight total and the sum
            // of its squared weights are both the same count.
            var k = neighbours[i].Length + 1;
            if (k >= n)
            {
                // Every cell is in the neighbourhood, so there is nothing left to compare it
                // against and the statistic is not defined rather than nought.
                continue;
            }

            var local = cells[i].Value;
            foreach (var j in neighbours[i])
            {
                local += cells[j].Value;
            }

            var denominator = sd * Math.Sqrt((((double)n * k) - ((double)k * k)) / (n - 1d));
            if (!(denominator > 0d))
            {
                continue;
            }

            var z = (local - (mean * k)) / denominator;
            if (double.IsFinite(z))
            {
                scores[i] = z;
            }
        }

        return scores;
    }

    /// <summary>
    /// The adjacency list, built from the lattice indices the cells carry. Cells the grid did not
    /// return are simply not neighbours of anything — see the class remarks on why an absent cell
    /// is not an empty one.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Two cells carry the same lattice position. A grid cannot offer the same cell twice, and the
    /// alternative to refusing it is worse than it looks: keeping the first occurrence and letting
    /// the duplicate look its own neighbours up gives the duplicate out-edges that nothing has
    /// in-edges to, so the adjacency matrix stops being symmetric — and the two weight moments the
    /// Moran variance is computed from are written as the reductions that hold only for a
    /// symmetric binary matrix. Every figure downstream would then be taken from wrong moments
    /// with nothing on the wire recording it.
    /// </exception>
    private static int[][] BuildNeighbours(IReadOnlyList<GridCellValue> cells)
    {
        var byPosition = new Dictionary<(long X, long Y), int>(cells.Count);
        for (var i = 0; i < cells.Count; i++)
        {
            if (!byPosition.TryAdd((cells[i].X, cells[i].Y), i))
            {
                throw new ArgumentException(
                    "A grid may offer each cell position only once.", nameof(cells));
            }
        }

        var lists = new int[cells.Count][];
        var found = new List<int>(8);
        for (var i = 0; i < cells.Count; i++)
        {
            found.Clear();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    if (byPosition.TryGetValue((cells[i].X + dx, cells[i].Y + dy), out var j)
                        && j != i)
                    {
                        found.Add(j);
                    }
                }
            }

            lists[i] = [.. found];
        }

        return lists;
    }
}
