// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// Pure semantics of the cabinet filing tree: what a cabinet's materialized path is, how
/// the paths of a subtree are rewritten when it is re-parented, and the two grounds on
/// which a placement is refused — it would close a cycle, or it would push the tree past
/// the depth cap.
/// <para>
/// A path is the chain of cabinet ids from the root down to and including the cabinet
/// itself, dot-separated. That makes a subtree one indexed prefix match and a breadcrumb a
/// parse rather than a walk, which is why read paths never recurse: they match the stored
/// path. The cabinet write service is the only caller that mutates the tree — it loads
/// state, delegates here, and persists the recomputed paths, so "where does this cabinet
/// sit" has one answer.
/// </para>
/// </summary>
public static class CabinetHierarchyRules
{
    /// <summary>Separator between path labels — the one the path column's type uses.</summary>
    public const char Separator = '.';

    /// <summary>
    /// How deep the tree may nest, counting a root cabinet as level 1. This is a sanity
    /// bound against pathological trees, not a filing policy: ten levels is far past any
    /// scheme a caving club writes down, and it keeps a path short enough that the prefix
    /// match stays a cheap index read.
    /// </summary>
    public const int MaxDepth = 10;

    /// <summary>Refusal: the move would put a cabinet inside its own subtree.</summary>
    public const string CycleCode = "cabinet.cycle";

    /// <summary>Refusal: the placement would nest deeper than <see cref="MaxDepth"/>.</summary>
    public const string TooDeepCode = "cabinet.too_deep";

    /// <summary>
    /// The path a cabinet takes under a given parent path (null or empty for a root).
    /// Ids keep their hyphens: those are legal path labels on the database this runs on.
    /// </summary>
    public static string PathOf(Guid cabinetId, string? parentPath) =>
        string.IsNullOrEmpty(parentPath) ? cabinetId.ToString() : parentPath + Separator + cabinetId;

    /// <summary>How many levels a path names; 0 for an empty one.</summary>
    public static int DepthOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return 0;
        }

        var depth = 1;
        foreach (var c in path)
        {
            if (c == Separator)
            {
                depth++;
            }
        }

        return depth;
    }

    /// <summary>
    /// The cabinet ids a path names, root first and ending with the cabinet itself — the
    /// breadcrumb, without a query per level.
    /// </summary>
    public static IReadOnlyList<Guid> IdsOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return [];
        }

        var labels = path.Split(Separator);
        var ids = new Guid[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            ids[i] = Guid.Parse(labels[i]);
        }

        return ids;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="ancestorPath"/> or sits below
    /// it. Compared on a label boundary, so "a.bc" is not read as living under "a.b".
    /// </summary>
    public static bool IsWithin(string path, string ancestorPath)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(ancestorPath);

        return path.Equals(ancestorPath, StringComparison.Ordinal)
            || (path.Length > ancestorPath.Length
                && path[ancestorPath.Length] == Separator
                && path.StartsWith(ancestorPath, StringComparison.Ordinal));
    }

    /// <summary>
    /// The path a row inside a moved subtree takes once that subtree lands elsewhere: the
    /// moved cabinet's old path prefix is swapped for its new one, and everything below it
    /// keeps its relative position.
    /// </summary>
    public static string Rebase(string path, string movedPath, string newMovedPath)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(movedPath);
        ArgumentNullException.ThrowIfNull(newMovedPath);

        if (!IsWithin(path, movedPath))
        {
            throw new ArgumentException(
                $"'{path}' is not inside the moved subtree '{movedPath}'.", nameof(path));
        }

        return newMovedPath + path[movedPath.Length..];
    }

    /// <summary>
    /// Why a placement must be refused, or null when it is sound.
    /// <paramref name="movedPath"/> is the current path of the cabinet being moved — null
    /// when creating a new one, which cannot close a cycle. <paramref name="newParentPath"/>
    /// is the path of the cabinet it will sit under, null for a root.
    /// <paramref name="subtreeHeight"/> is how many levels hang below the moved cabinet
    /// (0 for a leaf or a new cabinet), because a move takes its whole subtree with it and
    /// the deepest descendant is what the cap has to hold.
    /// </summary>
    public static string? ValidatePlacement(string? movedPath, string? newParentPath, int subtreeHeight = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(subtreeHeight);

        if (!string.IsNullOrEmpty(movedPath)
            && !string.IsNullOrEmpty(newParentPath)
            && IsWithin(newParentPath, movedPath))
        {
            return CycleCode;
        }

        return DepthOf(newParentPath) + 1 + subtreeHeight > MaxDepth ? TooDeepCode : null;
    }

    /// <summary>
    /// How many levels hang below the cabinet at <paramref name="movedPath"/>, given every
    /// path in its subtree (which includes its own). 0 when it is a leaf.
    /// </summary>
    public static int HeightOf(string movedPath, IEnumerable<string> subtreePaths)
    {
        ArgumentNullException.ThrowIfNull(movedPath);
        ArgumentNullException.ThrowIfNull(subtreePaths);

        var root = DepthOf(movedPath);
        var deepest = root;
        foreach (var path in subtreePaths)
        {
            var depth = DepthOf(path);
            if (depth > deepest)
            {
                deepest = depth;
            }
        }

        return deepest - root;
    }
}
