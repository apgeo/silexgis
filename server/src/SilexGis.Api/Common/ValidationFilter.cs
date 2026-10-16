// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;

namespace SilexGis.Api.Common;

/// <summary>
/// Endpoint filter running the FluentValidation validator for the request body.
/// Failures → 400 ProblemDetails with an errors dictionary.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter
    where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (argument is not null && validator is not null)
        {
            var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                return Results.Problem(
                    detail: "Validation failed.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "validation.failed",
                        ["errors"] = result.Errors
                            .GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                    });
            }
        }

        return await next(context);
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>
    /// Runs the request's validator before the handler, and says in the served description that
    /// the route can answer 400 with the validator's messages.
    /// </summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder)
        where T : class => builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
