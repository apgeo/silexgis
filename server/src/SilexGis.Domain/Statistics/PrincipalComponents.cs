// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Statistics;

/// <summary>
/// One derived axis: how much each original column contributes to it, and how much of the spread
/// in the whole standardised space it accounts for.
/// </summary>
/// <param name="Loadings">One entry per column of the space it was derived from, same order. A
/// column's loading is its weight along this axis; a large magnitude means the axis is largely that
/// column, and the sign says which end. Unit length as a vector, so the loadings of one axis are
/// comparable with each other but not with a raw measurement.</param>
/// <param name="ExplainedShare">Share of the total spread this one axis accounts for, between nought
/// and one. <b>This is the figure that decides whether the picture means anything</b> and it is not
/// optional decoration: two axes carrying nine tenths of the spread are very nearly the whole
/// dataset, two carrying a third are a shadow of it, and the two scatter plots are indistinguishable
/// by eye.</param>
public sealed record PrincipalAxis(IReadOnlyList<double> Loadings, double ExplainedShare);

/// <summary>One subject's position on the derived axes.</summary>
/// <param name="SubjectId">The subject, so the position joins back to whatever was measured.</param>
/// <param name="X">Position along the first axis, in standardised units.</param>
/// <param name="Y">Position along the second.</param>
public sealed record PrincipalPosition(Guid SubjectId, double X, double Y);

/// <summary>
/// A projection of a standardised metric space onto its two most spread-out axes.
/// </summary>
/// <param name="Columns">The columns the projection was derived from — <b>not necessarily every
/// column asked for</b>. A column on which every eligible subject recorded the same value has no
/// spread to contribute and no standardised form at all, so it is left out and named here rather
/// than being silently weighted at nought.</param>
/// <param name="First">The most spread-out axis.</param>
/// <param name="Second">The next, at right angles to the first.</param>
/// <param name="Positions">Every eligible subject's position, in the order the subjects were
/// given.</param>
public sealed record PrincipalProjection(
    IReadOnlyList<string> Columns,
    PrincipalAxis First,
    PrincipalAxis Second,
    IReadOnlyList<PrincipalPosition> Positions)
{
    /// <summary>Share of the whole space's spread the two drawn axes account for together.</summary>
    public double ExplainedShare => First.ExplainedShare + Second.ExplainedShare;
}

/// <summary>
/// Projects a standardised metric space onto the two directions along which its subjects are most
/// spread out, so several measurements can be drawn as one picture.
/// </summary>
/// <remarks>
/// <para>
/// <b>This changes no answer; it chooses what a reader looks along.</b> Which subjects resemble each
/// other is settled in the full standardised space and is not affected by anything here. What this
/// adds is the ability to draw that space when it has more columns than a scatter has axes — and the
/// cost of that convenience is that the two axes drawn are combinations rather than measurements, so
/// a distance along one cannot be read back as metres of anything.
/// </para>
/// <para>
/// <b>The axes come out of the correlation matrix rather than the covariance matrix</b>, which is
/// the same statement as "the space was standardised first". It has to be: the columns are lengths
/// in metres, volumes in cubic metres and ratios between nought and one, and on raw covariance the
/// column with the widest units would simply become the first axis every time, telling the reader
/// about the choice of units rather than about the caves.
/// </para>
/// <para>
/// <b>No numerics library is added for this.</b> The matrix whose eigenvectors are wanted is small —
/// one row and column per selected metric — and symmetric, and for that shape the classical Jacobi
/// rotation is short, has no pathological cases, and converges quadratically. A general-purpose
/// dependency would be a large thing to carry for one small well-conditioned problem.
/// </para>
/// <para>
/// <b>An axis's sign is arbitrary and is therefore fixed here.</b> An eigenvector negated is the same
/// axis: nothing in the arithmetic prefers one end. Left alone, the same data would draw mirrored
/// from one run to the next, which reads as the caves having moved. The convention is that the
/// loading of largest magnitude is positive, so a given dataset always draws the same way round.
/// </para>
/// </remarks>
public static class PrincipalComponents
{
    /// <summary>Sweeps of the rotation. Far more than the small matrices here ever need.</summary>
    private const int MaxSweeps = 100;

