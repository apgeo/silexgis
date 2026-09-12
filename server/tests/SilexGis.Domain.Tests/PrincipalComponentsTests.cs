// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Statistics;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The projection is checked against answers known by construction — data laid along an axis whose
/// direction is chosen in advance, a space whose spread is known to be equal in every direction —
/// rather than against numbers this code produced once and was then pinned to.
/// </summary>
public class PrincipalComponentsTests
{
    private static IReadOnlyList<Guid> Ids(int n) =>
        Enumerable.Range(0, n).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToArray();

    private static IReadOnlyList<IReadOnlyList<double>> Rows(params double[][] rows) => rows;

    [Fact]
    public void Finds_the_direction_the_data_actually_lies_along()
    {
        // Everything on the line y = x, so the first axis must be that diagonal and the second must
        // carry nothing. Loadings are unit length, so the diagonal is (0.707…, 0.707…).
        var rows = Rows(
            [-2, -2], [-1, -1], [0, 0], [1, 1], [2, 2]);

        var projection = PrincipalComponents.Project(["a", "b"], Ids(5), rows);

        projection.ShouldNotBeNull();
        projection!.First.Loadings[0].ShouldBe(0.7071, 0.001);
        projection.First.Loadings[1].ShouldBe(0.7071, 0.001);
        projection.First.ExplainedShare.ShouldBe(1.0, 0.001);
        projection.Second.ExplainedShare.ShouldBe(0.0, 0.001);
    }

