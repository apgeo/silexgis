// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// Per-object permission flags stored in object_acl.permissions (02-data-model.md §7,
/// 05-auth-permissions.md). Values are part of the schema contract — do not renumber.
/// </summary>
[Flags]
public enum ObjectPermission
{
    None = 0,
    Read = 1,
    Write = 2,
    Delete = 4,
    Share = 8,
    ManagePermissions = 16,
    ViewExactLocation = 32,
}
