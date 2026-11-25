// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>
/// Shares turned into exact counts. A profile says "8 % of caves are protected"; drawing that
/// independently per cave gives 6 % one run and 11 % the next, and none at all in a small run. A
/// quota gives the stated share as closely as whole items allow, and at least one item to every
/// case asked for whenever there are enough items to go round — so a small run still shows every
/// restriction.
/// </summary>
public static class TestDataQuota
{
    /// <summary>
    /// <paramref name="count"/> values, each case appearing in proportion to its percent, in a
    /// shuffled order drawn from <paramref name="rng"/>.
    /// </summary>
    public static T[] Allocate<T>(int count, IReadOnlyList<(T Value, double Percent)> cases, TestDataRng rng)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(rng);
        if (count <= 0 || cases.Count == 0)
        {
            return [];
        }

        var total = cases.Sum(c => Math.Max(0, c.Percent));
        if (total <= 0)
        {
            return Enumerable.Repeat(cases[0].Value, count).ToArray();
        }

        // Largest remainder, so the counts add up to exactly the number of items.
        var exact = cases.Select(c => Math.Max(0, c.Percent) / total * count).ToArray();
        var counts = exact.Select(e => (int)Math.Floor(e)).ToArray();
        var left = count - counts.Sum();
        foreach (var i in Enumerable.Range(0, cases.Count).OrderByDescending(i => exact[i] - counts[i]).ThenBy(i => i).Take(left))
        {
            counts[i]++;
        }

        // Every case asked for gets one, taken from the largest, while there are items to spare.
        var asked = Enumerable.Range(0, cases.Count).Where(i => cases[i].Percent > 0).ToList();
        if (count >= asked.Count)
        {
            foreach (var i in asked.Where(i => counts[i] == 0))
            {
                var donor = Enumerable.Range(0, counts.Length).OrderByDescending(j => counts[j]).First();
                counts[donor]--;
                counts[i]++;
            }
        }

        var values = new List<T>(count);
        for (var i = 0; i < cases.Count; i++)
        {
            values.AddRange(Enumerable.Repeat(cases[i].Value, counts[i]));
        }

        // Fisher–Yates with the run's own chance.
        var shuffled = values.ToArray();
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled;
    }

    /// <summary>Which of <paramref name="count"/> items are picked, for one yes/no share.</summary>
    public static bool[] Flags(int count, double percent, TestDataRng rng) =>
        Allocate(count, [(true, percent), (false, 100 - percent)], rng);
}
