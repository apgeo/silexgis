// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.TestData;

/// <summary>
/// The shipped vocabulary codes the generator draws from. They are the seeded rows' codes; the
/// generator looks each one up by code when it runs, so a renamed or missing one fails the run by
/// name rather than writing a row against the wrong entry.
/// </summary>
public static class TestDataVocabulary
{
    public static IReadOnlySet<string> CaveTypes { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "cave", "pit", "spring_cave", "mine", "rock_shelter" };

    /// <summary>Entrance types by weight for a natural cave; a mine's entrances are artificial.</summary>
    public static IReadOnlyDictionary<string, int> NaturalEntranceWeights { get; } = new Dictionary<string, int>
    {
        ["natural"] = 85,
        ["excavated"] = 8,
        ["collapsed"] = 5,
        ["artificial"] = 2,
    };

    public const string ArtificialEntrance = "artificial";

    public const string MassifType = "massif";

    public const string KarstAreaType = "karst_area";

    /// <summary>Geological ages a karst rock in these regions is plausibly given.</summary>
    public static IReadOnlyList<string> RockAgesRo { get; } =
    [
        "Jurasic superior", "Jurasic mediu", "Cretacic inferior", "Triasic mediu", "Triasic superior",
        "Barremian–Aptian", "Tithonic–Neocomian", "Paleogen", "Devonian (marmură)",
    ];

    /// <summary>Romanian cave protection classes as a register writes them.</summary>
    public static IReadOnlyList<string> ProtectionClasses { get; } = ["A", "B", "C", "D"];
}
