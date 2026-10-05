// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Seeds the one checklist the product ships, owned by the installation's administrator and
/// published to every account.
/// </summary>
/// <remarks>
/// An ordinary list and not a special row: a published default is that same shape with a wide
/// audience. Guarded on the list as a whole and not on its lines, deliberately unlike the demo
/// dataset: the list is the administrator's to adapt, and a seeder that put a deleted line back
/// would overrule an edit it cannot tell from a loss. Deleting the whole list is the way to get the
/// shipped one back on the next start.
/// </remarks>
public static class ChecklistSeeder
{
    public static async Task SeedAsync(SilexGisDbContext db, Guid adminUserId, CancellationToken ct = default)
    {
        if (await db.Checklists.AnyAsync(c => c.Title == ChecklistSeeds.DefaultTitle, ct))
        {
            return;
        }

        var list = new Checklist
        {
            Title = ChecklistSeeds.DefaultTitle,
            Description = ChecklistSeeds.DefaultDescription,
            OwnerUserId = adminUserId,
            Visibility = Visibility.Authenticated,
        };
        db.Checklists.Add(list);

        var order = 0;
        foreach (var text in ChecklistSeeds.DefaultItems)
        {
            db.ChecklistItems.Add(new ChecklistItem { ChecklistId = list.Id, Text = text, SortOrder = order++ });
        }

        await db.SaveChangesAsync(ct);
    }
}
