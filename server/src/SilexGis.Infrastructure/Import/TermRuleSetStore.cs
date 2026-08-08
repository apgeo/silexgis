// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// Reads rule sets and answers which one applies. The scope walk itself is a Domain rule
/// (<see cref="TermRuleSetRules.Resolve"/>); this loads the rows it decides between.
/// </summary>
public sealed class TermRuleSetStore(SilexGisDbContext db)
{
    /// <summary>
    /// The rules of a set, or an empty list if its document cannot be read. A set is a row of
    /// jsonb that an import, a file upload and a hand edit can all have written, so a preview
    /// against a mangled one must come back proposing nothing rather than failing — the screen
    /// then shows a set that claims no candidates, which is a diagnosable state.
    /// </summary>
    public static IReadOnlyList<TermRule> RulesOf(TermRuleSet? set)
    {
        if (set is null || string.IsNullOrWhiteSpace(set.Rules))
        {
            return [];
        }

        try
        {
            return ImportJson.Deserialize<TermRuleDocument>(set.Rules)?.Rules ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The set that would run for this caller if they named none.</summary>
    public async Task<TermRuleSet?> ResolveDefaultAsync(AccessContext ctx, CancellationToken ct = default)
    {
        // Only the rows that can win: a default at some scope, or the shipped fallback.
        var candidates = await db.TermRuleSets.AsNoTracking()
            .Where(s => s.IsDefault || s.IsSeeded)
            .ToListAsync(ct);
        return TermRuleSetRules.Resolve(candidates, ctx.UserId, ctx.CavingGroupIds);
    }

    /// <summary>
    /// The set an import should run: the one named, else the caller's default. A named set
    /// that no longer exists comes back null, and the caller reports it rather than quietly
    /// running somebody else's rules.
    /// </summary>
    public async Task<TermRuleSet?> ResolveAsync(AccessContext ctx, Guid? setId, CancellationToken ct = default)
    {
        if (setId is null)
        {
            return await ResolveDefaultAsync(ctx, ct);
        }

        return await db.TermRuleSets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == setId, ct);
    }
}
