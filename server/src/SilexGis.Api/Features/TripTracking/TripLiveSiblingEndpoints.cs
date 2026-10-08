// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// The parties in one cave right now: every trip of it being followed at this moment, for somebody
/// holding a published link to any trip of that cave and carrying nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> A share token names exactly one trip, so a club running two parties
/// into one cave on the same day could publish both and show neither beside the other: each link
/// followed its own party, and the archive route next door deliberately excludes a trip still being
/// followed. A page could be handed every token, and for a fixed set of trips that works — but a
/// camp gains trips while it is running, and a page whose configuration must be edited to keep up
/// is a page that is wrong on the day it matters. One link now keeps up by itself.
/// </para>
/// <para>
/// <b>This and the archive partition, which is the property a page relies on.</b>
/// <see cref="TripPublicationWindow.IsFollowableByAnyLink"/> answers here and
/// <see cref="TripPastTrackWindow.IsReadableAsPast"/> answers there, and a published trip satisfies
/// exactly one of them at any instant — so the two lists never double-count and never leave a trip
/// invisible. A trip crossing from this list to that one, as the grace window after its watch
/// closes runs out, is the ordinary course of events; a Domain test holds the partition so that
/// changing one rule without the other cannot quietly open a gap.
/// </para>
/// <para>
/// <b>Every party here is drawn on the token's own survey, never on each trip's.</b> The followed
/// route hands over exactly one model and that is the model a page has loaded, so drawability is
/// decided against it: a sibling trip whose reports were measured on a different survey comes back
/// placeless with <c>positionOnOtherModel</c> set. That is the rule the followed page already
/// applies to a watch re-pointed mid-trip, asked across trips instead of within one — not a new
/// rule, and deliberately not a survey identifier a page would have to be trusted to compare.
/// </para>
/// <para>
/// <b>The gate is the archive's gate, and that is a decision rather than a convenience.</b> This
/// route opens while the caller's own link is inside either of its windows — live, or still
/// readable as past — exactly as the archive does. Gating it on the live window alone would break
/// the case it was built for: the journal page's link is to one of several trips, and when that one
/// closes the page must still show the parties still underground. The consequence, named rather
/// than left to be discovered: <b>an old link goes on reporting who is in the cave now for as long
/// as its trip stays readable as past</b>, which by default is forever, because the archive's
/// retention is unset by default. An installation that does not want a years-old article naming
/// today's parties has two settings for it. <c>SILEXGIS__TripTracking__SiblingWindowAfterLapse</c>
/// ends this list alone, that long after the link's own trip, and leaves the past trips readable;
/// <c>SILEXGIS__TripPastTracks__Retention</c> ages the trip out of the archive and closes both
/// routes together. Both are unset by default.
/// </para>
/// <para>
/// Every refusal is the single 404 an invented token gets, with no branch a caller can read —
/// unknown, malformed, over-length, revoked, lapsed, retention run out, the archive switched off,
/// a link too long past its own trip, or a cave whose coordinates have since been protected.
/// </para>
/// </remarks>
public static class TripLiveSiblingEndpoints
{
    public static RouteGroupBuilder MapTripLiveSiblingEndpoints(this RouteGroupBuilder api)
    {
        // Anonymous on the same terms as its two neighbours: the token in the address is the whole
        // of the caller's claim, and this adds no new kind of address to that surface.
        api.MapGet("/public/trips/{token}/live", ListAsync)
            .WithTags("TripTracking")
            .AllowAnonymous()
            .RequireRateLimiting(PublicTripRateLimits.PolicyName)
            .WithPublicTripValidator()
            .WithMetadata(PublicTripRoute.Live)
            .WithSummary("Trips of this link's cave being followed right now, each with its party, drawn on this link's survey.");

        return api;
    }

    /// <summary>
    /// How many followed trips are read as candidates before the window rule is asked of them.
    /// </summary>
    /// <remarks>
    /// The rule lives in Domain and is asked through the function that owns it, so it cannot be a
    /// <c>WHERE</c>: some trips are read and then dropped, and this bounds how many. The same
    /// shape, and the same reasoning, as the archive list's own candidate bound.
    /// </remarks>
    private static int CandidateCap(int listSize) => Math.Max(100, listSize * 4);

    /// <summary>
    /// How much earlier than the grace window's own edge the candidate read still accepts a closed
    /// watch.
    /// </summary>
    /// <remarks>
    /// Only so that the narrowing in the query is looser than the rule and never equal to it: the
    /// database keeps instants to the microsecond and compares against a bound rounded to one,
    /// while the rule is asked of the clock's own finer reading. A minute is far more than that
    /// difference and costs at most the few watches closed in that minute.
    /// </remarks>
    private static readonly TimeSpan CandidateSlack = TimeSpan.FromMinutes(1);

