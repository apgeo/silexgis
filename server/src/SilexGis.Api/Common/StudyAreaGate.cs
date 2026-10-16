// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Permissions;

namespace SilexGis.Api.Common;

/// <summary>
/// The one place a route turns "may this caller scope a question by that area" into its answer
/// on the wire. The decision is <see cref="FeatureProtection.ScopeGeometryAsync"/> and
/// <see cref="FeatureProtection.MayScopeByAsync"/>; what lives here is the refusal, so every route
/// that takes an area refuses with the same status, code and words — for an area that does not
/// exist, one the caller cannot read and one they cannot place alike.
/// </summary>
public static class StudyAreaGate
{
    /// <summary>
    /// The sentence every refusal of an unusable study area carries. It is the same for each of
    /// the three reasons on purpose; a client is told what to do, not which reason applied.
    /// </summary>
    public const string NotFoundDetail =
        "No such area, or not one this question can be asked over for you.";

    /// <summary>
    /// The refusal for a study area the caller may not use, or null when they may. For routes that
    /// measure against the area's declared contents and do not need its outline in hand.
    /// </summary>
    public static async Task<ProblemHttpResult?> RefusalAsync(
        FeatureProtection protection, AccessContext ctx, Guid areaId, CancellationToken ct) =>
        await protection.MayScopeByAsync(ctx, areaId, ct) ? null : NotFound();

    /// <summary>
    /// The outline of a study area the caller may use, or the refusal: not found for an area they
    /// may not use, then — only for one they may — "not an outline" under the route's own code
    /// for a point or a line.
    /// </summary>
    /// <param name="routePrefix">The route's code prefix, e.g. <c>density</c>.</param>
    /// <param name="notAnOutlineDetail">What the route would have done with the ground it lacks.</param>
    public static async Task<(Geometry? Outline, ProblemHttpResult? Refusal)> OutlineAsync(
        FeatureProtection protection,
        AccessContext ctx,
        Guid areaId,
        string routePrefix,
        string notAnOutlineDetail,
        CancellationToken ct)
    {
        var geometry = await protection.ScopeGeometryAsync(ctx, areaId, ct);
        if (geometry is null)
        {
            return (null, NotFound());
        }

        if (!StudyArea.IsOutline(geometry))
        {
            return (null, ApiProblems.BadRequest(
                StudyArea.NotAnOutlineCode(routePrefix),
                "the study area must be an outline; a point or a line encloses no ground to "
                + notAnOutlineDetail + "."));
        }

        return (geometry, null);
    }

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(
            detail: NotFoundDetail,
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["code"] = StudyArea.NotFoundCode });
}
