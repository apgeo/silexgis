// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Map;

/// <summary>
/// A rectangle of the world in degrees, as a basemap extract is cut to it: west, south, east, north.
/// </summary>
public sealed record BasemapRegion(double West, double South, double East, double North);

/// <summary>
/// The rules of the basemap an installation serves itself: one archive of OpenStreetMap vector
/// tiles cut out of a published planet build for a region, kept in one directory, and served at an
/// address that changes with every file so that a long cache is always safe.
/// </summary>
/// <remarks>
/// <para>
/// An address is never reused: each extract is written under a name of its own — the build it was
/// cut from and the moment it was cut — and that name is the address its tiles are served at.
/// Anything a browser, a proxy or a content network cached from one extract is therefore never
/// mistaken for the next one, and the tiles can be cached for as long as anybody likes. They are
/// OpenStreetMap data, identical for every caller and filtered for nobody, so caching them
/// anywhere discloses nothing.
/// </para>
/// <para>
/// A new extract is written beside the one being served and takes its place only when it is
/// complete; the old one is removed after that. A file being served is never rewritten.
/// </para>
/// </remarks>
public static partial class SelfHostedBasemap
{
    /// <summary>
    /// What a catalogue entry writes where the current extract's name goes in its tile template;
    /// replaced when the entry is published, and an entry carrying it is not published while there
    /// is no extract.
    /// </summary>
    public const string Placeholder = "{basemap}";

    /// <summary>The deepest level a planet build holds: an extract asked for more gains nothing.</summary>
    public const int MaxZoom = 15;

    /// <summary>The shortest box side an extract may have, in degrees: about a hundred metres.</summary>
    public const double MinimumSide = 0.001;

    /// <summary>The prefix every extract's file name starts with — and the only files the directory is read for.</summary>
    public const string FilePrefix = "basemap-";

    /// <summary>The extension of a finished extract; one still being written carries another.</summary>
    public const string FileExtension = ".pmtiles";

    /// <summary>What an extract still being written is called: a name the tile server does not serve.</summary>
    public const string PartialExtension = ".pmtiles.partial";

    /// <summary>Romania and the edges of its neighbours.</summary>
    public static readonly BasemapRegion Romania = new(20.2, 43.6, 29.8, 48.3);

    /// <summary>A karst region: roughly the Apuseni mountains.</summary>
    public static readonly BasemapRegion Apuseni = new(22.2, 46.2, 23.4, 47.0);

    /// <summary>The regions offered by name, before a box of one's own.</summary>
    public static readonly IReadOnlyDictionary<string, BasemapRegion> Presets = new Dictionary<string, BasemapRegion>
    {
        ["romania"] = Romania,
        ["apuseni"] = Apuseni,
    };

    /// <summary>Refusal codes.</summary>
    public static class Codes
    {
        /// <summary>This installation does not serve a basemap of its own.</summary>
        public const string SelfHostingOff = "basemap.self_hosting_off";

        /// <summary>An extract is already queued or being made: one at a time.</summary>
        public const string DownloadRunning = "basemap.download_running";

        /// <summary>The box is not a box of the world.</summary>
        public const string RegionInvalid = "basemap.region_invalid";

        /// <summary>The zoom asked for is outside what a build holds.</summary>
        public const string ZoomInvalid = "basemap.zoom_invalid";

        /// <summary>The build is not one written as its date.</summary>
        public const string BuildInvalid = "basemap.build_invalid";

        /// <summary>The source publishes no such build, or none at all could be listed.</summary>
        public const string BuildNotFound = "basemap.build_not_found";

        /// <summary>The source could not be reached.</summary>
        public const string SourceUnreachable = "basemap.source_unreachable";

        /// <summary>The extract would be larger than the installation allows.</summary>
        public const string TooLarge = "basemap.too_large";

        /// <summary>The disk holding the basemap has no room for the extract beside the current one.</summary>
        public const string DiskFull = "basemap.disk_full";

        /// <summary>The extract was called off.</summary>
        public const string Cancelled = "basemap.cancelled";

        /// <summary>The program that cuts the extract failed.</summary>
        public const string ExtractFailed = "basemap.extract_failed";

        /// <summary>No such download.</summary>
        public const string NotFound = "basemap.not_found";
    }

    /// <summary>What is wrong with a box, or null for a box of the world an extract can be cut to.</summary>
    public static string? RegionProblem(BasemapRegion region)
    {
        double[] values = [region.West, region.South, region.East, region.North];
        if (values.Any(v => !double.IsFinite(v)))
        {
            return "Every side of the box must be a number.";
        }

        if (region.West < -180 || region.East > 180 || region.South < -85.05 || region.North > 85.05)
        {
            return "The box must lie between 180° west and east and between 85° south and north.";
        }

        if (region.East - region.West < MinimumSide || region.North - region.South < MinimumSide)
        {
            return "The box must be wider and taller than it is: its east side east of its west, its north side north of its south.";
        }

        return null;
    }

