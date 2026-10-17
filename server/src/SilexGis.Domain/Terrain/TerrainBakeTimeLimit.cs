// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Terrain;

/// <summary>
/// A bake stopped because it reached this installation's time limit: how long it had run, and what
/// the limit was.
/// </summary>
/// <remarks>
/// <para>
/// The two numbers are written into the build's message, in a sentence somebody reading the row can
/// act on, and read back out of it for the screen, which says the same thing in the reader's own
/// language. Both directions live here, in one type, so the sentence and the reading of it cannot
/// drift apart: a reworded sentence that the reader no longer recognised would leave the screen
/// with no numbers to show and nothing anywhere saying why.
/// </para>
/// <para>
/// Kept in the message rather than in columns of their own because they describe one stop of one
/// run, are only ever wanted beside the reason that stop carries, and are cleared with it when the
/// build is run again.
/// </para>
/// </remarks>
/// <param name="ElapsedSeconds">How long the bake had been waited for when it was stopped.</param>
/// <param name="LimitSeconds">The limit in force, after the clamp the setting is held to.</param>
public sealed partial record TerrainBakeTimeLimit(long ElapsedSeconds, long LimitSeconds)
{
    /// <summary>The setting that raises the limit, as an operator writes it in the environment file.</summary>
    public const string Setting = "SILEXGIS__Terrain__BakeTimeoutSeconds";

    /// <summary>The shortest limit the setting is brought up to.</summary>
    public const int ShortestSeconds = 60;

    /// <summary>The longest limit the setting is brought down to: seven days.</summary>
    public const int LongestSeconds = 604_800;

    /// <summary>The limit a configured number of seconds comes to.</summary>
    /// <remarks>
    /// Clamped rather than refused: this is a safety net, and a net set to nothing would abandon
    /// every real bake, while one set to "for ever" is the absence of a net that the queue already
    /// is. Zero is therefore a minute, not "no limit".
    /// </remarks>
    public static TimeSpan Limit(int configuredSeconds) =>
        TimeSpan.FromSeconds(Math.Clamp(configuredSeconds, ShortestSeconds, LongestSeconds));

    /// <summary>
    /// What the build's row says about it, naming the time it ran, the limit and the setting.
    /// </summary>
    /// <param name="confirmed">
    /// Whether the tile-maker said it stopped. When it has not, whatever it writes from now on is
    /// discarded when the build is run again, and running it again waits for that answer.
    /// </param>
    public string Sentence(bool confirmed)
    {
        var ran = Hours(ElapsedSeconds);
        var limit = Hours(LimitSeconds);
        var stopped = confirmed
            ? "The tile maker stopped and what it had written was discarded."
            : "The tile maker was told to stop and has not confirmed it yet; run the build again once it has.";

        // Kept well inside the five hundred characters a build's message holds, because the
        // numbers have to survive being stored to be read back.
        var elapsed = ElapsedSeconds.ToString(CultureInfo.InvariantCulture);
        var seconds = LimitSeconds.ToString(CultureInfo.InvariantCulture);
        return $"Time limit reached: the bake ran {ran} h ({elapsed} s); the limit is {limit} h "
            + $"({Setting}={seconds}). {stopped} For a large area, raise {Setting} and restart "
            + "the application; running the build again keeps what it obtained and prepared.";
    }

    /// <summary>
    /// The numbers a sentence written by <see cref="Sentence"/> carries, or null for any other text.
    /// </summary>
    public static TerrainBakeTimeLimit? TryRead(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        var match = Written().Match(message);
        if (!match.Success
            || !long.TryParse(match.Groups["elapsed"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var elapsed)
            || !long.TryParse(match.Groups["limit"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit))
        {
            return null;
        }

        return new TerrainBakeTimeLimit(elapsed, limit);
    }

    private static string Hours(long seconds) =>
        (seconds / 3600d).ToString("0.#", CultureInfo.InvariantCulture);

    [GeneratedRegex(
        @"^Time limit reached: the bake ran [0-9.]+ h \((?<elapsed>[0-9]+) s\); the limit is [0-9.]+ h "
        + @"\(SILEXGIS__Terrain__BakeTimeoutSeconds=(?<limit>[0-9]+)\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex Written();
}
