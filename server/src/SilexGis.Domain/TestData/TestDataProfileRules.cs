// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>What makes a profile runnable. Pure; the API and the command line ask the same question.</summary>
public static class TestDataProfileRules
{
    /// <summary>A profile asking for more caves than this is refused whatever the deployment allows.</summary>
    public const int AbsoluteMaxCaves = 50_000;

    /// <summary>The problems with <paramref name="profile"/>, each as a short sentence; empty when it can run.</summary>
    public static IReadOnlyList<string> Problems(TestDataProfile profile, int maxCaves)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var problems = new List<string>();

        if (!TestDataLocales.All.Contains(profile.Locale))
        {
            problems.Add($"locale must be one of: {string.Join(", ", TestDataLocales.All)}.");
        }
        else if (!TestDataRegions.HasPack(profile.Locale))
        {
            problems.Add($"the '{profile.Locale}' language pack is not available yet.");
        }

        if (profile.Regions is { Count: > 0 } regions)
        {
            var unknown = regions.Where(r => TestDataRegions.Find(r) is null).ToList();
            if (unknown.Count > 0)
            {
                problems.Add($"unknown region(s): {string.Join(", ", unknown)}.");
            }
        }

        var caves = profile.Caves;
        var limit = Math.Min(maxCaves, AbsoluteMaxCaves);
        if (caves.Count < 1 || caves.Count > limit)
        {
            problems.Add($"caves.count must be between 1 and {limit}.");
        }

        if (caves.TypeWeights.Count == 0 || caves.TypeWeights.Values.Any(w => w < 0) || caves.TypeWeights.Values.Sum() <= 0)
        {
            problems.Add("caves.typeWeights must name at least one type with a positive weight, and none negative.");
        }

        var unknownTypes = caves.TypeWeights.Keys.Where(k => !TestDataVocabulary.CaveTypes.Contains(k)).ToList();
        if (unknownTypes.Count > 0)
        {
            problems.Add($"unknown cave type(s) in caves.typeWeights: {string.Join(", ", unknownTypes)}.");
        }

        if (caves.LengthMedianM is <= 0 or > 100_000 || caves.LengthSigma is <= 0 or > 5
            || caves.MaxLengthM < caves.LengthMedianM || caves.MaxLengthM > 1_000_000)
        {
            problems.Add("caves length: the median must be positive, sigma between 0 and 5, and the maximum at least the median (at most 1,000 km).");
        }

        Percent(problems, "caves.multiEntrancePercent", caves.MultiEntrancePercent);

        var access = profile.Access;
        var v = access.VisibilityPercent;
        foreach (var (name, value) in new[]
                 {
                     ("public", v.Public), ("authenticated", v.Authenticated),
                     ("cavingGroup", v.CavingGroup), ("private", v.Private),
                 })
        {
            Percent(problems, $"access.visibilityPercent.{name}", value);
        }

        if (Math.Abs(v.Public + v.Authenticated + v.CavingGroup + v.Private - 100) > 0.001)
        {
            problems.Add("access.visibilityPercent must add up to 100.");
        }

        Percent(problems, "access.locationProtectedCavesPercent", access.LocationProtectedCavesPercent);
        Percent(problems, "access.exactLocationGrantAccountsPercent", access.ExactLocationGrantAccountsPercent);
        Percent(problems, "access.denyOrObjectGrantPercent", access.DenyOrObjectGrantPercent);

        var tag = profile.Marking.Tag;
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 64 || Entities.Tag.Slugify(tag).Length == 0)
        {
            problems.Add("marking.tag must be a tag name of 1 to 64 characters with at least one letter or digit.");
        }

        return problems;
    }

    private static void Percent(List<string> problems, string name, double value)
    {
        if (value is < 0 or > 100 || double.IsNaN(value))
        {
            problems.Add($"{name} must be between 0 and 100.");
        }
    }
}
