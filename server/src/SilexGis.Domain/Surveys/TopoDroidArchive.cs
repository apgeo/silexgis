// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace SilexGis.Domain.Surveys;

/// <summary>
/// The first entry of a TopoDroid survey archive: which TopoDroid wrote it, the version of the
/// database the survey was dumped from, and the survey's name.
/// </summary>
/// <param name="AppVersion">TopoDroid's version as it writes it, such as <c>6.5.34</c>.</param>
/// <param name="AppCode">TopoDroid's version code, when the line carries one.</param>
/// <param name="DatabaseVersion">The version of the database the dump was written from.</param>
/// <param name="SurveyName">The survey's name in TopoDroid.</param>
public sealed record TopoDroidManifest(string AppVersion, long? AppCode, int DatabaseVersion, string SurveyName);

/// <summary>A fixed station of a TopoDroid survey: where the survey is tied to the ground.</summary>
/// <remarks>
/// A position, and treated as one: it is read so the cave's entrance can be written through the
/// path that protects it, and never goes into a log line, a message or a status row.
/// </remarks>
/// <param name="Station">The station the fix is on.</param>
/// <param name="Longitude">WGS84 longitude in degrees.</param>
/// <param name="Latitude">WGS84 latitude in degrees.</param>
/// <param name="EllipsoidHeight">Height above the ellipsoid in metres, as TopoDroid computed it.</param>
/// <param name="GeoidHeight">Height above the geoid in metres, as the caver entered it.</param>
public sealed record TopoDroidFixedPoint(
    string Station, double Longitude, double Latitude, double? EllipsoidHeight, double? GeoidHeight);

/// <summary>What a TopoDroid survey dump says about the survey as a whole.</summary>
/// <param name="Name">The survey's name, from its row.</param>
/// <param name="Date">The survey's date, when it reads as one.</param>
/// <param name="Team">The survey party as TopoDroid holds it; personal data of the people named.</param>
/// <param name="Declination">The magnetic declination in degrees, when it reads as one.</param>
/// <param name="Comment">The survey's description.</param>
/// <param name="FixedPoints">The fixed stations whose coordinates read as a position on the globe.</param>
/// <param name="FixedRowCount">Every fixed-station row the dump holds, whether or not it read as a position.</param>
public sealed record TopoDroidSurveyFacts(
    string? Name,
    DateOnly? Date,
    string? Team,
    double? Declination,
    string? Comment,
    IReadOnlyList<TopoDroidFixedPoint> FixedPoints,
    int FixedRowCount);

/// <summary>
/// The rules a TopoDroid survey archive is read by: which entries matter, which names are refused,
/// and how its manifest and its SQL dump are read without running a database engine over what a
/// phone sent.
/// </summary>
/// <remarks>
/// <para>
/// TopoDroid writes the archive as: the manifest first, then the dump of the survey's database rows
/// as SQL text, then the note, the sketches and the photo and audio folders. Only the first two are
/// read here; everything else stays inside the archive, unread.
/// </para>
/// <para>
/// The dump is read with a line scanner that copies TopoDroid's own tolerant reading of it, because
/// genuine output is not strict SQL: the survey row of the database version current in 2026 ends
/// with two values that have no comma between them. Values are separated by commas, spaces, or
/// both; strings are in double quotes and never contain one, because TopoDroid replaces a quote
/// inside a string with the escape character when it writes and back when it reads.
/// </para>
/// </remarks>
public static class TopoDroidArchive
{
    /// <summary>The name of the manifest entry, which TopoDroid always writes first.</summary>
    public const string ManifestEntry = "manifest";

    /// <summary>
    /// The oldest database version TopoDroid's own importer still reads. An archive older than that
    /// is refused here for the same reason: its rows are laid out in a way nothing maintains.
    /// </summary>
    public const int MinDatabaseVersion = 21;

    /// <summary>The most entries a survey archive may hold before it is refused unread.</summary>
    public const int MaxEntries = 10_000;

    /// <summary>The most a manifest may be; a genuine one is four short lines.</summary>
    public const int MaxManifestBytes = 4 * 1024;

    /// <summary>
    /// The most a survey dump may be. A genuine dump is a few kilobytes per hundred shots, so this
    /// is a survey of hundreds of thousands of shots.
    /// </summary>
    public const long MaxDumpBytes = 64L * 1024 * 1024;

    private const char EscapedQuote = '\u001b';

    /// <summary>Whether an entry is the survey's SQL dump: a <c>.sql</c> file at the archive's root.</summary>
    public static bool IsDump(string entryName) =>
        entryName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
        && entryName.IndexOfAny(['/', '\\']) < 0;

    /// <summary>
    /// Whether an entry name stays inside the archive. Nothing here is ever written out under an
    /// entry's name, but an archive that names a path climbing out of wherever it is unpacked is a
    /// crafted one, and a crafted archive is not read at all. Both separators count, because the
    /// one that is ordinary on one platform is a separator on the other.
    /// </summary>
    public static bool IsSafeEntryName(string entryName)
    {
        if (string.IsNullOrEmpty(entryName) || entryName.Contains('\0'))
        {
            return false;
        }

        var normalised = entryName.Replace('\\', '/');
        if (normalised.StartsWith('/') || (normalised.Length >= 2 && normalised[1] == ':'))
        {
            return false;
        }

        return normalised.Split('/').All(segment => segment != "..");
    }

