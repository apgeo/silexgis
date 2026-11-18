// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace SilexGis.Api.Common;

/// <summary>
/// Marks a request body that is a whole record saved as sent: every yes/no, number and choice it
/// carries has to be present, because a member left out would otherwise be read as false, 0 or
/// the first choice and saved over whatever was stored.
/// </summary>
/// <remarks>
/// A settings section is the case. The page sends the section it read back with its edits, so
/// a body that leaves a number out is a client that did not mean to send 0 — an older page, a
/// script that only meant to change one switch — and taking it as written would quietly reset a
/// retention period to nothing or a port to 0. Members that may be absent by design (a secret the
/// page never had, a text left empty) are reference types and are not affected: only value-type
/// members, whose missing value has a meaning nobody wrote, are required.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EveryValueRequiredAttribute : Attribute;

/// <summary>
/// The contract rule behind <see cref="EveryValueRequiredAttribute"/>, and how its refusal is
/// recognised when the serializer reports it.
/// </summary>
/// <remarks>
/// The serializer checks required members while reading, before any validator sees the record,
/// and reports every one that was missing in a single exception. The binding handler answers that
/// as a validation failure naming the members, which is what a client sending an incomplete
/// section has to act on. Only reading is affected: the same records are written in answers, and
/// a required flag does nothing there.
/// </remarks>
public static class CompleteBodies
{
    public static void RequireValueMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object
            || typeInfo.Type.GetCustomAttribute<EveryValueRequiredAttribute>() is null)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (property.AssociatedParameter is { HasDefaultValue: false }
                && property.PropertyType.IsValueType
                && Nullable.GetUnderlyingType(property.PropertyType) is null)
            {
                property.IsRequired = true;
            }
        }
    }

    /// <summary>Whether a body of this declared type is held to every value being present.</summary>
    public static bool IsComplete(Type? bodyType) =>
        bodyType?.GetCustomAttribute<EveryValueRequiredAttribute>() is not null;
}