    /// <summary>Whether a zoom is one an extract can be cut to.</summary>
    public static bool IsZoom(int zoom) => zoom is >= 0 and <= MaxZoom;

    /// <summary>Whether a build is named as the published planet builds are: its date, <c>yyyyMMdd</c>.</summary>
    public static bool IsBuild(string? build) =>
        build is { Length: 8 }
        && DateOnly.TryParseExact(build, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>The newest of the builds a source lists by name, or null when it lists none.</summary>
    public static string? Latest(IEnumerable<string> builds) =>
        builds.Where(IsBuild).Order(StringComparer.Ordinal).LastOrDefault();

    /// <summary>
    /// The file an extract is kept in: the build it was cut from, and the moment it was cut, to the
    /// second. The name without its extension is the address its tiles are served under.
    /// </summary>
    public static string FileName(string build, DateTimeOffset extractedAt) =>
        $"{FilePrefix}{build}-{extractedAt.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}{FileExtension}";

    /// <summary>The build and the moment of cutting a file name says, or null for a file that is not an extract.</summary>
    public static (string Build, DateTimeOffset ExtractedAt)? ParseFileName(string fileName)
    {
        var match = FileNamePattern().Match(fileName);
        if (!match.Success
            || !IsBuild(match.Groups["build"].Value)
            || !DateTime.TryParseExact(
                match.Groups["at"].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            return null;
        }

        return (match.Groups["build"].Value, new DateTimeOffset(at, TimeSpan.Zero));
    }

    /// <summary>The extract being served out of a directory's files: the one cut last, or null when there is none.</summary>
    public static string? Current(IEnumerable<string> fileNames) =>
        fileNames
            .Select(name => (Name: name, Parsed: ParseFileName(name)))
            .Where(file => file.Parsed is not null)
            .OrderBy(file => file.Parsed!.Value.ExtractedAt)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .Select(file => file.Name)
            .LastOrDefault();

    /// <summary>The name an extract's tiles are served under: its file name without the extension.</summary>
    public static string SourceId(string fileName) =>
        fileName.EndsWith(FileExtension, StringComparison.Ordinal) ? fileName[..^FileExtension.Length] : fileName;

    /// <summary>Whether a catalogue address is the installation's own basemap's.</summary>
    public static bool IsSelfHosted(string urlTemplate) => urlTemplate.Contains(Placeholder, StringComparison.Ordinal);

    /// <summary>A self-hosted entry's address with the current extract's name put in.</summary>
    public static string Resolve(string urlTemplate, string sourceId) =>
        urlTemplate.Replace(Placeholder, sourceId, StringComparison.Ordinal);

    /// <summary>
    /// The address an entry is published with: as it stands for any other source, with the current
    /// extract's name put in for the installation's own basemap — and null, not published at all,
    /// while there is no extract to serve. An entry with a placeholder still in it would ask for
    /// tiles at an address nothing answers.
    /// </summary>
    public static string? Published(string urlTemplate, string? currentSourceId) =>
        !IsSelfHosted(urlTemplate) ? urlTemplate
        : currentSourceId is null ? null
        : Resolve(urlTemplate, currentSourceId);

    /// <summary>
    /// The size of the archive an extraction reports it would write, in bytes, read from the tool's
    /// closing line — <c>… for an archive size of 36 MB</c> — or null when it says nothing of the kind.
    /// </summary>
    public static long? ArchiveSize(string output)
    {
        var match = ArchiveSizePattern().Matches(output).LastOrDefault();
        if (match is null
            || !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var unit = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "B" => 1d,
            "KB" => 1e3,
            "MB" => 1e6,
            "GB" => 1e9,
            "TB" => 1e12,
            _ => double.NaN,
        };
        return double.IsNaN(unit) ? null : (long)Math.Round(value * unit);
    }

    /// <summary>
    /// How far an extraction has got, in percent, from the last progress figure in a piece of the
    /// tool's output (<c>fetching chunks  45% |…</c>), or null when the piece holds none.
    /// </summary>
    public static int? ProgressPercent(string output)
    {
        var match = ProgressPattern().Matches(output).LastOrDefault();
        return match is not null && int.TryParse(match.Groups["percent"].Value, CultureInfo.InvariantCulture, out var percent)
            ? Math.Clamp(percent, 0, 100)
            : null;
    }

    [GeneratedRegex(@"^basemap-(?<build>\d{8})-(?<at>\d{14})\.pmtiles$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"archive size of (?<value>\d+(?:\.\d+)?)\s*(?<unit>[kKMGT]?B)", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveSizePattern();

    [GeneratedRegex(@"(?<percent>\d{1,3})%", RegexOptions.CultureInvariant)]
    private static partial Regex ProgressPattern();
}