    /// <summary>Off-diagonal mass below which the matrix counts as diagonal.</summary>
    private const double Tolerance = 1e-12;

    /// <summary>
    /// Projects already-standardised rows onto their two most spread-out axes.
    /// </summary>
    /// <param name="columns">Column names, in the order the values appear.</param>
    /// <param name="subjectIds">One id per row, same order as <paramref name="rows"/>.</param>
    /// <param name="rows">Standardised values: one row per eligible subject, one entry per column.
    /// Callers pass the same matrix their grouping used, so the picture and the grouping cannot
    /// come to disagree about who was in the population.</param>
    /// <returns>
    /// The projection, or <c>null</c> when there is nothing honest to draw: fewer than two columns
    /// with any spread, or fewer than two subjects. A projection onto one usable axis is not a
    /// scatter, and returning a flat line with a second axis of zeroes would look like a finding.
    /// </returns>
    public static PrincipalProjection? Project(
        IReadOnlyList<string> columns,
        IReadOnlyList<Guid> subjectIds,
        IReadOnlyList<IReadOnlyList<double>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(subjectIds);
        ArgumentNullException.ThrowIfNull(rows);
        if (subjectIds.Count != rows.Count)
        {
            throw new ArgumentException(
                "One subject id is needed per row; the picture would otherwise label positions with "
                + "the wrong caves.", nameof(subjectIds));
        }

        if (rows.Count < 2) return null;

        // A column that never varies has no standardised form — the grouping divides by its spread
        // and gets zero. It is dropped rather than carried at nought, because a loading of nought
        // on a drawn axis reads as "this metric does not matter here" when the truth is that the
        // metric was constant and could not have mattered.
        var usable = new List<int>(columns.Count);
        for (var c = 0; c < columns.Count; c++)
        {
            if (HasSpread(rows, c)) usable.Add(c);
        }

        if (usable.Count < 2) return null;

        var width = usable.Count;
        var matrix = Correlation(rows, usable);
        var (values, vectors) = Jacobi(matrix);

        // Largest spread first. The eigenvalues of a correlation matrix sum to the number of
        // columns, which is what makes an explained share a share.
        var order = Enumerable.Range(0, width).OrderByDescending(i => values[i]).ToArray();
        var total = values.Sum();
        if (total <= 0) return null;

        var first = AxisAt(order[0], values, vectors, total, width);
        var second = AxisAt(order[1], values, vectors, total, width);

        var positions = new PrincipalPosition[rows.Count];
        for (var r = 0; r < rows.Count; r++)
        {
            double x = 0, y = 0;
            for (var i = 0; i < width; i++)
            {
                var value = rows[r][usable[i]];
                x += value * first.Loadings[i];
                y += value * second.Loadings[i];
            }

            positions[r] = new PrincipalPosition(subjectIds[r], x, y);
        }

        return new PrincipalProjection(
            usable.Select(c => columns[c]).ToArray(), first, second, positions);
    }

    private static bool HasSpread(IReadOnlyList<IReadOnlyList<double>> rows, int column)
    {
        var firstValue = rows[0][column];
        for (var r = 1; r < rows.Count; r++)
        {
            if (Math.Abs(rows[r][column] - firstValue) > 1e-12) return true;
        }

        return false;
    }