    [Fact]
    public void Reports_two_axes_that_share_the_spread_when_the_data_has_no_preferred_direction()
    {
        // A square: no direction is more spread out than any other, so neither axis may claim to be
        // the interesting one. The test that matters is the share, not the loadings — the axes of a
        // circle are arbitrary and any pair at right angles is a correct answer.
        var rows = Rows([-1, -1], [-1, 1], [1, -1], [1, 1]);

        var projection = PrincipalComponents.Project(["a", "b"], Ids(4), rows);

        projection.ShouldNotBeNull();
        projection!.First.ExplainedShare.ShouldBe(0.5, 0.001);
        projection.Second.ExplainedShare.ShouldBe(0.5, 0.001);
        projection.ExplainedShare.ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void The_shares_of_all_axes_are_a_share_of_something()
    {
        // Three correlated columns: whatever the axes come out as, the two drawn ones cannot
        // account for more than the whole, and each is between nought and one. This is the property
        // the reader depends on when they read "these two axes carry 84% of the spread".
        var rows = Rows(
            [-2, -1.8, 0.4], [-1, -1.1, -0.9], [0, 0.2, 1.1],
            [1, 0.9, -0.3], [2, 2.1, 0.6], [0.5, 0.4, -1.4]);

        var projection = PrincipalComponents.Project(["a", "b", "c"], Ids(6), rows);

        projection.ShouldNotBeNull();
        projection!.First.ExplainedShare.ShouldBeInRange(0, 1);
        projection.Second.ExplainedShare.ShouldBeInRange(0, 1);
        projection.ExplainedShare.ShouldBeLessThanOrEqualTo(1.0 + 1e-9);
        // The most spread-out axis is the first one, by definition of which is drawn where.
        projection.First.ExplainedShare.ShouldBeGreaterThanOrEqualTo(projection.Second.ExplainedShare);
    }

    [Fact]
    public void Draws_the_same_way_round_however_the_arithmetic_fell_out()
    {
        // An eigenvector negated is the same axis, so nothing in the arithmetic prefers one end.
        // Without a convention the same caves would draw mirrored between runs, which reads as the
        // caves having moved. Asserted as: the largest loading is positive, both times.
        var ascending = Rows([-2, -2], [-1, -1], [1, 1], [2, 2]);
        var descending = Rows([2, 2], [1, 1], [-1, -1], [-2, -2]);

        var a = PrincipalComponents.Project(["a", "b"], Ids(4), ascending)!;
        var b = PrincipalComponents.Project(["a", "b"], Ids(4), descending)!;

        a.First.Loadings.OrderByDescending(Math.Abs).First().ShouldBeGreaterThan(0);
        b.First.Loadings.OrderByDescending(Math.Abs).First().ShouldBeGreaterThan(0);
        for (var i = 0; i < a.First.Loadings.Count; i++)
        {
            a.First.Loadings[i].ShouldBe(b.First.Loadings[i], 0.001);
        }
    }

    [Fact]
    public void Leaves_out_a_column_nobody_varied_on_and_says_it_did()
    {
        // A column every subject recorded the same has no standardised form at all. Carrying it at
        // a loading of nought would read as "this metric does not matter here", when the truth is
        // that it was constant and could not have mattered.
        var rows = Rows(
            [-2, 5, -1], [-1, 5, 0.5], [0, 5, 1], [1, 5, -0.5], [2, 5, 0.2]);

        var projection = PrincipalComponents.Project(["varies", "constant", "alsoVaries"], Ids(5), rows);

        projection.ShouldNotBeNull();
        projection!.Columns.ShouldBe(["varies", "alsoVaries"]);
        projection.First.Loadings.Count.ShouldBe(2);
    }

    [Fact]
    public void Refuses_rather_than_drawing_a_line_and_calling_it_a_scatter()
    {
        // One usable column cannot make a scatter. A second axis of zeroes would draw as a perfect
        // horizontal line, which looks like a finding rather than like an absence.
        var oneUsable = Rows([-1, 7], [0, 7], [1, 7]);
        PrincipalComponents.Project(["varies", "constant"], Ids(3), oneUsable).ShouldBeNull();

        // And a single subject is not a spread.
        PrincipalComponents.Project(["a", "b"], Ids(1), Rows([1, 2])).ShouldBeNull();
    }

    [Fact]
    public void Places_every_subject_it_was_given_and_labels_each_with_its_own_id()
    {
        var ids = Ids(5);
        var rows = Rows([-2, -1], [-1, 0.5], [0, 1], [1, -0.5], [2, 0.2]);

        var projection = PrincipalComponents.Project(["a", "b"], ids, rows)!;

        projection.Positions.Count.ShouldBe(5);
        projection.Positions.Select(p => p.SubjectId).ShouldBe(ids);
        projection.Positions.ShouldAllBe(p => !double.IsNaN(p.X) && !double.IsNaN(p.Y));
    }

    [Fact]
    public void Refuses_a_row_count_that_does_not_match_its_labels()
    {
        // Silently zipping a short list of ids against a longer list of rows would label positions
        // with the wrong caves — a picture that is wrong in the one way nobody would check.
        Should.Throw<ArgumentException>(() =>
            PrincipalComponents.Project(["a", "b"], Ids(2), Rows([1, 2], [3, 4], [5, 6])));
    }

    [Fact]
    public void Re_expressing_a_metric_in_other_units_does_not_move_the_picture()
    {
        // The space is standardised before it arrives, so a column scaled by a constant is the same
        // column. This is the same property the grouping has, and it has to hold here too or the
        // picture and the grouping would disagree about the same caves.
        var metres = Rows([-2, -1], [-1, 0.5], [0, 1], [1, -0.5], [2, 0.2]);
        var doubled = metres.Select(r => (IReadOnlyList<double>)new[] { r[0] * 2, r[1] }).ToArray();

        var a = PrincipalComponents.Project(["a", "b"], Ids(5), metres)!;
        var b = PrincipalComponents.Project(["a", "b"], Ids(5), doubled)!;

        a.First.ExplainedShare.ShouldBe(b.First.ExplainedShare, 0.001);
        for (var i = 0; i < a.First.Loadings.Count; i++)
        {
            a.First.Loadings[i].ShouldBe(b.First.Loadings[i], 0.001);
        }
    }

    private static MetricVector Subject(int n, double a, double? b, double c) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{n:D12}"), [a, b, c]);

    [Fact]
    public void The_grouping_carries_a_projection_over_exactly_the_subjects_it_grouped()
    {
        // The reason the projection is computed inside the grouping rather than beside it. A
        // subject missing any selected metric is excluded from the grouping, so it must be absent
        // from the picture too: a point drawn for a cave that was not grouped would sit uncoloured
        // among coloured ones and read as a cave that failed to join a group, rather than as one
        // that was never eligible to try.
        var vectors = new[]
        {
            Subject(1, 1.0, 2.0, 3.0), Subject(2, 2.0, 1.0, 5.0),
            Subject(3, 5.0, 4.0, 1.0), Subject(4, 4.0, 5.0, 2.0),
            Subject(5, 1.5, 2.5, 3.5), Subject(6, 4.5, 4.5, 1.5),
            Subject(7, 2.5, 1.5, 4.5), Subject(8, 5.5, 5.0, 0.5),
            Subject(9, 0.5, 2.2, 3.2),
            Subject(10, 3.0, null, 4.0),   // missing a metric: eligible for neither
        };

        var model = MetricClustering.Compute(["a", "b", "c"], vectors, 2);

        model.Population.Eligible.ShouldBe(9);
        model.Projection.ShouldNotBeNull();

        var drawn = model.Projection!.Positions.Select(p => p.SubjectId).OrderBy(id => id).ToArray();
        var grouped = model.Assignments.Select(a => a.SubjectId).OrderBy(id => id).ToArray();
        drawn.ShouldBe(grouped);
        drawn.ShouldNotContain(Guid.Parse("00000000-0000-0000-0000-000000000010"));
    }

    [Fact]
    public void There_is_no_projection_where_there_is_no_grouping()
    {
        // Below the grouping's floor it refuses, for the reason that groups over a handful of
        // subjects restate them instead of describing them. The axes of a projection over the same
        // handful are arithmetic rather than description in exactly the same way, and a scatter
        // with no groups to colour would invite a reader to find structure in what is left.
        var few = new[]
        {
            Subject(1, 1.0, 2.0, 3.0), Subject(2, 2.0, 1.0, 5.0),
            Subject(3, 5.0, 4.0, 1.0), Subject(4, 4.0, 5.0, 2.0),
        };

        var model = MetricClustering.Compute(["a", "b", "c"], few, 2);

        model.Assignments.ShouldBeEmpty();
        model.Projection.ShouldBeNull();
    }
}
