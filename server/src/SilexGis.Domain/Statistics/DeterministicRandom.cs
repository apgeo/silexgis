// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Statistics;

/// <summary>
/// A small counter-based generator, written out here rather than taken from the framework so a
/// stated seed means the same numbers on every machine and every runtime version. An envelope or
/// a held-out split whose seed does not reproduce is not a stated seed.
/// </summary>
/// <remarks>
/// A value type that carries its own state: hand it on by reference, or the receiver draws from a
/// copy and the caller repeats the same numbers.
/// </remarks>
public struct DeterministicRandom(int seed)
{
    private ulong state = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL) + 0x9E3779B97F4A7C15UL;

    /// <summary>The next value in [0, 1).</summary>
    public double NextDouble()
    {
        // SplitMix64: one multiply-shift mixing round over an incrementing counter.
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        var z = state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        z ^= z >> 31;

        return (z >> 11) * (1.0 / 9007199254740992.0);
    }

    /// <summary>The next whole number in [0, <paramref name="exclusiveUpper"/>).</summary>
    public int NextBelow(int exclusiveUpper)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpper);

        // The product is below the bound for every value NextDouble can return; the clamp is for
        // the reader, not the arithmetic.
        return Math.Min((int)(NextDouble() * exclusiveUpper), exclusiveUpper - 1);
    }
}
