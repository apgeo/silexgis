// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Statistics;

namespace SilexGis.Domain.Tests;

public sealed class MetricClusteringTests
{
    private static readonly string[] Columns = ["surveyedLength", "depth", "ramificationIndex"];
    private static readonly string[] TwoColumns = ["surveyedLength", "ramificationIndex"];

    private static readonly double[] GroupLengths = [900, 1000, 1100];
    private static readonly double[] GroupRatios = [0.10, 0.50, 0.90];
    private static readonly double[] Jitter = [-1, 0, 1, 0];

    /// <summary>
    /// Twelve caves in three groups of four, described by a length in metres and a ratio between
    /// nought and one, both of which say the same thing about which group a cave is in.
    /// </summary>
    private static MetricVector[] SeparatedGroups()
    {
        var subjects = new List<MetricVector>();
        for (var g = 0; g < 3; g++)
        {
            for (var i = 0; i < 4; i++)
            {
                subjects.Add(new MetricVector(
                    Id((g * 4) + i),
                    [GroupLengths[g] + Jitter[i], GroupRatios[g] + (Jitter[i] * 0.01)]));
            }
        }

        return [.. subjects];
    }

    /// <summary>
    /// The same twelve caves with a third metric added — a depth of either 200 m or 1800 m, two of
    /// each inside every group, so depth says nothing at all about which group a cave is in while
    /// spanning by far the widest raw range of the three.
    /// </summary>
    /// <remarks>
    /// Built this way so that <b>the unit a metric is expressed in decides the unstandardised
    /// answer</b>: with depth in metres its 1600-metre spread swamps the length's 200 and the
    /// ratio's 0.8, and an unstandardised grouping follows depth; with the same depths expressed in
    /// kilometres the length takes over and the grouping follows the length instead. Standardising
    /// is what makes those two requests — which are the same measurements — give the same answer.
    /// </remarks>
    private static MetricVector[] WithAConfoundingMetric(
        double lengthUnit = 1d, double depthUnit = 1d, double ratioUnit = 1d)
    {
        var subjects = new List<MetricVector>();
        for (var g = 0; g < 3; g++)
        {
            for (var i = 0; i < 4; i++)
            {
                subjects.Add(new MetricVector(
                    Id((g * 4) + i),
                    [
                        (GroupLengths[g] + Jitter[i]) * lengthUnit,
                        (i < 2 ? 200d : 1800d) * depthUnit,
                        (GroupRatios[g] + (Jitter[i] * 0.01)) * ratioUnit,
                    ]));
            }
        }

        return [.. subjects];
    }

    private static Guid Id(int i) => new($"00000000-0000-0000-0000-{i:D12}");

    /// <summary>
    /// A spread of points with no groups in it, from a fixed recurrence rather than a random
    /// generator so the fixture cannot differ between runs.
    /// </summary>
    private static (double X, double Y)[] Scatter(int count)
    {
        var state = 20260909u;
        double Next()
        {
            state = (state * 1664525u) + 1013904223u;
            return (state >> 8) % 1000u / 1000d;
        }

        return [.. Enumerable.Range(0, count).Select(_ => (Next(), Next()))];
    }

    private static IReadOnlyList<IReadOnlyList<Guid>> Partition(MetricClusterModel model) =>
        [.. model.Assignments
            .GroupBy(a => a.Cluster)
            .Select(g => (IReadOnlyList<Guid>)[.. g.Select(a => a.SubjectId).Order()])
            .OrderBy(g => g[0])];

    [Fact]
    public void Three_separated_groups_are_recovered()
    {
        var model = MetricClustering.Compute(TwoColumns, SeparatedGroups(), 3);

        model.Clusters.Count.ShouldBe(3);
        model.Clusters.Select(c => c.Count).ShouldAllBe(c => c == 4);
        model.Converged.ShouldBeTrue();

        Partition(model).ShouldBe([
            [Id(0), Id(1), Id(2), Id(3)],
            [Id(4), Id(5), Id(6), Id(7)],
            [Id(8), Id(9), Id(10), Id(11)],
        ]);

        // The middles come back in the units the metrics arrived in, so they can be read against
        // the caves rather than against the standardised space the distances were taken in.
        model.Clusters.Select(c => c.Centre[0]).Order().ShouldBe([900d, 1000d, 1100d], 1e-9);
        model.Clusters.Select(c => c.ScaledCentre[0]).Order().Select(Math.Abs).Max()
            .ShouldBeLessThan(2);
    }

