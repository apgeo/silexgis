// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SilexGis.Api.Common;

/// <summary>
/// A contract rule for request bodies: an object that has to say which of several shapes it is,
/// and does not, is a body that cannot be read — a 400 — rather than a server fault.
/// </summary>
/// <remarks>
/// <para>
/// A polymorphic member is declared as an abstract type with a discriminator naming the concrete
/// shape. When the discriminator is missing the serializer falls back to the declared type, finds
/// it abstract, and throws <see cref="NotSupportedException"/>. That is not one of the exceptions
/// the framework treats as a bad body (it turns only <see cref="JsonException"/> and I/O faults
/// into a 400), so it escapes request binding entirely and the caller gets a 500 for what is only
/// a malformed request. A discriminator that names no shape is already a
/// <see cref="JsonException"/>, so after this rule both arrive at the binding handler the same way.
/// </para>
/// <para>
/// The rule gives each abstract polymorphic type a factory that throws a
/// <see cref="JsonException"/> instead. The factory is consulted only when no concrete shape was
/// named — a body that names one is created by that shape's own contract — and only when reading,
/// so nothing that is written changes. The serializer attaches the position in the body to the
/// exception, which is what lets the handler say which part of the body was unreadable.
/// </para>
/// </remarks>
public static class PolymorphicBodies
{
    public static void RefuseShapelessObjects(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind == JsonTypeInfoKind.Object
            && typeInfo.Type.IsAbstract
            && typeInfo.PolymorphismOptions is { } polymorphism)
        {
            var discriminator = polymorphism.TypeDiscriminatorPropertyName;
            typeInfo.CreateObject = () => throw new JsonException(
                $"The object does not say which shape it is: its '{discriminator}' member is missing.");
        }
    }
}
