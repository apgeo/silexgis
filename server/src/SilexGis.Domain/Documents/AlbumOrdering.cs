// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// Where a picture sits in an album, and what moving one costs.
///
/// <para>
/// Positions are a sparse integer sequence rather than 0,1,2,… so that dropping a picture
/// between two others rewrites one row instead of the whole album. A club album runs to
/// hundreds of pictures and reordering is done by dragging, which means a great many small
/// moves; renumbering every row on each of them is both slow and a large audit entry for
/// somebody nudging one photograph.
/// </para>
/// <para>
/// The gaps run out eventually — dropping repeatedly into the same place halves the space each
/// time — so this also says when a sequence has to be spread out again, which is the one moment
/// the whole album is rewritten.
/// </para>
/// </summary>
public static class AlbumOrdering
{
    /// <summary>Distance between positions when a sequence is laid out fresh.</summary>
    public const int Step = 1024;

    /// <summary>
    /// How many pictures one album may hold. A bound rather than a policy: an album is a set
    /// somebody arranged by hand, and past this it is a filter they should be saving instead.
    /// </summary>
    public const int MaxItems = 2000;

    /// <summary>The position a picture appended to the end takes.</summary>
    public static int Append(int? highestExisting) =>
        highestExisting is { } highest ? highest + Step : Step;

    /// <summary>
    /// The position a picture takes when dropped between two others — or null when there is no
    /// room left between them and the sequence has to be spread out first.
    /// </summary>
    /// <param name="before">The position of the picture it goes after, or null for the start.</param>
    /// <param name="after">The position of the picture it goes before, or null for the end.</param>
    public static int? Between(int? before, int? after)
    {
        if (before is null && after is null)
        {
            return Step;
        }

        if (before is null)
        {
            // Before everything: halve the gap below the first, unless there is none left.
            return after!.Value > int.MinValue / 2 && after.Value - Step < after.Value
                ? Midpoint(after.Value - (2 * Step), after.Value)
                : null;
        }

        if (after is null)
        {
            return before.Value + Step;
        }

        return Midpoint(before.Value, after.Value);
    }

    /// <summary>
    /// The positions a whole album takes when it is spread out again, in the order given.
    /// </summary>
    /// <remarks>
    /// Called when <see cref="Between"/> has run out of room. It is the expensive operation and
    /// the reason the gaps are wide: with a step of a thousand, an album has to be dropped into
    /// the same gap ten times running before this is needed at all.
    /// </remarks>
    public static IReadOnlyList<int> Respread(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return [.. Enumerable.Range(1, count).Select(i => i * Step)];
    }

    /// <summary>A position exactly between two others, or null when they are adjacent.</summary>
    private static int? Midpoint(int before, int after)
    {
        if (after - before < 2)
        {
            return null;
        }

        // Written as a difference rather than as a sum of the two, which would overflow for
        // positions near the ends of the range.
        return before + ((after - before) / 2);
    }
}
