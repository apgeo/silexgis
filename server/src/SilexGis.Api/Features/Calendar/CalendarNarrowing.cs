// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;

namespace SilexGis.Api.Features.Calendar;

/// <summary>
/// How the calendar reads a narrowing that names several members of a closed vocabulary at once.
/// </summary>
/// <remarks>
/// Two of its parameters are sets — the families of record wanted, and the kinds of club date —
/// and they are read by one rule so that they cannot come to mean different things by being
/// written twice. Separate from the handler so each arm of that rule can be exercised directly:
/// the arms that differ only in what was <em>not</em> said are the ones a request-level test
/// reaches least readily.
/// </remarks>
public static class CalendarNarrowing
{
    /// <summary>
    /// Reads a comma-separated list of member names of <typeparamref name="TEnum"/>.
    /// </summary>
    /// <param name="written">The parameter as it arrived, or null when it did not.</param>
    /// <param name="chosen">
    /// The members named, or null when the parameter was absent or blank — which is the absence
    /// of a narrowing and not a narrowing to nothing.
    /// </param>
    /// <param name="unknownWord">
    /// When the answer is false, the first word that names no member; null when the list was
    /// present and named nothing at all.
    /// </param>
    /// <returns>
    /// False when the list cannot be honoured, and the caller refuses the request. Each word is
    /// read on its own and one bad word refuses the whole list: dropping it and answering for the
    /// rest would hand back a wider answer than was asked for and say nothing about it. A list of
    /// separators and nothing else is refused too — a caller who sent a narrowing and named
    /// nothing in it is far more likely to have a bug than to want everything, and answering
    /// everything would be answering a question they did not write.
    /// </returns>
    /// <remarks>
    /// Blank entries between real ones are passed over, so a trailing comma is not a mistake.
    /// Names are read through the one parser every closed vocabulary on a route or query string
    /// goes through: by name, in either spelling and any case, and never as the underlying number.
    /// </remarks>
    public static bool TryParseSet<TEnum>(
        string? written, out IReadOnlySet<TEnum>? chosen, out string? unknownWord)
        where TEnum : struct, Enum
    {
        chosen = null;
        unknownWord = null;
        if (string.IsNullOrWhiteSpace(written))
        {
            return true;
        }

        var wanted = new HashSet<TEnum>();
        foreach (var word in written.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!RouteEnums.TryParse<TEnum>(word, out var member))
            {
                unknownWord = word;
                return false;
            }

            wanted.Add(member);
        }

        if (wanted.Count == 0)
        {
            return false;
        }

        chosen = wanted;
        return true;
    }
}
