// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using SilexGis.Domain;

namespace SilexGis.Infrastructure.Identity;

/// <summary>
/// Application user — ASP.NET Identity with uuid v7 keys plus profile fields
/// (02-data-model.md §1, ADR-018). Lives in Infrastructure so Domain stays framework-free;
/// domain code references users by Guid only.
/// </summary>
public class SilexGisUser : IdentityUser<Guid>, ITimestamped
{
    public SilexGisUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string? DisplayName { get; set; }

    public string? Bio { get; set; }

    public string Locale { get; set; } = "en";

    public Guid? AvatarFileId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
