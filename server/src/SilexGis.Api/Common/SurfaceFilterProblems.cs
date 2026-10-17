// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Filters;

namespace SilexGis.Api.Common;

/// <summary>One problem of a refused filter, as it travels in the refusal's <c>problems</c> member.</summary>
/// <remarks>
/// The English sentence stays in the refusal's detail, for a log; a reader is told what is wrong in
/// their own language from the code, on the row the path points at, and never shown the sentence.
/// </remarks>
public sealed record FilterProblemWire(string Code, string Path, string? Field, string? Op, int? Limit);

/// <summary>A surface's refusal of the filter it was handed, as the problem response it sends.</summary>
public static class SurfaceFilterProblems
{
    /// <summary>The member of a <c>filter.invalid</c> refusal that lists what was wrong.</summary>
    public const string ProblemsMember = "problems";

    public static ProblemHttpResult ToProblem(this SurfaceFilterRefusal refusal) =>
        refusal.Status == StatusCodes.Status404NotFound
            ? ApiProblems.NotFound(refusal.Code)
            : refusal.Problems is { } problems
                ? ApiProblems.BadRequest(refusal.Code, refusal.Detail, ProblemsMember, ToWire(problems))
                : ApiProblems.BadRequest(refusal.Code, refusal.Detail);

    /// <summary>
    /// The refusal of a document that was read and cannot be run: every problem with its code and
    /// its place in the document, the sentences joined into the detail.
    /// </summary>
    public static ProblemHttpResult Invalid(IReadOnlyList<FilterProblem> problems) =>
        ApiProblems.BadRequest(
            SurfaceFilters.InvalidCode,
            string.Join(" ", problems.Select(p => p.Detail)),
            ProblemsMember,
            ToWire(problems));

    private static FilterProblemWire[] ToWire(IReadOnlyList<FilterProblem> problems) =>
        [.. problems.Select(p => new FilterProblemWire(
            p.Code,
            p.Path,
            p.Field,
            p.Op is { } op ? RouteEnums.WireNaming.ConvertName(op.ToString()) : null,
            p.Limit))];
}
