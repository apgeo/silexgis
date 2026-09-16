// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Pictures on the moments of a tracked trip — the write side only.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no read endpoint here, and that is deliberate.</b> What is stored is an ordinary
/// resource link anchored to an instant of the trip, so the surface that shows the pictures reads
/// the general link route for this trip and derives them, exactly as the survey viewer's station
/// strip already does for a model. That leaves one protection surface to audit rather than two, and
/// every gate on it is one that already exists and is already tested: the link read refuses a
/// caller who may not read the trip, a document member's picture is minted only inside the branch
/// that decided the caller may read the document, and the URL it mints is renderings-only.
/// </para>
/// <para>
/// <b>The write is purpose-built because the generic one cannot serve the act.</b> Somebody
/// emptying a memory card a week after the trip has thirty pictures at thirty instants; through the
/// general link surface that is thirty round trips, each composing three members and looking up a
/// relation code by hand, with no way to say "these belong to this trip" that the trip's own rules
/// can check.
/// </para>
/// <para>
/// <b>Nothing here mints a URL.</b> The one place a link chip's picture is minted stays the one
/// place, and this write answers with ids; the surface re-reads the links and gets its URLs from
/// there. A second mint site is how a wider reach gets handed out by a surface that never resolved
/// the right to it.
/// </para>
/// </remarks>
public static class TripTrackingPictureEndpoints
{
    /// <summary>Refusal codes, one home so the client can name them and the tests can assert them.</summary>
    public const string NotTrackedCode = "tracking.not_tracked";
    public const string InFutureCode = "tracking.picture_in_future";
    public const string NotParticipantCode = "tracking.caver_not_participant";
    public const string NotAPictureCode = "tracking.picture_not_image";
    public const string AlreadyAttachedCode = "tracking.picture_already_attached";
    public const string VocabularyMissingCode = "tracking.picture_relation_missing";
    public const string NotFoundCode = "tracking.picture_not_found";

    public static RouteGroupBuilder MapTripTrackingPictureEndpoints(this RouteGroupBuilder api)
    {
        var pictures = api.MapGroup("/trip-logs/{tripLogId:guid}/tracking/pictures").WithTags("TripTracking");

        pictures.MapPost("/", AttachAsync).WithValidation<TrackingPictureWriteRequest>()
            .WithSummary("Hang photographs on the moments of the trip they were taken at — a memory card at a time.");
        pictures.MapDelete("/{memberId:guid}", DetachAsync)
            .WithSummary("Take one photograph off the moment it was hung on.");

        return api;
    }

    private static async Task<Results<Ok<TrackingPictureResultDto>, ProblemHttpResult>> AttachAsync(
        Guid tripLogId,
        TrackingPictureWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var trip = ctx is null
            ? null
            : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null || user is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        // A trip nobody ever watched has no moments to hang anything on: there is no log to fold
        // and no replay to place a picture in, so the attachment would be stored and never drawn.
        // Armed-ever rather than armed-now, because the act this exists for happens after the
        // watch has been closed.
        var tracking = await db.TripTrackings.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TripLogId == tripLogId, ct);
        if (tracking?.ArmedAt is null)
        {
            return ApiProblems.Conflict(NotTrackedCode, "This trip was never tracked, so it has no moments.");
        }

        var items = request.Items!;
        var now = DateTimeOffset.UtcNow;

        var caverIds = items.Where(i => i.CaverId is not null).Select(i => i.CaverId!.Value).Distinct().ToList();
        var onRoster = caverIds.Count == 0
            ? []
            : await db.TripLogParticipants.AsNoTracking()
                .Where(p => p.TripLogId == tripLogId && caverIds.Contains(p.CaverId))
                .Select(p => p.CaverId)
                .ToHashSetAsync(ct);

