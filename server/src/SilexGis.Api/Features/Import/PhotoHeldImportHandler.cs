// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// A drop of photographs confirmed by an account under review, written on acceptance by the same
/// commit that files a drop at once.
/// </summary>
internal sealed class PhotoHeldImportHandler(
    SilexGisDbContext db,
    FeatureProtection protection,
    IAccessService access,
    PhotoCommitService commits) : HeldFileImportHandler(db, protection)
{
    /// <summary>The section a held drop's lines are filed under.</summary>
    public const string Section = "photos";

    /// <summary>None of the held drop's pictures can be read by the uploader any more.</summary>
    public const string FilesGoneCode = "photo_import.no_files";

    public override ImportSource Source => ImportSource.Photos;

    protected override async Task<HeldCommitFailure?> CommitAsync(
        ImportBatch batch, IReadOnlySet<string> accepted, HeldCommit into, AccessContext uploader, CancellationToken ct)
    {
        var request = batch.HeldRequest is null ? null : ImportJson.Deserialize<PhotoCommitRequest>(batch.HeldRequest);
        if (request is null)
        {
            return new HeldCommitFailure(UnreadableCode, "The held confirmation cannot be read.");
        }

        // Everything the confirmation route asks before it files a drop is asked again, of the
        // uploader as they stand now.
        if (!CreateRules.MayCreate(uploader, AccessDomain.Features, request.Options.CavingGroupId))
        {
            return new HeldCommitFailure(CreateRules.ForbiddenCode, "The account may no longer create objects here.");
        }

        if (request.Options.CavingGroupId is { } groupId && !CavingGroupBindingRules.MayBind(uploader, AccessDomain.Features, groupId))
        {
            return new HeldCommitFailure(CavingGroupBindingRules.ForbiddenCode, "The account may no longer file objects under this group.");
        }

        if (request.Options.TripLogId is { } tripId)
        {
            var trip = await Db.TripLogs.FirstOrDefaultAsync(t => t.Id == tripId, ct);
            if (trip is null || !(await access.DecideAsync(uploader, AccessAction.Write, trip, ct)).Allowed)
            {
                return new HeldCommitFailure("trip_log.not_found", "The account may no longer file pictures under this trip.");
            }
        }

        var (files, _) = await PhotoImportEndpoints.ReadableFilesAsync(Db, access, uploader, request.FileIds, ct);
        if (files.Count == 0)
        {
            return new HeldCommitFailure(FilesGoneCode, "None of the pictures of this import can be read any more.");
        }

        var decisions = PhotoImportEndpoints.ReadDecisions(request.Decisions);
        var replayed = request.Selection.Distinct().ToDictionary(
            key => key,
            key => accepted.Contains(PhotoCommitService.KeyOf(key))
                ? decisions.GetValueOrDefault(key) ?? new PhotoDecision()
                : new PhotoDecision { Action = ImportDecisionAction.Skip });
        try
        {
            await commits.CommitAsync(files, request.Options, request.Selection, replayed, uploader, ct, into);
        }
        catch (ImportCommitException e)
        {
            return new HeldCommitFailure(e.Code, e.Message);
        }

        await PhotoImportEndpoints.ClearSpentReviewAsync(Db, batch.ConfirmedByUserId, request.FileIds, ct);
        return null;
    }

    /// <summary>The candidates a held drop waits with: every selected group not set aside.</summary>
    public static IReadOnlyList<HeldCandidate> CandidatesOf(
        IReadOnlyList<Guid> selection, IReadOnlyDictionary<Guid, PhotoDecision> decisions, bool locationProtected) =>
        [.. selection.Distinct()
            .Select(key => (Key: key, Decision: decisions.GetValueOrDefault(key) ?? new PhotoDecision()))
            .Where(c => c.Decision.Action != ImportDecisionAction.Skip)
            .Select(c => new HeldCandidate(
                PhotoCommitService.KeyOf(c.Key),
                c.Decision.Name,
                c.Decision,
                c.Decision.AttachToFeatureId is { } target ? [target] : [],
                locationProtected))];
}
