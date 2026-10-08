// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// The past tracks of one cave: which trips of it are over and readable, and one of those played
/// back, for somebody holding a published link and carrying nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two windows on one token, and this is the second of them.</b> A published link has always
/// had a live window — <see cref="TripPublicationWindow"/> — which decides whether it may follow a
/// party right now, and which closes when the watch closes, when the link lapses, or when somebody
/// revokes it. These two routes ask a different and longer question, kept in
/// <see cref="TripPastTrackWindow"/>: may this link read trips of the same cave that are
/// <em>already over</em>?
/// </para>
/// <para>
/// <b>Why the past window outlives the live one, stated here because it is the decision most
/// likely to be "fixed" by somebody tidying up.</b> Gating both on the live window would be
/// simpler and would be wrong: an archive reachable only while some party of that cave happens to
/// be underground is reachable almost never, and the history was asked for precisely so that it
/// could be read over time from a club's own article. So the routes below open while the link's
/// own trip is live <em>or</em> while that trip is itself readable as a past track. This is a
/// deliberate pair of lifetimes, not an inconsistency with the route next door.
/// </para>
/// <para>
/// <b>What the split does not cost.</b> A link outside <em>both</em> windows answers the identical
/// 404 with the identical code an invented token gets — so "this token was once real" remains
/// exactly as unlearnable as it was, which is the property the live window was protecting. Every
/// other refusal on these routes is that same answer too: unknown, malformed, over-length,
/// revoked, lapsed-with-the-watch-still-armed, retention run out, the archive switched off, a trip
/// of another cave, a trip nobody published, a trip whose links were all withdrawn, and a trip
/// still being followed. One code, no detail, no branch a caller can read.
/// </para>
/// <para>
/// <b>Protection is re-decided here on every read and cached nowhere</b>, exactly as the live page
/// re-decides it: a cave whose coordinates are protected is refused publication outright, so it
/// has no archive either, and a cave guarded after a link was handed out closes these routes the
/// moment it is guarded. Positions are withheld per row against each row's own cave anchor, and
/// names go through the one resolver the live page uses — so a caver kept off a live page by a
/// caption is off this one, and a caption typed after the trip takes effect here immediately.
/// </para>
/// </remarks>
public static class TripPastTrackEndpoints
{
    public static RouteGroupBuilder MapTripPastTrackEndpoints(this RouteGroupBuilder api)
    {
        // Anonymous for the same reason the followed page is: the token in the address is the
        // whole of the caller's claim. What these add to that surface is a second window on the
        // same token and nothing else — no new kind of URL, no new file route, no new code.
        api.MapGet("/public/trips/{token}/past", ListAsync)
            .WithTags("TripTracking")
            .AllowAnonymous()
            .RequireRateLimiting(PublicTripRateLimits.PolicyName)
            .WithPublicTripValidator()
            .WithMetadata(PublicTripRoute.Past)
            .WithSummary("Past trips of this link's cave: the ones that were published and are now over, newest first.");

        api.MapGet("/public/trips/{token}/past/{tripLogId:guid}", TrackAsync)
            .WithTags("TripTracking")
            .AllowAnonymous()
            .RequireRateLimiting(PublicTripRateLimits.PolicyName)
            .WithPublicTripValidator()
            .WithMetadata(PublicTripRoute.PastTrip)
            .WithSummary("One past trip of this link's cave, played back: the party by their place in it and where each was reported over time.");

        return api;
    }

    /// <summary>
    /// How many past trips are read as candidates before the window rule is asked of them.
    /// </summary>
    /// <remarks>
    /// The rule cannot be a <c>WHERE</c> — it lives in Domain and is asked through the function
    /// that owns it — so some trips are read and then dropped, and this bounds how many. Read from
    /// the same end as the list itself (newest first), which is the lesson the published page's
    /// picture bound already paid for: a bound taken from one end and a list taken from the other
    /// disagree about which rows are missing, and nothing anywhere says so.
    /// </remarks>
    private static int CandidateCap(int listSize) => Math.Max(200, listSize * 4);

    /// <summary>
    /// How many reports one playback carries.
    /// </summary>
    /// <remarks>
    /// Taken from the oldest end, because a playback starts at the beginning — so what a very long
    /// log loses is its tail, and the response says so rather than simply stopping. A track that
    /// stopped silently would read as a party who stopped being heard from.
    /// </remarks>
    private const int MaxTrackFixes = 2000;

