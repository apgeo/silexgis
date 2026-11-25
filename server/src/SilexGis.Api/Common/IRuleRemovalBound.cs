// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Common;

/// <summary>
/// The bound on lifting a refusal: may this caller take these rules away?
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> A deny is lifted by removing it, and a removal is a grant reached from the
/// other end: whoever takes a deny away hands its subject back every action it withheld. So it is
/// bounded the way adding an allow is — the remover must hold, at the deny's own anchor, every
/// action the deny withholds. Removing an allow takes nothing from anybody that the remover could
/// not have taken themselves and is never refused here.
/// </para>
/// <para>
/// <b>Why it is an interface here.</b> The bound is the permission model's rule and is judged by the
/// permission area, which owns the facts each anchor carries. Other areas remove rules as a side
/// effect of their own writes — a trip leaving a camp, a camp being deleted, both taking the rules
/// the camp's sharing wrote on its trips — and only ask.
/// </para>
/// </remarks>
public interface IRuleRemovalBound
{
    /// <summary>
    /// The refusal the removal must be answered with (403, code
    /// <c>access_entry.removal_exceeds_own_rights</c>), or null when the caller may remove every
    /// one of the rules. A full administrator is never refused.
    /// </summary>
    Task<ProblemHttpResult?> RefuseRemovalAsync(
        AccessContext caller, IReadOnlyCollection<AccessEntry> removed, CancellationToken ct);
}