    /// <summary>
    /// Correlation between every pair of the usable columns.
    /// </summary>
    /// <remarks>
    /// Computed from the rows as given rather than assuming they are exactly centred and scaled.
    /// The caller's standardisation is honest but finite-precision, and a matrix whose diagonal is
    /// 0.9999 instead of 1 makes the explained shares not quite sum to one, which is the sort of
    /// discrepancy a reader notices and cannot explain.
    /// </remarks>
    private static double[,] Correlation(IReadOnlyList<IReadOnlyList<double>> rows, List<int> usable)
    {
        var width = usable.Count;
        var n = rows.Count;

        var means = new double[width];
        for (var i = 0; i < width; i++)
        {
            double sum = 0;
            for (var r = 0; r < n; r++) sum += rows[r][usable[i]];
            means[i] = sum / n;
        }

        var deviations = new double[width];
        for (var i = 0; i < width; i++)
        {
            double sum = 0;
            for (var r = 0; r < n; r++)
            {
                var d = rows[r][usable[i]] - means[i];
                sum += d * d;
            }

            deviations[i] = Math.Sqrt(sum / n);
        }

        var matrix = new double[width, width];
        for (var i = 0; i < width; i++)
        {
            for (var j = i; j < width; j++)
            {
                double sum = 0;
                for (var r = 0; r < n; r++)
                {
                    sum += (rows[r][usable[i]] - means[i]) * (rows[r][usable[j]] - means[j]);
                }

                var denominator = deviations[i] * deviations[j] * n;
                var value = denominator > 0 ? sum / denominator : 0;
                matrix[i, j] = value;
                matrix[j, i] = value;
            }
        }

        return matrix;
    }

    /// <summary>
    /// Eigenvalues and eigenvectors of a symmetric matrix, by cyclic Jacobi rotation.
    /// </summary>
    /// <remarks>
    /// Each sweep zeroes every off-diagonal entry in turn by rotating the plane of the two
    /// coordinates it joins; the rotations accumulate into the eigenvector matrix. The sum of the
    /// squares of the off-diagonal entries falls every rotation and never rises, which is why this
    /// terminates rather than merely usually terminating. The sweep cap is a backstop against a
    /// matrix of NaNs, not a convergence limit.
    /// </remarks>
    private static (double[] Values, double[,] Vectors) Jacobi(double[,] matrix)
    {
        var n = matrix.GetLength(0);
        var a = (double[,])matrix.Clone();
        var v = new double[n, n];
        for (var i = 0; i < n; i++) v[i, i] = 1;

        for (var sweep = 0; sweep < MaxSweeps; sweep++)
        {
            double off = 0;
            for (var p = 0; p < n; p++)
            {
                for (var q = p + 1; q < n; q++) off += a[p, q] * a[p, q];
            }

            if (off <= Tolerance) break;

            for (var p = 0; p < n; p++)
            {
                for (var q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p, q]) <= Tolerance) continue;

                    // The rotation angle that makes this entry exactly zero. Taken through the
                    // smaller root so the angle stays under 45 degrees, which is what keeps the
                    // accumulated rotations well conditioned.
                    var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    var cos = 1 / Math.Sqrt(t * t + 1);
                    var sin = t * cos;

                    for (var k = 0; k < n; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = cos * akp - sin * akq;
                        a[k, q] = sin * akp + cos * akq;
                    }

                    for (var k = 0; k < n; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = cos * apk - sin * aqk;
                        a[q, k] = sin * apk + cos * aqk;
                    }

                    for (var k = 0; k < n; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = cos * vkp - sin * vkq;
                        v[k, q] = sin * vkp + cos * vkq;
                    }
                }
            }
        }

        var values = new double[n];
        for (var i = 0; i < n; i++) values[i] = a[i, i];
        return (values, v);
    }

    private static PrincipalAxis AxisAt(
        int index, double[] values, double[,] vectors, double total, int width)
    {
        var loadings = new double[width];
        for (var i = 0; i < width; i++) loadings[i] = vectors[i, index];

        // The sign convention. Largest magnitude positive, ties broken by position so the choice is
        // total rather than merely usual.
        var dominant = 0;
        for (var i = 1; i < width; i++)
        {
            if (Math.Abs(loadings[i]) > Math.Abs(loadings[dominant])) dominant = i;
        }

        if (loadings[dominant] < 0)
        {
            for (var i = 0; i < width; i++) loadings[i] = -loadings[i];
        }

        return new PrincipalAxis(loadings, Math.Max(values[index], 0) / total);
    }
}
