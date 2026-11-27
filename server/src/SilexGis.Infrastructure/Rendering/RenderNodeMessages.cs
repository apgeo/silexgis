// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Therion.Blender;

namespace SilexGis.Infrastructure.Rendering;

/// <summary>
/// The render node protocol's messages read and written with the survey library's own records and
/// checked by its own receiver check, so this server and the worker cannot disagree about a shape.
/// </summary>
/// <remarks>
/// A message a node sends is held to the library's schema as a receiver holds it
/// (<see cref="RenderNodeProtocol.Validate"/>): a property it does not know is accepted, as the
/// protocol says; what is required, every type, pattern, enum and bound, and the protocol's
/// version, are checked. What this server answers is written by the library's own serializer:
/// camelCase, absent values left out rather than written as null.
/// </remarks>
public static class RenderNodeMessages
{
    public const string Claim = "claim";
    public const string Heartbeat = "heartbeat";
    public const string Complete = "complete";
    public const string Fail = "fail";

    /// <summary>
    /// The message in <paramref name="json"/> as the record <typeparamref name="T"/>, or what is
    /// wrong with it, as the library's receiver check says.
    /// </summary>
    public static (T? Message, IReadOnlyList<string> Problems) Read<T>(string name, string json)
        where T : class
    {
        IReadOnlyList<string> problems;
        try
        {
            problems = RenderNodeProtocol.Validate(name, json);
        }
        catch (JsonException e)
        {
            problems = [$"the body is not JSON ({e.Message})"];
        }

        if (problems.Count > 0)
        {
            return (null, [.. problems.Take(20)]);
        }

        return RenderNodeProtocol.Read<T>(json) is { } message
            ? (message, [])
            : (null, [$"the body is not a {name} message"]);
    }

    /// <summary>A message as the protocol writes it.</summary>
    public static string Write<T>(T message) => RenderNodeProtocol.Write(message);
}
