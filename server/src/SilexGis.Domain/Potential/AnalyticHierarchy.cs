// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Potential;

/// <summary>
/// How consistent a set of pairwise judgments is with itself.
/// </summary>
/// <param name="PrincipalEigenvalue">
/// The largest eigenvalue of the comparison matrix. Equal to the number of things compared when
/// every judgment agrees with every other, and larger the more they disagree.
/// </param>
/// <param name="Index">The consistency index: how far the eigenvalue lies above the matrix size, per degree of freedom.</param>
/// <param name="RandomIndex">The average index of randomly filled matrices of the same size, from the published table.</param>
/// <param name="Ratio">
/// The index over the random index. Zero for one or two things compared, where no judgment can
/// contradict another.
/// </param>
/// <param name="Acceptable">Whether the ratio is within the customary one tenth.</param>
public sealed record PairwiseConsistency(
    double PrincipalEigenvalue,
    double Index,
    double RandomIndex,
    double Ratio,
    bool Acceptable);

/// <summary>The weights a comparison matrix yields, and how consistent it is.</summary>
public sealed record PairwiseWeights(IReadOnlyList<double> Weights, PairwiseConsistency Consistency);

/// <summary>
/// Weights from pairwise comparisons, by the analytic hierarchy process: the principal eigenvector
/// of the reciprocal comparison matrix, with the consistency ratio that says how far the judgments
/// can be trusted.
/// </summary>
/// <remarks>
/// <para>
/// A judgment says how many times more important one thing is than another, on the customary
/// scale of one (equal) to nine (extremely more), or its reciprocal the other way about. Only the
/// judgments above the diagonal are ever taken: the matrix is built reciprocal from them, so it
/// cannot arrive saying that A is three times B and B is twice A.
/// </para>
/// <para>
/// An inconsistent matrix is measured, not refused. Whoever made the judgments is shown the ratio
/// and decides; a rule that refused them at one tenth would only teach people to adjust numbers
/// until the warning went away.
/// </para>
/// </remarks>
public static class AnalyticHierarchy
{
    /// <summary>The weakest judgment on the scale: the first thing is one ninth as important as the second.</summary>
    public const double LeastJudgment = 1d / 9d;

    /// <summary>The strongest judgment on the scale.</summary>
    public const double GreatestJudgment = 9d;

    /// <summary>The ratio above which a set of judgments is customarily revised.</summary>
    public const double AcceptableRatio = 0.1;

    /// <summary>
    /// Saaty's random consistency index by matrix size, from one upwards: the mean consistency
    /// index of reciprocal matrices filled at random from the one-to-nine scale.
    /// </summary>
    /// <remarks>
    /// The table as published with the method (Saaty, The Analytic Hierarchy Process, 1980), which
    /// is the one the customary one-tenth threshold was stated against. Later simulations give
    /// slightly different figures; mixing them with that threshold would move it.
    /// </remarks>
    private static readonly double[] RandomIndices =
    [
        0, 0, 0.58, 0.90, 1.12, 1.24, 1.32, 1.41, 1.45, 1.49, 1.51, 1.48, 1.56, 1.57, 1.59,
    ];

    /// <summary>The largest matrix the random index table reaches.</summary>
    public static int LargestSize => RandomIndices.Length;

    /// <summary>The random consistency index for a matrix of this size.</summary>
    public static double RandomIndex(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, RandomIndices.Length);
        return RandomIndices[size - 1];
    }

    /// <summary>
    /// The full reciprocal matrix of the judgments above its diagonal.
    /// </summary>
    /// <param name="size">How many things are compared.</param>
    /// <param name="upperTriangle">
    /// Row by row, each thing against every later one: row <c>i</c> holds <c>size - 1 - i</c>
    /// judgments, the importance of thing <c>i</c> over things <c>i + 1</c> onwards. The last
    /// thing has no later one, so there are <c>size - 1</c> rows.
    /// </param>
    public static double[,] Reciprocal(int size, IReadOnlyList<IReadOnlyList<double>> upperTriangle)
    {
        ArgumentNullException.ThrowIfNull(upperTriangle);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        if (upperTriangle.Count != size - 1)
        {
            throw new ArgumentException("One row of judgments for every thing but the last.", nameof(upperTriangle));
        }

        var matrix = new double[size, size];
        for (var i = 0; i < size; i++)
        {
            matrix[i, i] = 1;
            if (i == size - 1)
            {
                break;
            }

            var row = upperTriangle[i];
            if (row is null || row.Count != size - 1 - i)
            {
                throw new ArgumentException("Each row compares one thing with every later one.", nameof(upperTriangle));
            }

            for (var k = 0; k < row.Count; k++)
            {
                var judgment = row[k];
                if (!double.IsFinite(judgment) || judgment <= 0)
                {
                    throw new ArgumentException("A judgment is a positive number.", nameof(upperTriangle));
                }

                var j = i + 1 + k;
                matrix[i, j] = judgment;
                matrix[j, i] = 1 / judgment;
            }
        }

        return matrix;
    }

    /// <summary>
    /// The weights and consistency of a positive reciprocal matrix.
    /// </summary>
    /// <remarks>
    /// By power iteration. A matrix of positive entries has one eigenvalue larger in size than all
    /// the others, with an eigenvector of positive entries (Perron), so repeated multiplication
    /// converges on it from any positive start; the weights are that vector scaled to sum to one.
    /// </remarks>
    public static PairwiseWeights Solve(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var size = matrix.GetLength(0);
        if (size < 1 || matrix.GetLength(1) != size)
        {
            throw new ArgumentException("A comparison matrix is square.", nameof(matrix));
        }

        if (size > RandomIndices.Length)
        {
            throw new ArgumentException("No random index is published for a matrix this large.", nameof(matrix));
        }

        foreach (var entry in matrix)
        {
            if (!double.IsFinite(entry) || entry <= 0)
            {
                throw new ArgumentException("Every entry of a comparison matrix is a positive number.", nameof(matrix));
            }
        }

        var weights = new double[size];
        Array.Fill(weights, 1d / size);
        var next = new double[size];
        var eigenvalue = (double)size;

        // The eigenvalues of a nine-point reciprocal matrix are well separated, so this settles in
        // a few dozen rounds; the bound is only there so a pathological input cannot spin.
        for (var round = 0; round < 10_000; round++)
        {
            var sum = 0d;
            for (var i = 0; i < size; i++)
            {
                var value = 0d;
                for (var j = 0; j < size; j++)
                {
                    value += matrix[i, j] * weights[j];
                }

                next[i] = value;
                sum += value;
            }

            // The weights sum to one going in, so the sum coming out is the eigenvalue estimate.
            eigenvalue = sum;
            var change = 0d;
            for (var i = 0; i < size; i++)
            {
                var scaled = next[i] / sum;
                change = Math.Max(change, Math.Abs(scaled - weights[i]));
                weights[i] = scaled;
            }

            if (change < 1e-13)
            {
                break;
            }
        }

        // Rounding can leave a consistent matrix a hair under its own size, which would read as a
        // negative inconsistency.
        eigenvalue = Math.Max(eigenvalue, size);
        var index = size > 1 ? (eigenvalue - size) / (size - 1) : 0;
        var random = RandomIndices[size - 1];
        var ratio = random > 0 ? index / random : 0;
        return new PairwiseWeights(
            weights, new PairwiseConsistency(eigenvalue, index, random, ratio, ratio <= AcceptableRatio));
    }
}
