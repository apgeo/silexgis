// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.ResLinks;

/// <summary>One shipped relation-type row. <paramref name="Name"/> reads from the main
/// member towards the rest; <paramref name="InverseName"/> reads back from the non-main
/// side and only directed relations carry one.</summary>
public readonly record struct ResLinkRelationTypeSeed(
    string Code, string Name, bool Directed, string? InverseName);

/// <summary>
/// The shipped relation vocabulary — the single home for which codes are the exchange
/// vocabulary. The startup seeder inserts these rows, and the admin surface consults the
/// same list to refuse code changes and deletion on them: seeded codes are what clients
/// translate labels by and what installations exchange, so a renamed or deleted code
/// would silently break both. Custom rows are installation-local and fully editable.
/// </summary>
public static class ResLinkRelationTypeSeeds
{
    public static readonly IReadOnlyList<ResLinkRelationTypeSeed> All =
    [
        new("same-object", "Same object as", false, null),
        new("related-to", "Related to", false, null),
        new("contains", "Contains", true, "Contained in"),
        new("documents", "Documented by", true, "Documents"),
        new("derived-from", "Source of", true, "Derived from"),
        new("adjacent-to", "Adjacent to", false, null),
        new("duplicate-of", "Original of", true, "Duplicate of"),
        new("needs-clarification", "Needs clarification", false, null),
    ];

    private static readonly HashSet<string> Codes = [.. All.Select(s => s.Code)];

    /// <summary>Whether a code names a shipped row (code and directedness immutable,
    /// row undeletable).</summary>
    public static bool IsSeeded(string code) => Codes.Contains(code);
}