    /// <summary>
    /// Reads a manifest, or answers null with why it is not one. Four lines: the version and its
    /// code, the database version, the survey name, the date the archive was written.
    /// </summary>
    public static TopoDroidManifest? ReadManifest(string text, out string? problem)
    {
        var lines = text.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 3)
        {
            problem = "The survey archive's manifest is incomplete.";
            return null;
        }

        var versionLine = lines[0].Trim();
        if (versionLine.Length == 0)
        {
            problem = "The survey archive's manifest names no TopoDroid version.";
            return null;
        }

        var versionParts = versionLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        long? code = versionParts.Length > 1
            && long.TryParse(versionParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCode)
                ? parsedCode
                : null;

        if (!int.TryParse(lines[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var database))
        {
            problem = "The survey archive's manifest names no database version.";
            return null;
        }

        if (database < MinDatabaseVersion)
        {
            problem = $"The survey archive was written from a TopoDroid database older than version {MinDatabaseVersion}.";
            return null;
        }

        var name = lines[2].Trim();
        if (name.Length == 0)
        {
            problem = "The survey archive's manifest names no survey.";
            return null;
        }

        problem = null;
        return new TopoDroidManifest(versionParts[0], code, database, name);
    }

    /// <summary>
    /// Reads the survey row and the fixed-station rows of a dump, one line at a time. Every other
    /// table is passed over unread; a line that is not an insert is ignored.
    /// </summary>
    /// <returns>Null when the dump holds no survey row at all.</returns>
    public static TopoDroidSurveyFacts? ReadDump(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string[]? survey = null;
        var fixes = new List<TopoDroidFixedPoint>();
        var fixedRows = 0;

        foreach (var line in lines)
        {
            var table = TableOf(line);
            if (table is null)
            {
                continue;
            }

            if (survey is null && table.Equals("surveys", StringComparison.OrdinalIgnoreCase))
            {
                survey = Values(line);
            }
            else if (table.Equals("fixeds", StringComparison.OrdinalIgnoreCase))
            {
                fixedRows++;
                if (FixedPointOf(Values(line)) is { } fix)
                {
                    fixes.Add(fix);
                }
            }
        }

        if (survey is null)
        {
            return null;
        }

        // The survey row: id, name, day, team, declination, comment, then values this reading
        // does not need.
        return new TopoDroidSurveyFacts(
            Text(survey, 1),
            DateOf(At(survey, 2)),
            Text(survey, 3),
            Number(At(survey, 4)) is { } d && d is >= -180 and <= 180 ? d : (double?)null,
            Text(survey, 5),
            fixes,
            fixedRows);
    }

    /// <summary>
    /// The values between an insert's opening and closing parenthesis, as TopoDroid's own scanner
    /// splits them: quoted strings whole, everything else up to the next comma or space.
    /// </summary>
    public static string[] Values(string line)
    {
        var open = line.IndexOf('(', StringComparison.Ordinal);
        var close = line.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return [];
        }

        var values = new List<string>();
        var position = open + 1;
        while (position < close)
        {
            var c = line[position];
            if (c is ' ' or ',' or '\t')
            {
                position++;
                continue;
            }

            if (c is '"' or '\'')
            {
                var end = line.IndexOf(c, position + 1);
                if (end < 0 || end > close)
                {
                    end = close;
                }

                values.Add(line[(position + 1)..end]);
                position = end + 1;
                continue;
            }

            var next = position;
            while (next < close && line[next] is not (' ' or ',' or '\t'))
            {
                next++;
            }

            values.Add(line[position..next]);
            position = next;
        }

        return [.. values];
    }

    /// <summary>The table an insert line writes to, or null for any other line.</summary>
    private static string? TableOf(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("insert", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var words = trimmed.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 3 && words[1].Equals("into", StringComparison.OrdinalIgnoreCase)
            ? words[2]
            : null;
    }

    /// <summary>
    /// A fixed-station row: survey id, row id, station, longitude, latitude, ellipsoid height,
    /// geoid height, then values this reading does not need.
    /// </summary>
    private static TopoDroidFixedPoint? FixedPointOf(string[] values)
    {
        var station = Text(values, 2);
        if (station is null
            || Number(At(values, 3)) is not { } longitude
            || Number(At(values, 4)) is not { } latitude
            || longitude is < -180 or > 180
            || latitude is < -90 or > 90)
        {
            return null;
        }

        return new TopoDroidFixedPoint(station, longitude, latitude, Number(At(values, 5)), Number(At(values, 6)));
    }

    private static string? At(string[] values, int index) => index < values.Length ? values[index] : null;

    /// <summary>A string value as TopoDroid meant it: its escaped quotes restored, blank as absent.</summary>
    private static string? Text(string[] values, int index)
    {
        var value = At(values, index)?.Replace(EscapedQuote, '"').Trim();
        return string.IsNullOrEmpty(value) ? null : Normalised(value);
    }

    /// <summary>Text from a phone, with the control characters a database column has no use for removed.</summary>
    private static string Normalised(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsControl(c) && c is not ('\n' or '\t') ? ' ' : c);
        }

        return builder.ToString();
    }

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : null;

    /// <summary>TopoDroid writes a survey's day as <c>yyyy.MM.dd</c>; a dash-separated date is taken too.</summary>
    private static DateOnly? DateOf(string? value) =>
        DateOnly.TryParseExact(
            value?.Trim(),
            ["yyyy.MM.dd", "yyyy-MM-dd", "yyyy.M.d"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
}
