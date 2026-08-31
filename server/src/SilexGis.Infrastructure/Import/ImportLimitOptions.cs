// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Import;

/// <summary>
/// How much of an uploaded survey file one review, and one confirmation, may take on.
/// </summary>
/// <remarks>
/// Both were constants, and both were wrong for the same reason: they were sized against a
/// hand-recorded list of cave entrances, and the files people actually upload are GPS units
/// emptied at the end of a season. A day's track log is tens of thousands of points on its own.
/// </remarks>
public sealed class ImportLimitOptions
{
    public const string SectionName = "Import";

    /// <summary>
    /// The most objects one confirmation creates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a storage limit. The write path recomputes the containment closure once per created
    /// object, so a confirmation of thousands is a slow request rather than a cheap one — and
    /// splitting it also splits the undo unit into something a person can reason about, which is
    /// worth something on its own the first time somebody imports the wrong file.
    /// </para>
    /// <para>
    /// Raised from the original thousand because a thousand is below what a single field season
    /// produces, and being told to "confirm them in smaller batches" ten times in a row is not a
    /// limit anybody experiences as protective. Raise it further if your imports are bigger and
    /// your patience for a long request is greater; the failure mode of too high is a request that
    /// takes minutes, not one that loses data.
    /// </para>
    /// </remarks>
    public int MaxCommitItems { get; set; } = 10000;

    /// <summary>
    /// The most rows one scan reads out of an uploaded file to build the review list.
    /// </summary>
    /// <remarks>
    /// A file larger than this is still imported whole and still drawn as a layer — only the
    /// review is bounded, and the reviewer is told it was, because a silently truncated candidate
    /// list reads exactly like a complete one.
    /// </remarks>
    public int MaxScanRows { get; set; } = 50000;
}
