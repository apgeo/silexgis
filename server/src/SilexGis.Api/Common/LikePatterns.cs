// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>
/// What somebody typed, made into a LIKE pattern that matches it literally.
/// </summary>
/// <remarks>
/// A pattern treats <c>%</c> and <c>_</c> as wildcards, and both are ordinary in what people type
/// into a search box: "50%" is a figure, and an underscore separates the parts of many codes and
/// file names. Left as typed, a query of two underscores passes any minimum length and matches
/// every row, so a capped list fills with whatever sorts first and calls it a match. Every pattern
/// built here is passed to the database together with <see cref="Escape"/>, which names the
/// character that marks the next one as literal.
/// </remarks>
public static class LikePatterns
{
    /// <summary>The escape character every pattern from this class is written with.</summary>
    public const string Escape = "\\";

    /// <summary>The pattern matching every text that contains <paramref name="typed"/>, read literally.</summary>
    public static string Containing(string typed) => $"%{Literal(typed)}%";

    private static string Literal(string typed) =>
        typed.Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);
}
