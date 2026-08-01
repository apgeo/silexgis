// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>Role of a user inside a caving group. Stored values — do not renumber.</summary>
public enum CavingGroupRole : short
{
    Member = 0,
    Admin = 1,
    Owner = 2,
}
