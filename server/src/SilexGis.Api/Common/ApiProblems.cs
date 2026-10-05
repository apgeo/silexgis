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
    /// <summary>
    /// A value that has to be one of a closed list of names and is not — a word the enum does not
    /// have, or a null where the shape has no room for one. One code wherever the value arrived:
    /// a request body, a query string or a route segment.
    /// </summary>
    public const string InvalidEnumCode = "validation.invalid_enum";

    /// <summary>
    /// The refusal for a closed-vocabulary value that named no member. It names the member the
    /// way the validators do and lists what it takes; the offending input is not echoed back.
    /// </summary>
    public static ProblemHttpResult InvalidEnum(string member, IReadOnlyList<string> accepted)
    {
        var sentence = $"Must be one of: {string.Join(", ", accepted)}.";
        return TypedResults.Problem(
            detail: $"'{member}' must be one of: {string.Join(", ", accepted)}.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = InvalidEnumCode,
                ["errors"] = new Dictionary<string, string[]> { [member] = [sentence] },
            });
    }

    public static ProblemHttpResult Forbidden(string code = "acl.forbidden", string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult NotFound(string code) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult BadRequest(string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// A refusal carrying one machine-readable fact about what was wrong with the request.
    /// </summary>
    /// <remarks>
    /// Same reasoning as the conflict overload below: the detail sentence is for a person and
    /// only the code and the members are a contract, so a client that has to act on which
    /// items were rejected is handed them rather than left parsing English.
    /// </remarks>
    public static ProblemHttpResult BadRequest(
        string code, string? detail, string member, object? value) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = code, [member] = value });

    public static ProblemHttpResult PreconditionFailed(string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status412PreconditionFailed,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult PreconditionRequired(string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status428PreconditionRequired,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// Something this installation depends on is not there, or would not answer. Used for a
    /// service outside this application: it says "not now, and not your fault", which is a
    /// different sentence from a bad request and leads whoever reads it to a different place.
    /// </summary>
    public static ProblemHttpResult ServiceUnavailable(string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult Conflict(string code, string? detail = null) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// A conflict carrying one machine-readable fact about what it collided with.
    /// </summary>
    /// <remarks>
    /// The detail sentence is for a person to read, and only the code is a contract. A client
    /// that needs the identifier of the thing in the way has to be given it as a member of its
    /// own — scraping it back out of English prose makes rewording or translating that sentence
    /// a silent break in whatever was parsing it.
    /// </remarks>
    public static ProblemHttpResult Conflict(
        string code, string? detail, string member, object? value) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = code, [member] = value });
}
