// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Notifications;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// The outcome of one hand-driven retry, with what the caller needs to record it: which
/// notification it was, and when the next attempt will be.
/// </summary>
public sealed record NotificationRetryResult(
    NotificationRetryOutcome Outcome,
    long? NotificationId,
    string? TemplateKey,
    DateTimeOffset? DueAt)
{
    /// <summary>Whether the delivery was actually changed, which is what is worth recording.</summary>
    public bool Changed =>
        Outcome is NotificationRetryOutcome.Queued or NotificationRetryOutcome.Suppressed;

    internal static NotificationRetryResult Refused(NotificationRetryOutcome outcome) =>
        new(outcome, null, null, null);
}
