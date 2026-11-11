// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace SilexGis.Domain.Scans;

/// <summary>
/// What an HDMapping session archive must be before anything unpacks it or hands it to HDMapping:
/// an archive that does not unpack to far more than it holds, and a <c>session.mjs</c> whose file
/// and folder names all stay inside the session folder.
/// </summary>
/// <remarks>
/// <para>
/// <c>session.mjs</c> is JSON naming the session's files — each scan's point file, the pose files,
/// the folders written to. HDMapping reads a relative name against the session's own folder and an
/// absolute one as it stands, so a session that names <c>/elsewhere/cloud.laz</c> or
/// <c>../../another-job/cloud.laz</c> would have the export read a file that is not part of it —
/// another upload's cloud, on the volume every job shares. Every such name must be relative and
/// free of parent steps.
/// </para>
/// <para>
/// The scan worker checks the same rules again when it unpacks a session, with the numbers below
/// written out in its own script; a change here is a change there.
/// </para>
/// </remarks>
public static class HdmappingSessionRules
{
    /// <summary>The session's description at the root of the archive.</summary>
    public const string SessionFile = "session.mjs";

    /// <summary>
    /// How many times the archive's size its members may add up to, unpacked. A session is mostly
    /// LAZ, stored as it is because it is compressed already; the poses and settings beside it are
    /// small text. Four times leaves wide room for a session of unusually many small files and
    /// still refuses an archive built to unpack into the whole disk.
    /// </summary>
    public const int MaxUnpackedFactor = 4;

    /// <summary>
    /// Bytes allowed unpacked on top of <see cref="MaxUnpackedFactor"/>: 1 MiB, so a tiny session,
    /// whose few text files compress far better than four to one, is not refused for its ratio.
    /// </summary>
    public const long UnpackedAllowanceBytes = 1024 * 1024;

    /// <summary>The largest <c>session.mjs</c> read, in bytes: 16 MiB, hundreds of times a real one.</summary>
    public const long MaxSessionFileBytes = 16L * 1024 * 1024;

    /// <summary>Whether an archive of <paramref name="archiveBytes"/> may unpack to <paramref name="unpackedBytes"/>.</summary>
    public static bool UnpackedFits(long archiveBytes, long unpackedBytes)
    {
        var archive = Math.Max(0, archiveBytes);
        var allowed = archive > (long.MaxValue - UnpackedAllowanceBytes) / MaxUnpackedFactor
            ? long.MaxValue
            : archive * MaxUnpackedFactor + UnpackedAllowanceBytes;
        return unpackedBytes >= 0 && unpackedBytes <= allowed;
    }

    /// <summary>
    /// Whether a name in <c>session.mjs</c> stays inside the session folder: empty, or relative with
    /// no parent step, no drive or scheme and no NUL. Both separators are taken as separators,
    /// since a session written on Windows uses either.
    /// </summary>
    public static bool IsContainedName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
        {
            return true;
        }

        if (name[0] is '/' or '\\' || name.Contains(':') || name.Contains('\0'))
        {
            return false;
        }

        return !name.Replace('\\', '/').Split('/').Any(part => part.Trim() == "..");
    }

    /// <summary>
    /// Why a parsed <c>session.mjs</c> may not be used, or null when every file and folder name it
    /// holds stays inside the session folder. Names are found by their keys wherever they sit in
    /// the document — <c>file_name</c>, <c>poses_file_name</c>, <c>folder_name</c>,
    /// <c>out_folder_name</c> and their kin — so a name HDMapping adds later is checked too.
    /// </summary>
    public static string? SessionProblem(JsonElement session)
    {
        if (session.ValueKind != JsonValueKind.Object)
        {
            return "The session file is not a JSON object.";
        }

        return FirstEscape(session, key: null) is { } key
            ? $"The session file names a file outside the session folder (under \"{(key.Length > 64 ? key[..64] : key)}\"): every file and folder it names must be a relative name inside it."
            : null;
    }

    /// <summary>Whether a key of <c>session.mjs</c> holds a file or folder name.</summary>
    public static bool IsPathKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var lower = key.ToLowerInvariant();
        return lower.EndsWith("file_name", StringComparison.Ordinal)
            || lower.EndsWith("file_names", StringComparison.Ordinal)
            || lower.EndsWith("folder_name", StringComparison.Ordinal)
            || lower.EndsWith("_folder", StringComparison.Ordinal)
            || lower.EndsWith("_path", StringComparison.Ordinal)
            || lower.EndsWith("_directory", StringComparison.Ordinal);
    }

    private static string? FirstEscape(JsonElement element, string? key)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (FirstEscape(property.Value, property.Name) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (FirstEscape(item, key) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case JsonValueKind.String when key is not null && IsPathKey(key):
                return IsContainedName(element.GetString()!) ? null : key;
            default:
                return null;
        }
    }
}
