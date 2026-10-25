// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace SilexGis.Domain.Surveys;

/// <summary>
/// The name a cave gets when an upload from a device names no cave and one is made from the
/// survey itself.
/// </summary>
public static class SurveyCaveNaming
{
    /// <summary>The longest name a cave may have.</summary>
    public const int MaxLength = 255;

    /// <summary>
    /// What the cave is named after: the survey's title, else the tool's name for the survey,
    /// trimmed and brought within a cave name's length. Null when both are blank.
    /// </summary>
    public static string? TitleOf(string? surveyTitle, string? surveyName)
    {
        var title = !string.IsNullOrWhiteSpace(surveyTitle) ? surveyTitle.Trim()
            : !string.IsNullOrWhiteSpace(surveyName) ? surveyName.Trim()
            : null;
        return title is null ? null : Cut(title, MaxLength);
    }

    /// <summary>
    /// <paramref name="title"/> when no cave in <paramref name="visibleNames"/> already has it,
    /// else <c>title (2)</c>, <c>(3)</c>, … — the first such name none of them has.
    /// </summary>
    /// <remarks>
    /// The names compared against must be those of caves the uploader can see, and only those:
    /// the suffix is something the uploader reads, so a suffix taken because of a cave hidden from
    /// them would tell them that cave exists. Cave names are not unique anyway, so a hidden cave
    /// sharing the name costs nothing. Names compare exactly, as written.
    /// </remarks>
    public static string Unclaimed(string title, IEnumerable<string> visibleNames)
    {
        var name = Cut(title.Trim(), MaxLength);
        var taken = visibleNames.Select(n => n.Trim()).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(name))
        {
            return name;
        }

        for (var n = 2; ; n++)
        {
            var suffix = string.Create(CultureInfo.InvariantCulture, $" ({n})");
            var candidate = Cut(name, MaxLength - suffix.Length).TrimEnd() + suffix;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Text cut to a length without splitting a surrogate pair.</summary>
    internal static string Cut(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        var end = char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
        return text[..end];
    }
}

/// <summary>
/// The version an upload is of its survey, and the names its artefacts carry because of it.
/// </summary>
/// <remarks>
/// Every upload of a survey is a version of it, so everything one run produces is named for the
/// version — a later upload's plan never replaces an earlier one's under the same name, and anyone
/// holding a downloaded file can tell which upload it came from.
/// </remarks>
public static class SurveyVersionStamp
{
    /// <summary>The stamp's written form: UTC, to the second, with no separators a file name could trip over.</summary>
    public const string Format = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>The longest artefact name produced: the common file-name limit.</summary>
    public const int MaxFileNameLength = 255;

    /// <summary>The longest document title, as documents hold it.</summary>
    public const int MaxTitleLength = 300;

    /// <summary>
    /// The version a new upload of a survey gets: <paramref name="now"/> in UTC, to the whole
    /// second — and a second after the survey's latest version when that is not already earlier,
    /// so two uploads opened within one second (a double tap, a retried request) still get two
    /// different stamps, and a survey's versions sort in the order they arrived.
    /// </summary>
    public static DateTimeOffset Next(DateTimeOffset now, DateTimeOffset? latest)
    {
        var stamp = WholeSecond(now);
        if (latest is { } previous && stamp <= WholeSecond(previous))
        {
            stamp = WholeSecond(previous).AddSeconds(1);
        }

        return stamp;
    }

    /// <summary>The stamp of a version, e.g. <c>20261025T013200Z</c>.</summary>
    public static string Of(DateTimeOffset version) =>
        WholeSecond(version).ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>
    /// An artefact's file name: <c>&lt;survey&gt;-&lt;stamp&gt;.&lt;extension&gt;</c>. The survey
    /// part is made safe for a file name on any system — a separator or a character Windows refuses
    /// becomes a dash — and shortened, never the stamp or the extension, when the whole would be
    /// too long.
    /// </summary>
    public static string ArtefactName(string surveyName, DateTimeOffset version, string extension)
    {
        var ext = extension.Trim().TrimStart('.').ToLowerInvariant();
        var tail = "-" + Of(version) + (ext.Length > 0 ? "." + ext : "");
        var stem = SafeStem(surveyName);
        return SurveyCaveNaming.Cut(stem, MaxFileNameLength - tail.Length) + tail;
    }

    /// <summary>
    /// The title of a document a run produces: the survey's title with the version's date and time,
    /// so two versions' plans read apart in any list.
    /// </summary>
    public static string DocumentTitle(string surveyTitle, DateTimeOffset version)
    {
        var when = " — " + WholeSecond(version).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
        return SurveyCaveNaming.Cut(surveyTitle.Trim(), MaxTitleLength - when.Length) + when;
    }

    private static DateTimeOffset WholeSecond(DateTimeOffset t)
    {
        var utc = t.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    // A fixed set rather than the running system's own list, which differs between Windows and
    // Linux: the same survey must get the same artefact name wherever the server runs.
    private const string Unsafe = "/\\:*?\"<>|";

    private static string SafeStem(string surveyName)
    {
        var builder = new StringBuilder(surveyName.Length);
        foreach (var c in surveyName.Trim())
        {
            builder.Append(char.IsControl(c) || Unsafe.Contains(c) ? '-' : c);
        }

        // A leading dot hides a file on most systems and a trailing dot or space is dropped by
        // Windows, so neither survives.
        var stem = builder.ToString().Trim().Trim('.').Trim();
        return stem.Length > 0 ? stem : "survey";
    }
}
