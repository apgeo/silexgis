// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Documents;

namespace SilexGis.Domain.Trips;

/// <summary>
/// How long a deleted trip can still be put back, and when it goes for good.
/// </summary>
/// <remarks>
/// <para>
/// A deleted trip keeps every row it had — its roster, the caves it names, its place in a camp,
/// what was hung on it, who may read it — so putting it back is exact. What ends that is one
/// scheduled pass, which removes a trip once it has been deleted for longer than the window; and
/// the window is the whole of the design, for the reason it is for a deleted document: too short
/// and a mistake noticed on Monday is unrecoverable, unbounded and nothing anybody deletes ever
/// leaves.
/// </para>
/// <para>
/// One departure from the documents' rule, and it is deliberate. There, a window of nothing is a
/// real setting — deletion means deletion, at the next pass. Here a window of nothing means the
/// pass never runs and a deleted trip is kept until an operator says otherwise, because a trip
/// has no bytes whose weight makes keeping it a cost, and an installation that wants nothing
/// removed without a person deciding should be able to say so with the one number it already
/// has to set.
/// </para>
/// </remarks>
public static class TripDeletionRules
{
    /// <summary>
    /// The window an installation gets when it names none: the one a deleted document gets, so
    /// "how long do I have to change my mind" has one answer across the archive unless somebody
    /// chose otherwise.
    /// </summary>
    public static readonly int DefaultRetentionDays = (int)SoftDeleteRules.DefaultRetention.TotalDays;

    /// <summary>Refusal: the trip is not deleted, so there is nothing to restore.</summary>
    public const string NotDeletedCode = "trip_log.not_deleted";

    /// <summary>
    /// Refusal: the trip was deleted longer ago than this installation keeps one. The pass that
    /// removes it may not have reached it yet, but it is owed nothing more: the moment a list
    /// said it would go has passed, and a restore that still worked would make that moment mean
    /// "or whenever the pass next runs".
    /// </summary>
    public const string RestoreWindowPassedCode = "trip_log.restore_window_passed";

    /// <summary>
    /// The window a configured number of days stands for, or null when deleted trips are kept
    /// until somebody says otherwise — which is what zero or less asks for.
    /// </summary>
    public static TimeSpan? Window(int retentionDays) =>
        retentionDays > 0 ? TimeSpan.FromDays(retentionDays) : null;

    /// <summary>
    /// When a trip deleted at <paramref name="deletedAt"/> stops being restorable and becomes
    /// eligible to be removed, or null when nothing removes it. What a "12 days left" line is
    /// drawn from.
    /// </summary>
    public static DateTimeOffset? RemovedAt(DateTimeOffset deletedAt, TimeSpan? window) =>
        window is { } kept ? deletedAt + kept : null;

    /// <summary>Whether a trip deleted at <paramref name="deletedAt"/> may still be put back.</summary>
    /// <remarks>
    /// The same line the pass measures against, read from the other side: a trip that may still
    /// be restored must not be removed, and one the pass may remove must not be offered back.
    /// </remarks>
    public static bool IsRestorable(DateTimeOffset deletedAt, DateTimeOffset now, TimeSpan? window) =>
        window is not { } kept || SoftDeleteRules.IsRestorable(deletedAt, now, kept);

    /// <summary>
    /// The moment the pass measures against — anything deleted at or before it is past its
    /// window — or null when there is no window and so nothing for a pass to remove.
    /// </summary>
    /// <remarks>
    /// A cut-off rather than a test per row, so the selection is one indexed comparison.
    /// </remarks>
    public static DateTimeOffset? PurgeCutoff(DateTimeOffset now, TimeSpan? window) =>
        window is { } kept ? SoftDeleteRules.PurgeCutoff(now, kept) : null;
}
