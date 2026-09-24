// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Publishing a tracked trip: a link an administrator hands out, and the page somebody without
/// an account follows the party on.
/// </summary>
/// <remarks>
/// <para>
/// The link is a capability and nothing else — 32 random bytes, base64url, stored only as a
/// SHA-256 and shown once at mint, revocable. Malformed, unknown, revoked, lapsed, belonging to a
/// watch that has closed, and pointing at a trip that may no longer be published all answer one
/// identical 404: the token is the whole of a follower's claim, so nothing about its validity may
/// be distinguishable, and a refusal that said which of those it was would be an answer about
/// which tokens exist. In particular there is deliberately no "expired" answer — it would tell a
/// stranger holding a guess that their guess was once real.
/// </para>
/// <para>
/// <b>A publication ends without anybody remembering to end it.</b> Revoking is immediate and
/// still the only way to end one early, but it was for a long time the <em>only</em> way, and it
/// does not cover what this feature actually does: the paste-in block puts the token into a club's
/// own article, which is indexable and archivable, so the address outlives whoever wrote the
/// article caring about it. Three things now end a publication and the earliest wins — the watch
/// closing (with a short grace window, because the moment a party comes out is the moment the page
/// most needs to say so), an expiry fixed at mint, and revocation. The rule and the argument for
/// each part live in <see cref="TripPublicationWindow"/>, and it is asked again on every read
/// exactly as the protection refusal is.
/// </para>
/// <para>
/// <b>And the people named are told.</b> An installation publishes real names by default, and the
/// people that names are not the person who pressed the button. So the trip's own read now says
/// whether it is published and what the page calls each of them — to anybody who may read the trip,
/// needing no preference and no write access — and a notice goes to each of them when a link is
/// minted. See <see cref="TripPublicationAnnouncer"/>.
/// </para>
/// <para>
/// <b>A trip whose cave is protected is not published at all.</b> Not blurred, not partial —
/// refused. A followed page hands over the party's stations and a delivery URL for the survey
/// model, which is the whole measured drawing of the cave, so there is no version of it that
/// withholds the position it exists to show. The refusal is decided at mint <em>and again on
/// every read</em>, because protection is switched on by somebody who has just decided a cave
/// needs it, which is exactly the moment a link handed out last week must stop working. The one
/// thing that outlives it is a delivery URL already in somebody's hands: those are signed for a
/// file and a lifetime and re-decide nothing, so the drawing stays fetchable for the rest of
/// that lifetime — bounded, the same staleness every delivery URL in this application carries,
/// and the reason the envelope hands out no other capability.
/// </para>
/// <para>
/// <b>Publishing takes the right to share the cave, not only to write the trip.</b> The trip
/// says who is underground; the cave's ACL says whether its drawing may leave the installation,
/// and that is what a published page hands over. Deciding it on trip write alone would authorise
/// one object's exposure from another object's permissions — so minting asks
/// <see cref="AccessAction.Share"/> on the anchor cave as well, the same right the feature share
/// and QR publication surfaces ask before handing a cave to somebody outside. Like those, the
/// right is asked when the decision is taken: a link already minted stands until it is revoked.
/// </para>
/// <para>
/// Who a follower is told about is the other half. The envelope is keyed by a participant's
/// place in the party, never by a caver id; what it calls them is whatever an administrator
/// typed, failing that the caver's roster name where the installation publishes names, and
/// failing that nothing at all — see <see cref="PublicTripParticipantDto"/> and
/// <see cref="TripTrackingOptions.PublishRealNames"/>.
/// </para>
/// </remarks>
public static class TripTrackingPublicationEndpoints
{
    public static RouteGroupBuilder MapTripTrackingPublicationEndpoints(this RouteGroupBuilder api)
    {
        var shares = api.MapGroup("/trip-logs/{tripLogId:guid}/tracking/shares").WithTags("TripTracking");

        shares.MapPost("/", MintAsync)
            .WithSummary("Publish the tracked trip: mints a follow link (trip write access, plus the right to share the trip's cave). The token is returned once and never stored; a trip whose cave is position-protected is refused.");
        shares.MapGet("/", ListAsync)
            .WithSummary("The trip's follow links — metadata only, never tokens (trip write access).");
        shares.MapDelete("/{shareId:guid}", RevokeAsync)
            .WithSummary("Revoke a follow link (trip write access); idempotent.");

        // The token IS the credential, so this route sits on the anonymous allow-list. What it
        // answers is a self-contained envelope built here and nowhere else, and the publication
        // refusal is re-decided on the way through — so this route never *opens* more than the
        // cave's protection allows at the moment it is called. The delivery URL it hands out is
        // the one thing that carries past that moment, for its own lifetime and no longer.
        api.MapGet("/public/trips/{token}", FollowAsync)
            .WithTags("TripTracking")
            .AllowAnonymous()
            .RequireRateLimiting(PublicTripRateLimits.PolicyName)
            .WithSummary("Follow a published trip: the party, where each of them was last reported, and the survey model to draw it in.");

        return api;
    }

    /// <summary>
    /// The one answer every unusable token gets. Distinct from the feature and album share codes
    /// on purpose: the surfaces are different and a client that can tell them apart can say
    /// something useful, while a follower still cannot tell why this one failed.
    /// </summary>
    /// <remarks>
    /// Shared with the past-track routes rather than copied, and they add no code of their own: a
    /// refusal that told "your token is bad" apart from "that past trip is gone" would be an answer
    /// about which trips exist and which tokens were once real.
    /// </remarks>
    internal const string NotFoundCode = "tracking.share_not_found";

    /// <summary>
    /// The caller may run this trip but may not hand its cave to the internet.
    /// </summary>
    /// <remarks>
    /// One code whether or not they can read the cave, and deliberately not the two-shaped
    /// refusal the cave's own routes give. The resource in the address is the trip, which this
    /// caller demonstrably holds; splitting the answer would turn a refusal about publishing
    /// into an answer about which caves this account may read.
    /// </remarks>
    private const string CaveRefusedCode = "tracking.publication_refused_cave";

    // ---- management ----------------------------------------------------------------------