    [Fact]
    public void The_same_measurements_in_different_units_give_the_same_grouping()
    {
        // This is the assertion that fails the moment the columns stop being standardised. The
        // fixture's depth is deliberately uninformative and deliberately the widest raw range, so an
        // unstandardised grouping follows depth when depth is in metres and follows the length when
        // the identical depths are given in kilometres — two different answers to one question.
        var metres = MetricClustering.Compute(Columns, WithAConfoundingMetric(), 3);
        var kilometres = MetricClustering.Compute(
            Columns, WithAConfoundingMetric(depthUnit: 0.001d), 3);
        var perMille = MetricClustering.Compute(
            Columns, WithAConfoundingMetric(ratioUnit: 1000d), 3);
        var lengthInKm = MetricClustering.Compute(
            Columns, WithAConfoundingMetric(lengthUnit: 0.001d), 3);

        Partition(kilometres).ShouldBe(Partition(metres));
        Partition(perMille).ShouldBe(Partition(metres));
        Partition(lengthInKm).ShouldBe(Partition(metres));

        // What each metric was divided by is published, so a reader can see that it happened rather
        // than take it on trust.
        metres.Scaling.Select(s => s.Column).ShouldBe(Columns);
        metres.Scaling[0].Mean.ShouldBe(1000, 1e-9);
        metres.Scaling[1].Mean.ShouldBe(1000, 1e-9);
        metres.Scaling[1].StandardDeviation.ShouldBe(800, 1e-9);
        kilometres.Scaling[1].StandardDeviation.ShouldBe(0.8, 1e-9);

        // And a middle is reported in the units it arrived in, so it moves with the unit even
        // though the grouping does not.
        kilometres.Clusters[0].Centre[1].ShouldBe(metres.Clusters[0].Centre[1] * 0.001d, 1e-9);
    }

    [Fact]
    public void The_same_caves_in_a_different_order_give_the_same_grouping()
    {
        var forwards = WithAConfoundingMetric();
        var shuffled = new[] { 7, 2, 11, 0, 5, 9, 3, 10, 1, 6, 8, 4 }
            .Select(i => forwards[i])
            .ToArray();

        var a = MetricClustering.Compute(Columns, forwards, 3);
        var b = MetricClustering.Compute(Columns, shuffled, 3);

        Partition(b).ShouldBe(Partition(a));
        b.Clusters.Select(c => c.ScaledCentre).ShouldBe(a.Clusters.Select(c => c.ScaledCentre));

        // Asked twice, the same request answers the same way — there is no seed to differ.
        Partition(MetricClustering.Compute(Columns, forwards, 3)).ShouldBe(Partition(a));
    }

    [Fact]
    public void A_grouping_that_found_nothing_says_so_through_its_spread()
    {
        var separated = MetricClustering.Compute(TwoColumns, SeparatedGroups(), 3);

        // The same count of subjects with no structure at all. k-means returns three groups here
        // too, which is exactly why the existence of a grouping is evidence of nothing.
        var scatter = Scatter(36);
        var noise = MetricClustering.Compute(
            TwoColumns,
            [.. Enumerable.Range(0, 36).Select(i =>
                new MetricVector(Id(i), [scatter[i].X, scatter[i].Y]))],
            3);

        separated.Clusters.Count.ShouldBe(3);
        noise.Clusters.Count.ShouldBe(3);

        separated.Separation!.MeanWithinDistance.ShouldBeLessThan(
            separated.Separation.MeanBetweenDistance);
        // Both populations separate, so both ratios are readings rather than the "the middles do
        // not separate at all" answer, and asserting that first is what keeps the comparisons below
        // from passing on two absent numbers.
        var separatedRatio = separated.Separation.Ratio.ShouldNotBeNull();
        var noiseRatio = noise.Separation!.Ratio.ShouldNotBeNull();

        separatedRatio.ShouldBeLessThan(0.05);
        noiseRatio.ShouldBeGreaterThan(0.35);
        noiseRatio.ShouldBeGreaterThan(separatedRatio * 5);
    }

    [Fact]
    public void A_population_that_records_one_reading_throughout_states_that_it_did_not_separate()
    {
        // Every subject identical on every selected metric. Each column then has no spread, every
        // standardised point is the origin, all three middles land on top of each other, and the
        // between-group distance is nought. Dividing by it would give a non-finite double, which
        // is not a number that can be put on a wire — it would fail the serializer rather than
        // describe the population. So the reading is stated by its absence, which is also the
        // strongest possible "these groups are not real".
        var subjects = Enumerable.Range(0, 12)
            .Select(i => new MetricVector(Id(i), [1000d, 0.5d]))
            .ToArray();

        var model = MetricClustering.Compute(TwoColumns, subjects, 3);

        model.Clusters.Count.ShouldBe(3);
        model.Separation.ShouldNotBeNull();
        model.Separation!.MeanBetweenDistance.ShouldBe(0d);
        model.Separation.Ratio.ShouldBeNull();
    }

