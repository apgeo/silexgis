// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Expeditions;

/// <summary>One shipped camp-roster role: the code installations exchange it by, and the English
/// name a client without wording of its own falls back to.</summary>
public readonly record struct ExpeditionRosterRoleSeed(string Code, string Name);

/// <summary>
/// The shipped vocabulary of what somebody was at a camp as — the single home for which codes
/// ship. The startup seeder inserts these rows, and the admin surface consults the same list to
/// refuse code changes and deletion on them: shipped codes are what clients translate labels by
/// and what installations exchange records under, so a renamed or deleted code would silently
/// break both. Rows a club adds are installation-local and fully editable.
/// <para>
/// Its own vocabulary rather than the one a trip's people are recorded under, because being at a
/// camp is not a job underground. Cooking for thirty people and keeping the base camp are the
/// reason a camp's roster exists at all — the person who never went below was still there for the
/// fortnight — and putting them among the trip jobs would offer them on the trip form, where they
/// mean nothing.
/// </para>
/// </summary>
public static class ExpeditionRosterRoleSeeds
{
    /// <summary>
    /// Having been at the camp at all. The role a name carries when nothing else is said, so it
    /// must exist for a camp to record anybody's presence.
    /// </summary>
    public const string MemberCode = "member";

    /// <summary>
    /// Append only. The seeder derives a row's sort order from its position here and never
    /// re-sorts a row that already exists, so a code inserted in the middle takes a different
    /// sort order on a fresh installation than on one being upgraded — silently, and only the
    /// vocabulary's display order gives it away.
    /// </summary>
    public static readonly IReadOnlyList<ExpeditionRosterRoleSeed> All =
    [
        new(MemberCode, "Member"),
        new("organiser", "Organiser"),
        new("cook", "Cook"),
        new("base_camp", "Base camp"),
        new("driver", "Driver"),
        new("medic", "Medic"),
        new("equipment", "Equipment"),
        new("guest", "Guest"),
    ];

    private static readonly HashSet<string> Codes = [.. All.Select(s => s.Code)];

    /// <summary>Whether a code names a shipped row (code immutable, row undeletable).</summary>
    public static bool IsSeeded(string code) => Codes.Contains(code);
}
