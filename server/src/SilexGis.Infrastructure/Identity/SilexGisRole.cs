// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;

namespace SilexGis.Infrastructure.Identity;

/// <summary>Global role (Admin/Manager/Editor/Viewer), uuid keys.</summary>
public class SilexGisRole : IdentityRole<Guid>
{
    public SilexGisRole() => Id = Guid.CreateVersion7();

    public SilexGisRole(string roleName)
        : this() => Name = roleName;
}
