// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Statistics;

/// <summary>
/// A lognormal fitted to a sample by maximum likelihood: the parameters of the normal distribution
/// the logarithms follow.
/// </summary>
/// <param name="Mu">Mean of the natural logarithms.</param>
/// <param name="Sigma">Standard deviation of the natural logarithms, taken about the fitted mean
/// with the maximum-likelihood divisor rather than the unbiased one — the two differ by a factor of
/// n/(n-1) and only the first is the maximum-likelihood answer.</param>
/// <param name="Count">Observations the fit was taken over: strictly positive values only.</param>
/// <param name="Median">The fitted median, <c>exp(mu)</c>. The value a reader recognises, because a
/// lognormal's median is where half the caves fall and its mean is not.</param>
/// <param name="Mean">The fitted arithmetic mean, <c>exp(mu + sigma²/2)</c>, which for a skewed
/// distribution sits well above the median and above most of the sample.</param>
public sealed record LognormalFit(double Mu, double Sigma, int Count, double Median, double Mean);

/// <summary>
/// A power law fitted to the upper tail of a sample: the exponent, the lower bound the tail was
/// taken from, and how far the fitted curve stands from the data it was fitted to.
/// </summary>
/// <param name="Alpha">The exponent. Larger means a tail that falls away faster.</param>
/// <param name="LowerBound">The value the tail begins at, chosen rather than assumed.</param>
/// <param name="TailCount">Observations at or above that bound.</param>
/// <param name="AlphaStandardError">The exponent's standard error, <c>(alpha-1)/sqrt(n)</c>. A fit
/// whose error is a large fraction of the exponent is a fit over too few observations, and reading
/// the exponent without it invites a claim the data does not support.</param>
/// <param name="KolmogorovSmirnov">The largest gap between the sample's own cumulative distribution
/// and the fitted one, over the tail. It is the quantity the lower bound was chosen to minimise, so
/// it is a goodness statistic and not a test: a small value says the power law describes the tail
/// well, and says nothing about whether some other curve would describe it better.</param>
public sealed record ParetoTailFit(
    double Alpha,
    double LowerBound,
    int TailCount,
    double AlphaStandardError,
    double KolmogorovSmirnov);

/// <summary>
/// The two distribution fits a registry of measured caves is read through, written out as
/// arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// Both are closed forms — a mean and a variance of logarithms for the first, a sum of logarithms
/// for the second — so neither needs an optimiser, a special function, or a numerical library. That
/// is why they are here rather than behind a dependency: the whole of both fits is thirty lines of
/// floating-point arithmetic, and a library would be a package to keep current in exchange for
/// nothing.
/// </para>
/// <para>
/// <b>Why these two.</b> Cave lengths and depths are not symmetric about their mean and do not go
/// negative: most caves in any registry are short, a few are very long, and the long ones carry a
/// disproportionate share of the total passage. A lognormal describes that whole shape in two
/// numbers. A power law describes only the upper tail, but describes it better than the lognormal
/// does — and the exponent of that tail is the figure a karstologist compares between regions.
/// Fitting both, and reporting where the tail was judged to begin, says more than either alone.
/// </para>
/// <para>
/// <b>Zero and negative values are dropped, never clamped.</b> The logarithm of a length that was
/// recorded as zero does not exist, and substituting a small number for it would invent an
/// observation. The count each fit reports is the count it was actually taken over, which is how a
/// reader sees that a third of the registry had nothing to contribute.
/// </para>
/// </remarks>
public static class DistributionFits
{
    /// <summary>
    /// Fewest observations a fit is offered for. Below it the parameters exist arithmetically and
    /// mean nothing: two points determine a lognormal exactly and the answer is a restatement of
    /// the two points rather than a description of a population.
    /// </summary>
    public const int MinimumSampleCount = 8;

    /// <summary>
    /// Fewest observations that may stand in a fitted tail. The published method suggests fifty for
    /// a confident exponent; a cave registry of a few hundred caves would then never obtain one at
    /// all, so the floor here is lower and the standard error is published beside the exponent so a
    /// thin tail declares itself instead of hiding.
    /// </summary>
    public const int MinimumTailCount = 12;

    /// <summary>
    /// How many lower bounds the tail search tries. The search is over a curve that varies smoothly
    /// with the bound, so trying every distinct observation costs time proportional to the square of
    /// the sample and buys a bound a fraction of a bin different from the one a thinned scan finds.
    /// This runs inside a web request over a whole registry, which is what settles it.
    /// </summary>
    public const int LowerBoundCandidates = 128;

