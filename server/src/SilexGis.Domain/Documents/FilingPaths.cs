// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// What a path from outside is allowed to become inside the filing tree.
///
/// <para>
/// Three quite different things hand paths to this: a browser reporting the folder a dropped
/// file came from, an archive naming its own entries, and a walk over a directory on the
/// server's own disk. All three are untrusted in the same way and for the same reason — the
/// path is written by whoever produced the source, and an archive entry called
/// <c>../../../../etc/passwd</c> is the oldest trick there is. So there is one rule here
/// rather than one per caller: a path is either turned into a list of plain cabinet names,
/// or it is refused, and nothing downstream ever sees a separator again.
/// </para>
/// <para>
/// Refusal rather than sanitisation is deliberate. Silently rewriting <c>..</c> into a
/// folder called "dotdot" would file somebody's archive somewhere they did not ask for and
/// call it success; the report says the entry was refused and names it, which is a thing the
/// person who made the archive can go and look at.
/// </para>
/// </summary>
public static class FilingPaths
{
    /// <summary>
    /// Longest a single cabinet name may be — the width of the column it lands in. A source
    /// folder named longer than this is refused rather than truncated: two folders differing
    /// only past this length would silently become one shelf holding both their contents.
    /// </summary>
    public const int MaxSegmentLength = 200;

    /// <summary>
    /// How many folder levels a source path may contribute. Below the tree's own depth cap,
    /// because the mirrored subtree hangs under a cabinet somebody chose — the two have to
    /// fit together, and the placement check applies the real cap afterwards.
    /// </summary>
    public const int MaxSegments = CabinetHierarchyRules.MaxDepth - 1;

    /// <summary>
    /// The folder names a source path contributes, outermost first, with the file name
    /// itself dropped — or null when the path may not be filed at all.
    /// </summary>
    /// <remarks>
    /// Both separators are treated as separators whatever the platform: an archive written on
    /// Windows names its entries with backslashes and is routinely expanded on Linux, and a
    /// path that is one folder on one machine and one long file name on another is exactly
    /// the ambiguity a traversal hides in.
    /// </remarks>
    public static IReadOnlyList<string>? FolderSegmentsOf(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return [];
        }

        // A rooted path names a place rather than a position inside the source, and there is
        // nothing sensible to file it as. Both spellings are refused on every platform, since
        // the machine that wrote the path is not the machine reading it.
        if (sourcePath.StartsWith('/') || sourcePath.StartsWith('\\') || HasDriveLetter(sourcePath))
        {
            return null;
        }

        var parts = sourcePath.Split(['/', '\\'], StringSplitOptions.None);
        var segments = new List<string>(parts.Length);

        // The last part is the file's own name and never becomes a cabinet.
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var segment = parts[i].Trim();

            // An empty part is a doubled separator or a trailing one: it names no folder, so
            // it is skipped rather than refused. "a//b" is the same place as "a/b".
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == ".." || !IsFilableName(segment))
            {
                return null;
            }

            segments.Add(segment);
        }

        return segments.Count > MaxSegments ? null : segments;
    }

    /// <summary>
    /// The file's own name, stripped of every folder part — what the stored document is
    /// titled. Never null: a path ending in a separator has no file to store and is refused
    /// before this is asked.
    /// </summary>
    public static string FileNameOf(string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        var lastSeparator = sourcePath.LastIndexOfAny(['/', '\\']);
        return lastSeparator < 0 ? sourcePath : sourcePath[(lastSeparator + 1)..];
    }

    /// <summary>
    /// Whether a name can be a cabinet: present, not over-long, and free of the control
    /// characters and separators that would make it something other than a name.
    /// </summary>
    /// <remarks>
    /// Control characters are refused rather than stripped because they are never in a folder
    /// name by accident — a newline in the middle of one is either a corrupted archive or
    /// somebody trying to make a log entry read as two.
    /// </remarks>
    public static bool IsFilableName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxSegmentLength)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsControl(c) || c == '/' || c == '\\')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the path opens with a Windows drive specification (<c>C:\</c>, or the bare
    /// <c>C:</c> form that names the drive's own current directory).
    /// </summary>
    private static bool HasDriveLetter(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
}
