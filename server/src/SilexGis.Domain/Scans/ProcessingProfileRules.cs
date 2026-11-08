// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Scans;

/// <summary>
/// Who may see and who may change a processing profile. The single home of the rule: the list
/// query, the single-profile routes and anything later that offers profiles all ask here.
/// </summary>
/// <remarks>
/// <para>
/// A personal profile is its author's alone, to read and to change; no right reaches it, not even
/// the right over shared profiles, because it is somebody's working notes rather than shared policy.
/// </para>
/// <para>
/// An installation profile is readable by every account — it is what the installation offers
/// everyone — and a caving-group profile by the group's members. Changing either is the
/// <see cref="AccessDomain.ScanProfiles"/> right, asked of the domain: a profile carries no
/// position and no audience of its own, so there is nothing about one row that would make the
/// answer differ from the next. Holding Read in that domain additionally shows every group's
/// profiles, which is what someone managing them for the installation needs to see.
/// </para>
/// <para>
/// A built-in profile is read-only for everyone: it is what the installation ships with, and
/// changing it in place would change what "the defaults" mean for everybody without anyone having
/// chosen to.
/// </para>
/// </remarks>
public static class ProcessingProfileRules
{
    public static bool MayRead(AccessContext ctx, ProcessingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Scope switch
        {
            ProcessingProfileScope.Installation => true,
            ProcessingProfileScope.Personal => profile.OwnerUserId == ctx.UserId,
            ProcessingProfileScope.CavingGroup =>
                (profile.CavingGroupId is { } group && ctx.CavingGroupIds.Contains(group)) || MayReadEveryGroup(ctx),
            _ => false,
        };
    }

    /// <summary>Whether the caller sees every caving group's profiles, not only their own groups'.</summary>
    public static bool MayReadEveryGroup(AccessContext ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.ScanProfiles, AccessAction.Read, null).Allowed;

    /// <summary>
    /// Whether the caller may create a profile of this scope. <paramref name="action"/> is
    /// <see cref="AccessAction.Create"/>, <see cref="AccessAction.Write"/> or
    /// <see cref="AccessAction.Delete"/>.
    /// </summary>
    public static bool MayManage(AccessContext ctx, ProcessingProfileScope scope, Guid? ownerUserId, AccessAction action)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return scope switch
        {
            ProcessingProfileScope.Personal => ownerUserId == ctx.UserId,
            ProcessingProfileScope.Installation or ProcessingProfileScope.CavingGroup =>
                AccessEvaluator.Decide(ctx, AccessDomain.ScanProfiles, action, null).Allowed,
            _ => false,
        };
    }

    /// <summary>Whether the caller may edit or delete this stored profile.</summary>
    public static bool MayChange(AccessContext ctx, ProcessingProfile profile, AccessAction action)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return !ProcessingProfileOrigins.IsBuiltIn(profile.Origin)
            && MayRead(ctx, profile)
            && MayManage(ctx, profile.Scope, profile.OwnerUserId, action);
    }
}
