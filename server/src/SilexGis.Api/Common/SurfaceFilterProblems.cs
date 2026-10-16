// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Infrastructure.Filters;

namespace SilexGis.Api.Common;

/// <summary>A surface's refusal of the filter it was handed, as the problem response it sends.</summary>
public static class SurfaceFilterProblems
{
    public static ProblemHttpResult ToProblem(this SurfaceFilterRefusal refusal) =>
        refusal.Status == StatusCodes.Status404NotFound
            ? ApiProblems.NotFound(refusal.Code)
            : ApiProblems.BadRequest(refusal.Code, refusal.Detail);
}
