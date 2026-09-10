// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Statistics;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The two distribution fits, checked against the truth rather than against themselves.
///
/// <para>
/// Every sample below is drawn from a distribution whose parameters are written down in the test,
/// so an assertion is a comparison with a known answer and not with whatever the implementation
/// happened to produce the first time it ran. The draws are deterministic — a fixed seed and, for
/// the power law, an exact inverse of its cumulative distribution — so a failure is a defect and
/// never a run of bad luck.
/// </para>
/// <para>
/// The tolerances are set from the sampling error of the estimator itself, not from taste. The
/// logarithmic mean of n draws has standard error sigma/sqrt(n); the power-law exponent has
/// (alpha-1)/sqrt(n). Each tolerance below is several of those, so a correct implementation passes
/// with room and a wrong one does not creep through.
/// </para>
/// </summary>
public class DistributionFitTests
{
    [Fact]
    public void The_lognormal_fit_recovers_the_parameters_the_sample_was_drawn_from()
    {
        const double mu = 1.9;
        const double sigma = 0.8;
        const int count = 200_000;

        var sample = Lognormal(mu, sigma, count, seed: 20260908);

        var fit = DistributionFits.FitLognormal(sample);

        fit.ShouldNotBeNull();
        fit.Count.ShouldBe(count);

        // Standard error of the logarithmic mean is 0.8/sqrt(200000) ≈ 0.0018; ten of those.
        fit.Mu.ShouldBe(mu, 0.02);
        fit.Sigma.ShouldBe(sigma, 0.02);

        // The fitted median and mean are the parameters restated, and a skewed distribution puts
        // them well apart — which is the whole reason the pair is published rather than one mean.
        fit.Median.ShouldBe(Math.Exp(mu), Math.Exp(mu) * 0.03);
        fit.Mean.ShouldBe(Math.Exp(mu + (sigma * sigma / 2)), Math.Exp(mu + (sigma * sigma / 2)) * 0.03);
        fit.Mean.ShouldBeGreaterThan(fit.Median);
    }

    [Fact]
    public void The_lognormal_fit_is_refused_below_a_sample_it_could_describe()
    {
        // Seven values, one under the floor of eight, and deliberately a well-behaved sample: the
        // refusal is about how much can be claimed from a handful of caves, not about bad data.
        var sample = new double[] { 1, 2, 3, 5, 8, 13, 21 };

        DistributionFits.FitLognormal(sample).ShouldBeNull();

        // The same values plus one more are enough, so the refusal above is the floor and not a
        // fixture that could not be fitted at all.
        DistributionFits.FitLognormal([.. sample, 34d]).ShouldNotBeNull();
    }

    [Fact]
    public void Values_that_have_no_logarithm_are_dropped_rather_than_moved()
    {
        var drawn = Lognormal(1.0, 0.6, 4_000, seed: 7);
        var withUnmeasured = new List<double>(drawn);
        for (var i = 0; i < 1_000; i++)
        {
            withUnmeasured.Add(0);
        }

        var clean = DistributionFits.FitLognormal(drawn);
        var polluted = DistributionFits.FitLognormal(withUnmeasured);

        clean.ShouldNotBeNull();
        polluted.ShouldNotBeNull();

        // The count states what the fit was actually taken over, and the parameters are unmoved:
        // a thousand caves recorded as zero metres long neither shorten the registry nor widen it.
        polluted.Count.ShouldBe(clean.Count);
        polluted.Mu.ShouldBe(clean.Mu, 1e-12);
        polluted.Sigma.ShouldBe(clean.Sigma, 1e-12);
    }

    [Fact]
    public void The_power_law_fit_recovers_the_exponent_the_tail_was_drawn_from()
    {
        const double alpha = 2.5;
        const double lowerBound = 40;
        const int count = 50_000;

        var sample = Pareto(alpha, lowerBound, count, seed: 4242);

        var fit = DistributionFits.FitParetoTail(sample);

        fit.ShouldNotBeNull();

        // Standard error of the exponent is (alpha-1)/sqrt(n) ≈ 0.0067; six of those.
        fit.Alpha.ShouldBe(alpha, 0.04);

        // Every value in this sample is drawn from the power law, so the search should keep almost
        // all of it rather than discarding the body it has no reason to distrust.
        fit.TailCount.ShouldBeGreaterThan((int)(count * 0.9));
        fit.LowerBound.ShouldBeLessThan(lowerBound * 1.2);

        // The published standard error is the formula, computed over the tail that was kept.
        fit.AlphaStandardError.ShouldBe((fit.Alpha - 1) / Math.Sqrt(fit.TailCount), 1e-12);

        // A curve fitted to data drawn from it stands very close to it. The largest gap for fifty
        // thousand draws is of the order of one over the square root of that.
        fit.KolmogorovSmirnov.ShouldBeLessThan(0.02);
    }

