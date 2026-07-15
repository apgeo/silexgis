// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;

namespace SilexGis.Api.Common;

/// <summary>
/// RFC 9457 problem responses with a stable machine-readable code.
/// Typed (ProblemHttpResult) so handlers can use exact Results&lt;...&gt; unions and the
/// OpenAPI contract stays complete.
/// </summary>
public static class ApiProblems
{
    public static ProblemHttpResult Forbidden(string code = "acl.forbidden") =>
        TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult NotFound(string code) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult BadRequest(string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult PreconditionFailed(string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status412PreconditionFailed,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult Conflict(string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
