// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using FluentValidation.Validators;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace SilexGis.Api.Common;

/// <summary>
/// Publishes a request's presence rules in its schema: a member its validator always requires is
/// marked required and not nullable, and one that may not be empty says so.
/// </summary>
/// <remarks>
/// <para>
/// What a request must carry is stated once, in the request's validator, and the framework's schema
/// for the type knows nothing of it. A member typed as optional but refused when absent came out as
/// "may be null" in the published document, so a generated client could build a request the server
/// always refuses with no type error to warn it. Reading the rules off the validator keeps the
/// validator as their one home and makes the document say what it enforces.
/// </para>
/// <para>
/// Only unconditional rules count. A rule that applies <c>When</c> something else holds, or to each
/// item of a collection rather than the collection, is not a statement about whether the member is
/// present, and the document would lie if it said otherwise. Only presence is published — whether a
/// member is there and whether it may be empty — because that is what a client's types can carry;
/// the server stays the judge of everything else and refuses with a code.
/// </para>
/// </remarks>
public sealed class ValidatorPresenceSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        if (schema.Properties is not { Count: > 0 } properties || context.JsonPropertyInfo is not null)
        {
            return Task.CompletedTask;
        }

        using var scope = context.ApplicationServices.CreateScope();
        var validatorType = typeof(IValidator<>).MakeGenericType(context.JsonTypeInfo.Type);
        if (scope.ServiceProvider.GetService(validatorType) is not IValidator validator)
        {
            return Task.CompletedTask;
        }

        var descriptor = validator.CreateDescriptor();
        foreach (var property in context.JsonTypeInfo.Properties)
        {
            if (property.AttributeProvider is not System.Reflection.MemberInfo member
                || !properties.TryGetValue(property.Name, out var published)
                || published is not OpenApiSchema propertySchema)
            {
                continue;
            }

            var presence = PresenceOf(descriptor, member.Name);
            if (presence == Presence.None)
            {
                continue;
            }

            schema.Required ??= new HashSet<string>();
            schema.Required.Add(property.Name);
            NotNullable(propertySchema);

            if (presence == Presence.NotEmpty)
            {
                if (propertySchema.Type?.HasFlag(JsonSchemaType.String) == true)
                {
                    propertySchema.MinLength ??= 1;
                }
                else if (propertySchema.Type?.HasFlag(JsonSchemaType.Array) == true)
                {
                    propertySchema.MinItems ??= 1;
                }
            }
        }

        return Task.CompletedTask;
    }

    private enum Presence { None, NotNull, NotEmpty }

    /// <summary>The strongest unconditional presence rule the validator holds for a member.</summary>
    private static Presence PresenceOf(IValidatorDescriptor descriptor, string member)
    {
        var presence = Presence.None;
        foreach (var rule in descriptor.GetRulesForMember(member))
        {
            if (IsPerItem(rule))
            {
                continue;
            }

            foreach (var component in rule.Components)
            {
                if (component.HasCondition || component.HasAsyncCondition)
                {
                    continue;
                }

                if (component.Validator is INotEmptyValidator)
                {
                    presence = Presence.NotEmpty;
                }
                else if (component.Validator is INotNullValidator && presence == Presence.None)
                {
                    presence = Presence.NotNull;
                }
            }
        }

        return presence;
    }

    /// <summary>Whether a rule speaks about each item of a collection rather than the member itself.</summary>
    private static bool IsPerItem(IValidationRule rule) =>
        rule.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollectionRule<,>));

    /// <summary>Takes "or null" off a property's published type, written either way the framework writes it.</summary>
    private static void NotNullable(OpenApiSchema schema)
    {
        if (schema.Type is { } type && type.HasFlag(JsonSchemaType.Null) && type != JsonSchemaType.Null)
        {
            schema.Type = type & ~JsonSchemaType.Null;
        }

        foreach (var alternatives in new[] { schema.OneOf, schema.AnyOf })
        {
            if (alternatives is not { Count: > 1 })
            {
                continue;
            }

            for (var i = alternatives.Count - 1; i >= 0; i--)
            {
                if (alternatives[i] is OpenApiSchema { Type: JsonSchemaType.Null })
                {
                    alternatives.RemoveAt(i);
                }
            }
        }
    }
}
