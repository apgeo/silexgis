// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;

namespace SilexGis.Api.Common;

/// <summary>
/// Endpoint filter running the FluentValidation validator for the request body.
/// Failures → 400 ProblemDetails with an errors dictionary.
/// </summary>
/// <remarks>
/// The answer's <c>code</c> is <c>validation.failed</c>, except when every failure comes from rules
/// marked with <see cref="ProblemCodeRules.WithProblemCode{T, TProperty}"/> and they all name the
/// same code: then it is that code, so a refusal a client acts on by name (too many ids, a line
/// named twice) keeps its own code whether the check sits in a validator or in the handler. A
/// request with problems of more than one kind is described by no single code and stays generic.
/// </remarks>
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
                        ["code"] = ProblemCodeRules.CodeOf(result.Errors),
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
        where T : class => builder
            .AddEndpointFilter<ValidationFilter<T>>()
            .WithMetadata(new ValidatedBodyMetadata(typeof(T)))
            .ProducesValidationProblem();
}

/// <summary>
/// Names the body type a route's validation filter checks. A filter leaves no trace on the built
/// endpoint, and the filter passes a body through silently when no validator is registered for it,
/// so without this mark nothing could tell a validated route from one that forgot the call: the
/// endpoint table is walked by a test that holds every body-taking route to both halves.
/// </summary>
public sealed record ValidatedBodyMetadata(Type RequestType);

/// <summary>
/// Gives a validation rule a stable code of its own that the answer carries in place of the
/// generic <c>validation.failed</c>.
/// </summary>
/// <remarks>
/// Marked through the failure's custom state rather than read from every error code, because
/// FluentValidation fills an error code on every failure (its own validator names) and some
/// validators set codes for callers that read the failures directly; only a rule that asks for it
/// changes what the route answers.
/// </remarks>
public static class ProblemCodeRules
{
    /// <summary>The code of a refusal that no single rule's own code describes.</summary>
    public const string GenericCode = "validation.failed";

    public static IRuleBuilderOptions<T, TProperty> WithProblemCode<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule, string code) =>
        rule.WithErrorCode(code).WithState(_ => Marker.Instance);

    /// <summary>The code a refusal with these failures answers with.</summary>
    public static string CodeOf(IEnumerable<FluentValidation.Results.ValidationFailure> failures)
    {
        var codes = failures
            .Select(f => f.CustomState is Marker ? f.ErrorCode : null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return codes is [{ } code] ? code : GenericCode;
    }

    private sealed class Marker
    {
        public static readonly Marker Instance = new();
    }
}
