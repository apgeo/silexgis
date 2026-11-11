// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Which notice to hand out.</summary>
public sealed record CavingGroupAnnouncementPayload(Guid AnnouncementId);

/// <summary>
/// Writes one notification per member for a notice sent to a large caving group.
/// </summary>
/// <remarks>
/// <para>
/// The same work the request does for a small roster, moved off the request. What it does not do
/// is decide anything: who may send was answered before the notice was recorded, and the wording,
/// the group's name and the sender's name were all frozen then, so nothing here can disagree with
/// what the sender was told would happen.
/// </para>
/// <para>
/// Everything lands in a single save — the notifications and the stamp saying they were written,
/// together. This queue re-runs an interrupted pass from the beginning, so anything that saved
/// halfway would tell half a club twice; one save makes it happen once or not at all.
/// </para>
/// </remarks>
public sealed class CavingGroupAnnouncementHandler(
    SilexGisDbContext db,
    ILogger<CavingGroupAnnouncementHandler> logger) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.CavingGroupAnnouncement;

    /// <summary>
    /// Safe to run twice for the reason the single save gives: an attempt that failed wrote
    /// nothing, and one that got as far as saving stamped the announcement expanded, which the
    /// next attempt reads and stops at. Not retrying lost a large club's announcement for good to
    /// a dropped connection.
    /// </summary>
    public bool RetriesPassingFailures => true;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CavingGroupAnnouncementPayload>(
            job.Payload, JsonSerializerOptions.Web);
        if (payload is null || payload.AnnouncementId == Guid.Empty)
        {
            throw new InvalidOperationException("A caving group announcement job names no announcement.");
        }

        var announcement = await db.CavingGroupAnnouncements
            .FirstOrDefaultAsync(a => a.Id == payload.AnnouncementId, ct);
        if (announcement is null)
        {
            // The group was deleted, taking its pending notices with it. Nobody is waiting to be
            // told about a club that no longer exists, so this is done rather than failed.
            logger.LogInformation(
                "Announcement {AnnouncementId} is gone; nothing to hand out.", payload.AnnouncementId);
            return;
        }

        if (announcement.ExpandedAt is not null)
        {
            return;
        }

        try
        {
            await ExpandAsync(announcement, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && !JobRetry.WillBeRetried(job, e, ct))
        {
            // The last word on it: nothing will hand it out now, so it stops being headroom the
            // day has promised. Written straight to the row — the notifications this attempt
            // added are still on the context and must not be saved with it.
            await GiveUpAsync(announcement.Id, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// The queue gave the job up after its runner was lost on every attempt it was allowed: the
    /// notice will not be handed out, and stops counting against the day's paid ceiling.
    /// </summary>
    public async Task AbandonedAsync(ProcessingJob job, CancellationToken ct)
    {
        if (AbandonedJob.PayloadOf<CavingGroupAnnouncementPayload>(job) is { } payload)
        {
            await MarkGivenUpAsync(payload.AnnouncementId, ct);
        }
    }

    private async Task ExpandAsync(CavingGroupAnnouncement announcement, CancellationToken ct)
    {
        var recipients = await db.AnnouncementRecipientsAsync(
            announcement.CavingGroupId, announcement.SenderUserId, ct);

        var placeholders = new Dictionary<string, string>
        {
            ["actorName"] = announcement.SenderName,
            ["cavingGroupName"] = announcement.CavingGroupName,
            ["announcement"] = announcement.Message,
            ["url"] = NotificationLinks.Inbox,
        };

        foreach (var recipient in recipients)
        {
            NotificationQueue.Enqueue(
                db,
                recipient,
                NotificationCategory.GroupAnnouncement,
                MessageTemplateCatalog.NotifyGroupAnnouncement,
                placeholders,
                NotificationTargetKind.CavingGroup,
                announcement.CavingGroupId,

                // Carried over rather than weighed again, so that what the day has promised does
                // not change as the notice turns into notifications: the sender was measured
                // against this figure, and a second announcement made in between is refused
                // against the same one.
                announcement.SegmentsPerCopy);
        }

        announcement.ExpandedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task GiveUpAsync(Guid announcementId, CancellationToken ct)
    {
        try
        {
            await MarkGivenUpAsync(announcementId, ct);
        }
        catch (Exception e)
        {
            // The database that ended the attempt may be refusing this too. The job still fails,
            // and the notice goes on counting until the day is over — the outcome before this
            // existed, never a worse one.
            logger.LogError(e, "Announcement {AnnouncementId} could not be marked given up", announcementId);
        }
    }

    private Task MarkGivenUpAsync(Guid announcementId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return db.CavingGroupAnnouncements
            .Where(a => a.Id == announcementId && a.ExpandedAt == null && a.FailedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FailedAt, now), ct);
    }
}
