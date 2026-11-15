// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Map;

/// <summary>
/// The shape of a reference to a map symbol — what a feature kind and the installation's entrance
/// markers name to say how a point is drawn.
/// </summary>
/// <remarks>
/// <para>
/// A reference is one of two things. A <b>file name</b> from the symbol set bundled beside the
/// browser application (<c>pit.png</c>, <c>osm_cave.svg</c>): never a path or an address, because
/// the browser loads it from beside the application and a path would let a reference point it
/// anywhere. Or a <b>drawn shape</b> (<c>shape:cave</c>, <c>shape:triangle:d46b08</c>): one of a
/// fixed list the client draws itself, optionally in a colour given as six lower-case hex digits.
/// </para>
/// <para>
/// The server knows only this shape, not the catalogue: which files exist is decided by what the
/// client ships, and a test on the client holds its catalogue to the folder. That keeps a symbol
/// added to the folder from needing a server release, and a reference to a file that is later
/// removed degrades to the client's fallback drawing rather than to an error.
/// </para>
/// </remarks>
public static partial class MapSymbol
{
    /// <summary>The longest reference stored — the width of the column that holds a kind's symbol.</summary>
    public const int MaxLength = 100;

    /// <summary>The shapes the client draws itself. Adding one needs the client's drawing too.</summary>
    public static IReadOnlyList<string> Shapes { get; } =
        ["circle", "dot", "ring", "square", "triangle", "diamond", "star", "cross", "cave"];

    /// <summary>The installation's entrance marker until an administrator chooses another.</summary>
    public const string DefaultEntrance = "shape:cave";

    /// <summary>
    /// The marker of an entrance shown only approximately: the same glyph in the protection orange,
    /// so the two read as one kind of thing in two states rather than as two kinds of thing.
    /// </summary>
    public const string DefaultApproximateEntrance = "shape:cave:d46b08";

    /// <summary>Whether <paramref name="reference"/> is a well-formed symbol reference.</summary>
    public static bool IsValid(string? reference) =>
        reference is not null && reference.Length <= MaxLength && Pattern().IsMatch(reference);

    [GeneratedRegex(
        @"^(?:[a-z0-9_\-]+\.(?:png|svg)|shape:(?:circle|dot|ring|square|triangle|diamond|star|cross|cave)(?::[0-9a-f]{6})?)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
