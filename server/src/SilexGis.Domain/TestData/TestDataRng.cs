// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace SilexGis.Domain.TestData;

/// <summary>
/// The generator's source of chance: SplitMix64, seeded per section and per item.
/// </summary>
/// <remarks>
/// <para>
/// Written out rather than taken from <see cref="Random"/>, whose sequence for a given seed is not
/// promised to stay the same between runtime versions — and "the same seed gives the same
/// dataset" is a promise this generator makes to whoever repeats a run a year later.
/// </para>
/// <para>
/// One stream per item (<see cref="For(long,string,long)"/>) rather than one stream for a whole
/// run: a section that draws one number more than it used to, or an item that is skipped, then
/// changes only itself and never shifts every item after it.
/// </para>
/// </remarks>
public sealed class TestDataRng
{
    private ulong state;

    private TestDataRng(ulong state) => this.state = state;

    /// <summary>The stream for item <paramref name="index"/> of section <paramref name="section"/>.</summary>
    public static TestDataRng For(long seed, string section, long index = 0)
    {
        var mixed = Mix(unchecked((ulong)seed) ^ Fnv1a(section));
        mixed = Mix(mixed ^ unchecked((ulong)index * 0x9E3779B97F4A7C15UL));
        return new TestDataRng(mixed);
    }

    public ulong NextULong()
    {
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        return Mix(state);
    }

    /// <summary>A number in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>An integer in [0, <paramref name="maxExclusive"/>).</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)(NextDouble() * maxExclusive);
    }

    /// <summary>An integer in [<paramref name="min"/>, <paramref name="maxInclusive"/>].</summary>
    public int Between(int min, int maxInclusive) => min + Next(maxInclusive - min + 1);

    public double Between(double min, double max) => min + (NextDouble() * (max - min));

    public bool Chance(double probability) => NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items[Next(items.Count)];
    }

    /// <summary>A key drawn with chances proportional to its weight; ordinal key order keeps it stable.</summary>
    public string Weighted(IReadOnlyDictionary<string, int> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var ordered = weights.Where(w => w.Value > 0).OrderBy(w => w.Key, StringComparer.Ordinal).ToList();
        var total = ordered.Sum(w => w.Value);
        var roll = NextDouble() * total;
        foreach (var (key, weight) in ordered)
        {
            roll -= weight;
            if (roll < 0)
            {
                return key;
            }
        }

        return ordered[^1].Key;
    }

    /// <summary>A standard normal draw (Box–Muller).</summary>
    public double Normal()
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>A log-normal draw with the given median, clipped to [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public double LogNormal(double median, double sigma, double min, double max) =>
        Math.Clamp(median * Math.Exp(sigma * Normal()), min, max);

    private static ulong Mix(ulong z)
    {
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    private static ulong Fnv1a(string text)
    {
        var hash = 0xCBF29CE484222325UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash = unchecked((hash ^ b) * 0x100000001B3UL);
        }

        return hash;
    }
}
