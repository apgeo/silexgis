// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.TestData;

/// <summary>
/// What a test-data run is asked to generate. Every member has a default, so an empty document is
/// the default profile; sections the generator does not know yet are ignored when read, so a
/// profile written for a later version still runs here, without the parts this version cannot do.
/// </summary>
public sealed record TestDataProfile
{
    /// <summary>Same seed and same profile, same dataset (names, codes, places, numbers).</summary>
    public long Seed { get; init; } = 20261111;

    /// <summary>The language pack names and texts come from: <c>ro</c> or <c>en</c>.</summary>
    public string Locale { get; init; } = TestDataLocales.Romanian;

    /// <summary>Region codes to place things in; null or empty means the language pack's own set.</summary>
    public IReadOnlyList<string>? Regions { get; init; }

    public TestDataCaveProfile Caves { get; init; } = new();

    public TestDataAccessProfile Access { get; init; } = new();

    public TestDataMarkingProfile Marking { get; init; } = new();

    /// <summary>How profiles are read and written everywhere: web casing, enums by name, no indentation.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static TestDataProfile FromJson(string json) =>
        JsonSerializer.Deserialize<TestDataProfile>(json, JsonOptions)
        ?? throw new JsonException("A test-data profile cannot be null.");
}

public sealed record TestDataCaveProfile
{
    public int Count { get; init; } = 500;

    /// <summary>Relative weights by cave type code; a type left out is never generated.</summary>
    public IReadOnlyDictionary<string, int> TypeWeights { get; init; } = new Dictionary<string, int>
    {
        ["cave"] = 70,
        ["pit"] = 18,
        ["spring_cave"] = 5,
        ["mine"] = 3,
        ["rock_shelter"] = 4,
    };

    /// <summary>
    /// Surveyed length is drawn from a log-normal distribution with this median: most caves are
    /// short and a few are very long, as in any real register.
    /// </summary>
    public double LengthMedianM { get; init; } = 60;

    public double LengthSigma { get; init; } = 1.6;

    public double MaxLengthM { get; init; } = 25_000;

    /// <summary>Share of caves with two or more entrances, in percent.</summary>
    public double MultiEntrancePercent { get; init; } = 15;
}

/// <summary>
/// How much of the generated data is restricted. Most of it is readable by every account; the
/// restricted part is small but never empty, so every restriction path has something to show.
/// </summary>
public sealed record TestDataAccessProfile
{
    public TestDataVisibilityPercent VisibilityPercent { get; init; } = new();

    public double LocationProtectedCavesPercent { get; init; } = 8;

    public double ExactLocationGrantAccountsPercent { get; init; } = 15;

    public double DenyOrObjectGrantPercent { get; init; } = 2;
}

public sealed record TestDataVisibilityPercent
{
    public double Public { get; init; } = 45;

    public double Authenticated { get; init; } = 40;

    public double CavingGroup { get; init; } = 10;

    public double Private { get; init; } = 5;
}

public sealed record TestDataMarkingProfile
{
    /// <summary>The tag every generated object that can carry one is given.</summary>
    public string Tag { get; init; } = "testgenerated";

    /// <summary>Whether generated descriptions end with a line saying they are test data.</summary>
    public bool DescriptionFooter { get; init; } = true;
}

public static class TestDataLocales
{
    public const string Romanian = "ro";

    public const string English = "en";

    public static IReadOnlyList<string> All { get; } = [Romanian, English];
}