    [Fact]
    public void The_answer_says_who_was_excluded_and_which_metric_excluded_them()
    {
        var complete = WithAConfoundingMetric();
        var subjects = new List<MetricVector>();
        for (var i = 0; i < 8; i++)
        {
            subjects.Add(complete[i]);
        }

        // Three caves whose only missing measurement is the ratio, and one that is also missing a
        // depth. Only the first three would be recovered by dropping the ratio from the request,
        // and that is what the count has to say.
        for (var i = 8; i < 11; i++)
        {
            subjects.Add(complete[i] with
            {
                Values = [complete[i].Values[0], complete[i].Values[1], null],
            });
        }

        subjects.Add(complete[11] with { Values = [complete[11].Values[0], null, null] });

        var model = MetricClustering.Compute(Columns, subjects, 2);

        model.Population.Considered.ShouldBe(12);
        model.Population.Eligible.ShouldBe(8);
        model.Population.Excluded.ShouldBe(4);

        model.Population.Columns[0].ShouldBe(new MetricColumnCoverage("surveyedLength", 12, 0, 0));
        model.Population.Columns[1].ShouldBe(new MetricColumnCoverage("depth", 11, 1, 0));
        model.Population.Columns[2].ShouldBe(
            new MetricColumnCoverage("ramificationIndex", 8, 4, 3));

        // And the grouping really was taken over the eligible eight, not the twelve.
        model.Assignments.Count.ShouldBe(8);
        model.Clusters.Sum(c => c.Count).ShouldBe(8);
    }

    [Fact]
    public void A_reading_that_is_not_a_number_is_missing_rather_than_zero()
    {
        var subjects = WithAConfoundingMetric()
            .Select((s, i) => i == 0
                ? s with { Values = [double.NaN, s.Values[1], s.Values[2]] }
                : s)
            .ToArray();

        var model = MetricClustering.Compute(Columns, subjects, 3);

        model.Population.Eligible.ShouldBe(11);
        model.Population.Columns[0].Recorded.ShouldBe(11);
        model.Assignments.ShouldNotContain(a => a.SubjectId == Id(0));

        // Zero would have been a length, and a length of nought would have dragged the mean and the
        // spread of the whole column with it.
        model.Scaling[0].Mean.ShouldBeGreaterThan(900);
    }

    [Fact]
    public void Too_few_measured_caves_yields_the_account_of_why_rather_than_groups()
    {
        var subjects = WithAConfoundingMetric()
            .Select((s, i) => i < 7 ? s : s with { Values = [s.Values[0], s.Values[1], null] })
            .ToArray();

        var model = MetricClustering.Compute(Columns, subjects, 3);

        model.Clusters.ShouldBeEmpty();
        model.Assignments.ShouldBeEmpty();
        model.Separation.ShouldBeNull();

        // The population account is the answer, not a diagnostic beside it.
        model.Population.Eligible.ShouldBe(7);
        model.Population.Excluded.ShouldBe(5);
        model.Population.Columns[2].SoleReason.ShouldBe(5);
        model.Columns.ShouldBe(Columns);
        model.RequestedClusterCount.ShouldBe(3);
    }

    [Fact]
    public void A_metric_every_cave_recorded_the_same_value_for_is_reported_as_doing_no_work()
    {
        string[] columns = ["a", "flat"];
        var model = MetricClustering.Compute(
            columns,
            [.. Enumerable.Range(0, 12).Select(i =>
                new MetricVector(Id(i), [i < 6 ? 0d : 100d, 42d]))],
            2);

        model.Scaling[1].Mean.ShouldBe(42);
        model.Scaling[1].StandardDeviation.ShouldBe(0);

        // It contributes nothing to any distance, so the grouping is the first metric's own.
        model.Clusters.Select(c => c.Count).ShouldBe([6, 6], ignoreOrder: true);
        foreach (var cluster in model.Clusters)
        {
            cluster.ScaledCentre[1].ShouldBe(0, 1e-12);
            cluster.Centre[1].ShouldBe(42, 1e-12);
        }
    }

    [Fact]
    public void A_request_that_cannot_mean_anything_is_refused()
    {
        var subjects = WithAConfoundingMetric();

        Should.Throw<ArgumentOutOfRangeException>(
            () => MetricClustering.Compute(Columns, subjects, 1));
        Should.Throw<ArgumentOutOfRangeException>(
            () => MetricClustering.Compute(Columns, subjects, 7));
        Should.Throw<ArgumentOutOfRangeException>(
            () => MetricClustering.Compute([], subjects, 2));
        Should.Throw<ArgumentException>(
            () => MetricClustering.Compute(["a", "a"], subjects, 2));
        Should.Throw<ArgumentException>(
            () => MetricClustering.Compute(["a"], subjects, 2));
        Should.Throw<ArgumentException>(
            () => MetricClustering.Compute(Columns, [.. subjects, subjects[0]], 2));
    }
}
