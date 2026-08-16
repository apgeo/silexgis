// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Terrain;

/// <summary>
/// What one side of the meshing step asks for, and what the other side answers — the whole of the
/// agreement between the application and the program that turns rasters into tiles.
/// </summary>
/// <remarks>
/// <para>
/// The tile-maker is a command-line tool that runs once and exits, and the application is
/// deliberately given no way to start containers of its own — that is a far larger privilege than
/// baking ground is worth. So the two meet on a directory they both have: the application leaves a
/// request there and waits, and whatever is watching that directory leaves its answer beside it.
/// </para>
/// <para>
/// Both files are plain <c>key=value</c> lines, one per line, because the other side of this
/// handover is a shell script inside an image built around a Java tool: a format both can read
/// without either of them gaining a parser. Both are written under a temporary name and renamed
/// into place, which is the only way either side can be sure it is reading a whole file — a reader
/// that looks while an ordinary write is half done sees a truncated one, and a truncated request is
/// a bake of the wrong thing.
/// </para>
/// </remarks>
public static class TerrainBakeHandover
{
    /// <summary>The file the application writes, once, to ask for a bake.</summary>
    public const string RequestFileName = "request";

    /// <summary>The file the other side writes the moment it takes the request.</summary>
    public const string StartedFileName = "started";

    /// <summary>Everything the tile-maker said, as it said it.</summary>
    public const string LogFileName = "bake.log";

    /// <summary>The file the other side writes last, when there is something to report.</summary>
    public const string ResultFileName = "result";

    /// <summary>
    /// The suffix a file carries while it is being written, before it is renamed into place.
    /// </summary>
    public const string PartialSuffix = ".part";

    /// <summary>
    /// The deepest tile level anyone may ask for, and the shallowest.
    /// </summary>
    /// <remarks>
    /// Not a matter of taste: each level doubles the resolution and roughly quadruples both the
    /// number of tiles and the time, and levels below about twenty describe ground finer than any
    /// published elevation data. A number outside this range is a mistake worth catching before an
    /// afternoon is spent on it.
    /// </remarks>
    public const int ShallowestDepth = 1;

    /// <inheritdoc cref="ShallowestDepth"/>
    public const int DeepestDepth = 22;