    // ---- the list ----------------------------------------------------------------------------

    private static async Task<Results<Ok<PublicPastTripListDto>, ProblemHttpResult>> ListAsync(
        string token, SilexGisDbContext db, FeatureProtection protection,
        IOptions<TripTrackingOptions> live, IOptions<TripPastTrackOptions> past,
        TimeProvider clock, PublicTripDiagnostics diagnostics, CancellationToken ct)
    {
        if (ArchiveOff(past.Value, PublicTripRoute.Past, token, diagnostics) is { } off) return off;

        // The one reading of the clock this request takes: the gate and the list below are both
        // asked at this instant.
        var now = clock.GetUtcNow();
        var opened = await OpenAsync(token, db, protection, live.Value, past.Value, now, ct);
        if (opened.Refusal is { } refused)
        {
            diagnostics.Refused(PublicTripRoute.Past, opened.Reason, token);
            return refused;
        }

        var list = await ListPastAsync(opened, now, db, live.Value, past.Value, ct);

        diagnostics.Served(PublicTripRoute.Past);
        return TypedResults.Ok(list);
    }

    /// <summary>
    /// The archive's own feature switch, asked before anything else on both of its routes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off, both routes answer exactly what an unknown token answers — the feature is not there
    /// rather than there and empty — while the list of parties underground now, which shares the
    /// gate below, is deliberately unaffected.
    /// </para>
    /// <para>
    /// <b>Asked before the link is looked up, so a switched-off archive costs the database
    /// nothing.</b> A page that reads the archive for every visitor would otherwise send a query
    /// per visit to an installation that has said it offers no archive, to arrive at the answer
    /// the setting had already given. Every token costs the same here — nothing — so the promise
    /// that a once-real token costs what an invented one costs is kept in its strongest form.
    /// </para>
    /// <para>
    /// <b>What that gives up, said so that nobody takes the log for more than it is.</b> The reason
    /// written for the operator is the switch whatever the link was, because the link was never
    /// read: on an installation with the archive off, a revoked or mistyped link asking for the
    /// archive is logged as "the archive is off" and not as what it is. That is true as far as it
    /// goes — switching the archive on is the first thing that would have to change — and the
    /// link's own state is named on the same link's other reads, which do look it up.
    /// </para>
    /// </remarks>
    /// <returns>The refusal, or null when the archive is on.</returns>
    private static ProblemHttpResult? ArchiveOff(
        TripPastTrackOptions past, PublicTripRoute route, string token, PublicTripDiagnostics diagnostics)
    {
        if (past.Enabled) return null;
        diagnostics.Refused(route, PublishedReadRefusal.ArchiveOff, token);
        return ApiProblems.NotFound(TripTrackingPublicationEndpoints.NotFoundCode);
    }

