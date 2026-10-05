// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Options;

namespace SilexGis.Api.Common;

/// <summary>
/// Turns a request the framework could not bind into the refusal a validator would have given:
/// a 400 Problem Details with a stable code naming what was wrong, instead of a server fault.
/// </summary>
/// <remarks>
/// <para>
/// Minimal APIs read and bind a request before any endpoint filter runs, so a body carrying a
/// word an enum does not have — or a null where the shape has no room for one — fails inside
/// the framework, where no validator ever sees it. The framework reports that as a
/// <see cref="BadHttpRequestException"/> (the route-handler option that throws on a bad request
/// is switched on for every environment at startup, so the answer is the same wherever the
/// application runs; left to its default it throws in development and answers an empty 400 in
/// production). Left alone, the exception-handling middleware answers it with a bare 500, which
/// is what every enum-bearing route in the application used to do.
/// </para>
/// <para>
/// Which member was wrong is read off the JSON path the serializer attaches to its exception
/// and resolved against the route's declared body type through the serializer's own contract —
/// so the answer names the member the way the validators do (<c>Participants[0].Role</c>) and,
/// when the member is an enum, lists the names it takes. Nothing of the offending input is
/// echoed back: the framework's own messages quote the value and the path, and a refusal is not
/// the place to reflect a request body to its sender.
/// </para>
/// </remarks>
public sealed partial class BindingProblemHandler(
    IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions) : IExceptionHandler
{
    /// <summary>Valid JSON whose value cannot be read as what the route declares, or no JSON at all.</summary>
    public const string BodyInvalidCode = "request.body_invalid";

    /// <summary>A route, query, header or form parameter that could not be read or was missing.</summary>
    public const string BindingFailedCode = "request.binding_failed";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad)
        {
            return false;
        }

        var problem = bad.InnerException is JsonException json
            ? BodyProblem(httpContext, json)
            : BindingProblem(bad);
        await problem.ExecuteAsync(httpContext);
        return true;
    }

    private ProblemHttpResult BodyProblem(HttpContext httpContext, JsonException json)
    {
        // The exception-handling middleware clears the endpoint off the context before it asks
        // any handler, so that a handler could re-route; the route that was running is kept on
        // its own feature, and that is where the declared body type is read from.
        var endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();
        var bodyType = endpoint?.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType;

        // Text that is not JSON — or stops short of being JSON — fails in the reader, and the
        // serializer wraps the reader's own exception (itself a JsonException) in another that
        // carries the path. That path names whatever member was being read when the text gave
        // out, not a member that was wrong, so it is not resolved: a body of `{"state": ` is a
        // broken body, not a bad state. A value the shape cannot hold fails in a converter
        // instead, and that exception wraps nothing.
        if (json.InnerException is JsonException)
        {
            return ApiProblems.BadRequest(BodyInvalidCode, "The request body could not be read as JSON.");
        }

        var (member, type) = Resolve(bodyType, json.Path);

        var underlying = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;
        if (member is not null && underlying is { IsEnum: true })
        {
            return ApiProblems.InvalidEnum(member, RouteEnums.WireNames(underlying));
        }

        return member is null
            ? ApiProblems.BadRequest(BodyInvalidCode, "The request body could not be read as the JSON this route expects.")
            : TypedResults.Problem(
                detail: $"'{member}' could not be read as the value this route expects.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = BodyInvalidCode,
                    ["errors"] = new Dictionary<string, string[]> { [member] = ["Could not be read as the value expected here."] },
                });
    }

    /// <summary>
    /// A parameter outside the body. The framework's message is of the form
    /// <c>Failed to bind parameter "Guid id" from "…"</c> or
    /// <c>Required parameter "int page" was not provided from query string</c>; the parameter's
    /// name is lifted out of it and the rest — which quotes the input — is not repeated.
    /// </summary>
    private static ProblemHttpResult BindingProblem(BadHttpRequestException bad)
    {
        var parameter = ParameterName().Match(bad.Message) is { Success: true } match
            ? match.Groups["name"].Value
            : null;

        if (bad.StatusCode != StatusCodes.Status400BadRequest)
        {
            // Too large, an unsupported media type and the like: the status says what happened,
            // and the code says only that the request was turned away before a handler saw it.
            return TypedResults.Problem(
                statusCode: bad.StatusCode,
                extensions: new Dictionary<string, object?> { ["code"] = "request.rejected" });
        }

        return parameter is null
            ? ApiProblems.BadRequest(BindingFailedCode, "A parameter of the request could not be read.")
            : TypedResults.Problem(
                detail: $"'{parameter}' could not be read.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = BindingFailedCode,
                    ["errors"] = new Dictionary<string, string[]> { [parameter] = ["Could not be read."] },
                });
    }

    /// <summary>
    /// Walks a serializer path (<c>$.participants[0].role</c>) down the declared body type, one
    /// contract lookup per segment, and answers the member spelled as the validators spell it and
    /// the type found at the end. Anything the contract cannot account for answers nothing: a
    /// guess at the member would be worse than no member.
    /// </summary>
    private (string? Member, Type? Type) Resolve(Type? bodyType, string? path)
    {
        if (bodyType is null || string.IsNullOrEmpty(path) || path[0] != '$')
        {
            return (null, null);
        }

        var options = jsonOptions.Value.SerializerOptions;
        var type = bodyType;
        var member = new StringBuilder();
        var i = 1;
        while (i < path.Length)
        {
            JsonTypeInfo contract;
            try
            {
                contract = options.GetTypeInfo(type);
            }
            catch (NotSupportedException)
            {
                return (null, null);
            }

            if (path[i] == '.')
            {
                var start = ++i;
                while (i < path.Length && path[i] != '.' && path[i] != '[')
                {
                    i++;
                }

                if (!Step(contract, path[start..i], member, out type))
                {
                    return (null, null);
                }
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i);
                if (end < 0)
                {
                    return (null, null);
                }

                var inside = path[(i + 1)..end];
                i = end + 1;

                if (inside.Length >= 2 && inside[0] == '\'')
                {
                    // A quoted segment: a dictionary key, or a property name the dotted form
                    // cannot carry.
                    var name = inside[1..^1];
                    if (contract.Kind == JsonTypeInfoKind.Dictionary && contract.ElementType is { } valueType)
                    {
                        member.Append('[').Append(name).Append(']');
                        type = valueType;
                    }
                    else if (!Step(contract, name, member, out type))
                    {
                        return (null, null);
                    }
                }
                else
                {
                    if (contract.ElementType is not { } elementType)
                    {
                        return (null, null);
                    }

                    member.Append('[').Append(inside).Append(']');
                    type = elementType;
                }
            }
            else
            {
                return (null, null);
            }
        }

        return member.Length == 0 ? (null, type) : (member.ToString(), type);
    }

    /// <summary>One property step: the wire name resolves to the declared member and its type.</summary>
    private static bool Step(JsonTypeInfo contract, string wireName, StringBuilder member, out Type type)
    {
        var property = contract.Properties.FirstOrDefault(p => p.Name == wireName);
        if (property is null)
        {
            type = contract.Type;
            return false;
        }

        if (member.Length > 0)
        {
            member.Append('.');
        }

        member.Append(property.AttributeProvider is MemberInfo declared ? declared.Name : property.Name);
        type = property.PropertyType;
        return true;
    }

    [GeneratedRegex("parameter \"(?:[^\"]* )?(?<name>[A-Za-z_][A-Za-z0-9_]*)\"")]
    private static partial Regex ParameterName();
}
