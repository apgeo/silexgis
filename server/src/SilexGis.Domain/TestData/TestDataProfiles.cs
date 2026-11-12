// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>The profiles the generator ships with, by name.</summary>
public static class TestDataProfiles
{
    public const string SmallName = "small";

    public const string DefaultName = "default";

    public const string LargeName = "large";

    /// <summary>A few dozen caves: for development, a quick look, and the tests.</summary>
    public static TestDataProfile Small { get; } = new() { Caves = new TestDataCaveProfile { Count = 25 } };

    /// <summary>The 500 caves the generator is sized for.</summary>
    public static TestDataProfile Default { get; } = new();

    /// <summary>Ten times the default, for measuring how the application copes with volume.</summary>
    public static TestDataProfile Large { get; } = new() { Caves = new TestDataCaveProfile { Count = 5000 } };

    public static IReadOnlyDictionary<string, TestDataProfile> All { get; } = new Dictionary<string, TestDataProfile>
    {
        [SmallName] = Small,
        [DefaultName] = Default,
        [LargeName] = Large,
    };
}
