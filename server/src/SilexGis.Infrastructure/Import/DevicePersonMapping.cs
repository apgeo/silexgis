// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Import;

/// <summary>
/// Reading and writing the phone-user links outside a phone upload — the archive import's prefill
/// and its write-back — through the same rows and the same replacement rule an upload uses.
/// </summary>
public static class DevicePersonMapping
{
    /// <summary>The caver each of <paramref name="deviceUserIds"/> is linked to, for those that are linked.</summary>
    public static async Task<Dictionary<Guid, Guid>> LinksOfAsync(
        SilexGisDbContext db, IReadOnlyCollection<Guid> deviceUserIds, CancellationToken ct)
    {
        if (deviceUserIds.Count == 0)
        {
            return [];
        }

        var ids = deviceUserIds.ToList();
        return await db.DevicePersons.AsNoTracking()
            .Where(p => ids.Contains(p.DeviceUserId) && p.CaverId != null)
            .ToDictionaryAsync(p => p.DeviceUserId, p => p.CaverId!.Value, ct);
    }

    /// <summary>
    /// Records the links somebody chose outside a phone, each where the replacement rule lets it
    /// stand: a phone user nobody has seen is recorded, one with a link of lower authority is
    /// relinked, and one somebody keeping the roster linked is left as it is. Tracked, not saved:
    /// the caller's own save writes them, in its own transaction.
    /// </summary>
    /// <remarks>
    /// A link to a caver who is no longer on the roster is not recorded. The choices come from a
    /// review that may be older than the roster — a caver merged into another or deleted since —
    /// and a link to a missing row would fail the caller's whole save on the foreign key, taking
    /// everything else it was writing down with it, over a choice that could not have stood anyway.
    /// </remarks>
    public static async Task RecordAsync(
        SilexGisDbContext db,
        IReadOnlyDictionary<Guid, Guid> links,
        DevicePersonLinkSource source,
        Guid actingUserId,
        CancellationToken ct)
    {
        if (links.Count == 0)
        {
            return;
        }

        var named = links.Values.Distinct().ToList();
        var onRoster = (await db.Cavers.AsNoTracking()
            .Where(c => named.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct)).ToHashSet();

        var ids = links.Keys.ToList();
        var known = await db.DevicePersons
            .Where(p => ids.Contains(p.DeviceUserId))
            .ToDictionaryAsync(p => p.DeviceUserId, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var (deviceUserId, caverId) in links)
        {
            if (!onRoster.Contains(caverId))
            {
                continue;
            }

            if (!known.TryGetValue(deviceUserId, out var person))
            {
                db.DevicePersons.Add(new DevicePerson
                {
                    DeviceUserId = deviceUserId,
                    CaverId = caverId,
                    LinkSource = source,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    UploadedByUserId = actingUserId,
                });
                continue;
            }

            if (person.CaverId != caverId && DevicePersonLinks.MayReplace(person.CaverId, person.LinkSource, source))
            {
                person.CaverId = caverId;
                person.LinkSource = source;
            }
        }
    }
}
