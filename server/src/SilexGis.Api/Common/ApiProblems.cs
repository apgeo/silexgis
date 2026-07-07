// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>RFC 9457 problem responses with the stable machine-readable code (03-api-spec.md §1).</summary>
public static class ApiProblems
{
    public static IResult Forbidden(string code = "acl.forbidden") =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult NotFound(string code) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult BadRequest(string code, string? detail = null) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
