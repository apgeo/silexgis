// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Puts the shipped detection rules in place, so a first import is useful before anyone has
/// configured anything.
///
/// <para>
/// Seeds once and then leaves the row alone. The shipped set is found by its own flag rather
/// than by name — renaming it is a legitimate edit — and an installation that tuned the
/// defaults to its club's habits must not have that work undone by a restart, which is the
/// single most damaging thing a seeder can do. Later versions of the shipped rules therefore
/// reach an existing installation as a file to import, not as a silent overwrite.
/// </para>
/// </summary>
public static class TermRuleSeeder
{
    public static async Task SeedAsync(SilexGisDbContext db, CancellationToken ct = default)
    {
        if (await db.TermRuleSets.AnyAsync(s => s.IsSeeded, ct))
        {
            return;
        }

        db.TermRuleSets.Add(new TermRuleSet
        {
            Name = TermRuleSeeds.DefaultSetName,
            Description = TermRuleSeeds.DefaultDocument.Description,
            Scope = TermRuleScope.Installation,
            // Nobody wrote it, so nobody owns it: it is part of what the installation ships.
            OwnerUserId = null,
            // The installation default until an administrator promotes another one, at which
            // point this row stays as the fallback that cannot be deleted.
            IsDefault = !await db.TermRuleSets.AnyAsync(s => s.IsDefault && s.Scope == TermRuleScope.Installation, ct),
            IsSeeded = true,
            Rules = ImportJson.Serialize(TermRuleSeeds.DefaultDocument),
        });

        await db.SaveChangesAsync(ct);
    }
}
