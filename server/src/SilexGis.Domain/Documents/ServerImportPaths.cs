// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// Whether a directory on the server's own disk is one an administrator is allowed to import
/// from.
///
/// <para>
/// Importing from the filesystem is the one path in this application where an authenticated
/// request names a location the server reads directly, so it is the one place where a mistake
/// hands over whatever the service account can open — configuration files, the database's own
/// data directory, other people's home directories. It is therefore not gated on the
/// administrator role alone. The operator lists the directories the feature may reach when
/// they deploy the installation, and a path outside all of them is refused however privileged
/// the caller is. An installation that lists none has the feature switched off, which is the
/// right default for something an operator has to opt into.
/// </para>
/// <para>
/// Containment is decided here, on already-resolved absolute paths. Resolving them — walking
/// symbolic links until the real location is known — is I/O and belongs to the caller, but it
/// is not optional: without it a link inside an allowed directory is a way to read anything
/// the link points at, which is precisely the check this exists to make.
/// </para>
/// </summary>
public static class ServerImportPaths
{
    /// <summary>Refusal: the path is not inside any configured root.</summary>
    public const string OutsideRootsCode = "import.path_not_allowed";

    /// <summary>
    /// Whether this caller may make the server read a location on its own disk at all.
    /// </summary>
    /// <remarks>
    /// Full administrators only, and deliberately not a grantable right: there is no object to
    /// scope one to, and the question is about the machine rather than about any content on it.
    /// Every feature that names a directory for the server to read asks this one predicate — two
    /// features answering it differently would mean the same capability had two bars, and the
    /// lower one would be the real one.
    /// </remarks>
    public static bool MayReadServerDisk(Access.AccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IsFullAdmin;
    }

    /// <summary>Refusal: the installation lists no roots, so the feature is off.</summary>
    public const string NoRootsCode = "import.no_roots_configured";

    /// <summary>Refusal: the directory does not exist, or cannot be read.</summary>
    public const string NotFoundCode = "import.directory_not_found";

    /// <summary>
    /// Whether <paramref name="resolvedCandidate"/> is one of the roots or sits inside one.
    /// Both arguments must already be absolute and link-resolved.
    /// </summary>
    /// <remarks>
    /// The comparison follows the platform, because the filesystem does: on Windows
    /// <c>C:\Archive</c> and <c>c:\archive</c> are one directory, and a check that called them
    /// different would refuse a legitimate path — or, spelled the other way round, would let
    /// one through. The separator is appended before comparing so that <c>/srv/archive-old</c>
    /// is not read as living inside <c>/srv/archive</c>.
    /// </remarks>
    public static bool IsWithinRoots(IReadOnlyCollection<string> resolvedRoots, string resolvedCandidate)
    {
        ArgumentNullException.ThrowIfNull(resolvedRoots);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedCandidate);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var candidate = WithTrailingSeparator(resolvedCandidate);

        foreach (var root in resolvedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var normalizedRoot = WithTrailingSeparator(root);
            if (candidate.StartsWith(normalizedRoot, comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A directory path ending in exactly one separator, so that a prefix comparison can only
    /// match on a directory boundary.
    /// </summary>
    private static string WithTrailingSeparator(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed + Path.DirectorySeparatorChar;
    }
}
