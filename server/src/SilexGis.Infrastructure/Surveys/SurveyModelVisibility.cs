// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Whether an account may see a cave's survey models at all — the one rule, for the web surfaces
/// and for the work done on an account's behalf with no request behind it.
/// </summary>
public static class SurveyModelVisibility
{
    /// <summary>
    /// Read on the cave, and the cave's exact location open to the account. The second half is not
    /// decoration — a model is a measured drawing of a passage, so serving one, or anything worked
    /// out of its stations, to an account without exact-location view would hand over the position
    /// the protection exists to withhold.
    /// </summary>
    public static async Task<bool> VisibleAsync(
        IAccessService access,
        FeatureProtection protection,
        AccessContext? ctx,
        Feature cave,
        CancellationToken ct) =>
        (await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed
        && await LocationOpenAsync(protection, ctx, cave.Id, ct);

    /// <summary>The exact-location half on its own.</summary>
    public static async Task<bool> LocationOpenAsync(
        FeatureProtection protection, AccessContext? ctx, Guid caveFeatureId, CancellationToken ct) =>
        (await protection.ExactViewIdsAsync(ctx, [caveFeatureId], ct)).Contains(caveFeatureId);
}
