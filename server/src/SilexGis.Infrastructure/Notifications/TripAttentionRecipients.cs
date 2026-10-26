// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Who is told that a phone recording needs somebody to look at it — the one place that decides,
/// so that widening the list is one change.
/// </summary>
/// <remarks>
/// <para>
/// The people who look after what phones send, and the account whose upload brought the recording.
/// The first are the accounts that may decide about uploads (the upload-review right's Write), full
/// administrators among them: the people a club has put in charge of what phones bring in.
/// </para>
/// <para>
/// Whoever is added must still be weighed against the thing the message is about: every caller
/// passes the list through the rule that keeps a message from reaching somebody who may not read
/// its subject, so a widened list can only ever narrow on the way out.
/// </para>
/// </remarks>
public static class TripAttentionRecipients
{
    /// <summary>The candidates to be told about a recording uploaded by <paramref name="uploaderUserId"/>.</summary>
    public static async Task<IReadOnlyList<Guid>> CandidatesAsync(
        SilexGisDbContext db, Guid uploaderUserId, CancellationToken ct = default)
    {
        var reviewers = await UploadReviewers.WriteHolderIdsAsync(db, ct);
        return [.. reviewers.Append(uploaderUserId).Distinct()];
    }

    /// <summary>The candidates who may read <paramref name="subject"/>, the trip or cave the message names.</summary>
    public static async Task<IReadOnlyList<Guid>> WhoMayBeToldAsync(
        SilexGisDbContext db,
        IAccessService access,
        IProtectedEntity subject,
        Guid uploaderUserId,
        CancellationToken ct = default)
    {
        var candidates = await CandidatesAsync(db, uploaderUserId, ct);
        return await NotificationRecipients.WhoMayReadAsync(db, access, subject, candidates, excluding: null, ct);
    }
}
