// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SilexGis.Api.Common;

/// <summary>
/// The refusals every operation of a kind can answer with, declared once for the whole API
/// rather than on each route: a 401 on every operation that needs a token, a 429 on every
/// operation behind a rate limit. A route's own refusals (403, 404, 409, …) are declared on the
/// route, where the handler that sends them is.
/// </summary>
/// <remarks>
/// Applied as a final convention so it sees each route's metadata as the route finished it — an
/// operation opened to anonymous callers by its own registration is known to be one by then.
/// What it adds is only a description: nothing here changes what any route answers.
/// </remarks>
public static class CommonFailures
{
    public const string UnauthenticatedCode = "auth.unauthenticated";
    public const string RateLimitedCode = "rate.limited";

    public static void Declare(EndpointBuilder endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var metadata = endpoint.Metadata;
        if (!metadata.OfType<IAllowAnonymous>().Any())
        {
            AddProblem(metadata, StatusCodes.Status401Unauthorized);
        }

        if (metadata.OfType<EnableRateLimitingAttribute>().Any())
        {
            AddProblem(metadata, StatusCodes.Status429TooManyRequests);
        }
    }

    /// <summary>
    /// The code a refusal written by the framework itself carries — the status-code pages, for a
    /// 401 or 429 sent with no body — so that no refusal of this API arrives without one.
    /// </summary>
    public static string? CodeFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => UnauthenticatedCode,
        StatusCodes.Status429TooManyRequests => RateLimitedCode,
        _ => null,
    };

    private static void AddProblem(IList<object> metadata, int status)
    {
        if (metadata.OfType<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata>()
            .Any(p => p.StatusCode == status))
        {
            return;
        }

        metadata.Add(new ProducesResponseTypeMetadata(
            status, typeof(ProblemDetails), ["application/problem+json"]));
    }
}
