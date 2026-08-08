// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.Import;

/// <summary>
/// Reads a coordinate written the way people and instruments actually write them.
///
/// <para>
/// A column of positions from a club's spreadsheet holds degrees-minutes-seconds next to plain
/// decimals next to numbers with a comma for a decimal point, often in the same file, because
/// three people filled it in. Refusing everything but one form would make the CSV path useless
/// for exactly the files it exists to read.
/// </para>
/// </summary>
public static class CoordinateText
{
    /// <summary>Hemisphere letters, English and Romanian (<c>V</c> for <i>vest</i>).</summary>
    private const string NegativeHemispheres = "SWV";

    private const string PositiveHemispheres = "NE";

    /// <summary>
    /// A single coordinate in degrees, or null. Accepts <c>45.123</c>, <c>45,123</c>,
    /// <c>45°23'12.5"N</c>, <c>N 45 23 12.5</c>, <c>45 23.456 N</c> and <c>-45°23'</c>.
    /// </summary>
    public static double? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        var sign = 1;

        // A hemisphere letter can open or close the value; either way it decides the sign, and
        // a value that carries both a letter and a minus is contradictory rather than clever.
        var first = char.ToUpperInvariant(text[0]);
        var last = char.ToUpperInvariant(text[^1]);
        if (NegativeHemispheres.Contains(first) || PositiveHemispheres.Contains(first))
        {
            sign = NegativeHemispheres.Contains(first) ? -1 : 1;
            text = text[1..].Trim();
        }
        else if (NegativeHemispheres.Contains(last) || PositiveHemispheres.Contains(last))
        {
            sign = NegativeHemispheres.Contains(last) ? -1 : 1;
            text = text[..^1].Trim();
        }

        if (text.Length == 0)
        {
            return null;
        }

        if (text[0] == '-')
        {
            sign *= -1;
            text = text[1..].Trim();
        }
        else if (text[0] == '+')
        {
            text = text[1..].Trim();
        }

        var parts = Split(text);
        if (parts.Count == 0 || parts.Count > 3)
        {
            return null;
        }

        double total = 0;
        double scale = 1;
        foreach (var part in parts)
        {
            if (!TryParseNumber(part, out var number) || number < 0)
            {
                return null;
            }

            total += number / scale;
            scale *= 60;
        }

        // Minutes and seconds above sixty are a sign the value was split on the wrong thing
        // (a decimal comma read as a separator, most often) rather than an unusual notation.
        if (parts.Count > 1 && parts.Skip(1).Any(p => TryParseNumber(p, out var n) && n >= 60))
        {
            return null;
        }

        return sign * total;
    }

    /// <summary>A plain number in a data column — altitude, depth — tolerating a decimal comma.</summary>
    public static double? ParseNumber(string? value) =>
        TryParseNumber((value ?? string.Empty).Trim(), out var number) ? number : null;

    /// <summary>Splits a degrees-minutes-seconds value into its parts, whatever it used to mark them.</summary>
    private static List<string> Split(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsDigit(ch) || ch == '.' || ch == ',')
            {
                current.Append(ch);
                continue;
            }

            // Anything else — a degree sign, a prime, a quote, a space — closes the part.
            if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static bool TryParseNumber(string text, out double value)
    {
        value = 0;
        if (text.Length == 0)
        {
            return false;
        }

        // A value carrying both marks is thousands-separated ("1,234.5"); one carrying only a
        // comma is a decimal comma, which is how most of Europe writes it.
        var normalized = text.Contains('.') && text.Contains(',')
            ? text.Replace(",", string.Empty)
            : text.Replace(',', '.');

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
