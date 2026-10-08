// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Common;

/// <summary>
/// A trip's tracking journal — what the watch was, where each person stood and what was reported,
/// in time order — for a part of the application that is not the tracking surface itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is an interface here.</b> A feature area does not reach into another's internals.
/// Something outside tracking that wants to print the journal — a trip's write-up is the first —
/// asks this, and the tracking area answers it by running the very routines its own screen reads
/// run. So a place kept from a reader on the tracking screen is kept from the same reader in
/// whatever is built from this, by construction rather than by a second copy of the rule.
/// </para>
/// <para>
/// <b>What it deliberately does not decide.</b> Whether the trip may be read at all. The caller has
/// already found that out, under its own rule and possibly for a different reader than the one the
/// journal is worded for: a document kept on a trip is made because somebody who may write the trip
/// asked, and says only what every reader of the trip may be told. The two are separate arguments
/// for exactly that reason.
/// </para>
/// </remarks>
public interface ITripTrackingJournal
{
    /// <summary>
    /// The journal of one trip as <paramref name="reading"/> may be told it, or null when the trip
    /// has neither a watch nor a single report — which is also the answer for a trip that has been
    /// deleted or was never there.
    /// </summary>
    /// <param name="tripLogId">The trip.</param>
    /// <param name="reading">
    /// Whose rights decide which places are stated. Every station, depth and survey is told or
    /// withheld against this and nothing else.
    /// </param>
    /// <param name="namedFor">
    /// Whose view of people decides the names. A name is not location data and follows the rule
    /// every signed-in surface names a person by, which is asked of an account; with none, nobody
    /// is named.
    /// </param>
    /// <param name="ct">Cancels the read.</param>
    Task<TripTrackingJournal?> ReadAsync(
        Guid tripLogId, AccessContext reading, UserContext? namedFor, CancellationToken ct);
}

/// <summary>
/// One trip's tracking journal as one reader may be told it.
/// </summary>
/// <param name="State">
/// The watch as it stands. <see cref="TripTrackingState.Off"/> beside reports is a trip whose
/// positions were brought in without a watch ever being started.
/// </param>
/// <param name="StartedAt">
/// When the watch was first started; null for one never started, which includes one an import
/// wrote already closed.
/// </param>
/// <param name="ClosedAt">When the watch was closed; null while it runs or was never started.</param>
/// <param name="People">
/// Everybody the trip names and everybody the log speaks of: the trip's own order first, then those
/// since taken off it, in the order the log first mentions each.
/// </param>
/// <param name="Entries">Every report on the log, oldest first. A report taken off the log is not here.</param>
/// <param name="AnyWithheld">
/// Whether anything about a place was kept back from this reader — a report's, somebody's last
/// place, or the survey the watch itself was set on.
/// </param>
public sealed record TripTrackingJournal(
    TripTrackingState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<TripTrackingJournalPerson> People,
    IReadOnlyList<TripTrackingJournalEntry> Entries,
    bool AnyWithheld);

/// <summary>
/// One person in a trip's tracking journal.
/// </summary>
/// <param name="CaverId">The person.</param>
/// <param name="Name">The name the reader sees this person under; null where they are told none.</param>
/// <param name="OnRoster">False for somebody the trip no longer lists while reports about them remain.</param>
/// <param name="Number">
/// The place this person holds in the party's numbering; null only for somebody the trip never listed.
/// </param>
/// <param name="Team">The title of the team they were last reported with, if any.</param>
/// <param name="Standing">Underground, out, or never heard from — the rule the tracking screen states it by.</param>
/// <param name="LastHeardAt">
/// The moment of the last report of any kind about them. Told whatever is withheld: that somebody
/// was heard from, and when, is the trip's.
/// </param>
/// <param name="Station">The station they were last reported at, where the reader may be told it.</param>
/// <param name="DepthM">The depth they were last reported at, where the reader may be told it.</param>
/// <param name="PlaceAt">
/// The moment of the report that place comes from — which is not <paramref name="LastHeardAt"/>
/// when a note or an exit followed it. Withheld together with the place.
/// </param>
/// <param name="PlaceWithheld">
/// True when this person was reported at a place and the reader may not be told it, so that
/// "no place" and "a place you are not told" are never one answer.
/// </param>
public sealed record TripTrackingJournalPerson(
    Guid CaverId,
    string? Name,
    bool OnRoster,
    int? Number,
    string? Team,
    TripStanding Standing,
    DateTimeOffset? LastHeardAt,
    string? Station,
    decimal? DepthM,
    DateTimeOffset? PlaceAt,
    bool PlaceWithheld);

/// <summary>
/// One report in a trip's tracking journal.
/// </summary>
/// <param name="At">The moment the report is about, as whoever recorded it gave it.</param>
/// <param name="CaverId">Whom it is about.</param>
/// <param name="Name">The name the reader sees that person under; null where they are told none.</param>
/// <param name="Team">The title of the team the report was recorded for, if any.</param>
/// <param name="Kind">What was reported.</param>
/// <param name="Station">The station named, where the reader may be told it.</param>
/// <param name="DepthM">The depth given, where the reader may be told it.</param>
/// <param name="Note">The free text recorded with it. It is the trip's and is never withheld.</param>
/// <param name="Withheld">
/// True when the report says where and the reader may not be told: its moment, its kind and its
/// note stand, and <paramref name="Station"/> and <paramref name="DepthM"/> are both null.
/// </param>
public sealed record TripTrackingJournalEntry(
    DateTimeOffset At,
    Guid CaverId,
    string? Name,
    string? Team,
    TripPositionEventKind Kind,
    string? Station,
    decimal? DepthM,
    string? Note,
    bool Withheld);
