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
/// today's parties sets <c>SILEXGIS__TripPastTracks__Retention</c>, and that one setting closes
/// both routes together.
/// </para>
/// <para>
/// Every refusal is the single 404 an invented token gets, with no branch a caller can read —
/// unknown, malformed, over-length, revoked, lapsed, retention run out, the archive switched off,
/// or a cave whose coordinates have since been protected.
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

    private static async Task<Results<Ok<PublicLiveTripListDto>, ProblemHttpResult>> ListAsync(
        string token, SilexGisDbContext db, FeatureProtection protection,
        IOptions<TripTrackingOptions> live, IOptions<TripPastTrackOptions> past,
        TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var opened = await TripPastTrackEndpoints.OpenAsync(
            token, db, protection, live.Value, past.Value, now, ct);
        if (opened.Refusal is { } refused) return refused;

        // This route's own gate over the shared one. A link still following its own party opens it
        // outright; a link whose trip is over opens it only while the archive is switched on, which
        // is what gives an operator a single lever over "an old article keeps naming today's
        // parties". Switching the archive off therefore stops old links and not current ones — the
        // distinction the shared gate keeps its two answers apart for.
        if (!opened.LiveWindowOpen && !(past.Value.Enabled && opened.PastReadable))
        {
            return ApiProblems.NotFound(TripTrackingPublicationEndpoints.NotFoundCode);
        }

        var configCave = opened.CaveFeatureId;
        var size = live.Value.EffectiveFollowedListSize;
        var cap = CandidateCap(size);

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
            }).Take(cap).ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return TypedResults.Ok(new PublicLiveTripListDto([], false));
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
                live.Value.ShareGraceAfterClose))
            .ToList();

        var more = followed.Count > size;
        var shown = followed.Take(size).ToList();

        var trips = new List<PublicLiveTripDto>(shown.Count);
        foreach (var row in shown)
        {
            // Drawn on the token's survey, not this trip's — see the remarks on the type. The party
            // fold is the one the followed page uses, so what may be shown of somebody here and
            // what may be shown of them there cannot come apart.
            var party = await TripTrackingPublicationEndpoints.PartyAsync(
                db, protection, live.Value, row.Id, opened.SurveyModelId, configCave, ct);

            trips.Add(new PublicLiveTripDto(
                row.Id,
                row.Title,
                row.TripDate,
                row.TripDateEnd,
                row.State,
                row.ArmedAt,
                row.ClosedAt,
                party.PositionsWithheld,
                party.Teams,
                party.Participants));
        }

        return TypedResults.Ok(new PublicLiveTripListDto(trips, more));
    }
}
