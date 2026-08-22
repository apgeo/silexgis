// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Domain.Messaging;

/// <summary>
/// Which of this installation's languages a browser asked for, out of the header it sends with
/// every request.
/// </summary>
/// <remarks>
/// <para>
/// Written down here, next to the catalogue, because the answer is only ever used to pick a
/// wording out of it: a language this installation has no text in is no answer at all, so an
/// unrecognised tag is passed over rather than returned and left to fall back later.
/// </para>
/// <para>
/// The header is a preference list with weights — <c>ro-RO,ro;q=0.9,en;q=0.8</c> — so the highest
/// weight that names a language we actually have wins, ties going to whichever was written first,
/// which is the order the browser itself considers most preferred. A weight of zero is the
/// header's way of saying "not this one" and is honoured as a refusal rather than as a low
/// preference. <c>*</c> asks for anything at all, which is not a request for a particular
/// language, so it selects nothing and lets the account's own stored language decide.
/// </para>
/// <para>
/// Region is dropped: the wordings are per language, so <c>ro-MD</c> and <c>ro-RO</c> are the same
/// text, and normalising here keeps the catalogue's own fallback rule the only one that exists.
/// </para>
/// </remarks>
public static class AcceptLanguage
{
    /// <summary>
    /// The best language this installation ships that the header asked for, or null when it asked
    /// for none of them — including when there is no header at all, which is what a call from
    /// something that is not a browser looks like.
    /// </summary>
    public static string? Preferred(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        string? best = null;
        var bestQuality = 0.0;

        foreach (var entry in header.Split(','))
        {
            var parts = entry.Split(';');
            var tag = parts[0].Trim();
            if (tag.Length == 0 || tag == "*")
            {
                continue;
            }

            var quality = 1.0;
            foreach (var parameter in parts.Skip(1))
            {
                var trimmed = parameter.Trim();
                if (trimmed.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(
                        trimmed[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    quality = parsed;
                }
            }

            var language = tag.Split('-')[0].Trim().ToLowerInvariant();
            if (quality <= 0 || !MessageTemplateCatalog.Locales.Contains(language))
            {
                continue;
            }

            // Strictly greater, so a tie keeps the earlier entry.
            if (quality > bestQuality)
            {
                best = language;
                bestQuality = quality;
            }
        }

        return best;
    }
}