        // Every named document in one read, with the file it currently serves. The rules stay the
        // per-row document walk; only their one storage-backed fact is batched.
        var documentIds = items.Select(i => i.DocumentId).Distinct().ToList();
        var documents = await db.Documents.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);
        var currentFiles = await CurrentFilesAsync(db, documentIds, ct);

        var pictures = new TripMomentPictures(db);
        var attached = new List<TrackingPictureAttachedDto>();
        var refused = new Dictionary<Guid, string>();
        // The same row twice in one request is one statement. Keyed by the whole triple, so the
        // ordinary case of one photograph on two different moments is still two attachments.
        var seen = new HashSet<(Guid Document, DateTimeOffset At, Guid? Caver)>();

        foreach (var item in items)
        {
            if (!seen.Add((item.DocumentId, item.At, item.CaverId)))
            {
                continue;
            }

            if (TripTrackingRules.MomentIsInFuture(item.At, now))
            {
                refused[item.DocumentId] = InFutureCode;
                continue;
            }

            if (item.CaverId is { } subject && !onRoster.Contains(subject))
            {
                refused[item.DocumentId] = NotParticipantCode;
                continue;
            }

            // Missing and unreadable answer the same way — silence in both lists — because a
            // refusal naming a photograph is a statement that it exists. Read is the floor and not
            // write: a link is an assertion about its main member, which is the trip, and the trip
            // write was established above.
            if (!documents.TryGetValue(item.DocumentId, out var document))
            {
                continue;
            }

            var content = currentFiles.GetValueOrDefault(item.DocumentId);
            if (!await DocumentAccessRules.CanReadAsync(db, access, ctx!, document, content, ct))
            {
                continue;
            }

            // Named rather than silent: this caller may read the document, so being told it is not
            // a picture discloses nothing they could not learn by opening it — and a silent skip
            // here would read as a defect on the one refusal a person can actually act on.
            if (content is not { Kind: FileKind.Image })
            {
                refused[item.DocumentId] = NotAPictureCode;
                continue;
            }

            var result = await pictures.AttachAsync(
                tripLogId, item.DocumentId, item.At, item.CaverId, item.Caption, user!.UserId, ct);
            switch (result.Outcome)
            {
                case TripMomentPictureOutcome.Attached:
                    attached.Add(new TrackingPictureAttachedDto(
                        result.MemberId, item.DocumentId, item.At, item.CaverId));
                    break;
                case TripMomentPictureOutcome.AlreadyThere:
                    refused[item.DocumentId] = AlreadyAttachedCode;
                    break;
                default:
                    return ApiProblems.Conflict(VocabularyMissingCode,
                        "The link vocabulary this feature is built on is not seeded here.");
            }
        }

        // The refusal map is keyed by photograph, so one photograph gets one answer. A photograph
        // that landed on at least one moment is reported as attached and not also as refused: the
        // reader is owed the outcome they can act on, and "it is on the trip" is that outcome.
        foreach (var row in attached)
        {
            refused.Remove(row.DocumentId);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new TrackingPictureResultDto(attached, refused));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DetachAsync(
        Guid tripLogId,
        Guid memberId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var trip = ctx is null
            ? null
            : await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripLogId, ct);
        var refusal = ctx is null
            ? ApiProblems.NotFound("trip_log.not_found")
            : await TripTrackingEndpoints.WriteGuardAsync(access, ctx, trip, ct);
        if (refusal is not null) return refusal;

        var pictures = new TripMomentPictures(db);
        if (!await pictures.DetachAsync(tripLogId, memberId, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>The file every named document currently serves, in one query.</summary>
    private static async Task<Dictionary<Guid, StoredFile>> CurrentFilesAsync(
        SilexGisDbContext db, IReadOnlyList<Guid> documentIds, CancellationToken ct) =>
        (await (
                from version in db.DocumentVersions.AsNoTracking()
                join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                where documentIds.Contains(version.DocumentId) && version.IsCurrent
                orderby file.CreatedAt, file.Id
                select new { version.DocumentId, File = file })
            .ToListAsync(ct))
        .GroupBy(x => x.DocumentId)
        .ToDictionary(g => g.Key, g => g.First().File);
}
