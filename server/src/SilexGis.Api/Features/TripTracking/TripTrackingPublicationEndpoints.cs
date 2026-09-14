// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
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
/// SHA-256 and shown once at mint, revocable. Malformed, unknown, revoked, and pointing at a
/// trip that may no longer be published all answer one identical 404: the token is the whole of
/// a follower's claim, so nothing about its validity may be distinguishable, and a refusal that
/// said which of those it was would be an answer about which tokens exist.
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
/// place in the party, never by a caver id, and it carries a name only where an administrator
/// typed one — see <see cref="PublicTripParticipantDto"/>.
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
            .WithSummary("Follow a published trip: the party, where each of them was last reported, and the survey model to draw it in.");

        return api;
    }

    /// <summary>
    /// The one answer every unusable token gets. Distinct from the feature and album share codes
    /// on purpose: the surfaces are different and a client that can tell them apart can say
    /// something useful, while a follower still cannot tell why this one failed.
    /// </summary>
    private const string NotFoundCode = "tracking.share_not_found";

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
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null ? null : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        if (await PublicationRefusalAsync(db, access, protection, ctx!, tripLogId, ct) is { } refused) return refused;

        // 32 random bytes, base64url-encoded, become the URL token; only its SHA-256 is stored,
        // so a database leak cannot resurrect live links and the token cannot be shown again.
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var share = new TripTrackingShare
        {
            TripLogId = tripLogId,
            TokenHash = HashToken(token),
            CreatedBy = ctx!.UserId,
        };
        db.TripTrackingShares.Add(share);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/trip-logs/{tripLogId}/tracking/shares/{share.Id}",
            new TripTrackingShareCreatedDto(share.Id, token, share.CreatedAt));
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
            .Select(s => new TripTrackingShareDto(s.Id, s.CreatedBy, s.CreatedAt, s.RevokedAt))
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
        IFileAccessTokenService tokens, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > TripTrackingRules.MaxShareTokenLength)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var hash = HashToken(token);
        var share = await db.TripTrackingShares.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == hash && s.RevokedAt == null, ct);
        if (share is null) return ApiProblems.NotFound(NotFoundCode);

        var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == share.TripLogId, ct);
        if (trip is null) return ApiProblems.NotFound(NotFoundCode);

        var tracking = await db.TripTrackings.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripLogId == trip.Id, ct);

        // The refusal, taken again. A cave that was open when the link was minted and is
        // protected now closes the page, and so does a configuration that has lost the cave it
        // was anchored to — answered as an unknown token is, because whether this trip exists is
        // part of what the refusal keeps back.
        if (tracking?.CaveFeatureId is not { } configCave) return ApiProblems.NotFound(NotFoundCode);
        var publishable = await TrackingWithholding.PublishableCaveIdsAsync(db, protection, [configCave], ct);
        if (!publishable.Contains(configCave)) return ApiProblems.NotFound(NotFoundCode);

        var teams = await db.TripTeams.AsNoTracking()
            .Where(t => t.TripLogId == trip.Id).OrderBy(t => t.Title).ToListAsync(ct);
        var labels = await db.TripTrackingParticipants.AsNoTracking()
            .Where(p => p.TripLogId == trip.Id)
            .ToDictionaryAsync(p => p.CaverId, p => p.DisplayLabel, ct);

        // Roster order, not caver id: the ordinal a follower sees is a number somebody could read
        // back over the phone, so it has to survive the roster gaining a name mid-trip. Ordering
        // by the row the person was first written on does that — a later arrival takes the next
        // number and nobody already on the page is renumbered.
        var roster = await db.TripLogParticipants.AsNoTracking()
            .Where(p => p.TripLogId == trip.Id)
            .GroupBy(p => p.CaverId)
            .Select(g => new { CaverId = g.Key, FirstRowId = g.Min(p => p.Id) })
            .ToListAsync(ct);

        // A tracked trip's whole event log is small (reports arrive by relayed word, minutes
        // apart) — fold the latest-per-caver in memory, exactly as the signed-in read does.
        var events = await db.TripPositionEvents.AsNoTracking()
            .Where(e => e.TripLogId == trip.Id)
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
        foreach (var member in roster.OrderBy(r => r.FirstRowId))
        {
            ordinal++;
            byCaver.TryGetValue(member.CaverId, out var own);
            var last = own?.Count > 0 ? own[^1] : null;
            var lastPositioned = own?.LastOrDefault(TrackingWithholding.HasPosition);
            var lastTeamed = own?.LastOrDefault(e => e.TeamId is not null);
            var positionOpen = lastPositioned is null
                || TrackingWithholding.PositionOpen(lastPositioned, openCaves);
            if (!positionOpen) withheldAny = true;

            var isOut = last?.Kind == TripPositionEventKind.Exited;
            participants.Add(new PublicTripParticipantDto(
                ordinal,
                labels.GetValueOrDefault(member.CaverId),
                lastTeamed?.TeamId,
                positionOpen ? lastPositioned?.StationName : null,
                positionOpen ? lastPositioned?.DepthEnteredM : null,
                last?.RecordedAt,
                // Somebody nothing has been said about yet is neither in nor out — a party that
                // has not set off must not read as one that is underground.
                In: last is not null && !isOut,
                Out: isOut));
        }

        return TypedResults.Ok(new PublicTripTrackingEnvelopeDto(
            trip.Title,
            trip.TripDate,
            trip.TripDateEnd,
            tracking.State,
            tracking.ArmedAt,
            tracking.ClosedAt,
            withheldAny,
            await ModelAsync(db, crs, tokens, tracking.SurveyModelId, configCave, ct),
            [.. teams.Select(t => new PublicTripTeamDto(t.Id, t.Title))],
            participants));
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
    private static async Task<PublicTripSurveyModelDto?> ModelAsync(
        SilexGisDbContext db, ICrsRegistry crs, IFileAccessTokenService tokens,
        Guid? surveyModelId, Guid configCave, CancellationToken ct)
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
            model.SourceEpsg is { } epsg ? crs.Proj4(epsg) : null);
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
    private static string HashToken(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string FileUrl(IFileAccessTokenService tokens, Guid fileId) =>
        $"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(fileId, FileDelivery.Full))}";
}
