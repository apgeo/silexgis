// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Carries out what an opt-out link asks for, once the endpoint has read and trusted the link.
/// </summary>
/// <remarks>
/// Here rather than in the endpoint because stopping this recipient's notification mail means
/// writing their own account row, and a feature slice that reaches for a user row is exactly what
/// the layering forbids: what may be read or changed of a person is settled in one layer. The same
/// reason the sender resolves the address, the language and the preferences here and lets producers
/// name nothing but an id.
/// </remarks>
public sealed class NotificationOptOut(SilexGisDbContext db)
{
    /// <summary>
    /// Switches off the notification email for one account, and answers whether there was an
    /// account to switch it off for. Alerts about the account's own credentials are unaffected:
    /// they ignore this switch by design, because whoever is taking an account over may be holding
    /// a live session while they do it.
    /// </summary>
    public async Task<bool> StopNotificationEmailAsync(Guid userId, CancellationToken ct) =>
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(rows => rows.SetProperty(u => u.NotifyEmailEnabled, false), ct) > 0;
}
