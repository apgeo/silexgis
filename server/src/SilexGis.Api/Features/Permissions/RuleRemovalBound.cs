// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

/// <summary>
/// The permission area's answer to the removal bound: the same check a list replace, a withdrawn
/// camp sharing or a deleted permission group runs before it takes a deny away.
/// </summary>
public sealed class RuleRemovalBound(SilexGisDbContext db) : IRuleRemovalBound
{
    public Task<ProblemHttpResult?> RefuseRemovalAsync(
        AccessContext caller, IReadOnlyCollection<AccessEntry> removed, CancellationToken ct) =>
        removed.Count == 0
            ? Task.FromResult<ProblemHttpResult?>(null)
            : AccessEntryMapping.RejectRemovalsAsync(db, caller, removed, ct);
}
