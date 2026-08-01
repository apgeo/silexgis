// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// Whether the caller may create in a domain. Creation has no row to evaluate yet, so
/// the walk runs against the *target context*: the prospective parent feature's facts
/// (which is how a subtree-scoped Create reaches, and how the ownership built-in lets
/// someone extend their own feature) plus the requested caving-group binding (which is
/// how a club's starter ruleset lets members add club content). A create with neither
/// sees global entries only.
/// </summary>
public static class CreateRules
{
    public const string ForbiddenCode = "access.create_forbidden";

    /// <summary>Creation with no parent feature — trip logs, uploads, saved views.</summary>
    public static bool MayCreate(
        AccessContext? ctx, AccessDomain domain, Guid? requestedCavingGroupId = null) =>
        MayCreate(ctx, domain, AccessTargetFacts.ForCreate(null, [], requestedCavingGroupId));

    /// <summary>Creation against a full target context (parent facts, binding, kind).</summary>
    public static bool MayCreate(AccessContext? ctx, AccessDomain domain, AccessTargetFacts target) =>
        AccessEvaluator.Decide(ctx, domain, AccessAction.Create, target).Allowed;
}