    /// <summary>
    /// The trips of an opened link's cave that are over and readable at <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The instant is handed in, never read here</b>, for the reason the list of parties
    /// underground now gives: the two lists partition a published trip's life only at one instant,
    /// so a caller that wants both asks both with the same reading of the clock.
    /// </para>
    /// <para>
    /// It decides nothing about whether the link may read the archive at all — the switch and the
    /// gate are the caller's, already asked — and it records nothing.
    /// </para>
    /// </remarks>
    internal static async Task<PublicPastTripListDto> ListPastAsync(
        OpenedToken opened, DateTimeOffset now, SilexGisDbContext db,
        TripTrackingOptions live, TripPastTrackOptions past, CancellationToken ct)
    {
        var configCave = opened.CaveFeatureId;

        var size = past.EffectiveListSize;
        var cap = CandidateCap(size);

        // Narrowed in SQL to what can be narrowed there — the cave, a watch that is not armed, and
        // the existence of a link nobody revoked — and ordered and bounded here rather than after
        // the fold, so the cost of this list is the same for a cave with four past trips and one
        // with four thousand. Ordinary EF and no raw SQL: the window rule would have had to be
        // restated in SQL to filter there, and a second spelling of a rule about who may read a
        // party's positions is the one duplication this slice must not have.
        var candidates = await (
            from tracking in db.TripTrackings.AsNoTracking()
            join trip in db.TripLogs.AsNoTracking() on tracking.TripLogId equals trip.Id
            where tracking.CaveFeatureId == configCave
                && tracking.State != TripTrackingState.Armed
                && db.TripTrackingShares.Any(s => s.TripLogId == trip.Id && s.RevokedAt == null)
            // Newest first: a visitor reads a club's recent history as context, and the oldest trip
            // in a twenty-year register is the least useful first row. Tiebroken by end date and
            // then by the identifier, which is unique, so the bound is deterministic.
            orderby trip.TripDate descending,
                (trip.TripDateEnd ?? trip.TripDate) descending,
                trip.Id descending
            select new
            {
                trip.Id,
                trip.Title,
                trip.TripDate,
                trip.TripDateEnd,
                tracking.State,
                tracking.ClosedAt,
                // The survey the playback would hand over, carried here so that the "there is
                // something to play" bit below can be decided against it rather than against the
                // hope that every report was measured on it.
                tracking.SurveyModelId,
                // Among links nobody revoked, only the latest expiry matters: the live rule is
                // monotone in the expiry once revocation is excluded, so this is the instant the
                // trip actually stops being published, and the rule is asked once per trip.
                LatestUnrevokedExpiry = db.TripTrackingShares
                    .Where(s => s.TripLogId == trip.Id && s.RevokedAt == null)
                    .Max(s => (DateTimeOffset?)s.ExpiresAt),
            })
            // One row past the bound, read only to learn whether there is one: "the read came back
            // full" is also what a cave with exactly that many candidates looks like, and saying
            // "more" of it promises older trips that do not exist.
            .Take(cap + 1)
            .ToListAsync(ct);

        var beyondTheBound = candidates.Count > cap;
        if (beyondTheBound) candidates.RemoveRange(cap, candidates.Count - cap);

        // The rule itself, asked in memory through the one function that owns it — never
        // transcribed into a Where. The slice already folds a trip's event log in memory for the
        // same reason.
        var readable = candidates
            .Where(c => TripPastTrackWindow.IsReadableAsPast(
                now, c.State, c.ClosedAt, c.LatestUnrevokedExpiry, c.TripDate, c.TripDateEnd,
                live.ShareGraceAfterClose, past.Retention))
            .ToList();

        // Something older exists either because the fold left more than one page of it, or because
        // a candidate lay beyond the bound and the oldest never arrived to be folded.
        var more = readable.Count > size || beyondTheBound;
        var page = readable.Take(size).ToList();
        var ids = page.Select(p => p.Id).ToList();

        // A headcount, which names nobody. Distinct by caver because one person can hold more than
        // one roster row on a trip, and the page numbers people rather than rows.
        var headcount = ids.Count == 0
            ? []
            : await db.TripLogParticipants.AsNoTracking()
                .Where(p => ids.Contains(p.TripLogId))
                .GroupBy(p => p.TripLogId)
                .Select(g => new { TripLogId = g.Key, Count = g.Select(p => p.CaverId).Distinct().Count() })
                .ToDictionaryAsync(x => x.TripLogId, x => x.Count, ct);

        // Whether there is anything to play, which has to survive both of the ways a playback comes
        // back empty, because a picker that offers an empty one is worse than one that offers
        // nothing:
        //
        //   * the place is withheld — narrowed in SQL to reports anchored to this very cave, which
        //     the gate above has already established publishable, so the per-row withholding
        //     cannot empty what this counted. A trip whose reports are all anchored to some other
        //     cave reads as not playable, which is the conservative direction;
        //   * the place is not on the survey the playback hands over — asked below through
        //     TripTrackingRules.DrawableOn, the same function the track itself asks. A watch
        //     re-pointed at a corrected survey and then closed with no further report leaves a
        //     whole log measured against the superseded one: every fix comes back placeless and
        //     flagged as measured elsewhere, so the trip genuinely has nothing to draw. Counting
        //     the reports without asking what survey they were measured in promised exactly that
        //     playback.
        //
        // The SQL predicate is deliberately narrower than "carries location data"
        // (TrackingWithholding.HasPosition, which also counts a bare survey reference): that one
        // answers "is this row protected", this one answers "is there a place a viewer can draw",
        // and a row naming only a model has no place in it. Two questions, not two spellings of
        // one — the drawability half is asked through the rule that owns it rather than restated.
        var placedOn = ids.Count == 0
            ? []
            : await db.TripPositionEvents.AsNoTracking()
                .Where(e => ids.Contains(e.TripLogId)
                    && e.CaveFeatureId == configCave
                    && (e.ViewerStationName != null || e.DepthEnteredM != null))
                .Select(e => new { e.TripLogId, e.SurveyModelId })
                .Distinct()
                .ToListAsync(ct);
        var modelOf = page.ToDictionary(p => p.Id, p => p.SurveyModelId);
        var hasTrack = placedOn
            .Where(r => TripTrackingRules.DrawableOn(r.SurveyModelId, modelOf.GetValueOrDefault(r.TripLogId)))
            .Select(r => r.TripLogId)
            .ToHashSet();

        var camps = await TripTrackingPublicationEndpoints.ExpeditionsOfAsync(
            db, [.. page.Select(p => p.Id)], ct);

        return new PublicPastTripListDto(
            [.. page.Select(p => new PublicPastTripDto(
                p.Id,
                camps.GetValueOrDefault(p.Id),
                p.Title,
                p.TripDate,
                p.TripDateEnd,
                p.ClosedAt,
                headcount.GetValueOrDefault(p.Id),
                hasTrack.Contains(p.Id)))],
            more);
    }

