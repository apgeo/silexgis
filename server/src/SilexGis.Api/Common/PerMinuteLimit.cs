// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace SilexGis.Api.Common;

/// <summary>
/// Reads one of the application's requests-a-minute settings, and refuses a value no window can
/// be built from.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a zero did before this.</b> The limiter builds a window lazily, the first time a
/// request arrives for a given key, and it throws on a limit that is not positive. So an
/// installation started normally, reported healthy, and then answered a server error to every
/// request on the limited surface — every sign-in, or every read of every published page — with
/// nothing in the message to connect it to a setting. Read here, while the application is still
/// being put together, the same value is a container that does not come up and a line that names
/// the variable.
/// </para>
/// <para>
/// <b>Why the message says what it does.</b> Somebody who writes zero means one of two things, and
/// neither is "refuse every request with an error". "No limit" has no value of its own and is
/// said with a large number. "Let nobody in" is not something a rate limit is for: the surface is
/// closed by whatever opens it — a link is revoked, sign-in is not offered.
/// </para>
/// </remarks>
public static class PerMinuteLimit
{
    /// <summary>The setting's value, or its default where the installation says nothing.</summary>
    /// <exception cref="InvalidOperationException">The value is zero or negative.</exception>
    public static int Read(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration.GetValue(key, fallback);
        return Problem(key, value) is { } problem ? throw new InvalidOperationException(problem) : value;
    }

    /// <summary>What is wrong with a value, in an operator's words; nothing when it is usable.</summary>
    public static string? Problem(string key, int value) =>
        value > 0
            ? null
            : $"{key} (SILEXGIS__{key.Replace(":", "__", StringComparison.Ordinal)}) must be at least 1; got "
              + $"{value.ToString(CultureInfo.InvariantCulture)}. It is how many requests one address may make "
              + "in a minute, and no window can be built from a number that is not positive. There is no "
              + "value meaning \"no limit\": set a large number instead (100000 is as good as none). A limit "
              + "is not a way to close a surface either — that is done where the surface is opened.";
}