    private static async Task<Results<Created<TripTrackingShareCreatedDto>, ProblemHttpResult>> MintAsync(
        Guid tripLogId, SilexGisDbContext db, IAccessService access, FeatureProtection protection,
        IAccessContextAccessor accessAccessor, IOptions<TripTrackingOptions> options,
        TripPublicationAnnouncer announcer, TimeProvider clock, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        if (await PublicationRefusalAsync(db, access, protection, ctx!, tripLogId, ct) is { } refused) return refused;

        // A link is only worth minting for a watch that is running. The published read answers
        // nothing for a watch that is off or closed — that is what ends a publication without
        // anybody remembering to — so minting one here would hand somebody an address that has
        // never worked and will not start working when they paste it into an article. Refused at
        // the moment of the decision instead, where there is somebody to tell.
        var tracking = await db.TripTrackings.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        if (tracking?.State is not TripTrackingState.Armed)
        {
            return ApiProblems.Conflict("tracking.publication_refused_not_armed",
                "A follow link opens a page only while the watch is running, so arm it before publishing.");
        }

        // 32 random bytes, base64url-encoded, become the URL token; only its SHA-256 is stored,
        // so a database leak cannot resurrect live links and the token cannot be shown again.
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var share = new TripTrackingShare
        {
            TripLogId = tripLogId,
            TokenHash = HashToken(token),
            CreatedBy = ctx!.UserId,
            // Fixed here and never extended by a read. What governs it — and why an expiry is only
            // one of three things that end a publication — is argued where the rule lives.
            ExpiresAt = TripPublicationWindow.ExpiresAtFor(
                clock.GetUtcNow(), trip!.TripDate, trip.TripDateEnd, options.Value.ShareLifetime),
        };
        db.TripTrackingShares.Add(share);

        // Queued before the save that commits the link, so nobody is told about a publication that
        // did not happen — and so that the people whose names this puts on the internet are told at
        // the moment it happens rather than never.
        await announcer.AnnounceAsync(trip, ct);

        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/trip-logs/{tripLogId}/tracking/shares/{share.Id}",
            new TripTrackingShareCreatedDto(share.Id, token, share.CreatedAt, share.ExpiresAt));
    }

    private static async Task<Results<Ok<List<TripTrackingShareDto>>, ProblemHttpResult>> ListAsync(
        Guid tripLogId, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var items = await db.TripTrackingShares.AsNoTracking()
            .Where(s => s.TripLogId == tripLogId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new TripTrackingShareDto(s.Id, s.CreatedBy, s.CreatedAt, s.RevokedAt, s.ExpiresAt))
            .ToListAsync(ct);
        return TypedResults.Ok(items);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeAsync(
        Guid tripLogId, Guid shareId, SilexGisDbContext db, IAccessService access,
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var share = await db.TripTrackingShares
            .FirstOrDefaultAsync(s => s.Id == shareId && s.TripLogId == tripLogId, ct);
        if (share is null) return ApiProblems.NotFound(NotFoundCode);

        // Idempotent: a second revoke keeps the original revocation timestamp.
        if (share.RevokedAt is null)
        {
            share.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    // ---- the published read ----------------------------------------------------------------

    private static async Task<Results<Ok<PublicTripTrackingEnvelopeDto>, ProblemHttpResult>> FollowAsync(
        string token, SilexGisDbContext db, FeatureProtection protection, ICrsRegistry crs,
        IFileAccessTokenService tokens, IOptions<TripTrackingOptions> options, TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > TripTrackingRules.MaxShareTokenLength)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var hash = HashToken(token);
        var share = await db.TripTrackingShares.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (share is null) return ApiProblems.NotFound(NotFoundCode);

        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == share.TripLogId, ct);
        if (trip is null) return ApiProblems.NotFound(NotFoundCode);

        var tracking = await db.TripTrackings.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripLogId == trip.Id, ct);

        // Whether this link is still a link, asked here rather than remembered anywhere. Revoked,
        // lapsed, and pointing at a watch that has closed or was never armed are four different
        // stories and one answer — the same 404 an invented token gets, built above and reused
        // below without a branch of its own, because a refusal that distinguished them would tell
        // somebody holding a guess that their guess was once real. The rule itself, and the
        // argument for it being three things rather than one, live in Domain.
        //
        // This is the LIVE window, and it governs this route alone. The past-track routes beside
        // it ask a second, longer window — see TripPastTrackWindow — because the archive of trips
        // that are already over was asked for so that it can be read over time, and gating it on
        // this window would make it reachable only while some party of that cave happened to be
        // underground. Nothing there widens this: a link outside both windows answers exactly what
        // an invented one answers, so neither route tells a stranger that a token was once real.
        // Do not "unify" the two gates — they are two deliberate lifetimes, not an inconsistency.
        var open = tracking is not null && TripPublicationWindow.IsOpen(
            clock.GetUtcNow(),
            share.RevokedAt,
            share.ExpiresAt,
            tracking.State,
            tracking.ClosedAt,
            options.Value.ShareGraceAfterClose);
        if (!open) return ApiProblems.NotFound(NotFoundCode);

        // The refusal, taken again. A cave that was open when the link was minted and is
        // protected now closes the page, and so does a configuration that has lost the cave it
        // was anchored to — answered as an unknown token is, because whether this trip exists is
        // part of what the refusal keeps back.
        if (tracking?.CaveFeatureId is not { } configCave) return ApiProblems.NotFound(NotFoundCode);
        var publishable = await TrackingWithholding.PublishableCaveIdsAsync(db, protection, [configCave], ct);
        if (!publishable.Contains(configCave)) return ApiProblems.NotFound(NotFoundCode);

        // The party, folded by the one routine both published reads use. Drawability is decided
        // against this trip's own survey, which for this route is the survey the envelope hands
        // over — the list route beside it passes a different one deliberately.
        var party = await PartyAsync(
            db, protection, options.Value, trip.Id, tracking.SurveyModelId, configCave, ct);

        return TypedResults.Ok(new PublicTripTrackingEnvelopeDto(
            trip.Id,
            trip.Title,
            trip.TripDate,
            trip.TripDateEnd,
            tracking.State,
            tracking.ArmedAt,
            tracking.ClosedAt,
            party.PositionsWithheld,
            await ModelAsync(db, protection, crs, tokens, tracking.SurveyModelId, configCave, ct),
            party.Teams,
            party.Participants));
    }


    /// <summary>
    /// A party as a published surface tells it: who is on the trip, what each of them is called
    /// here, which team they were last put in, and where each was last reported — decided against
    /// one named survey.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Extracted because two routes now fold the same party, and the rules inside it are the
    /// ones that must not be spelled twice.</b> Whether a position may be shown at all, whether it
    /// belongs to the survey being drawn, what somebody is called when nobody typed a caption, and
    /// what their standing is — each of those is a decision about disclosure or about honesty, and a
    /// second copy of any of them is how one public surface comes to answer what the other refuses.
    /// </para>
    /// <para>
    /// <b><paramref name="drawnOnSurveyModelId"/> is the parameter that earns the extraction.</b>
    /// The followed route passes the trip's own survey, because the envelope hands that survey over
    /// and the drawing is of it. The list route passes the survey the <em>token's</em> trip hands
    /// over, because that is the model the page already has loaded and the only one it can draw on
    /// — so a sibling trip whose reports were measured elsewhere comes back placeless with
    /// <c>PositionOnOtherModel</c> set, which is the existing rule about a re-pointed survey applied
    /// across trips rather than a new one invented for them.
    /// </para>
    /// </remarks>
    internal static async Task<PublicParty> PartyAsync(
        SilexGisDbContext db, FeatureProtection protection, TripTrackingOptions options,
        Guid tripLogId, Guid? drawnOnSurveyModelId, Guid configCave, CancellationToken ct)
    {
        var teams = await db.TripTeams.AsNoTracking()
            .Where(t => t.TripLogId == tripLogId).OrderBy(t => t.Title).ToListAsync(ct);
        var labels = await db.TripTrackingParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripLogId)
            .ToDictionaryAsync(p => p.CaverId, p => p.DisplayLabel, ct);

        var roster = await RosterOrderAsync(db, tripLogId, ct);

        // The roster's own name for each of them, where this installation publishes names at all.
        //
        // Read straight off the roster rather than through the shared caver-label resolver, and
        // deliberately: that resolver answers nothing to a caller with no account (an anonymous
        // caller never turns an id into a name, which is the rule everywhere else in this
        // application) and otherwise applies the account-profile rules, under which an account
        // holder's chosen display name can stand in for their roster name. Neither belongs here.
        // What a club publishes about a trip is the name the trip itself records — the roster's —
        // and it is published because the people running this installation decided to publish it,
        // not because a profile setting happened to allow it. A later reader tempted to "fix" this
        // by routing it through the resolver would silently empty every published page.
        //
        // <b>The consequence, stated so that nobody meets it as a surprise:</b> where a caver holds
        // an account and has set a display name, every signed-in surface calls them by it and this
        // page calls them by their roster name instead — so one person can appear under two names,
        // which is exactly what the shared resolver exists to prevent elsewhere. It is accepted
        // here, and the alternative is worse rather than merely different: the account label is
        // "display name, or user name, or `user-` and eight hex digits", and accounts are created
        // with the address as the user name, so deferring to it would publish `user-1a2b3c4d` for
        // every member who never chose a display name — a name-shaped string that identifies
        // nobody, on the page whose whole point is naming people. The control that does travel to
        // this page is the caption below, which an administrator sets per trip.
        var names = options.PublishRealNames
            ? await db.Cavers.AsNoTracking()
                .Where(c => roster.Contains(c.Id))
                .Select(c => new { c.Id, c.FullName })
                .ToDictionaryAsync(c => c.Id, c => c.FullName, ct)
            : [];

        // A tracked trip's whole event log is small (reports arrive by relayed word, minutes
        // apart) — fold the latest-per-caver in memory, exactly as the signed-in read does.
        var events = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == tripLogId)
            .OrderBy(e => e.RecordedAt).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(ct);

        // Per row, because history can span models: a position anchored to some other cave is
        // shown only if that cave could itself have been published, and one whose anchor is gone
        // is shown to nobody. The page-level refusal above is about the trip; this is about the
        // row, and both are the same predicate asked of different caves.
        var caveIds = events.Where(e => e.CaveFeatureId is not null).Select(e => e.CaveFeatureId!.Value)
            .Append(configCave).Distinct().ToList();
        var openCaves = await TrackingWithholding.PublishableCaveIdsAsync(db, protection, caveIds, ct);

        var byCaver = events.GroupBy(e => e.CaverId).ToDictionary(g => g.Key, g => g.ToList());
        var participants = new List<PublicTripParticipantDto>();
        var withheldAny = false;
        var ordinal = 0;
        foreach (var caverId in roster)
        {
            ordinal++;
            byCaver.TryGetValue(caverId, out var own);
            var last = own?.Count > 0 ? own[^1] : null;
            var lastPositioned = own?.LastOrDefault(TrackingWithholding.HasPosition);
            var lastTeamed = own?.LastOrDefault(e => e.TeamId is not null);
            var positionOpen = lastPositioned is null
                || TrackingWithholding.PositionOpen(lastPositioned, openCaves);
            if (!positionOpen) withheldAny = true;

            // Whether that place belongs to the survey this page hands over. A watch re-pointed at
            // a corrected survey mid-trip leaves every earlier report naming the one it was made
            // against, and this page is a drawing: a name from another survey put beside it reads
            // as a place on it. Refused here rather than in the viewer, because a follower's page
            // must not be trusted to hide what the server sent, and the row is dropped to the same
            // shape a placeless report has — with the bit below saying that this one is not that.
            var drawable = lastPositioned is null
                || TripTrackingRules.DrawableOn(lastPositioned.SurveyModelId, drawnOnSurveyModelId);
            // Only ever said of a position this reader was allowed in the first place: a withheld
            // row is already an absence, and a second bit explaining that absence would give back
            // exactly what the withholding keeps.
            var elsewhere = positionOpen && !drawable;
            var shown = positionOpen && drawable ? lastPositioned : null;

            // Standing is Domain's answer, asked exactly as the signed-in read asks it. Somebody
            // nothing has been said about yet is neither in nor out — a party that has not set
            // off must not read as one that is underground — and a note about somebody must move
            // neither them nor the count they are in, which is the whole reason this is one rule
            // in one place rather than a test on the latest report written out twice.
            var standing = TripTrackingRules.StandingOf(own);
            participants.Add(new PublicTripParticipantDto(
                ordinal,
                NameFor(caverId, labels, names),
                lastTeamed?.TeamId,
                shown?.ViewerStationName,
                shown?.DepthEnteredM,
                last?.RecordedAt,
                // The position's own time rides the position's own withholding: when the station
                // is kept back the time that would date it is kept back with it. It rides the
                // other-survey refusal too — an hour beside no place is a place the reader dates
                // from whatever is nearest, which on this page is a station measured elsewhere.
                //
                // Note what this is and is not, because the past-track playback next door keeps
                // the hour of a report whose place it withholds and that looks like a
                // contradiction. It is not: the field above — when this person was last heard
                // from — is sent unconditionally here too, and it is usually this very instant,
                // the last report being usually the positioned one. What is withheld is the
                // second instant *labelled as the shown position's*, because this page draws one
                // station per person and a bare hour printed beside it dates that station. The
                // playback has no such pairing: a row there is one report, carrying its own hour
                // beside its own null station. The argument in full is at the point it emits.
                shown?.RecordedAt,
                PositionOnOtherModel: elsewhere,
                In: standing == TripStanding.Underground,
                Out: standing == TripStanding.Out));
        }

        return new PublicParty(
            [.. teams.Select(t => new PublicTripTeamDto(t.Id, t.Title))],
            participants,
            withheldAny);
    }

    /// <summary>
    /// The party in the order a published page numbers them: one entry per person, ordered by the
    /// roster row they were first written on, so their place in the list is their ordinal minus one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Roster order, not caver id: the ordinal a follower sees is a number somebody could read back
    /// over the phone, so it has to survive the roster gaining a name mid-trip. Ordering by the row
    /// the person was first written on does that — a later arrival takes the next number and nobody
    /// already on the page is renumbered.
    /// </para>
    /// <para>
    /// <b>One derivation, called by both public surfaces.</b> The live page and the past-track
    /// playback number the same party, and a second copy of this is how "Caver 3" comes to mean two
    /// different people on two pages about the same trip — with nothing on either page able to say
    /// which of them is meant.
    /// </para>
    /// </remarks>
    internal static async Task<List<Guid>> RosterOrderAsync(
        SilexGisDbContext db, Guid tripLogId, CancellationToken ct)
    {
        var rows = await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == tripLogId)
            .GroupBy(p => p.CaverId)
            .Select(g => new { CaverId = g.Key, FirstRowId = g.Min(p => p.Id) })
            .ToListAsync(ct);
        return [.. rows.OrderBy(r => r.FirstRowId).Select(r => r.CaverId)];
    }

    /// <summary>
    /// What a followed page calls one member of the party, or null for "a place in the party".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the whole rule and it is read top to bottom. <b>A label an administrator typed
    /// wins over everything</b>, because that field exists precisely so that one person can be kept
    /// off a public page by name while the rest of the party is named — if the installation's
    /// setting could override it, somebody who asked not to appear would appear the moment a
    /// setting somewhere else was flipped. Failing a label, the roster's own name, where the
    /// installation publishes names. Failing both, nothing, and the page that draws the envelope
    /// says "Caver 3" in its reader's own language.
    /// </para>
    /// <para>
    /// A roster row with a blank name answers nothing rather than an empty string: a name-shaped
    /// hole on the page is worse than the ordinal it would replace, and the difference between
    /// "unnamed" and "named badly" is one a follower cannot see.
    /// </para>
    /// <para>
    /// <b>Reachable from the signed-in read, and that is the point of it being one function.</b>
    /// The trip's own tracking read tells each participant what the published page calls them, and
    /// it has to be the same answer rather than a second implementation of the same three rules —
    /// a surface that told somebody they would appear as a place in the party while the page named
    /// them would be worse than saying nothing.
    /// </para>
    /// </remarks>
    internal static string? NameFor(
        Guid caverId, Dictionary<Guid, string> labels, Dictionary<Guid, string> rosterNames)
    {
        if (labels.TryGetValue(caverId, out var label) && !string.IsNullOrWhiteSpace(label))
        {
            return label;
        }
        var name = rosterNames.GetValueOrDefault(caverId);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// The survey model a follower's viewer draws, when the configuration still names one.
    /// </summary>
    /// <remarks>
    /// Only ever the model of the cave the publication decision was taken about: the refusal
    /// above asked about <paramref name="configCave"/>, so a model that answers to some other
    /// cave has not been decided on and is not handed over. The delivery tokens are signed for
    /// the stored bytes, the same reach the signed-in survey routes use — a survey model is only
    /// useful as its own bytes — which is exactly why a protected cave may not be published at
    /// all rather than published without its drawing.
    /// </remarks>
    internal static async Task<PublicTripSurveyModelDto?> ModelAsync(
        SilexGisDbContext db, FeatureProtection protection, ICrsRegistry crs,
        IFileAccessTokenService tokens, Guid? surveyModelId, Guid configCave, CancellationToken ct)
    {
        if (surveyModelId is null) return null;
        var model = await db.SurveyModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == surveyModelId && m.CaveFeatureId == configCave, ct);
        if (model is null) return null;

        return new PublicTripSurveyModelDto(
            model.Format,
            FileUrl(tokens, model.FileId),
            model.ConvertedFileId is { } converted ? FileUrl(tokens, converted) : null,
            model.Anchor?.X,
            model.Anchor?.Y,
            model.AnchorHeightM,
            model.SourceEpsg,
            model.SourceEpsg is { } epsg ? crs.Proj4(epsg) : null,
            // Built on this branch and no other: the model in hand is a model of the very cave
            // the publication decision was taken about, so a picture hung on one of its stations
            // is hung inside a cave that has already been established to carry no protection.
            await StationPicturesAsync(db, protection, tokens, model.Id, ct),
            // On the same branch for the same reason: a sheet declared on this model is a
            // drawing of a cave already established to carry no protection, re-decided on
            // every read exactly as the envelope itself is.
            await RasterMapsAsync(db, protection, tokens, model.Id, ct));
    }

    // ---- the pictures a follower is shown ----------------------------------------------------

    /// <summary>
    /// How many station-anchoring links one published model is read for.
    /// </summary>
    /// <remarks>
    /// The same number the signed-in panels page a model's links with — and, because a bound is
    /// only half of a window, taken from the same end: the most recently created ones. A model can
    /// accumulate more links than this, and the two surfaces reading opposite ends of the same list
    /// would be the worst possible failure here — a club marks last week's photograph for the
    /// gallery, watches it appear on the signed-in strip, and it never reaches the published page,
    /// with nothing anywhere to tell that apart from the correct answer of "nothing is published".
    /// </remarks>
    private const int MaxPictureLinks = 200;

    /// <summary>
    /// How many pictures one station is published with. The viewer draws a thumbnail per entry
    /// and fetches each one as the strip appears, on a phone, over a model surface that fits two
    /// across — so a station somebody has linked forty photographs to would be forty requests to
    /// show a handful. The same bound the signed-in derivation applies, for the same reason.
    /// </summary>
    private const int MaxPicturesPerStation = 12;

    /// <summary>
    /// How many pictures the whole envelope carries. A published page is not a bulk export, and a
    /// link somebody put in an article must not become the cheapest way to walk an archive.
    /// </summary>
    private const int MaxPictures = 200;

    /// <summary>
    /// The width the pictures are published at; a viewer re-points the same URL at the width it
    /// actually draws, spending the token it was handed rather than asking for a second one.
    /// </summary>
    private const int PublishedPictureSize = 480;

    /// <summary>
    /// The photographs a followed page hangs on this model's stations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no photo-per-station table and none is invented here.</b> "This photograph was
    /// taken at station 7" is a link with a member anchored to that station and a member that is
    /// the photograph — authored by clicking the station in the viewer — and this reads it back,
    /// the same shape the signed-in panels read. What it does not do is read it on the same terms:
    /// those ask a caller's rights, and here there is no caller to ask. So every gate below is a
    /// fact about the picture and about the cave, never a fact about who is looking.
    /// </para>
    /// <para>
    /// <b>The consent gate.</b> Only a photograph an administrator has put in the installation's
    /// public gallery. That flag is an act of publication that already exists and already means
    /// "anyone may see this", and it is the only thing here that a person decided about this one
    /// picture. The trip's own share token cannot stand in for it: the token was minted once, and
    /// the set of pictures linked to a model keeps growing afterwards — a picture linked next month
    /// would otherwise appear in an article published today, reviewed by nobody. The consequence is
    /// accepted rather than softened: an installation that has curated no gallery publishes no
    /// pictures here, and that is the correct answer rather than a gap to fill.
    /// </para>
    /// <para>
    /// <b>The location gates, which the consent flag is not.</b> Somebody marking a picture public
    /// decided it may be seen; they did not decide anything about where a cave is. Three separate
    /// things answer that, and all three are on the server because the caller is a stranger:
    /// </para>
    /// <para>
    /// <em>One.</em> The cave. This runs only inside the branch that already holds a model of the
    /// cave the publication decision was taken about — and that decision refuses a protected cave
    /// the whole page, re-decided on every read. So a link minted while a cave was open and read
    /// after it was guarded answers the same 404 it always did, with no pictures in it, because
    /// there is no envelope at all.
    /// </para>
    /// <para>
    /// <em>Two.</em> The link. A link relates any number of things, and publishing one of its
    /// members at a point in space gives the whole association a position — which is the same
    /// reasoning that withholds a geotagged photograph shown under a guarded feature's name, spelled
    /// for a surface that names no feature. So a link naming any feature that is not itself
    /// unguarded publishes none of its pictures: not the guarded feature's name, which this envelope
    /// never carries anyway, but the picture that would otherwise stand as the position of it. A
    /// feature that is missing, soft-deleted or protected all fail the same closed way, and a member
    /// naming another survey model is resolved to that model's cave and asked the same question.
    /// </para>
    /// <para>
    /// <em>Three.</em> The reach. Every URL minted here opens a rendering and never the upload, so
    /// the fix a camera wrote into the file cannot leave by this door whatever the picture is of or
    /// however the URL is used afterwards. That is the one gate that holds even if a picture arrives
    /// here by a mistake in the two above.
    /// </para>
    /// <para>
    /// <b>What none of this can decide is what the picture depicts.</b> A photograph of a
    /// recognisable entrance places a cave by being looked at, and no rule can see that. The gallery
    /// flag is the answer to it and the only possible one — a person looked at the picture and
    /// published it — which is a further reason the flag is the gate rather than the share token.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<PublicTripStationPictureDto>> StationPicturesAsync(
        SilexGisDbContext db, FeatureProtection protection, IFileAccessTokenService tokens,
        Guid surveyModelId, CancellationToken ct)
    {
        // The links that anchor something to a station of this model. Ordered and bounded here
        // rather than after the joins, so the cost of this list is the same for a model with four
        // pictures on it and one with four thousand.
        //
        // Newest first, which is the order the signed-in panel pages the same model's links in.
        // Ordering by the link's creation is what makes the bound above mean the same thing on both
        // surfaces: a photograph linked today is inside the window on both, and what a heavily
        // linked model loses is the oldest links on both. Ordering by the identifier would read
        // the same way — these are time-ordered — but it would say it by accident, and the day the
        // key changes shape it would quietly start reading some other end of the list.
        var anchored = db.ResLinkMembers.AsNoTracking()
            .Where(m => m.EntityType == AttachedEntityType.SurveyModel
                && m.EntityId == surveyModelId
                && m.AnchorKind == AnchorKind.ModelStation);
        var linkIds = await db.ResLinks.AsNoTracking()
            .Where(l => anchored.Any(m => m.ResLinkId == l.Id))
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .Select(l => l.Id)
            .Take(MaxPictureLinks)
            .ToListAsync(ct);
        if (linkIds.Count == 0) return [];

        var members = await db.ResLinkMembers.AsNoTracking()
            .Where(m => linkIds.Contains(m.ResLinkId))
            .OrderBy(m => m.ResLinkId).ThenBy(m => m.SortOrder).ThenBy(m => m.Id)
            .ToListAsync(ct);

        // Every survey model any of these links names — including ones belonging to other caves,
        // which is how a link comes to touch a second cave at all. A model whose row has gone is
        // deliberately absent from this map, and a link naming it is withheld below: with nothing
        // left to evaluate protection against, the only safe answer is no answer.
        var namedModelIds = members
            .Where(m => m.EntityType == AttachedEntityType.SurveyModel && m.EntityId != null)
            .Select(m => m.EntityId!.Value).Distinct().ToList();
        var caveOfModel = await db.SurveyModels.AsNoTracking()
            .Where(m => namedModelIds.Contains(m.Id))
            .Select(m => new { m.Id, m.CaveFeatureId })
            .ToDictionaryAsync(m => m.Id, m => m.CaveFeatureId, ct);

        var namedFeatureIds = members
            .Select(m => m.FeatureId)
            .OfType<Guid>()
            .Concat(caveOfModel.Values)
            .Distinct()
            .ToList();
        var unguarded = await TrackingWithholding.UnguardedFeatureIdsAsync(db, protection, namedFeatureIds, ct);

        // The photographs, filtered to the ones somebody published. Joined through the document's
        // current version, because what a picture is now is the file that version serves — the same
        // file every other surface shows, so a replaced photograph is published as its replacement
        // rather than as the bytes it was uploaded with.
        var documentIds = members
            .Where(m => m.EntityType == AttachedEntityType.Document && m.EntityId != null)
            .Select(m => m.EntityId!.Value).Distinct().ToList();
        var published = documentIds.Count == 0
            ? []
            : await (from details in db.PhotoDetails.AsNoTracking()
                     where documentIds.Contains(details.DocumentId) && details.InPublicGallery
                     join document in db.Documents.AsNoTracking() on details.DocumentId equals document.Id
                     join version in db.DocumentVersions.AsNoTracking() on document.Id equals version.DocumentId
                     where version.IsCurrent
                     join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                     // No rendering exists for anything but an image, and a URL pointing at one
                     // that cannot be drawn is a broken picture on somebody's website.
                     where file.Kind == FileKind.Image
                     orderby file.CreatedAt, file.Id
                     select new
                     {
                         DocumentId = document.Id,
                         document.Title,
                         details.Caption,
                         FileId = file.Id,
                     })
                .ToListAsync(ct);
        var pictureOf = published
            .GroupBy(row => row.DocumentId)
            .ToDictionary(g => g.Key, g => g.First());
        if (pictureOf.Count == 0) return [];

        var pictures = new List<PublicTripStationPictureDto>();
        var perStation = new Dictionary<string, int>(StringComparer.Ordinal);
        var placed = new HashSet<(string Station, Guid Document)>();
        var byLink = members.GroupBy(m => m.ResLinkId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var linkId in linkIds)
        {
            var own = byLink.GetValueOrDefault(linkId) ?? [];
            if (!LinkIsUnguarded(own, caveOfModel, unguarded)) continue;

            // Only stations of the model on screen. A link can anchor to a station of some other
            // model as well, and that name means nothing in this drawing.
            var stations = own
                .Where(m => m.EntityType == AttachedEntityType.SurveyModel
                    && m.EntityId == surveyModelId
                    && m.AnchorKind == AnchorKind.ModelStation)
                .Select(m => StationOf(m.Anchor))
                .OfType<string>()
                .ToList();
            if (stations.Count == 0) continue;

            var linked = own
                .Where(m => m.EntityType == AttachedEntityType.Document && m.EntityId != null)
                .Select(m => pictureOf.GetValueOrDefault(m.EntityId!.Value))
                .Where(row => row is not null)
                .ToList();
            if (linked.Count == 0) continue;

            // A link holding several stations and several pictures puts all of its pictures on all
            // of its stations: what it says is that these things belong together, and it names no
            // pairing inside itself for this to read one out of.
            foreach (var station in stations)
            {
                foreach (var row in linked)
                {
                    if (pictures.Count >= MaxPictures) return pictures;
                    if (perStation.GetValueOrDefault(station) >= MaxPicturesPerStation) break;
                    // The same photograph reaches one station through two links as often as not —
                    // it is linked to the station and to the passage the station stands in — and
                    // the strip would otherwise show it twice.
                    if (!placed.Add((station, row!.DocumentId))) continue;

                    pictures.Add(new PublicTripStationPictureDto(
                        station,
                        ThumbnailUrl(tokens, row.FileId, PublishedPictureSize),
                        string.IsNullOrWhiteSpace(row.Caption) ? NullIfBlank(row.Title) : row.Caption));
                    perStation[station] = perStation.GetValueOrDefault(station) + 1;
                }
            }
        }

        return pictures;
    }

    /// <summary>
    /// Whether every feature this link names carries no protection — the link-level gate.
    /// </summary>
    /// <remarks>
    /// Written as "nothing it names falls outside the unguarded set" rather than "nothing it names
    /// is protected", because the two differ on everything the set could not account for: a feature
    /// that has been deleted, one whose row is gone, a survey-model member whose model no longer
    /// exists. Those are the cases where there is nothing left to evaluate, and the answer to
    /// nothing must be no.
    /// </remarks>
    private static bool LinkIsUnguarded(
        IReadOnlyList<ResLinkMember> members,
        IReadOnlyDictionary<Guid, Guid> caveOfModel,
        HashSet<Guid> unguarded)
    {
        foreach (var member in members)
        {
            if (member.FeatureId is { } feature && !unguarded.Contains(feature)) return false;
            if (member.EntityType == AttachedEntityType.SurveyModel && member.EntityId is { } modelId)
            {
                if (!caveOfModel.TryGetValue(modelId, out var cave) || !unguarded.Contains(cave))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// The station a model-station anchor names, or null when the payload does not carry one.
    /// </summary>
    /// <remarks>
    /// The string is passed through exactly as it was stored, and is deliberately not rebuilt from
    /// the model's station rows: what an anchor holds is the name the viewer handed over when
    /// somebody clicked the station, and it is the name the viewer resolves a picture by. Spelling
    /// it again from the rows would invent a second spelling of an address that already exists.
    /// </remarks>
    private static string? StationOf(string? anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return null;
        try
        {
            using var payload = JsonDocument.Parse(anchor);
            if (payload.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!payload.RootElement.TryGetProperty("station", out var station)) return null;
            return station.ValueKind == JsonValueKind.String ? NullIfBlank(station.GetString()) : null;
        }
        catch (JsonException)
        {
            // A payload this build cannot read anchors nothing it can place, which is the same
            // answer as no payload at all.
            return null;
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    // ---- the map sheets a follower is shown --------------------------------------------------

    /// <summary>
    /// How many of a model's newest map-declaration links are read. Far above what any real
    /// cave declares — the strip that shows these fits a handful of tabs — and a window
    /// rather than an unbounded read because everything on this surface is answered to
    /// strangers: the cost of the answer must not be a fact the caller controls.
    /// </summary>
    private const int MaxMapLinks = 50;

    /// <summary>
    /// How many sheets the envelope carries, applied after the deterministic sort so the
    /// sheets that survive are the ones every surface lists first.
    /// </summary>
    private const int MaxMaps = 12;

    /// <summary>
    /// How many of a model's newest station-point links are read. Newest-first for the
    /// reason the picture window is: the signed-in fold reads the same end, so what a
    /// heavily-pinned model loses is the oldest pins on both surfaces — and the newest-wins
    /// duplicate rule below makes the newest end the meaningful one.
    /// </summary>
    private const int MaxMapPointLinks = 1000;

    /// <summary>
    /// The width map sheets are published at — the largest rendering the thumbnail route
    /// offers, because a map is read rather than glanced at: station labels and passage
    /// names on a scanned plan are exactly what a follower zooms into.
    /// </summary>
    private const int PublishedMapSize = 1200;

    /// <summary>
    /// The scanned map sheets a followed page draws the party on, with their station points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no map table and none is invented here.</b> "This image is the plan view
    /// of this model" is a link under a seeded map-of code with the document as main member,
    /// and "this point on the image is station S" is a link under the seeded pin code pairing
    /// an image-region point with a model station — authored in the survey viewer, read back
    /// here the same shape the signed-in folds read. Code AND structure are both required,
    /// exactly as those folds require them: the code alone must not turn a mislabeled link
    /// into a sheet, and structure alone must not promote a casual annotation into one.
    /// </para>
    /// <para>
    /// <b>Everything the signed-in fold resolves per reader is resolved here per nothing.</b>
    /// A signed-in pane matches each pin's measured-against file to the rendering on screen
    /// and counts the rest as superseded; an anonymous page has no version history to ask, so
    /// only pins measured against the very file being served travel, and the superseded ones
    /// are simply absent — a count of them is version-history information this envelope does
    /// not carry. Duplicate pins fold newest-wins under the same tie-break the client fold
    /// uses, so the two kinds of surface never disagree about where a station sits.
    /// </para>
    /// <para>
    /// <b>The gates are the surface's own, none invented and none skipped.</b> This runs only
    /// inside the branch that already refused a protected cave the whole page, re-decided per
    /// read; each link passes the same link-level guard the pictures pass, so a declaration
    /// or a pin whose link names a guarded feature publishes nothing; and every URL minted
    /// here opens a rendering and never the upload — <see cref="FileDelivery.DerivativesOnly"/>
    /// unconditionally, the station-pictures reach, because a scan's own bytes carry whatever
    /// its format recorded. Which maps a publication <em>ought</em> to cover is a consent
    /// question the owner has deferred (2026-09-22); until it is decided this list is the
    /// declared maps of the published model, provisional by instruction.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<PublicTripRasterMapDto>> RasterMapsAsync(
        SilexGisDbContext db, FeatureProtection protection, IFileAccessTokenService tokens,
        Guid surveyModelId, CancellationToken ct)
    {
        // The seeded vocabulary rows, by id, because links store the id. The seeder guarantees
        // them on every installation; a database missing one yields no sheets of that kind
        // rather than an error a stranger can provoke.
        var codes = ResLinkRelationTypeSeeds.MapViewCodes
            .Append(ResLinkRelationTypeSeeds.MapStationPointCode).ToArray();
        var relations = await db.ResLinkRelationTypes.AsNoTracking()
            .Where(r => codes.Contains(r.Code))
            .Select(r => new { r.Id, r.Code })
            .ToListAsync(ct);
        var codeOf = relations.ToDictionary(r => r.Id, r => r.Code);
        var declarationTypeIds = relations
            .Where(r => r.Code != ResLinkRelationTypeSeeds.MapStationPointCode)
            .Select(r => r.Id).ToArray();
        var pinTypeId = relations
            .Where(r => r.Code == ResLinkRelationTypeSeeds.MapStationPointCode)
            .Select(r => (long?)r.Id).FirstOrDefault();
        if (declarationTypeIds.Length == 0) return [];

        var namesThisModel = db.ResLinkMembers.AsNoTracking()
            .Where(m => m.EntityType == AttachedEntityType.SurveyModel && m.EntityId == surveyModelId);

        var declarations = await db.ResLinks.AsNoTracking()
            .Where(l => l.RelationTypeId != null
                && declarationTypeIds.Contains(l.RelationTypeId!.Value)
                && namesThisModel.Any(m => m.ResLinkId == l.Id))
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Take(MaxMapLinks)
            .Select(l => new { l.Id, l.RelationTypeId, l.CreatedAt })
            .ToListAsync(ct);
        if (declarations.Count == 0) return [];

        var pinLinkRows = await db.ResLinks.AsNoTracking()
            .Where(l => pinTypeId != null && l.RelationTypeId == pinTypeId
                && namesThisModel.Any(m => m.ResLinkId == l.Id))
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Take(MaxMapPointLinks)
            .Select(l => new { l.Id, l.CreatedAt })
            .ToListAsync(ct);

        var linkIds = declarations.Select(d => d.Id).Concat(pinLinkRows.Select(p => p.Id)).ToList();
        var members = await db.ResLinkMembers.AsNoTracking()
            .Where(m => linkIds.Contains(m.ResLinkId))
            .OrderBy(m => m.ResLinkId).ThenBy(m => m.SortOrder).ThenBy(m => m.Id)
            .ToListAsync(ct);
        var byLink = members.GroupBy(m => m.ResLinkId).ToDictionary(g => g.Key, g => g.ToList());

        // The same link-level gate the pictures pass, computed the same way: every feature any
        // of these links names — directly, or through a survey-model member resolved to its
        // cave — has to be affirmatively unguarded, and anything the sets cannot account for
        // fails closed. The rule's home and its reasoning are on the pictures above.
        var namedModelIds = members
            .Where(m => m.EntityType == AttachedEntityType.SurveyModel && m.EntityId != null)
            .Select(m => m.EntityId!.Value).Distinct().ToList();
        var caveOfModel = await db.SurveyModels.AsNoTracking()
            .Where(m => namedModelIds.Contains(m.Id))
            .Select(m => new { m.Id, m.CaveFeatureId })
            .ToDictionaryAsync(m => m.Id, m => m.CaveFeatureId, ct);
        var namedFeatureIds = members
            .Select(m => m.FeatureId)
            .OfType<Guid>()
            .Concat(caveOfModel.Values)
            .Distinct()
            .ToList();
        var unguarded = await TrackingWithholding.UnguardedFeatureIdsAsync(db, protection, namedFeatureIds, ct);

        // The structural half of each declaration, mirroring the signed-in fold: a
        // whole-document member (the image) and this model under a coverage-shaped anchor —
        // the whole model, or a "this sheet is the northern branch" statement. A map-of link
        // onto some other model is that model's map; one with no document declares nothing
        // drawable; a member anchored to a single station is not how coverage is declared.
        var candidates = new List<(Guid LinkId, string Code, DateTimeOffset CreatedAt, Guid DocumentId)>();
        foreach (var declaration in declarations)
        {
            var own = byLink.GetValueOrDefault(declaration.Id) ?? [];
            if (!LinkIsUnguarded(own, caveOfModel, unguarded)) continue;
            var covers = own.Any(m => m.EntityType == AttachedEntityType.SurveyModel
                && m.EntityId == surveyModelId
                && m.AnchorKind is AnchorKind.Whole or AnchorKind.ModelSurvey or AnchorKind.ModelStationRange);
            var document = own.FirstOrDefault(m => m.EntityType == AttachedEntityType.Document
                && m.EntityId != null && m.AnchorKind == AnchorKind.Whole);
            if (!covers || document is null) continue;
            candidates.Add((declaration.Id, codeOf[declaration.RelationTypeId!.Value],
                declaration.CreatedAt, document.EntityId!.Value));
        }
        if (candidates.Count == 0) return [];

        // What each declared document is now: its current version's image file and its title —
        // the same join the published pictures use, because a replaced scan is published as its
        // replacement, and a document whose current file is not an image is a tab that cannot
        // draw and is left out rather than published broken.
        var documentIds = candidates.Select(c => c.DocumentId).Distinct().ToList();
        var images = await (from document in db.Documents.AsNoTracking()
                            where documentIds.Contains(document.Id)
                            join version in db.DocumentVersions.AsNoTracking() on document.Id equals version.DocumentId
                            where version.IsCurrent
                            join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                            where file.Kind == FileKind.Image
                            orderby file.CreatedAt, file.Id
                            select new { document.Id, document.Title, FileId = file.Id })
            .ToListAsync(ct);
        var imageOf = images.GroupBy(row => row.Id).ToDictionary(g => g.Key, g => g.First());

        // Every point defined on any declared document, folded newest-wins per (document,
        // station) under the exact tie-break the client fold uses — creation instant, then the
        // link id's string form, ordinally — so both kinds of surface pick the same duplicate.
        // Only point-shaped payloads measured against the file being served survive: regions
        // are reserved for a future approximate-area variant and never host a marker, and a
        // pin measured against a superseded scan is absent here rather than drawn mislocated.
        var newestPin = new Dictionary<(Guid Document, string Station),
            (DateTimeOffset CreatedAt, string LinkId, double X, double Y)>();
        foreach (var pin in pinLinkRows)
        {
            var own = byLink.GetValueOrDefault(pin.Id) ?? [];
            if (!LinkIsUnguarded(own, caveOfModel, unguarded)) continue;
            var stations = own
                .Where(m => m.EntityType == AttachedEntityType.SurveyModel
                    && m.EntityId == surveyModelId
                    && m.AnchorKind == AnchorKind.ModelStation)
                .Select(m => StationOf(m.Anchor))
                .OfType<string>()
                .ToList();
            if (stations.Count == 0) continue;

            foreach (var member in own)
            {
                if (member.EntityType != AttachedEntityType.Document
                    || member.EntityId is not { } documentId
                    || member.AnchorKind != AnchorKind.ImageRegion
                    || member.AnchorFileId is not { } measuredAgainst
                    || !imageOf.TryGetValue(documentId, out var image)
                    || measuredAgainst != image.FileId
                    || PointOf(member.Anchor) is not { } point)
                {
                    continue;
                }
                foreach (var station in stations)
                {
                    var key = (documentId, station);
                    var claim = (pin.CreatedAt, LinkId: pin.Id.ToString(), point.X, point.Y);
                    if (!newestPin.TryGetValue(key, out var held)
                        || claim.CreatedAt > held.CreatedAt
                        || (claim.CreatedAt == held.CreatedAt
                            && string.CompareOrdinal(claim.LinkId, held.LinkId) > 0))
                    {
                        newestPin[key] = claim;
                    }
                }
            }
        }

        // One tab per (document, view): the same declaration stated twice is one sheet, and
        // the newest statement of it wins — which the newest-first window already ordered.
        var seen = new HashSet<(Guid Document, string Code)>();
        var maps = new List<(string? Title, string Code, DateTimeOffset CreatedAt, Guid LinkId, Guid DocumentId, Guid FileId)>();
        foreach (var candidate in candidates)
        {
            if (!imageOf.TryGetValue(candidate.DocumentId, out var image)) continue;
            if (!seen.Add((candidate.DocumentId, candidate.Code))) continue;
            maps.Add((NullIfBlank(image.Title), candidate.Code, candidate.CreatedAt,
                candidate.LinkId, candidate.DocumentId, image.FileId));
        }

        // The tab-strip order every surface sorts by: plan before profile before other, then
        // title, then age — deterministic without a stored ordering, because links have none.
        return maps
            .OrderBy(map => (int)ViewKindOf(map.Code))
            .ThenBy(map => map.Title ?? "", StringComparer.Ordinal)
            .ThenBy(map => map.CreatedAt)
            .ThenBy(map => map.LinkId)
            .Take(MaxMaps)
            .Select(map => new PublicTripRasterMapDto(
                map.Title,
                ViewKindOf(map.Code),
                ThumbnailUrl(tokens, map.FileId, PublishedMapSize),
                [.. newestPin
                    .Where(entry => entry.Key.Document == map.DocumentId)
                    .OrderBy(entry => entry.Key.Station, StringComparer.Ordinal)
                    .Select(entry => new PublicTripMapPointDto(entry.Key.Station, entry.Value.X, entry.Value.Y))]))
            .ToList();
    }

    /// <summary>The envelope's spelling of a seeded map-of code — the code read out, nothing more.</summary>
    private static PublicTripMapViewKind ViewKindOf(string code) =>
        code == ResLinkRelationTypeSeeds.MapPlanOfCode ? PublicTripMapViewKind.Plan
        : code == ResLinkRelationTypeSeeds.MapProfileOfCode ? PublicTripMapViewKind.Profile
        : PublicTripMapViewKind.Other;

    /// <summary>
    /// The point an image-region anchor stores, or null for any other shape or an unreadable
    /// payload. Fractions outside 0–1 — which the write path refuses, but this reads rows and
    /// not requests — anchor nothing rather than a marker off the picture.
    /// </summary>
    private static (double X, double Y)? PointOf(string? anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return null;
        try
        {
            using var payload = JsonDocument.Parse(anchor);
            if (payload.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!payload.RootElement.TryGetProperty("shape", out var shape)
                || shape.ValueKind != JsonValueKind.String
                || shape.GetString() != "point")
            {
                return null;
            }
            if (!payload.RootElement.TryGetProperty("x", out var x) || x.ValueKind != JsonValueKind.Number
                || !payload.RootElement.TryGetProperty("y", out var y) || y.ValueKind != JsonValueKind.Number)
            {
                return null;
            }
            var point = (X: x.GetDouble(), Y: y.GetDouble());
            return point is { X: >= 0 and <= 1, Y: >= 0 and <= 1 } ? point : null;
        }
        catch (JsonException)
        {
            // A payload this build cannot read defines nothing it can draw — the same answer
            // the station reader next door gives.
            return null;
        }
    }

    // ---- shared ----------------------------------------------------------------------------

    /// <summary>
    /// Whether this caller may publish this trip at all, as a refusal or null for "they may".
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tracked trip is published against one cave — the snapshot the tracking configuration
    /// took when its model was chosen — and two separate things have to be true of it. The
    /// caller has to hold the right to share that cave, because the drawing and the station
    /// names a published page hands over are the cave's and not the trip's; and the cave has to
    /// be under no protection anywhere above it, which is a fact about the cave and is asked
    /// again on every read. A configuration with no model has no cave to decide about and is
    /// refused too: arming requires a model, so a trip in that state is not one anybody can
    /// follow, and the alternative is a link that mints successfully and never opens.
    /// </para>
    /// <para>
    /// The order is deliberate. A caller who may not share the cave is told that and nothing
    /// else — whether the cave carries protection is a fact about a cave they hold no right on,
    /// so it is not part of their refusal.
    /// </para>
    /// </remarks>
    private static async Task<ProblemHttpResult?> PublicationRefusalAsync(
        SilexGisDbContext db, IAccessService access, FeatureProtection protection,
        AccessContext ctx, Guid tripLogId, CancellationToken ct)
    {
        var tracking = await db.TripTrackings.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        if (tracking?.CaveFeatureId is not { } caveId)
        {
            return ApiProblems.Conflict("tracking.model_missing",
                "Tracking has no survey model, so there is nothing for a follower to see.");
        }

        // A cave that is gone answers no rights question worth asking; it is simply not
        // publishable, which the predicate below says on its own — fail closed either way.
        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveId && f.Kind == FeatureKind.Cave, ct);
        if (cave is not null && !(await access.DecideAsync(ctx, AccessAction.Share, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden(CaveRefusedCode,
                "Publishing this trip hands over its cave's survey drawing, which takes the right to share that cave.");
        }

        var publishable = await TrackingWithholding.PublishableCaveIdsAsync(db, protection, [caveId], ct);
        return publishable.Contains(caveId)
            ? null
            : ApiProblems.Conflict("tracking.publication_refused_protected",
                "This trip's cave has protected coordinates, so the trip cannot be published.");
    }

    /// <summary>SHA-256 of the URL token, base64url — the stored/looked-up form; the plaintext never persists.</summary>
    internal static string HashToken(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// The stored bytes of one file, for the caller of this envelope. Reserved for the survey
    /// model, which is only useful as its own bytes — see the picture mint below for why nothing
    /// else on this surface gets this reach.
    /// </summary>
    private static string FileUrl(IFileAccessTokenService tokens, Guid fileId) =>
        $"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(fileId, FileDelivery.Full))}";

    /// <summary>
    /// A rendering of one image file, and never the upload it was drawn from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FileDelivery.DerivativesOnly"/> unconditionally, and not as a default anybody may
    /// widen later. A photograph's own bytes carry the fix its camera wrote, which is a position
    /// rather than a fact about one; the rendering is produced here with every metadata profile
    /// stripped, so it shows what the picture shows and carries nothing the picture did not. The
    /// route that redeems this token re-decides nothing — a URL handed out is a decision already
    /// taken — so the reach signed in at this line is the whole of what a follower can ever spend
    /// it on, and it refuses the stored bytes however the URL is used afterwards.
    /// </para>
    /// <para>
    /// The delivery route it points at is already on the anonymous allow-list and has to be: a
    /// browser loads an image ambiently and can attach no header, so the token in the address is
    /// the whole of the caller's claim. Publishing pictures here therefore widens the set of routes
    /// a stranger can reach by nothing at all.
    /// </para>
    /// </remarks>
    private static string ThumbnailUrl(IFileAccessTokenService tokens, Guid fileId, int size) =>
        $"/api/v1/files/{fileId}/thumbnail?size={size}"
            + $"&token={Uri.EscapeDataString(tokens.CreateToken(fileId, FileDelivery.DerivativesOnly))}";
}
