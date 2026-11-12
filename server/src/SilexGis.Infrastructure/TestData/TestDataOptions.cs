// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// The deployment's half of the test-data generator's switch, and its limits. The administrators'
/// half is the <c>testData</c> settings section; the generator works only where both are on.
/// </summary>
public sealed class TestDataOptions
{
    public const string SectionName = "TestData";

    /// <summary>Whether this deployment offers the generator at all. On by default.</summary>
    public bool Allowed { get; set; } = true;

    /// <summary>The most caves one run may ask for on this deployment.</summary>
    public int MaxCaves { get; set; } = 20_000;
}