    // ---- one past track ----------------------------------------------------------------------

    private static async Task<Results<Ok<PublicPastTrackDto>, ProblemHttpResult>> TrackAsync(
        string token, Guid tripLogId, SilexGisDbContext db, FeatureProtection protection,
        ICrsRegistry crs, IFileAccessTokenService tokens,
        IOptions<TripTrackingOptions> live, IOptions<TripPastTrackOptions> past,
        TimeProvider clock, PublicTripDiagnostics diagnostics, CancellationToken ct)
    {
        if (ArchiveOff(past.Value, PublicTripRoute.PastTrip, token, diagnostics) is { } off) return off;

        var now = clock.GetUtcNow();
        var opened = await OpenAsync(token, db, protection, live.Value, past.Value, now, ct);
        if (opened.Refusal is { } refused)
        {
            diagnostics.Refused(PublicTripRoute.PastTrip, opened.Reason, token);
            return refused;
        }

        var configCave = opened.CaveFeatureId;

        // The link is good from here on, and what can still be refused is the trip it asked for.
        // One word for all three ways below, as there is one answer for them: which of "another
        // cave's", "no such trip" and "not history yet" it was is not something a holder of the
        // link is told, and an operator looking at one link's refusals does not need it either.
        ProblemHttpResult NotInArchive()
        {
            diagnostics.Refused(PublicTripRoute.PastTrip, PublishedReadRefusal.TripNotInArchive, token);
            return ApiProblems.NotFound(TripTrackingPublicationEndpoints.NotFoundCode);
        }

        // The scope check, and it is load-bearing: without it a link to one cave would read past
        // trips of every other cave on the installation. Asked as part of the lookup rather than
        // after it, so a trip of another cave is indistinguishable from a trip that does not exist.
        //
        // The watch, its trip and the trip's latest unrevoked expiry in one round trip, as the gate
        // above reads the link's: three facts about one trip are not three questions. The expiry is
        // null when the trip was never published or every link of it has been withdrawn, and null
        // is what "not published" means on this surface — there is no archive flag and no second
        // act, only the publication rows that already exist. Which is also why revoking every link
        // of a trip takes it out of the archive: revocation already means "end this publication,
        // now", and it is read here as meaning exactly that.
        var asked = await (
            from watch in db.TripTrackings.AsNoTracking()
            join log in db.TripLogs.AsNoTracking() on watch.TripLogId equals log.Id
            where watch.TripLogId == tripLogId && watch.CaveFeatureId == configCave
            select new
            {
                Tracking = watch,
                Trip = log,
                LatestUnrevokedExpiry = db.TripTrackingShares
                    .Where(s => s.TripLogId == log.Id && s.RevokedAt == null)
                    .Max(s => (DateTimeOffset?)s.ExpiresAt),
            }).FirstOrDefaultAsync(ct);
        if (asked is null) return NotInArchive();

        var (tracking, trip, latestUnrevokedExpiry) =
            (asked.Tracking, asked.Trip, asked.LatestUnrevokedExpiry);

        // The same four clauses the list applies, asked of this one trip through the same function:
        // published and not withdrawn, not armed, its live window over, inside retention. A trip
        // that fails any of them answers exactly what an unguessed identifier answers, so guessing
        // one buys nothing.
        if (!TripPastTrackWindow.IsReadableAsPast(
                now, tracking.State, tracking.ClosedAt, latestUnrevokedExpiry,
                trip.TripDate, trip.TripDateEnd, live.Value.ShareGraceAfterClose, past.Value.Retention))
        {
            return NotInArchive();
        }

        var teams = await db.TripTeams.AsNoTracking()
            .Where(t => t.TripLogId == trip.Id).OrderBy(t => t.Title).ToListAsync(ct);

        // The captions of *this* trip, not the live one. A caption is the only individual opt-out
        // there is, and reading it off the trip being played back is what makes it retroactive:
        // somebody captioned after the fact is renamed here on the next read.
        var labels = await db.TripTrackingParticipants.AsNoTracking()
            .Where(p => p.TripLogId == trip.Id)
            .ToDictionaryAsync(p => p.CaverId, p => p.DisplayLabel, ct);

        // The party and its numbers, from the one derivation the live page numbers it by — so the
        // person shown as "Caver 3" while the trip was followed is "Caver 3" in its playback.
        var roster = (await TripTrackingPublicationEndpoints.RosterOrderAsync(db, trip.Id, ct)).Roster;
        var rosterIds = roster.Select(place => place.CaverId).ToList();

        // Read off the roster where the installation publishes names at all, exactly as the live
        // page reads them — same source, same setting, same resolver below, so the two pages cannot
        // come to call the same person by two different names.
        var names = live.Value.PublishRealNames
            ? await db.Cavers.AsNoTracking()
                .Where(c => rosterIds.Contains(c.Id))
                .Select(c => new { c.Id, c.FullName })
                .ToDictionaryAsync(c => c.Id, c => c.FullName, ct)
            : [];

        // Oldest first, because that is the order a playback runs in, and bounded from that end so
        // that what a very long log loses is its tail rather than its beginning.
        var rows = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip.Id)
            .OrderBy(e => e.RecordedAt).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Take(MaxTrackFixes + 1)
            .ToListAsync(ct);
        var truncated = rows.Count > MaxTrackFixes;
        if (truncated) rows.RemoveRange(MaxTrackFixes, rows.Count - MaxTrackFixes);

