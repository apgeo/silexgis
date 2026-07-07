// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>Role of a user inside a team (02-data-model.md §1). Stored values — do not renumber.</summary>
public enum TeamRole : short
{
    Member = 0,
    Admin = 1,
    Owner = 2,
}
