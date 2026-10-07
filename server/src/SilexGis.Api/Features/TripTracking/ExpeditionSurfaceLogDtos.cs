// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// A camp's surface log: every trip of the camp whose party somebody is following or has just
/// stopped following, with who is underground, who is out and who has not been heard from.
/// </summary>
/// <remarks>
/// <para>
/// <b>A head count, not a map.</b> Nothing in this answer or under it says where anybody is: no
/// station, no depth, no survey and no cave. That is by construction — the members are not here
/// to be withheld — and it is what lets the log tell a reader that somebody is underground in a
/// cave whose position that reader may not be told, exactly as the trip's own watch does.
/// </para>
/// <para>
/// Named properties rather than positional: a member inserted between two of the same type would
/// be absorbed by the neighbour it displaces, and nothing would fail until a reader took one
/// time for another.
/// </para>
/// </remarks>
public sealed record ExpeditionSurfaceLogDto
{
    /// <summary>
    /// Running watches first, the most recently started on top, then the watches closed inside
    /// the installation's window, the most recently closed on top.
    /// </summary>
    public required IReadOnlyList<ExpeditionSurfaceLogTripDto> Trips { get; init; }

    /// <summary>
    /// Whether the camp has more such trips than one answer carries. A yes or no, and what is
    /// left off is always the oldest finished trips — never a running watch while a finished one
    /// is listed.
    /// </summary>
    public required bool Truncated { get; init; }
}

/// <summary>
/// One trip on the log: which trip, how its watch stands, and its party counted.
/// </summary>
public sealed record ExpeditionSurfaceLogTripDto
{
    public required Guid TripLogId { get; init; }

    public required string Title { get; init; }

    public required DateOnly TripDate { get; init; }

    public DateOnly? TripDateEnd { get; init; }

    /// <summary>
    /// Armed or closed — a trip whose watch was never armed is not on the log, and that includes
    /// one filed as closed by importing a recording that nobody followed while it was made.
    /// </summary>
    public required TripTrackingState State { get; init; }

    public DateTimeOffset? ArmedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    /// <summary>
    /// The hour the party said it would be out by, as the trip itself tells every reader of it.
    /// A time and nothing more: this answer does not say whether it has passed, and nothing is
    /// sent, raised or stood down because of it — that is the callout's business, and the
    /// callout is deliberately not on this answer.
    /// </summary>
    public DateTimeOffset? ExpectedReturnAt { get; init; }

    /// <summary>How many of the party the last word puts inside the cave.</summary>
    public required int Underground { get; init; }

    /// <summary>How many of the party the last word puts out of it.</summary>
    public required int Out { get; init; }

    /// <summary>
    /// How many of the party nothing has yet placed inside or out. Its own number and never
    /// folded into either of the others: somebody still at the tents and somebody safely back
    /// are the two answers a coordinator most needs told apart.
    /// </summary>
    public required int Unheard { get; init; }

    /// <summary>
    /// When any of the party was last heard from, by a report of any kind — a note counts, since
    /// a note is word from them. Null when nobody listed has a report.
    /// </summary>
    public DateTimeOffset? LastRecordedAt { get; init; }

    public required IReadOnlyList<ExpeditionSurfaceLogPersonDto> Party { get; init; }
}

/// <summary>
/// One member of a party on the log.
/// </summary>
/// <remarks>
/// The same three members the trip's own watch tells of a person, under the same names, so that
/// one reading of "underground, out or not heard from" serves both screens.
/// </remarks>
public sealed record ExpeditionSurfaceLogPersonDto
{
    public required Guid CaverId { get; init; }

    /// <summary>The name the trip's own page shows this reader for the person.</summary>
    public required string Name { get; init; }

    /// <summary>The last word that spoke to it put them inside the cave.</summary>
    public required bool In { get; init; }

    /// <summary>
    /// The last word that spoke to it was that they are out. False together with
    /// <see cref="In"/> means nothing has placed them either way.
    /// </summary>
    public required bool Out { get; init; }

    /// <summary>When they were last heard from, by a report of any kind.</summary>
    public DateTimeOffset? LastRecordedAt { get; init; }
}