        // Per row, because history can span models and every report carries its own cave anchor: a
        // position anchored to some other cave is shown only if that cave could itself have been
        // published, and one whose anchor is gone is shown to nobody. The same predicate the live
        // page applies, asked of the same caves — not restated here.
        var caveIds = rows.Where(e => e.CaveFeatureId is not null).Select(e => e.CaveFeatureId!.Value)
            .Append(configCave).Distinct().ToList();
        var openCaves = await TrackingWithholding.PublishableCaveIdsAsync(db, protection, caveIds, ct);

        var byCaver = rows.GroupBy(e => e.CaverId).ToDictionary(g => g.Key, g => g.ToList());
        var participants = new List<PublicPastTrackParticipantDto>();
        var withheldAny = false;
        foreach (var (caverId, ordinal) in roster)
        {
            var own = byCaver.GetValueOrDefault(caverId) ?? [];
            // Every report about this person in order, including the ones that are not emitted:
            // the standing is Domain's answer over the whole prefix, and handing it a filtered
            // list would be this surface deciding what moves a standing, which is exactly the
            // decision that has one home.
            var prefix = new List<TripPositionEvent>(own.Count);
            var track = new List<PublicPastTrackFixDto>();

            foreach (var report in own)
            {
                prefix.Add(report);

                var placed = TrackingWithholding.HasPosition(report);
                var positionOpen = TrackingWithholding.PositionOpen(report, openCaves);
                if (!positionOpen) withheldAny = true;

                // Whether the place belongs to the survey this response hands over. A watch
                // re-pointed at a corrected survey mid-trip leaves earlier reports naming the one
                // they were made against, and the same path in the new survey is a different place
                // or no place at all. A row with no place to draw is drawable by definition.
                var drawable = !placed
                    || TripTrackingRules.DrawableOn(report.SurveyModelId, tracking.SurveyModelId);
                // Only ever said of a place this reader was allowed in the first place: a withheld
                // row is already an absence, and a second bit explaining it would give back what
                // the withholding kept.
                var elsewhere = positionOpen && !drawable;
                var shown = positionOpen && drawable;

                // A note is a coordinator's free text about a person and has never been on a public
                // surface. It moves no standing either, so dropping it changes nothing emitted —
                // and it stays in the prefix above so that the rule, not this filter, decides that.
                var statesStanding = report.Kind is TripPositionEventKind.Entered
                    or TripPositionEventKind.Exited;
                if (!placed && !statesStanding) continue;

                var standing = TripTrackingRules.StandingOf(prefix);
                track.Add(new PublicPastTrackFixDto(
                    // The row's own hour, kept whatever happened to the row's place — and this
                    // looks, from the live page, like the opposite rule, so here is why it is not.
                    //
                    // The live page shows one row per person and carries two instants: when they
                    // were last heard from, sent always, and when the position it is drawing was
                    // taken, sent only when that position is drawn. The second is withheld not
                    // because an hour is a secret — the first is usually the very same hour, since
                    // the last report is usually the positioned one — but because a bare instant
                    // printed beside a single station is read as dating that station.
                    //
                    // Here there is no such pairing to mislead: a row *is* one report, its hour is
                    // that report's hour, and its own station is null on the same row. So this
                    // field is the per-row form of "last heard from", which the live page also
                    // sends unconditionally — not of "where they were, at this time".
                    //
                    // Nor would dropping it hide what a withheld row discloses. The track is
                    // ordered, so a placeless row already sits between the two drawable fixes
                    // either side of it whether or not it carries a clock; only removing the row
                    // would remove that, and removing it would replace an honest gap with the
                    // claim that the log is continuous — the same false reading TrackTruncated
                    // exists to refuse at the end of a long track. And nobody described here is
                    // underground: an armed watch is never readable as past, so the live rule's
                    // subject, a party who can still be found, is not on this surface at all.
                    report.RecordedAt,
                    report.TeamId,
                    shown ? report.ViewerStationName : null,
                    shown ? report.DepthEnteredM : null,
                    PositionOnOtherModel: elsewhere,
                    In: standing == TripStanding.Underground,
                    Out: standing == TripStanding.Out));
            }

            participants.Add(new PublicPastTrackParticipantDto(
                ordinal,
                TripTrackingPublicationEndpoints.NameFor(caverId, labels, names),
                track));
        }

