// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// Carries out what an opt-out link asks for, once the endpoint has read and trusted the link.
/// </summary>
/// <remarks>
/// Here rather than in the endpoint because it has to know which account the link belongs to and
/// write that account's settings, and a feature slice that reaches into a person's own rows is
/// exactly what the layering forbids: what may be read or changed of a person is settled in one
/// layer. The same reason the sender resolves the address, the language and the preferences here
/// and lets producers name nothing but an id.
/// </remarks>
public sealed class NotificationOptOut(SilexGisDbContext db)
{
    /// <summary>
    /// Switches one category's mail off, and answers whether there was an account to switch it off
    /// for. Nothing else moves: somebody who clicked a link in a mail client has said where they do
    /// not want to be reached, and has said nothing at all about the inbox inside the application.
    /// </summary>
    public Task<bool> StopCategoryEmailAsync(
        Guid userId, NotificationCategory category, CancellationToken ct) =>
        StopEmailAsync(userId, [category], ct);

    /// <summary>
    /// Switches mail off for every category whose mail may be switched off. Categories nobody may
    /// mute are left alone — an alert about the account's own credentials ignores this by design,
    /// because whoever is taking an account over may be holding a live session while they do it.
    /// </summary>
    public Task<bool> StopAllEmailAsync(Guid userId, CancellationToken ct) =>
        StopEmailAsync(
            userId,
            [.. NotificationCategories.All.Where(NotificationCategories.IsUserConfigurable)],
            ct);

    private async Task<bool> StopEmailAsync(
        Guid userId, IReadOnlyList<NotificationCategory> categories, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return false;
        }

        // Read, then write, then read again if somebody else wrote first. Two writers of one cell
        // is an ordinary event here and not a rare one: this link needs no session, so the person
        // holding the phone and the link scanner their mail provider runs can open it within the
        // same second, and the settings page writes the same rows. The unique key over the cell is
        // what settles the race; what it must not do is answer somebody's opt-out with a failure
        // that reads to them as a link that no longer works. One retry is enough — the second pass
        // finds the winner's row and updates it, and there is nothing further to lose to.
        for (var attempt = 0; ; attempt++)
        {
            var rows = await db.UserNotificationPreferences
                .Where(p => p.UserId == userId &&
                            p.Channel == NotificationChannelKind.Email &&
                            categories.Contains(p.Category))
                .ToDictionaryAsync(p => p.Category, ct);

            foreach (var category in categories)
            {
                // Reconciled through the change tracker rather than by a bulk update, so the
                // timestamps these rows carry are maintained by the same interceptor that
                // maintains every other row's.
                if (rows.TryGetValue(category, out var row))
                {
                    row.Choice = NotificationChannelChoice.Off;
                }
                else
                {
                    db.UserNotificationPreferences.Add(new UserNotificationPreference
                    {
                        UserId = userId,
                        Category = category,
                        Channel = NotificationChannelKind.Email,
                        Choice = NotificationChannelChoice.Off,
                    });
                }
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // The rows this attempt tried to insert are the ones that lost. Detaching them is
                // what lets the next read see the winner's rows instead of re-proposing our own.
                foreach (var entry in db.ChangeTracker
                             .Entries<UserNotificationPreference>()
                             .Where(e => e.State == EntityState.Added)
                             .ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
    }
}
