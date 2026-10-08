// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// A trip's tracking journal for the rest of the application, answered by the tracking screen's
/// own reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is decided here.</b> Who is in the party and in what order, where each of them
/// stands, which report is the last that claimed a place, and what of a place this reader may be
/// told all come from the state read; each report, and what is taken out of it, comes from the
/// log's read over the log's own rows. This turns those two answers into one plain shape and adds
/// names. A rule changed on the tracking screen is therefore changed in everything printed from the
/// journal, and a place this would state is a place the screen states to the same reader.
/// </para>
/// <para>
/// <b>What is left behind on purpose.</b> Whether somebody has been quiet for too long is measured
/// from the present moment, and what a published page shows is a statement about a page: neither is
/// part of a record of what was reported, and a document built from this must come out the same
/// tomorrow as today. So they are read — the routine answers them together with the rest — and not
/// carried.
/// </para>
/// </remarks>
public sealed class TripTrackingJournalReads(
    SilexGisDbContext db,
    IAccessService access,
    FeatureProtection protection,
    IOptions<TripTrackingOptions> options,
    TimeProvider clock) : ITripTrackingJournal
{
    /// <inheritdoc />
    public async Task<TripTrackingJournal?> ReadAsync(
        Guid tripLogId, AccessContext reading, UserContext? namedFor, CancellationToken ct)
    {
        var names = new NamedFor(namedFor);
        var state = await TripTrackingEndpoints.StateShownToAsync(
            db, access, protection, reading, tripLogId, options, clock, names, ct);

        // The log's own rows in the log's own order, turned round: the screen lists the newest
        // report first and a journal is read from the beginning. The whole of it — a tracked
        // trip's log is small, and the state read above has just folded every row of it too.
        //
        // The reports about people, which is what every line of a journal is keyed by: who, then
        // what was said of them. A note about the cave is about nobody and is not carried, as it
        // is not in the log written out as a sheet — printed in a line made for a person it would
        // read as something said of somebody the document does not name.
        var rows = await TripTrackingEndpoints.NewestFirst(TripTrackingEndpoints.LogOf(db, tripLogId).AboutPeople())
            .ToListAsync(ct);
        rows.Reverse();

        // Nothing returns a watch to "off", so off means never started; with no report either
        // there is no journal to speak of. A deleted trip arrives here too: its watch and its
        // reports are hidden with it, and it answers as a trip that was never followed.
        if (state.State == TripTrackingState.Off && rows.Count == 0) return null;

        var shown = await TripTrackingEndpoints.ShownToAsync(db, access, protection, reading, tripLogId, rows, ct);

        // People the trip still lists are named by the trip to whoever reads it; the state read
        // names only those it no longer lists. Here both are wanted, by the one rule every
        // signed-in surface names a person by.
        var formerNames = state.Participants
            .Where(person => person.Name is not null)
            .ToDictionary(person => person.CaverId, person => person.Name!);
        var rosterNames = await CaverDirectory.ResolveLabelsAsync(
            db, namedFor, state.Participants.Where(person => person.OnRoster).Select(person => person.CaverId), ct);
        string? NameOf(Guid caverId) =>
            rosterNames.TryGetValue(caverId, out var listed) ? listed : formerNames.GetValueOrDefault(caverId);

        var teams = state.Teams.ToDictionary(team => team.Id, team => team.Title);
        string? TeamOf(Guid? teamId) => teamId is { } id ? teams.GetValueOrDefault(id) : null;

        var entries = new List<TripTrackingJournalEntry>(rows.Count);
        // Whether the last report that claimed a place for each person was kept back. Read off the
        // log's answer for that report, so that "has no place" and "has one this reader is not
        // told" are told apart without asking the withholding rule a second time.
        var lastPlaceWithheld = new Dictionary<Guid, bool>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var told = shown[i];
            // A report that says where, answered with no station, no depth and no survey: the
            // place was taken out. One that says where and is answered with any of the three is
            // whole — the three are told or withheld together.
            var claimsPlace = TrackingWithholding.HasPosition(row);
            var withheld = claimsPlace
                && told.StationName is null && told.DepthEnteredM is null && told.SurveyModelId is null;
            var about = row.Person();
            if (claimsPlace) lastPlaceWithheld[about] = withheld;

            entries.Add(new TripTrackingJournalEntry(
                told.RecordedAt,
                about,
                NameOf(about),
                TeamOf(told.TeamId),
                told.Kind,
                told.StationName,
                told.DepthEnteredM,
                told.Note,
                withheld));
        }

        var people = state.Participants
            .Select(person => new TripTrackingJournalPerson(
                person.CaverId,
                NameOf(person.CaverId),
                person.OnRoster,
                person.Ordinal,
                TeamOf(person.TeamId),
                person.In ? TripStanding.Underground : person.Out ? TripStanding.Out : TripStanding.Unheard,
                person.LastRecordedAt,
                person.StationName,
                person.DepthM,
                person.PositionRecordedAt,
                lastPlaceWithheld.GetValueOrDefault(person.CaverId)))
            .ToList();

        return new TripTrackingJournal(
            state.State,
            // The true beginning. Starting a closed watch again moves the other moment, at exactly
            // the time the first one matters most.
            state.FirstArmedAt ?? state.ArmedAt,
            state.ClosedAt,
            people,
            entries,
            // The state read answers for the watch's own survey and for each person's last place;
            // the log answers for every report before that.
            state.PositionsWithheld || entries.Any(entry => entry.Withheld));
    }

    /// <summary>
    /// The account the names are for, in the shape the state read asks for it.
    /// </summary>
    /// <remarks>
    /// That routine takes the request's own accessor because on the tracking screen the reader and
    /// the account are one. Here they are two arguments — a document kept on a trip is worded for
    /// every reader of it and still named for somebody — so the account is handed over as given
    /// and the request is not consulted behind the caller's back.
    /// </remarks>
    private sealed class NamedFor(UserContext? user) : IUserContextAccessor
    {
        public Task<UserContext?> GetAsync(CancellationToken ct = default) => Task.FromResult(user);
    }
}