    private static async Task<Results<Ok<PublicLiveTripListDto>, ProblemHttpResult>> ListAsync(
        string token, SilexGisDbContext db, FeatureProtection protection,
        IOptions<TripTrackingOptions> live, IOptions<TripPastTrackOptions> past,
        TimeProvider clock, PublicTripDiagnostics diagnostics, CancellationToken ct)
    {
        // The one reading of the clock this request takes: the gate and the list below are both
        // asked at this instant.
        var now = clock.GetUtcNow();
        var opened = await TripPastTrackEndpoints.OpenAsync(
            token, db, protection, live.Value, past.Value, now, ct);
        if (opened.Refusal is { } refused)
        {
            diagnostics.Refused(PublicTripRoute.Live, opened.Reason, token);
            return refused;
        }

        // This route's own gate over the shared one. A link still following its own party opens it
        // outright. A link whose trip is over opens it only while the archive is switched on and,
        // where the installation has set a period for it, only for that long after the trip ended
        // — the two levers an operator has over "an old article keeps naming today's parties", the
        // second of which leaves the past trips readable. Neither touches a current link: that is
        // the distinction the shared gate keeps its two answers apart for. The reading is the
        // Domain's and is never wider than the one the administrator's list of published links
        // describes a link by, so this route cannot serve a link that list calls shut.
        var windows = new PublishedLinkWindows(opened.LiveWindowOpen, opened.PastReadable);
        if (!windows.OpensTheFollowedList(past.Value.Enabled, opened.WithinSiblingWindow))
        {
            // The shared gate opened, so one of the two windows is open, and it is the past one or
            // this would have opened. Which of the two levers shut it is told apart for the log
            // alone: where the link still opens its archive the switch is on, so it is the period.
            // The answer is the one an invented token gets either way.
            diagnostics.Refused(
                PublicTripRoute.Live,
                windows.OpensAnything(past.Value.Enabled)
                    ? PublishedReadRefusal.PastSiblingWindow
                    : PublishedReadRefusal.ArchiveOff,
                token);
            return ApiProblems.NotFound(TripTrackingPublicationEndpoints.NotFoundCode);
        }

        var list = await ListFollowedAsync(opened, now, db, protection, live.Value, ct);

        diagnostics.Served(PublicTripRoute.Live);
        return TypedResults.Ok(list);
    }

    /// <summary>
    /// The trips of an opened link's cave that are being followed at <paramref name="now"/>, each
    /// with its party.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The instant is handed in, never read here.</b> This list and the archive's partition a
    /// published trip's life between them, and that partition holds only at one instant: a caller
    /// that wants both asks both with the same reading of the clock, and a trip crossing the edge
    /// of its grace window is then in exactly one of them. Each routine reading the clock for
    /// itself is how a trip comes to be in both lists of one page, or in neither.
    /// </para>
    /// <para>
    /// It decides nothing about whether the link may read this list at all — that is the caller's
    /// gate, already asked — and it records nothing: counting a read as served belongs to the route.
    /// </para>
    /// </remarks>
    internal static async Task<PublicLiveTripListDto> ListFollowedAsync(
        TripPastTrackEndpoints.OpenedToken opened, DateTimeOffset now, SilexGisDbContext db,
        FeatureProtection protection, TripTrackingOptions live, CancellationToken ct)
    {
        var configCave = opened.CaveFeatureId;
        var size = live.EffectiveFollowedListSize;
        var cap = CandidateCap(size);

        // A closed watch is followable only until its grace window runs out, so one closed before
        // this instant cannot be on this list and is not read at all. Without it the candidates of
        // a cave were every published trip it ever had, newest first, and a party still underground
        // on a trip older than the newest hundred was crowded out by trips over for years.
        //
        // This is an optimisation and deliberately NOT the rule. It is strictly looser than
        // TripPublicationWindow.IsOpen, which is still asked of every row below and remains the
        // only thing that decides: that rule passes an armed watch, and a closed one while
        // now < closedAt + grace. Every armed watch passes here; a closed one inside its grace has
        // closedAt > now - grace, which is later than the bound below by the slack; and a closed
        // watch with no closing instant, which the rule refuses, is let through here to be refused
        // there. So no row the rule would pass is dropped by this, and the rows it lets through
        // that the rule refuses (a lapsed link, the slack, a missing instant) are dropped below as
        // they always were. Tightening the rule later keeps this correct; loosening it — a longer
        // way of being followable after closing — must loosen this with it.
        var back = live.ShareGraceAfterClose;
        var closedAfter = back >= now - DateTimeOffset.MinValue - CandidateSlack
            ? DateTimeOffset.MinValue
            : now - back - CandidateSlack;

        // Narrowed in SQL to what can be narrowed there — this cave, a watch that was actually
        // started, and the existence of a link nobody revoked — then ordered and bounded here, so
        // the cost of this list does not grow with a cave's whole register. The window rule itself
        // is asked below, in Domain, because a second spelling of "may this party be read" in SQL
        // is the one duplication this slice must not have.
        var candidates = await (
            from tracking in db.TripTrackings.AsNoTracking()
            join trip in db.TripLogs.AsNoTracking() on tracking.TripLogId equals trip.Id
            where tracking.CaveFeatureId == configCave
                && tracking.State != TripTrackingState.Off
                && (tracking.State == TripTrackingState.Armed
                    || tracking.ClosedAt == null
                    || tracking.ClosedAt > closedAfter)
                && db.TripTrackingShares.Any(s => s.TripLogId == trip.Id && s.RevokedAt == null)
            // Newest first, tiebroken to the identifier so the bound is deterministic — the same
            // ordering the archive uses, so a trip does not change its place in a reader's world as
            // it crosses from one list to the other.
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
                tracking.ArmedAt,
                tracking.ClosedAt,
                // The one column of the trip's plan this list may ever say; whether it does is
                // decided below, by the rule the followed page asks.
                trip.ExpectedReturnAt,
            })
            // One row past the bound, read only to learn whether there is one: "the read came back
            // full" is also what a cave with exactly that many candidates looks like, and saying
            // "more" of it sends a reader looking for parties that do not exist.
            .Take(cap + 1).ToListAsync(ct);

