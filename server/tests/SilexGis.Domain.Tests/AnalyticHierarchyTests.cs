// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Weights from pairwise comparisons, held to figures that were published with the method rather
/// than to figures this code produced.
/// </summary>
public class AnalyticHierarchyTests
{
    [Fact]
    public void Saatys_house_buying_example_yields_its_published_priorities_and_consistency()
    {
        // The eight criteria of the house-buying example in Saaty, "How to make a decision: the
        // analytic hierarchy process", European Journal of Operational Research 48 (1990): size,
        // transportation, neighbourhood, age, yard, modern facilities, condition, financing. Only
        // the judgments above the diagonal are given; the paper prints the reciprocal matrix.
        IReadOnlyList<IReadOnlyList<double>> judgments =
        [
            [5, 3, 7, 6, 6, 1d / 3, 1d / 4],
            [1d / 3, 5, 3, 3, 1d / 5, 1d / 7],
            [6, 3, 4, 6, 1d / 5],
            [1d / 3, 1d / 4, 1d / 7, 1d / 8],
            [1d / 2, 1d / 5, 1d / 6],
            [1d / 5, 1d / 6],
            [1d / 2],
        ];

        var solved = AnalyticHierarchy.Solve(AnalyticHierarchy.Reciprocal(8, judgments));

        // The priority vector as printed, to its three decimals.
        double[] published = [0.173, 0.054, 0.188, 0.018, 0.031, 0.036, 0.167, 0.333];
        for (var i = 0; i < published.Length; i++)
        {
            solved.Weights[i].ShouldBe(published[i], 0.0006, $"criterion {i + 1}");
        }

        solved.Weights.Sum().ShouldBe(1, 1e-12);
        solved.Consistency.PrincipalEigenvalue.ShouldBe(9.669, 0.0006);
        solved.Consistency.Index.ShouldBe(0.238, 0.0006);
        solved.Consistency.RandomIndex.ShouldBe(1.41);
        solved.Consistency.Ratio.ShouldBe(0.169, 0.0006);
        // The paper's own example is over the customary tenth, and is measured all the same.
        solved.Consistency.Acceptable.ShouldBeFalse();
    }

    [Fact]
    public void A_three_by_three_matrix_matches_its_closed_form()
    {
        // For three things the principal eigenvalue and vector have closed forms: with judgments
        // a (1 over 2), b (1 over 3) and c (2 over 3) the eigenvalue is 1 + r + 1/r where
        // r = cbrt(a c / b), and the weights are the rows' geometric means.
        var solved = AnalyticHierarchy.Solve(AnalyticHierarchy.Reciprocal(3, [[3, 5], [3]]));

        var r = Math.Cbrt(3d * 3 / 5);
        solved.Consistency.PrincipalEigenvalue.ShouldBe(1 + r + (1 / r), 1e-10);
        double[] means = [Math.Cbrt(15), 1, Math.Cbrt(1d / 15)];
        for (var i = 0; i < 3; i++)
        {
            solved.Weights[i].ShouldBe(means[i] / means.Sum(), 1e-10);
        }

        solved.Consistency.Ratio.ShouldBe((r + (1 / r) - 2) / 2 / 0.58, 1e-10);
        solved.Consistency.Acceptable.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0.58)]
    [InlineData(4, 0.90)]
    [InlineData(5, 1.12)]
    [InlineData(6, 1.24)]
    [InlineData(7, 1.32)]
    [InlineData(8, 1.41)]
    [InlineData(9, 1.45)]
    [InlineData(10, 1.49)]
    [InlineData(11, 1.51)]
    [InlineData(12, 1.48)]
    [InlineData(13, 1.56)]
    [InlineData(14, 1.57)]
    [InlineData(15, 1.59)]
    public void The_random_index_is_Saatys_published_table(int size, double expected) =>
        AnalyticHierarchy.RandomIndex(size).ShouldBe(expected);

    [Fact]
    public void The_random_index_table_covers_every_evidence_layer_at_once()
    {
        AnalyticHierarchy.LargestSize.ShouldBeGreaterThanOrEqualTo(EvidenceLayers.All.Count);
        Should.Throw<ArgumentOutOfRangeException>(() => AnalyticHierarchy.RandomIndex(0));
        Should.Throw<ArgumentOutOfRangeException>(() => AnalyticHierarchy.RandomIndex(AnalyticHierarchy.LargestSize + 1));
    }

    [Fact]
    public void Judgments_that_agree_with_each_other_give_back_the_weights_they_came_from()
    {
        // Every judgment is the ratio of two known weights, so nothing contradicts anything.
        double[] weights = [0.5, 0.3, 0.15, 0.05];
        IReadOnlyList<IReadOnlyList<double>> judgments =
        [
            [weights[0] / weights[1], weights[0] / weights[2], weights[0] / weights[3]],
            [weights[1] / weights[2], weights[1] / weights[3]],
            [weights[2] / weights[3]],
        ];

        var solved = AnalyticHierarchy.Solve(AnalyticHierarchy.Reciprocal(4, judgments));

        for (var i = 0; i < weights.Length; i++)
        {
            solved.Weights[i].ShouldBe(weights[i], 1e-10);
        }

        solved.Consistency.PrincipalEigenvalue.ShouldBe(4, 1e-9);
        solved.Consistency.Ratio.ShouldBe(0, 1e-9);
        solved.Consistency.Acceptable.ShouldBeTrue();
    }

    [Fact]
    public void One_or_two_things_cannot_be_inconsistent()
    {
        var one = AnalyticHierarchy.Solve(AnalyticHierarchy.Reciprocal(1, []));
        one.Weights.ShouldBe([1d]);
        one.Consistency.Ratio.ShouldBe(0);

        var two = AnalyticHierarchy.Solve(AnalyticHierarchy.Reciprocal(2, [[4]]));
        two.Weights[0].ShouldBe(0.8, 1e-10);
        two.Weights[1].ShouldBe(0.2, 1e-10);
        two.Consistency.Ratio.ShouldBe(0);
        two.Consistency.Acceptable.ShouldBeTrue();
    }

    [Fact]
    public void The_matrix_is_built_reciprocal_from_the_judgments_above_the_diagonal()
    {
        var matrix = AnalyticHierarchy.Reciprocal(3, [[2, 8], [0.25]]);

        matrix[0, 0].ShouldBe(1);
        matrix[0, 1].ShouldBe(2);
        matrix[1, 0].ShouldBe(0.5);
        matrix[0, 2].ShouldBe(8);
        matrix[2, 0].ShouldBe(0.125);
        matrix[1, 2].ShouldBe(0.25);
        matrix[2, 1].ShouldBe(4);
    }

    [Fact]
    public void Judgments_of_the_wrong_shape_or_sign_are_refused()
    {
        Should.Throw<ArgumentException>(() => AnalyticHierarchy.Reciprocal(3, [[2, 8]]));
        Should.Throw<ArgumentException>(() => AnalyticHierarchy.Reciprocal(3, [[2], [3]]));
        Should.Throw<ArgumentException>(() => AnalyticHierarchy.Reciprocal(2, [[0]]));
        Should.Throw<ArgumentException>(() => AnalyticHierarchy.Solve(new double[2, 3]));
        Should.Throw<ArgumentException>(() => AnalyticHierarchy.Solve(new double[,] { { 1, -2 }, { -0.5, 1 } }));
    }
}
