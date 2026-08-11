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
    /// <summary>
    /// Append only. The seeder derives a row's sort order from its position here and never
    /// re-sorts a row that already exists, so a code inserted in the middle takes a different
    /// sort order on a fresh installation than on one being upgraded — silently, and only the
    /// vocabulary's display order gives it away.
    /// </summary>
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

        // What a trip did to what it names. Directed with the trip as the main member, so the
        // forward name reads out of the trip ("Surveyed") and the inverse reads back from what
        // the trip named ("Surveyed on trip"); undirected roles would be ambiguous the first
        // time two trips ended up in one link. The codes are prefixed because this vocabulary
        // is shared and administrator-extensible — a bare "visited" would read as a general
        // relation between any two things, which it is not.
        new("trip-work-area", "Worked in", true, "Work area of trip"),
        new("trip-objective", "Aimed at", true, "Objective of trip"),
        new("trip-visited", "Visited", true, "Visited on trip"),
        new("trip-surveyed", "Surveyed", true, "Surveyed on trip"),
        new("trip-discovered", "Discovered", true, "Discovered on trip"),
        new("trip-dug", "Dug at", true, "Dug on trip"),
        new("trip-photographed", "Photographed", true, "Photographed on trip"),
        new("trip-searched-not-found", "Searched, not found", true, "Searched for on trip, not found"),
        new("trip-lead", "Left lead", true, "Lead left on trip"),
        new("trip-follows-on-from", "Follows on from", true, "Followed up by"),
    ];

    private static readonly HashSet<string> Codes = [.. All.Select(s => s.Code)];

    /// <summary>Whether a code names a shipped row (code and directedness immutable,
    /// row undeletable).</summary>
    public static bool IsSeeded(string code) => Codes.Contains(code);
}
