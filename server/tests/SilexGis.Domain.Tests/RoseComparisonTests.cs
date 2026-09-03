// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The divergence between two axial roses.
/// </summary>
/// <remarks>
/// Both ends of the scale are asserted here on purpose. A measure of disagreement that is wired up
/// wrongly — a factor left out, a normalisation against the wrong constant, a distance that folds
/// when it should not — still returns small numbers for roses that nearly agree, so a test that
/// only checks "similar roses score low" passes for an implementation that can never score
/// anything else. The two cases that catch it are the identical pair, which must be exactly zero,
/// and the quarter-turn pair, which must be exactly one.
/// </remarks>
public class RoseComparisonTests
{
    [Fact]
    public void Two_identical_roses_do_not_diverge_at_all()
    {
        var rose = Rose((3, 0.5), (11, 0.3), (16, 0.2));

        var divergence = RoseComparison.Compare(rose, rose, byLength: true, 35d, 35d);

        divergence.ShouldNotBeNull();
        divergence.TransportDegrees.ShouldBe(0d);
        divergence.Normalized.ShouldBe(0d);
        divergence.MeanAxisSeparationDegrees.ShouldBe(0d);
    }

    [Fact]
    public void Two_roses_a_quarter_turn_apart_diverge_as_far_as_the_measure_goes()
    {
        // Everything in the first sector against everything in the tenth: 0-10° against 90-100°,
        // which on the folded half-circle is as far apart as two trends can be. Every unit of the
        // rose's share has to move a full quarter turn, and there is no way round the circle that
        // is shorter, so the transport cost is exactly ninety degrees and the normalised figure is
        // exactly one. Nothing may score higher.
        var passages = Rose((0, 1d));
        var faults = Rose((9, 1d));

        var divergence = RoseComparison.Compare(passages, faults, byLength: true, 5d, 95d);

        divergence.ShouldNotBeNull();
        divergence.TransportDegrees.ShouldBe(90d, 1e-9);
        divergence.Normalized.ShouldBe(1d, 1e-9);
        divergence.MeanAxisSeparationDegrees!.Value.ShouldBe(90d, 1e-9);
    }

    [Fact]
    public void A_rose_turned_four_sectors_costs_four_sectors_of_turning()
    {
        var passages = Rose((0, 1d));
        var faults = Rose((4, 1d));

        var divergence = RoseComparison.Compare(passages, faults, byLength: true, null, null);

        divergence.ShouldNotBeNull();
        divergence.TransportDegrees.ShouldBe(40d, 1e-9);
        divergence.Normalized.ShouldBe(40d / 90d, 1e-9);
        divergence.MeanAxisSeparationDegrees.ShouldBeNull();
    }

    [Fact]
    public void The_first_sector_and_the_last_are_neighbours_and_not_opposites()
    {
        // 0-10° and 170-180° are ten degrees apart, because 175° and 5° are the same trend read
        // from either end. A measure that walked the histogram as a line rather than as a ring
        // would call this pair almost maximally divergent, which is the single most likely way to
        // get an axial comparison wrong and still have it look reasonable.
        var divergence = RoseComparison.Compare(
            Rose((0, 1d)), Rose((17, 1d)), byLength: true, null, null);

        divergence.ShouldNotBeNull();
        divergence.TransportDegrees.ShouldBe(10d, 1e-9);
    }

    [Fact]
    public void A_spread_rose_and_a_concentrated_one_diverge_by_how_far_the_spread_has_to_move()
    {
        // Half in the sector the faults occupy and half two sectors away: half the share moves
        // nothing and half moves twenty degrees, so the cost is ten degrees.
        var divergence = RoseComparison.Compare(
            Rose((5, 0.5), (7, 0.5)), Rose((5, 1d)), byLength: true, null, null);

        divergence.ShouldNotBeNull();
        divergence.TransportDegrees.ShouldBe(10d, 1e-9);
    }

    [Fact]
    public void The_weighting_asked_for_is_the_weighting_measured()
    {
        // One rose whose counts and whose metres say different things: a single long trace in one
        // sector against many short legs in another. Reading it by count and reading it by length
        // are two different answers and the caller picks which question was asked.
        var lopsided = new List<OrientationBin>();
        for (var i = 0; i < OrientationStatistics.BinCount; i++)
        {
            var count = i == 2 ? 9 : 0;
            var length = i == 12 ? 900d : 0d;
            lopsided.Add(new OrientationBin(
                i * 10d, (i * 10d) + 10d, count, length,
                i == 2 ? 1d : 0d, i == 12 ? 1d : 0d));
        }

        var target = Rose((2, 1d));

        var byCount = RoseComparison.Compare(lopsided, target, byLength: false, null, null);
        var byLength = RoseComparison.Compare(lopsided, target, byLength: true, null, null);

        byCount.ShouldNotBeNull();
        byCount.TransportDegrees.ShouldBe(0d);
        byLength.ShouldNotBeNull();
        byLength.TransportDegrees.ShouldBe(80d, 1e-9);
    }

    [Fact]
    public void An_empty_rose_has_no_disagreement_to_report_rather_than_a_perfect_one()
    {
        // Nothing was measured on one side. Zero would say the two agree exactly, which is the one
        // reading of "no faults were found near this cave" that the record does not support.
        RoseComparison.Compare(Rose((0, 1d)), Rose(), byLength: true, 5d, null).ShouldBeNull();
        RoseComparison.Compare(Rose(), Rose((0, 1d)), byLength: true, null, 5d).ShouldBeNull();
        RoseComparison.Compare(Rose(), Rose(), byLength: true, null, null).ShouldBeNull();
    }

    [Fact]
    public void Roses_binned_over_different_sectors_are_refused_rather_than_compared()
    {
        var coarse = new List<OrientationBin> { new(0d, 90d, 1, 1d, 1d, 1d) };

        Should.Throw<ArgumentException>(
            () => RoseComparison.Compare(Rose((0, 1d)), coarse, byLength: true, null, null));
    }

    [Theory]
    [InlineData(10d, 20d, 10d)]
    [InlineData(170d, 10d, 20d)]
    [InlineData(5d, 95d, 90d)]
    [InlineData(179d, 1d, 2d)]
    public void The_separation_between_two_axes_is_the_acute_one(double a, double b, double expected)
    {
        RoseComparison.MeanSeparation(a, b)!.Value.ShouldBe(expected, 1e-9);
    }

    [Fact]
    public void A_missing_mean_axis_leaves_the_separation_unstated()
    {
        RoseComparison.MeanSeparation(null, 10d).ShouldBeNull();
        RoseComparison.MeanSeparation(10d, null).ShouldBeNull();
    }

    /// <summary>
    /// A full eighteen-sector rose with the given shares in the given sectors. Both fractions are
    /// set to the same share so a fixture can be read by either weighting without restating it.
    /// </summary>
    private static List<OrientationBin> Rose(params (int Sector, double Share)[] shares)
    {
        var bins = new List<OrientationBin>();
        for (var i = 0; i < OrientationStatistics.BinCount; i++)
        {
            var share = 0d;
            foreach (var (sector, value) in shares)
            {
                if (sector == i)
                {
                    share += value;
                }
            }

            bins.Add(new OrientationBin(
                i * OrientationStatistics.BinWidthDegrees,
                (i * OrientationStatistics.BinWidthDegrees) + OrientationStatistics.BinWidthDegrees,
                (int)Math.Round(share * 100d),
                share * 100d,
                share,
                share));
        }

        return bins;
    }
}
