// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Potential;

/// <summary>One point of a rate curve.</summary>
/// <param name="AreaShare">The share of the mapped ground taken, most favourable first.</param>
/// <param name="OccurrenceShare">The share of the occupied cells that ground holds.</param>
public sealed record RatePoint(double AreaShare, double OccurrenceShare);

/// <summary>How well a favourability map ranks a set of occupied cells.</summary>
/// <param name="Points">
/// The curve from (0, 0) to (1, 1). Every point lies on the exact curve; a curve of many steps is
/// given at evenly spaced shares of the ground instead of at every step.
/// </param>
/// <param name="AreaUnderCurve">
/// The area under the exact curve. A half is a map that ranks no better than chance; one is out
/// of reach, since the occupied cells are themselves part of the ground.
/// </param>
/// <param name="Cells">How many cells the map has a value for.</param>
/// <param name="Occurrences">How many of the occupied cells lie on them.</param>
public sealed record RateCurve(IReadOnlyList<RatePoint> Points, double AreaUnderCurve, int Cells, int Occurrences);

/// <summary>
/// The success-rate and prediction-rate curves of a favourability map (Chung and Fabbri,
/// "Validation of spatial prediction models for landslide hazard mapping", Natural Hazards 30,
/// 2003): take the ground from the most favourable cell down, and plot how much of a set of
/// occupied cells has been found against how much ground has been taken.
/// </summary>
/// <remarks>
/// Drawn against the cells a model was trained on it is the success rate, which says how well the
/// model fits what it was shown. Drawn against cells that were kept back it is the prediction
/// rate, which is the one that says what the map is worth. The arithmetic is the same.
/// </remarks>
public static class RateCurves
{
    /// <summary>The most points a curve is given at: every hundredth of the ground, and both ends.</summary>
    public const int MaxPoints = 101;

    /// <summary>
    /// The curve of a map against a set of occupied cells, or null when the map has no value
    /// anywhere or none of the cells lies on it.
    /// </summary>
    /// <remarks>
    /// Cells of equal favourability are taken together, as one step of the curve. A map made from
    /// classes has few distinct values and very many cells of each; taking tied cells one at a
    /// time would let the order they happen to be stored in decide where the occupied ones fall,
    /// and the same map would have a different curve on a different day.
    /// </remarks>
    /// <param name="values">The map, not-a-number where it has no value.</param>
    /// <param name="occupiedCells">The cells to look for. Those the map has no value for are left out.</param>
    public static RateCurve? Of(ReadOnlySpan<float> values, IReadOnlyCollection<int> occupiedCells)
    {
        ArgumentNullException.ThrowIfNull(occupiedCells);

        var occupied = new bool[values.Length];
        foreach (var cell in occupiedCells)
        {
            if (cell < 0 || cell >= values.Length)
            {
                throw new ArgumentException("An occupied cell is a cell of the map.", nameof(occupiedCells));
            }

            occupied[cell] = true;
        }

        var keys = new float[values.Length];
        var found = new bool[values.Length];
        var cells = 0;
        var occurrences = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (!float.IsFinite(values[i]))
            {
                continue;
            }

            keys[cells] = values[i];
            found[cells] = occupied[i];
            cells++;
            if (occupied[i])
            {
                occurrences++;
            }
        }

        if (cells == 0 || occurrences == 0)
        {
            return null;
        }

        Array.Sort(keys, found, 0, cells);

        // Walked from the most favourable end, one point after each run of equal values.
        var exact = new List<RatePoint>(Math.Min(cells, 1024) + 1) { new(0, 0) };
        var taken = 0;
        var hits = 0;
        for (var i = cells - 1; i >= 0; i--)
        {
            taken++;
            if (found[i])
            {
                hits++;
            }

            if (i == 0 || keys[i - 1] != keys[i])
            {
                exact.Add(new RatePoint((double)taken / cells, (double)hits / occurrences));
            }
        }

        return new RateCurve(Thinned(exact), AreaUnder(exact), cells, occurrences);
    }

    /// <summary>The area under a curve of straight lines between its points, by the trapezoid rule.</summary>
    public static double AreaUnder(IReadOnlyList<RatePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var area = 0d;
        for (var i = 1; i < points.Count; i++)
        {
            area += (points[i].AreaShare - points[i - 1].AreaShare)
                * (points[i].OccurrenceShare + points[i - 1].OccurrenceShare) / 2d;
        }

        return area;
    }

    /// <summary>
    /// A curve of at most <see cref="MaxPoints"/> points: the exact one when it is that short,
    /// otherwise the exact one read off at every hundredth of the ground.
    /// </summary>
    private static IReadOnlyList<RatePoint> Thinned(List<RatePoint> exact)
    {
        if (exact.Count <= MaxPoints)
        {
            return exact;
        }

        var steps = MaxPoints - 1;
        var thinned = new List<RatePoint>(MaxPoints) { exact[0] };
        var segment = 1;
        for (var step = 1; step < steps; step++)
        {
            var share = (double)step / steps;
            while (exact[segment].AreaShare < share)
            {
                segment++;
            }

            var from = exact[segment - 1];
            var to = exact[segment];
            var along = (share - from.AreaShare) / (to.AreaShare - from.AreaShare);
            thinned.Add(new RatePoint(share, from.OccurrenceShare + (along * (to.OccurrenceShare - from.OccurrenceShare))));
        }

        thinned.Add(exact[^1]);
        return thinned;
    }
}
