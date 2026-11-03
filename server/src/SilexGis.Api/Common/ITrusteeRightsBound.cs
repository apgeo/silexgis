// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Api.Common;

/// <summary>
/// The no-amplification bound for roster writes: would seating an account's person in these
/// caving groups hand the account rights, through a permission group those caving groups are
/// trustees of, that the caller does not hold?
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> A caving group can be a trustee of any permission group, and an account
/// holds everything its person's caving groups are trustees of. Adding a caving group as a
/// trustee is bounded by what the author holds; filling that caving group is the same grant
/// reached from the other end — enrolling a member, linking an account to a person who is
/// already a member, folding two roster entries into one — and carries the same bound.
/// </para>
/// <para>
/// <b>Why it is an interface here.</b> The bound is the permission model's rule, judged the way a
/// rule write is judged, and it is answered by the permission area that owns that judgement; the
/// roster and the caving-group areas only ask it.
/// </para>
/// <para>
/// <b>What it deliberately lets through.</b> Rules over a club's own content (scoped to the very
/// caving group being joined), when the caller may write that club's roster: deciding who is in a
/// club is what keeping its roster means, and a club's members reading and writing the club's own
/// material is what being in it means. Without this, a club manager who is not themselves a
/// member could not enrol anybody into their own club. Anything else the trustee rulesets grant —
/// another club's content, a protected area, an installation-wide right — is bounded as usual.
/// Full Administrators is not this bound's question: its membership is the grant and it has no
/// rules to compare, and the full-administration guard answers for it.
/// </para>
/// </remarks>
public interface ITrusteeRightsBound
{
    /// <summary>
    /// True when the write must be refused with the no-amplification code.
    /// </summary>
    /// <param name="caller">Who is asking. A full administrator holds everything and is never refused.</param>
    /// <param name="accountId">
    /// The account whose paths the write extends. A person with no account gains nobody anything,
    /// and the link that later attaches one asks this same question.
    /// </param>
    /// <param name="caverIds">Roster entries whose caving groups the account comes to sit in.</param>
    /// <param name="cavingGroupIds">Caving groups the account's person joins.</param>
    Task<bool> ExceedsCallerAsync(
        AccessContext caller,
        Guid? accountId,
        IReadOnlyCollection<Guid> caverIds,
        IReadOnlyCollection<Guid> cavingGroupIds,
        CancellationToken ct);
}
