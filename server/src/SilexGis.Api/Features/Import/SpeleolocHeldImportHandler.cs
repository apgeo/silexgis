// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// A SpeleoLoc confirmation of an account under review, as it waits: the archive it was made
/// against, or the kept recording, and the request with the saved review's decisions folded in.
/// Exactly one of <paramref name="FileId"/> and <paramref name="RecordingId"/> is set.
/// </summary>
internal sealed record HeldSpeleolocImportRequest(
    Guid? FileId,
    Guid? RecordingId,
    SpeleolocImportOptions Options,
    IReadOnlyList<string> PointIds,
    IReadOnlyDictionary<string, SpeleolocPointDecision> Decisions);

/// <summary>
/// A recording confirmed by an account under review — read out of an archive or kept from a
/// phone — recorded on acceptance by the same commit that records one at once.
/// </summary>
/// <remarks>
/// Its candidates are the trip it would create, when it creates one, and each scan it would
/// record, and every scan depends on that trip: accepting a scan accepts the trip, and rejecting
/// the trip rejects every scan. The recording becomes the report on acceptance, so whether it
/// still may — it has no report yet, the account may still change it — is asked then, not when
/// the confirmation began to wait.
/// </remarks>
internal sealed class SpeleolocHeldImportHandler(
    SilexGisDbContext db,
    FeatureProtection protection,
    IAccessService access,
    SpeleolocTripImportCommitService commits) : HeldFileImportHandler(db, protection)
{
    private readonly FeatureProtection protection = protection;

    /// <summary>The section a held archive's lines are filed under.</summary>
    public const string ArchiveSection = "speleolocArchive";

    /// <summary>The section a held kept recording's lines are filed under.</summary>
    public const string RecordingSection = "keptRecording";

    public override ImportSource Source => ImportSource.SpeleolocArchive;

    protected override async Task<HeldCommitFailure?> CommitAsync(
        ImportBatch batch, IReadOnlySet<string> accepted, HeldCommit into, AccessContext uploader, CancellationToken ct)
    {
        var request = batch.HeldRequest is null ? null : ImportJson.Deserialize<HeldSpeleolocImportRequest>(batch.HeldRequest);
        if (request is null)
        {
            return new HeldCommitFailure(UnreadableCode, "The held confirmation cannot be read.");
        }

        // A confirmation accepted a part at a time goes on onto the trip its first part created,
        // rather than creating a second one.
        var options = request.Options;
        var written = await Db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == batch.Id && i.ReviewDecision == ImportReviewDecision.Accepted && i.TripLogId != null)
            .Select(i => i.TripLogId)
            .FirstOrDefaultAsync(ct);
        if (written is { } tripLogId)
        {
            options = options with { TripLogId = tripLogId, CreateTrip = false };
            into.ContinuesTripLogId = tripLogId;
        }

        // Everything the confirmation route asks before it records is asked again, of the
        // uploader as they stand now.
        if (SpeleolocImportCreateRights.Refusal(uploader, options) is { } refusal)
        {
            return new HeldCommitFailure(refusal.Code, refusal.Message);
        }

        StoredFile? file = null;
        TripRecording? recording = null;
        List<TripRecordingPoint> points = [];
        ProblemHttpResult? problem;
        if (request.RecordingId is { } recordingId)
        {
            (recording, points, problem) = await SpeleolocImportEndpoints.LoadKeptRecordingAsync(recordingId, Db, access, uploader, ct);
        }
        else
        {
            (file, problem) = await SpeleolocImportEndpoints.LoadReadableAsync(request.FileId!.Value, Db, access, uploader, ct);
        }

        if (problem is not null)
        {
            return new HeldCommitFailure(
                request.RecordingId is not null ? CodeOf(problem) : VectorHeldImportHandler.FileGoneCode,
                "What this import was made from is no longer here.");
        }

        var (target, problemOfTarget) = await SpeleolocImportEndpoints.CommitTargetAsync(options, Db, access, protection, uploader, ct);
        if (problemOfTarget is not null)
        {
            return new HeldCommitFailure(CodeOf(problemOfTarget), problemOfTarget.ProblemDetails.Detail ?? "The trip or the model is out of reach.");
        }

        var decisions = SpeleolocImportEndpoints.ReadDecisions(ImportJson.Serialize(request.Decisions));
        var replayed = request.PointIds
            .Select(SpeleolocTripImportCommitService.KeyOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                id => id,
                id => accepted.Contains(id)
                    ? decisions.GetValueOrDefault(id) ?? new SpeleolocPointDecision()
                    : new SpeleolocPointDecision { Action = SpeleolocPointAction.Skip },
                StringComparer.OrdinalIgnoreCase);
        try
        {
            if (recording is not null)
            {
                await commits.CommitRecordingAsync(
                    recording, points, options, target.Model, target.Cave, target.Trip, target.Tracking,
                    request.PointIds, replayed, uploader, ct, into);
            }
            else
            {
                await commits.CommitAsync(
                    file!, options, target.Model, target.Cave, target.Trip, target.Tracking,
                    request.PointIds, replayed, uploader, ct, into);
            }
        }
        catch (SpeleolocArchiveException e)
        {
            return new HeldCommitFailure(e.Code, e.Message);
        }
        catch (SpeleolocImportCommitException e)
        {
            return new HeldCommitFailure(e.Code, e.Message);
        }

        return null;
    }

    private static string CodeOf(ProblemHttpResult problem) =>
        problem.ProblemDetails.Extensions.TryGetValue("code", out var code) && code is string text ? text : "review.write_refused";

    /// <summary>
    /// The candidates a held recording waits with: the trip it would create, and every chosen
    /// scan not set aside. A scan's line names the cave whose model it is placed on, because the
    /// station a decision gives is a position in that cave: it is shown only to a reviewer who may
    /// place the cave exactly.
    /// </summary>
    public static IReadOnlyList<HeldCandidate> CandidatesOf(
        SpeleolocImportOptions options,
        IReadOnlyList<string> pointIds,
        IReadOnlyDictionary<string, SpeleolocPointDecision> decisions,
        Guid caveId,
        string? tripTitle,
        IReadOnlyDictionary<string, DateTimeOffset> scannedAt)
    {
        var candidates = new List<HeldCandidate>();
        IReadOnlyList<string>? dependsOn = null;
        if (options.TripLogId is null && options.CreateTrip)
        {
            candidates.Add(new HeldCandidate(
                SpeleolocTripImportCommitService.TripKey,
                tripTitle,
                new { createTrip = true, cavingGroupId = options.CavingGroupId, visibility = options.Visibility },
                [],
                false));
            dependsOn = [SpeleolocTripImportCommitService.TripKey];
        }

        candidates.AddRange(pointIds
            .Select(SpeleolocTripImportCommitService.KeyOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => (Id: id, Decision: decisions.GetValueOrDefault(id) ?? new SpeleolocPointDecision()))
            .Where(c => c.Decision.Action != SpeleolocPointAction.Skip)
            .Select(c => new HeldCandidate(
                c.Id,
                scannedAt.TryGetValue(c.Id, out var at) ? at.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : null,
                c.Decision,
                [caveId],
                false,
                dependsOn)));
        return candidates;
    }
}
