// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Statistics;

/// <summary>
/// The tail probabilities of the standard normal distribution, in one place because more than one
/// statistic in this project compares an observed figure against its expectation in units of
/// standard error and has to say how unlikely that departure is. Two copies of the same
/// approximation would eventually disagree in the last digits, and two p-values that disagree for
/// no stated reason are worse than one.
/// </summary>
public static class NormalTail
{
    /// <summary>
    /// Two-sided tail probability of the standard normal, from a rational approximation of the
    /// complementary error function accurate to better than one part in a million — far finer than
    /// any p-value in this project is quoted to.
    /// </summary>
    public static double TwoSided(double z)
    {
        var x = Math.Abs(z) / Math.Sqrt(2d);
        var t = 1d / (1d + (0.5d * x));
        var series = -(x * x) - 1.26551223d + (t * (1.00002368d + (t * (0.37409196d
            + (t * (0.09678418d + (t * (-0.18628806d + (t * (0.27886807d + (t * (-1.13520398d
            + (t * (1.48851587d + (t * (-0.82215223d + (t * 0.17087277d)))))))))))))))));

        return Math.Clamp(t * Math.Exp(series), 0d, 1d);
    }
}
