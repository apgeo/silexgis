// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// The vertical figures of a cave system — or of any area — across the caves filed under it:
/// highest and lowest entrance of the whole, the spread between them, the system's depth from its
/// highest entrance to the lowest surveyed point of any member, and the potential depth down to
/// the level the area's springs flow at.
///
/// <para>
/// The route answers under the same gate as the area statistics: the area must be one the caller
/// may place exactly, and the refusal is the shared one, word for word, so it says nothing about
/// which areas are protected. Beneath that, only member caves — and entrances — the caller may
/// place exactly contribute; the others are dropped before any figure is taken and are not counted.
/// The route takes no options, for the reason the single-cave route takes none.
/// </para>
/// </summary>
public static class AreaSystemFiguresEndpoints
{
    public static RouteGroupBuilder MapAreaSystemFiguresEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/features").WithTags("Features")
            .MapGet("/{id:guid}/system-figures", SystemFiguresAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Highest and lowest entrance, entrance spread, system depth and potential depth "
                + "across the caves filed under one area. Only caves the caller may place count.");

        return api;
    }

    private static async Task<Results<Ok<AreaSystemFiguresDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        SystemFiguresAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await StudyAreaGate.RefusalAsync(protection, ctx, id, ct) is { } refusal)
        {
            return refusal;
        }

        var result = await AreaSystemFigures.ForAreaAsync(db, protection, ctx, id, ct);

        return TypedResults.Ok(new AreaSystemFiguresDto(
            id,
            AreaKarstStatisticsLimits.DeclaredBasis,
            result.CaveCount,
            result.SurveyedCaveCount,
            result.Truncated,
            result.IsApproximation,
            result.HasAltitudes,
            result.Figures,
            result.SurveyHighestM,
            result.SurveyLowestM));
    }
}
