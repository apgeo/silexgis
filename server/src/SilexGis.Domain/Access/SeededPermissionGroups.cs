// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// The well-known seeded permission groups. Identified by slug — protected groups
/// cannot be renamed, so the slugs are stable anchors for the resolver (Full
/// Administrators short-circuit, the implicit All Users membership) and the guards.
/// Everything else about the seeds is ordinary, editable data.
/// </summary>
public static class SeededPermissionGroups
{
    /// <summary>The escape hatch: entry-less and protected — membership IS the grant,
    /// which is what makes it unreachable by deny and immune to entry edits. The
    /// installation must always keep at least one active, unlocked member.</summary>
    public const string FullAdministratorsSlug = "full-administrators";

    public const string FullAdministratorsName = "Full Administrators";

    /// <summary>Implicit member: every account — the resolver always includes it, no
    /// membership rows exist. Carries the baseline authenticated catalog reads.</summary>
    public const string AllUsersSlug = "all-users";

    public const string AllUsersName = "All Users";

    public const string AdministratorsSlug = "administrators";

    public const string AdministratorsName = "Administrators";

    public const string CavingGroupManagersSlug = "caving-group-managers";

    public const string CavingGroupManagersName = "Caving Group Managers";

    public const string EditorsSlug = "editors";

    public const string EditorsName = "Editors";

    public const string ReviewersSlug = "reviewers";

    public const string ReviewersName = "Reviewers";
}