        var beyondTheBound = candidates.Count > cap;
        if (beyondTheBound) candidates.RemoveRange(cap, candidates.Count - cap);

        if (candidates.Count == 0)
        {
            return new PublicLiveTripListDto([], false);
        }

        // One query for every candidate's latest unrevoked expiry rather than one each: the rule
        // below needs it per trip, and a list of twenty parties is not a reason for twenty round
        // trips.
        var candidateIds = candidates.Select(c => c.Id).ToList();
        var expiries = await db.TripTrackingShares.AsNoTracking()
            .Where(s => candidateIds.Contains(s.TripLogId) && s.RevokedAt == null)
            .GroupBy(s => s.TripLogId)
            .Select(g => new { TripLogId = g.Key, Expiry = g.Max(s => s.ExpiresAt) })
            .ToDictionaryAsync(x => x.TripLogId, x => x.Expiry, ct);

        var followed = candidates
            .Where(c => TripPublicationWindow.IsFollowableByAnyLink(
                now,
                expiries.TryGetValue(c.Id, out var expiry) ? expiry : null,
                c.State,
                c.ClosedAt,
                live.ShareGraceAfterClose))
            .ToList();

        // Something more exists either because the fold left more than one page of it, or because
        // a candidate lay beyond the bound and an older party still underground never arrived to be
        // folded — the same two reasons the archive's bit has. Read from the list alone, a cave
        // whose candidates outnumber the bound dropped a still-followable older trip and said
        // nothing.
        var more = followed.Count > size || beyondTheBound;
        var shown = followed.Take(size).ToList();
        var shownIds = shown.Select(row => row.Id).ToList();

        // One query for the whole list.
        var camps = await TripTrackingPublicationEndpoints.ExpeditionsOfAsync(db, shownIds, ct);

        // Every party in the list, read together: the number of reads this costs is the same for
        // one party as for twenty. Drawn on the token's survey, not each trip's — see the remarks
        // on the type. The fold is the one the followed page uses, so what may be shown of somebody
        // here and what may be shown of them there cannot come apart.
        var parties = await TripTrackingPublicationEndpoints.PartiesAsync(
            db, protection, live, shownIds, opened.SurveyModelId, configCave, ct);

        var trips = new List<PublicLiveTripDto>(shown.Count);
        foreach (var row in shown)
        {
            var party = parties[row.Id];
            trips.Add(new PublicLiveTripDto(
                row.Id,
                camps.GetValueOrDefault(row.Id),
                row.Title,
                row.TripDate,
                row.TripDateEnd,
                row.State,
                row.ArmedAt,
                row.ClosedAt,
                TripTrackingRules.PublishedExpectedReturn(
                    live.PublishExpectedReturn, row.ExpectedReturnAt, row.ArmedAt),
                party.PositionsWithheld,
                party.Teams,
                party.Participants));
        }

        return new PublicLiveTripListDto(trips, more);
    }
}
