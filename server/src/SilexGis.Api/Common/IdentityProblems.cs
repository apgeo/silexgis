// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace SilexGis.Api.Common;

/// <summary>
/// Turns an Identity failure into the house problem shape, with its per-error codes kept in the
/// errors dictionary so a client can tell "too short" from "needs a digit".
/// </summary>
public static class IdentityProblems
{
    public static ProblemHttpResult From(IdentityResult result, string code) =>
        TypedResults.Problem(
            detail: string.Join(" ", result.Errors.Select(e => e.Description)),
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["errors"] = result.Errors.ToDictionary(e => e.Code, e => e.Description),
            });

    /// <summary>Whether the failure carries a given Identity error code (e.g. DuplicateEmail).</summary>
    public static bool Has(this IdentityResult result, string errorCode) =>
        result.Errors.Any(e => string.Equals(e.Code, errorCode, StringComparison.Ordinal));
}