    /// <summary>
    /// Whether a line the tile-maker wrote means it quietly gave up on the detail it was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single most important line of this whole step. Short of memory the tool does not
    /// fail and does not stop: it stops refining and writes coarser tiles than were asked for, and
    /// says so only in its own log. Nothing else notices — the pyramid it produces is complete,
    /// every tile in it is valid, and the ground it draws is smooth and plausible and wrong. A
    /// build service that does not read for this publishes degraded terrain under a green tick.
    /// </para>
    /// <para>
    /// Matched on the two words the tool puts together whichever way it phrases it, rather than on
    /// a list of exact sentences: it announces the condition one way and its decision to stop
    /// refining another, and a list of sentences is a thing that goes stale silently the next time
    /// the tool is upgraded. An ordinary low-memory notice, which does not carry that severity
    /// word, is left alone — the thing being caught here is the tool deciding to ship less than it
    /// was asked for.
    /// </para>
    /// </remarks>
    public static bool MeansDegraded(string line) =>
        line.Contains("critical memory", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What the application asks for.</summary>
/// <param name="InputDirectory">
/// The directory of prepared rasters, one file per source. A directory rather than a file, and
/// never one merged sheet: the tile-maker works out how deep it may go from each raster's own pixel
/// size, so a fine local survey gets deep tiles and a coarse regional fill does not.
/// </param>
/// <param name="OutputDirectory">Where the pyramid is written.</param>
/// <param name="MaxDepth">
/// The deepest tile level to produce, always stated. Left to work it out for itself the tile-maker
/// derives it from the finest raster it was given, which for a small patch of half-metre survey
/// inside a coarse regional fill is several levels deeper than anyone wanted — and the price of a
/// bake tracks the number of tiles, not the area.
/// </param>
/// <param name="Datum">
/// Whether the heights are converted while meshing. Converting is truer and then requires every
/// surveyed altitude to be corrected by the local geoid undulation before it will sit on the drawn
/// ground; not converting leaves the two agreeing because they are wrong in the same direction.
/// </param>
public sealed record TerrainBakeRequest(
    string InputDirectory,
    string OutputDirectory,
    int MaxDepth,
    TerrainHeightDatum Datum)
{
    /// <summary>
    /// The request as the file holds it.
    /// </summary>
    /// <remarks>
    /// Line endings are explicitly the single character, whatever the host writing this uses. The
    /// side reading it is a shell script, and a depth of "13" followed by a carriage return is not
    /// a number.
    /// </remarks>
    public string ToText() =>
        string.Concat(
            "input=", InputDirectory, "\n",
            "output=", OutputDirectory, "\n",
            "maxDepth=", MaxDepth.ToString(CultureInfo.InvariantCulture), "\n",
            "datum=", Datum == TerrainHeightDatum.Ellipsoidal ? "ellipsoidal" : "orthometric", "\n");
}

/// <summary>How a bake ended, as the other side reports it.</summary>
public enum TerrainBakeOutcome
{
    /// <summary>
    /// The result file could not be read as one. A fault in the handover itself rather than in the
    /// bake, and kept apart from every other answer because acting on it means looking at the two
    /// programs that disagree, not at the elevation data.
    /// </summary>
    Unreadable = 0,

    /// <summary>The tile-maker ran and exited cleanly. Says nothing about what it produced.</summary>
    Succeeded = 1,

    /// <summary>
    /// The tile-maker ran and exited badly. A fact about the rasters it was given, most of the
    /// time, and repeating it against the same input will answer the same way.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// The request was never run, because the other side would not accept it. That is a fault in
    /// what was asked for — a path outside the shared volume, a depth that is not a number — and so
    /// a defect in this application rather than in anything an administrator did.
    /// </summary>
    Refused = 3,

    /// <summary>
    /// A bake was running and the thing running it stopped. Nothing is wrong with the request or
    /// with the data; the output is half written and the attempt is worth making again.
    /// </summary>
    Interrupted = 4,
}

/// <summary>What the other side answers.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="ExitCode">What the tile-maker exited with, when it got as far as running.</param>
/// <param name="Reason">One line, whenever the outcome is not success.</param>
public sealed record TerrainBakeResult(TerrainBakeOutcome Outcome, int? ExitCode, string? Reason)
{
    /// <summary>
    /// Reads a result file.
    /// </summary>
    /// <remarks>
    /// Never throws, and a file it cannot make sense of is an answer rather than an exception: this
    /// is read from a directory two programs share, and "the other side wrote something I do not
    /// understand" has to be reportable in the same shape as everything else it can say.
    /// </remarks>
    public static TerrainBakeResult Parse(string text)
    {
        string? status = null;
        string? reason = null;
        int? exit = null;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var separator = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator];
            var value = trimmed[(separator + 1)..];
            switch (key)
            {
                case "status":
                    status = value;
                    break;
                case "reason":
                    reason = value;
                    break;
                case "exit":
                    exit = int.TryParse(value, CultureInfo.InvariantCulture, out var code) ? code : null;
                    break;
                default:
                    break;
            }
        }

        var outcome = status switch
        {
            "succeeded" => TerrainBakeOutcome.Succeeded,
            "failed" => TerrainBakeOutcome.Failed,
            "refused" => TerrainBakeOutcome.Refused,
            "interrupted" => TerrainBakeOutcome.Interrupted,
            _ => TerrainBakeOutcome.Unreadable,
        };

        return new TerrainBakeResult(
            outcome,
            exit,
            string.IsNullOrWhiteSpace(reason) ? null : reason);
    }
}