        var trackCamps = await TripTrackingPublicationEndpoints.ExpeditionsOfAsync(db, [trip.Id], ct);
        // Read before the answer is built rather than inside it, so that the read is counted as
        // served only once nothing is left that could fail.
        // The names of depths ride with the survey under the setting the followed page reads: one
        // switch says whether this installation tells them, wherever that survey is handed over.
        var model = await TripTrackingPublicationEndpoints.ModelAsync(
            db, protection, crs, tokens, tracking.SurveyModelId, configCave,
            live.Value.PublishDepthPlaces, ct);

        diagnostics.Served(PublicTripRoute.PastTrip);
        return TypedResults.Ok(new PublicPastTrackDto(
            trip.Id,
            trackCamps.GetValueOrDefault(trip.Id),
            trip.Title,
            trip.TripDate,
            trip.TripDateEnd,
            tracking.ArmedAt,
            tracking.ClosedAt,
            withheldAny,
            truncated,
            // This trip's own model, and the refusal above already established that its cave is
            // the one the publication decision was taken about — so a model answering to any other
            // cave is not handed over, and a superseded survey is drawn as itself or not at all.
            model,
            [.. teams.Select(t => new PublicTripTeamDto(t.Id, t.Title))],
            participants));
    }

    // ---- the gate ----------------------------------------------------------------------------

    /// <summary>
    /// Whether this token opens the past at all, and if so which cave's past.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The second of the token's two windows, and the only place it is asked.</b> The live route
    /// asks <see cref="TripPublicationWindow.IsOpen"/> and nothing else; this asks
    /// <see cref="TripPublicationStatus.WindowsOf"/> for both windows and is open while
    /// <em>either</em> is — the link's own trip is still being followed, or that trip has itself
    /// become readable history, which is the gate <see cref="TripPastTrackWindow.OpensThePast"/>
    /// describes. The split is deliberate: an archive that closed whenever no party was underground
    /// would be unreachable almost always, which is not the thing that was asked for.
    /// </para>
    /// <para>
    /// <b>Every way out of here is the same way out.</b> The archive switched off, an empty or
    /// over-length token, an unknown one, a revoked one, one whose trip is neither live nor yet
    /// history, a watch with no cave configured, and a cave that has since been protected all
    /// return one 404 with one code and no detail — the answer an invented token gets, so that
    /// nothing here tells a stranger their guess was once real.
    /// </para>
    /// <para>
    /// <b>Which way out it was is kept for the operator and for nobody else.</b> Each refusal
    /// carries its reason beside the one shared answer, for the route to write into the
    /// installation's log. The reason is worked out from the rows the single read above already
    /// returned, so naming it adds no query — a once-real token still costs what an invented one
    /// costs — and it travels in a member no route puts into a response.
    /// </para>
    /// <para>
    /// <b>The cave is re-decided here on every call and remembered nowhere.</b> A cave guarded
    /// after the link was handed out closes the archive at the moment it is guarded, exactly as it
    /// closes the live page — and a watch that has lost the cave it was anchored to has nothing to
    /// decide about and fails closed.
    /// </para>
    /// </remarks>
    internal static async Task<OpenedToken> OpenAsync(
        string token, SilexGisDbContext db, FeatureProtection protection,
        TripTrackingOptions live, TripPastTrackOptions past, DateTimeOffset now, CancellationToken ct)
    {
        // One answer, built once and handed out of every refusal below: only the reason beside it
        // differs, and the reason goes to the log.
        var notFound = ApiProblems.NotFound(TripTrackingPublicationEndpoints.NotFoundCode);
        OpenedToken Refused(PublishedReadRefusal why) =>
            new(Guid.Empty, null, Guid.Empty, false, false, false, notFound, why);

        if (string.IsNullOrEmpty(token) || token.Length > TripTrackingRules.MaxShareTokenLength)
        {
            return Refused(PublishedReadRefusal.UnknownLink);
        }

        // One round trip whatever the token turns out to be. The link, its trip, its watch and the
        // trip's latest unrevoked expiry are read together, so a token that was once real — since
        // revoked, lapsed, or past its retention — costs the database exactly what an invented one
        // costs, and every refusal below is decided in memory. Read one at a time, a once-real
        // token took three more round trips than an unknown one before the same 404: a difference
        // in response time, which is the one bit this gate promises not to give away. A link whose
        // trip or watch is gone finds no row here and is refused the same way.
        var hash = TripTrackingPublicationEndpoints.HashToken(token);
        var found = await (
            from link in db.TripTrackingShares.AsNoTracking()
            join log in db.TripLogs.AsNoTracking() on link.TripLogId equals log.Id
            join watch in db.TripTrackings.AsNoTracking() on log.Id equals watch.TripLogId
            where link.TokenHash == hash
            select new
            {
                Share = link,
                Trip = log,
                Tracking = watch,
                // The trip's own latest unrevoked expiry rather than this link's, so that "is this
                // trip over" is read the same way here as it is read of every other trip in the
                // list — null when every link of it has been withdrawn, which is what "not
                // published" means on this surface. This link's own expiry still governs the live
                // half of the question, inside the rule.
                LatestUnrevokedExpiry = db.TripTrackingShares
                    .Where(s => s.TripLogId == log.Id && s.RevokedAt == null)
                    .Max(s => (DateTimeOffset?)s.ExpiresAt),
            }).FirstOrDefaultAsync(ct);
        if (found is null) return Refused(PublishedReadRefusal.UnknownLink);

        var (share, trip, tracking, latestUnrevokedExpiry) =
            (found.Share, found.Trip, found.Tracking, found.LatestUnrevokedExpiry);

        // The two windows, read where the administrator's list of published links reads them, so
        // that what that list says a link is doing and what this gate does with it cannot drift
        // apart. A revoked link has neither: only one of the two rules takes a revocation as an
        // argument — the archive's is about the trip's remaining links rather than about this one —
        // so the revocation is asked there first, ahead of both.
        //
        // Kept apart rather than folded together, because the routes that share this gate do not
        // apply the same feature switch to them. Following a party underground is the core of this
        // surface; reading a club's history is a thing an installation may switch off, and
        // switching it off must not stop anybody following tonight's party.
        var windows = TripPublicationStatus.WindowsOf(
            now,
            share.RevokedAt,
            share.ExpiresAt,
            tracking.State,
            tracking.ClosedAt,
            latestUnrevokedExpiry,
            trip.TripDate,
            trip.TripDateEnd,
            live.ShareGraceAfterClose,
            past.Retention);
        if (windows.Neither)
        {
            // Why, asked of the same facts through the function that explains this rule. It answers
            // null only when a window is open, which the line above has just excluded; the fallback
            // is there so that a disagreement between the two could never turn a refusal into an
            // exception on an anonymous route.
            return Refused(
                TripPublicationRefusal.OfBothWindows(
                    now,
                    share.RevokedAt,
                    share.ExpiresAt,
                    tracking.State,
                    tracking.ClosedAt,
                    latestUnrevokedExpiry,
                    trip.TripDate,
                    trip.TripDateEnd,
                    live.ShareGraceAfterClose,
                    past.Retention)
                ?? PublishedReadRefusal.Expired);
        }

        var (liveOpen, pastReadable) = (windows.Live, windows.Past);

        // The publication refusal, taken again and cached nowhere — the same call the live page
        // makes, about the same cave, on every single read.
        if (tracking.CaveFeatureId is not { } configCave) return Refused(PublishedReadRefusal.CaveWithheld);
        var publishable = await TrackingWithholding.PublishableCaveIdsAsync(db, protection, [configCave], ct);
        if (!publishable.Contains(configCave)) return Refused(PublishedReadRefusal.CaveWithheld);

        // Asked here because the trip's dates are in hand here and nowhere after: the one route
        // that reads the answer would otherwise have to fetch the trip a second time to ask it.
        var withinSiblingWindow = TripPublicationWindow.WithinSiblingWindow(
            now, trip.TripDate, trip.TripDateEnd, live.SiblingWindowAfterLapse);

        return new OpenedToken(
            configCave, tracking.SurveyModelId, trip.Id, liveOpen, pastReadable, withinSiblingWindow, null,
            // Never read: a token that opened has no reason to give.
            default);
    }

    /// <summary>
    /// What a token opened, once every gate on it has been asked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Shared by both list routes on purpose.</b> "Does this link open this cave's activity at
    /// all" is one question — the token is real and unrevoked, its watch's cave may still be
    /// published, and it is inside one of its two windows — and it is asked identically whether the
    /// caller went on to read the archive or the parties underground now. A second copy of it is
    /// how one of those routes comes to answer after the other has started refusing.
    /// </para>
    /// <para>
    /// It carries the token's own survey because the list of currently-followed trips draws every
    /// party on <em>that</em> survey rather than on each trip's own: it is the model the page was
    /// handed and the only one it has.
    /// </para>
    /// </remarks>
    /// <param name="LiveWindowOpen">
    /// True while this link still follows its own party — armed, or closed and inside the short
    /// grace window after "everybody out".
    /// </param>
    /// <param name="PastReadable">
    /// True while this link's own trip has become history and that history is still inside the
    /// archive's retention. Never true at the same instant as <paramref name="LiveWindowOpen"/>:
    /// the two rules partition a published trip's life, and a Domain test holds that they do.
    /// </param>
    /// <param name="WithinSiblingWindow">
    /// True while this link's own trip ended recently enough for it to go on listing the parties
    /// in its cave after it has stopped following its own — always, on an installation that sets no
    /// such period. Read by the list of followed trips and by nothing else: the archive is not
    /// bounded by it, and a link whose live window is open is never asked.
    /// </param>
    /// <param name="Refusal">The answer to give when the token opened nothing; null when it opened.</param>
    /// <param name="Reason">
    /// Why it opened nothing, for the installation's log and for nothing else. Meaningful only
    /// beside a <paramref name="Refusal"/>, and never put into a response.
    /// </param>
    internal sealed record OpenedToken(
        Guid CaveFeatureId,
        Guid? SurveyModelId,
        Guid TripLogId,
        bool LiveWindowOpen,
        bool PastReadable,
        bool WithinSiblingWindow,
        ProblemHttpResult? Refusal,
        PublishedReadRefusal Reason);
}
