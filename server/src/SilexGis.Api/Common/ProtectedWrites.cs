// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Permissions;

namespace SilexGis.Api.Common;

/// <summary>
/// Which of a page of protected rows this caller may change: one fetch of the facts for the
/// whole set, then the pure evaluator over each, so the cost of deciding a page does not grow
/// with the page.
/// </summary>
/// <remarks>
/// Shared rather than per-surface because more than one kind of answer now depends on it — a
/// listing that says which of its rows the caller may curate, and a record that holds back a
/// part of itself from anyone who may only read it. Asking the same question two ways is how
/// two surfaces come to disagree about the same row.
/// </remarks>
internal static class ProtectedWrites
{
    public static async Task<HashSet<Guid>> WritableAsync<T>(
        IAccessService access, AccessContext ctx, IReadOnlyList<T> rows, CancellationToken ct)
        where T : IProtectedEntity
    {
        if (rows.Count == 0)
        {
            return [];
        }

        if (ctx.IsFullAdmin)
        {
            return [.. rows.Select(r => r.Id)];
        }

        var facts = await access.FactsOfManyAsync([.. rows.Cast<IProtectedEntity>()], ct);
        return
        [
            .. rows
                .Where(r => AccessEvaluator.Decide(
                    ctx, AccessDomains.Of(r), AccessAction.Write, facts[r.Id]).Allowed)
                .Select(r => r.Id),
        ];
    }
}
