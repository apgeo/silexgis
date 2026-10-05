// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SilexGis.Api.Common;

/// <summary>
/// The one way an enum value is read out of a route segment or a query string.
/// </summary>
/// <remarks>
/// <para>
/// Minimal APIs bind an enum parameter with the runtime's own <c>Enum.TryParse</c>, and that
/// parse does three things this API's contract does not want. It is case-sensitive, so the
/// camelCase spelling every response publishes (<c>"cavingGroup"</c>, <c>"surveyedLength"</c>)
/// fails to bind — and a failed bind is reported as a server fault, not a refusal. It accepts the
/// underlying number, so <c>2</c> and <c>+2</c> name a member nobody spelled. And it accepts a
/// comma-separated list and combines the members by bit, so a request naming two values can be
/// answered as a third it never named. Measured on the statistics routes, where
/// <c>?measure=surveyedLength</c> answered 500 and a two-measure list answered as a different
/// measure.
/// </para>
/// <para>
/// So a closed vocabulary reaches a route or query string as text, declared <c>string?</c> on the
/// handler, and is parsed here: by name, case-insensitively, matching either the declared member
/// name or the spelling the JSON contract gives it, and nothing else. A number, a sign or a list is
/// not a name and is refused by construction rather than by a guard that has to anticipate the
/// next spelling. A test over the endpoint table asserts that no handler binds an enum from a
/// route or query string directly, so this stays the only path.
/// </para>
/// </remarks>
public static class RouteEnums
{
    /// <summary>
    /// How enum members are spelled on the wire — the policy the JSON converter is registered
    /// with, kept here so the names a refusal lists are the names a response would print.
    /// </summary>
    public static readonly JsonNamingPolicy WireNaming = JsonNamingPolicy.CamelCase;

    private static readonly ConcurrentDictionary<Type, (string Declared, string Wire, object Value)[]> Members = new();

    /// <summary>
    /// Parses <paramref name="value"/> as a member name of <typeparamref name="TEnum"/>, ignoring
    /// case. Blank, numeric, signed and comma-separated input all answer false.
    /// </summary>
    public static bool TryParse<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        if (TryParse(typeof(TEnum), value, out var boxed))
        {
            parsed = (TEnum)boxed;
            return true;
        }

        parsed = default;
        return false;
    }

    /// <summary>The non-generic form, for a caller holding the enum as a <see cref="Type"/>.</summary>
    public static bool TryParse(Type enumType, string? value, out object parsed)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var written = value.Trim();
            foreach (var (declared, wire, member) in MembersOf(enumType))
            {
                if (string.Equals(declared, written, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(wire, written, StringComparison.OrdinalIgnoreCase))
                {
                    parsed = member;
                    return true;
                }
            }
        }

        parsed = null!;
        return false;
    }

    /// <summary>
    /// The member names of <paramref name="enumType"/> as the wire spells them, in declaration
    /// order — what a refusal lists as the accepted values.
    /// </summary>
    public static IReadOnlyList<string> WireNames(Type enumType) =>
        [.. MembersOf(enumType).Select(m => m.Wire)];

    /// <summary>The same, for a caller holding the enum as a type parameter.</summary>
    public static IReadOnlyList<string> WireNames<TEnum>()
        where TEnum : struct, Enum => WireNames(typeof(TEnum));

    /// <summary>
    /// The refusal for a route or query parameter whose text names no member: 400 under the
    /// shared invalid-enum code, naming the parameter and listing what it takes. The offending
    /// text is not echoed back.
    /// </summary>
    public static ProblemHttpResult Invalid<TEnum>(string parameter)
        where TEnum : struct, Enum => ApiProblems.InvalidEnum(parameter, WireNames<TEnum>());

    private static (string Declared, string Wire, object Value)[] MembersOf(Type enumType)
    {
        if (!enumType.IsEnum)
        {
            throw new ArgumentException($"{enumType} is not an enum.", nameof(enumType));
        }

        return Members.GetOrAdd(enumType, static type =>
            [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => (
                    Declared: field.Name,
                    // A member may carry its own wire name; everything else is the declared
                    // name under the naming policy the converter applies.
                    Wire: field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
                        ?? WireNaming.ConvertName(field.Name),
                    Value: field.GetValue(null)!))]);
    }
}
