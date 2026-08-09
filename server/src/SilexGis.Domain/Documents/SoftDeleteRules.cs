// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>
/// How long a deleted document can still be got back, and when its bytes actually go.
///
/// <para>
/// Deleting is soft because a document is reached from many directions at once — attachments,
/// albums, cabinets, links, search — and the person deleting is looking at exactly one of them.
/// Marking it removes it from all of them immediately, which is what they asked for; purging it
/// later is what makes the mistake recoverable in the window where mistakes are noticed.
/// </para>
/// <para>
/// The window is the whole of the design. Too short and "I deleted the wrong survey" is
/// unrecoverable; unbounded and the store never shrinks, which is the state the development
/// archive is already in and the reason this exists.
/// </para>
/// </summary>
public static class SoftDeleteRules
{
    /// <summary>
    /// How long a deleted document stays restorable before its bytes are eligible to go.
    /// Configuration, with a default long enough that somebody coming back from a week in a
    /// cave can still undo what they did before they left.
    /// </summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

    /// <summary>Refusal: the document is deleted, so it cannot be changed.</summary>
    public const string DeletedCode = "document.deleted";

    /// <summary>Refusal: it is not deleted, so there is nothing to restore.</summary>
    public const string NotDeletedCode = "document.not_deleted";

    /// <summary>
    /// Whether a document deleted at <paramref name="deletedAt"/> may still be restored.
    /// </summary>
    /// <remarks>
    /// Restorability and purge-eligibility are two readings of one line, deliberately: a
    /// document that can still be restored must not be purged, and one whose bytes are gone
    /// must not offer a restore that would produce a row pointing at nothing.
    /// </remarks>
    public static bool IsRestorable(DateTimeOffset deletedAt, DateTimeOffset now, TimeSpan retention) =>
        now < deletedAt + retention;

    /// <summary>Whether a deleted document is now past its window and may be purged.</summary>
    public static bool IsPurgeable(DateTimeOffset deletedAt, DateTimeOffset now, TimeSpan retention) =>
        !IsRestorable(deletedAt, now, retention);

    /// <summary>
    /// The moment the sweep measures against: anything deleted at or before this is past its
    /// window.
    /// </summary>
    /// <remarks>
    /// Given to the sweep as a cut-off rather than having it test each row, so the query is one
    /// indexed comparison over a column rather than a predicate the database cannot use an index
    /// for.
    /// </remarks>
    public static DateTimeOffset PurgeCutoff(DateTimeOffset now, TimeSpan retention) => now - retention;

    /// <summary>
    /// How long is left before a deleted document is purged, or zero once it is past.
    /// What a "deleted, 12 days left" line is drawn from.
    /// </summary>
    public static TimeSpan RemainingWindow(
        DateTimeOffset deletedAt, DateTimeOffset now, TimeSpan retention)
    {
        var remaining = deletedAt + retention - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