    [Fact]
    public void The_search_finds_where_a_power_law_tail_begins_under_a_body_that_is_not_one()
    {
        // A registry's shape: a great many ordinary caves that are nothing like a power law, and a
        // tail of long ones that is. The fit should describe the tail and leave the body alone.
        const double alpha = 2.2;
        const double crossover = 500;

        var sample = new List<double>();
        sample.AddRange(Uniform(5, 60, 20_000, seed: 99));
        sample.AddRange(Pareto(alpha, crossover, 8_000, seed: 1234));

        var fit = DistributionFits.FitParetoTail(sample);

        fit.ShouldNotBeNull();

        // The bound lands in the tail, not in the body: everything below the crossover is uniform
        // and a bound placed there would be fitting a straight line to a rectangle.
        fit.LowerBound.ShouldBeGreaterThan(60);
        fit.Alpha.ShouldBe(alpha, 0.15);
        fit.TailCount.ShouldBeInRange(4_000, 8_000);
    }

    [Fact]
    public void The_goodness_statistic_tells_a_tail_that_is_a_power_law_from_one_that_is_not()
    {
        var powerLaw = DistributionFits.FitParetoTail(Pareto(2.5, 10, 20_000, seed: 5));

        // The worst case for a power law: every observation the same distance apart, so the
        // cumulative distribution is a straight line where the fit expects a curve. There is no
        // lower bound anywhere in it that a power law describes well.
        var straight = new List<double>();
        for (var i = 0; i < 20_000; i++)
        {
            straight.Add(10 + i);
        }

        var uniform = DistributionFits.FitParetoTail(straight);

        powerLaw.ShouldNotBeNull();
        uniform.ShouldNotBeNull();
        powerLaw.KolmogorovSmirnov.ShouldBeLessThan(0.02);
        uniform.KolmogorovSmirnov.ShouldBeGreaterThan(powerLaw.KolmogorovSmirnov * 3);
    }

    [Fact]
    public void The_power_law_fit_is_refused_below_a_tail_it_could_describe()
    {
        var eleven = Pareto(2.5, 1, 11, seed: 3);
        DistributionFits.FitParetoTail(eleven).ShouldBeNull();

        // Twelve of the same draws are enough, so what is refused above is the floor.
        DistributionFits.FitParetoTail(Pareto(2.5, 1, 12, seed: 3)).ShouldNotBeNull();
    }

    /// <summary>
    /// Draws from a power law exactly, by inverting its cumulative distribution: if u is uniform on
    /// (0,1] then <c>xmin · u^(-1/alpha)</c> is Pareto with that exponent. No rejection, no
    /// approximation — the sample is the distribution.
    /// </summary>
    private static List<double> Pareto(double alpha, double lowerBound, int count, int seed)
    {
        var random = new Random(seed);
        var values = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var u = 1 - random.NextDouble();
            values.Add(lowerBound * Math.Pow(u, -1 / (alpha - 1)));
        }

        return values;
    }

    /// <summary>
    /// Draws from a lognormal by drawing standard normals with the Box–Muller transform and
    /// exponentiating: exp(mu + sigma·z) is lognormal by definition.
    /// </summary>
    private static List<double> Lognormal(double mu, double sigma, int count, int seed)
    {
        var random = new Random(seed);
        var values = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var u1 = 1 - random.NextDouble();
            var u2 = random.NextDouble();
            var z = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            values.Add(Math.Exp(mu + (sigma * z)));
        }

        return values;
    }

    private static List<double> Uniform(double low, double high, int count, int seed)
    {
        var random = new Random(seed);
        var values = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            values.Add(low + (random.NextDouble() * (high - low)));
        }

        return values;
    }
}