    /// <summary>
    /// The lognormal maximum-likelihood fit: the mean and the spread of the logarithms.
    /// </summary>
    /// <returns>
    /// Null when fewer than <see cref="MinimumSampleCount"/> strictly positive values were given,
    /// or when every value is identical and the fitted spread would be zero — a distribution with
    /// no spread is a single value repeated, and calling it a lognormal would be a claim about a
    /// population made from one observation.
    /// </returns>
    public static LognormalFit? FitLognormal(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var logs = new List<double>(values.Count);
        foreach (var value in values)
        {
            if (value > 0 && double.IsFinite(value))
            {
                logs.Add(Math.Log(value));
            }
        }

        if (logs.Count < MinimumSampleCount)
        {
            return null;
        }

        var mu = 0d;
        foreach (var log in logs)
        {
            mu += log;
        }

        mu /= logs.Count;

        var variance = 0d;
        foreach (var log in logs)
        {
            var deviation = log - mu;
            variance += deviation * deviation;
        }

        variance /= logs.Count;
        var sigma = Math.Sqrt(variance);

        return sigma <= 0
            ? null
            : new LognormalFit(
                mu, sigma, logs.Count, Math.Exp(mu), Math.Exp(mu + (sigma * sigma / 2)));
    }

    /// <summary>
    /// The power-law fit to the upper tail, with the lower bound chosen rather than assumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a fixed lower bound the exponent has a closed form —
    /// <c>alpha = 1 + n / Σ ln(x_i / xmin)</c> — so the only thing to search over is the bound
    /// itself. Each candidate bound is scored by the largest gap between the sample's own
    /// cumulative distribution above it and the cumulative distribution the fitted exponent
    /// implies, and the bound with the smallest gap wins. That is the standard procedure and it is
    /// what stops the answer being "the tail begins wherever I say it does".
    /// </para>
    /// <para>
    /// The gap is measured with both one-sided differences at each observation, because the
    /// empirical distribution steps at the observation while the fitted one is continuous through
    /// it; taking only the difference after the step understates the gap by exactly one step
    /// height and reports a fit that is better than it is.
    /// </para>
    /// </remarks>
    /// <returns>
    /// Null when fewer than <see cref="MinimumTailCount"/> strictly positive values were given, or
    /// when no candidate bound leaves a usable tail.
    /// </returns>
    public static ParetoTailFit? FitParetoTail(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var sorted = new List<double>(values.Count);
        foreach (var value in values)
        {
            if (value > 0 && double.IsFinite(value))
            {
                sorted.Add(value);
            }
        }

        if (sorted.Count < MinimumTailCount)
        {
            return null;
        }

        sorted.Sort();

        // Suffix sums of the logarithms, so the exponent at any candidate bound is arithmetic
        // rather than another pass over the tail.
        var logSuffix = new double[sorted.Count + 1];
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            logSuffix[i] = logSuffix[i + 1] + Math.Log(sorted[i]);
        }

        // A candidate bound must leave a tail of at least the floor, and it must not sit on a value
        // repeated up to the end of the sample: a tail whose every member equals its own lower
        // bound has no spread to fit.
        var lastStart = sorted.Count - MinimumTailCount;
        if (lastStart < 0)
        {
            return null;
        }

        ParetoTailFit? best = null;
        var step = Math.Max(1, (lastStart + 1) / LowerBoundCandidates);

        for (var start = 0; start <= lastStart; start += step)
        {
            // Repeated values: a bound must be the first occurrence of its value, or the tail would
            // begin part-way through a run of equal observations and the empirical distribution
            // would be evaluated at a point it does not step at.
            if (start > 0 && sorted[start] == sorted[start - 1])
            {
                continue;
            }

            var candidate = Score(sorted, logSuffix, start);
            if (candidate is not null
                && (best is null || candidate.KolmogorovSmirnov < best.KolmogorovSmirnov))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static ParetoTailFit? Score(List<double> sorted, double[] logSuffix, int start)
    {
        var n = sorted.Count - start;
        var lowerBound = sorted[start];
        var logLowerBound = Math.Log(lowerBound);

        // Σ ln(x_i / xmin) over the tail, from the suffix sums.
        var sumLogRatio = logSuffix[start] - (n * logLowerBound);
        if (sumLogRatio <= 0 || !double.IsFinite(sumLogRatio))
        {
            return null;
        }

        var alpha = 1 + (n / sumLogRatio);

        var maxGap = 0d;
        for (var i = 0; i < n; i++)
        {
            var fitted = 1 - Math.Pow(sorted[start + i] / lowerBound, 1 - alpha);
            var below = (double)i / n;
            var above = (double)(i + 1) / n;
            maxGap = Math.Max(maxGap, Math.Max(Math.Abs(fitted - below), Math.Abs(above - fitted)));
        }

        return new ParetoTailFit(alpha, lowerBound, n, (alpha - 1) / Math.Sqrt(n), maxGap);
    }
}
