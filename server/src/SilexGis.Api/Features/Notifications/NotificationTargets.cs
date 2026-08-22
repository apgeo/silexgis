// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Notifications;

/// <summary>One thing a notification is about.</summary>
public readonly record struct NotificationTarget(NotificationTargetKind Kind, Guid Id);

/// <summary>
/// What a notification is about, decided again at the moment somebody reads it.
/// </summary>
/// <remarks>
/// <para>
/// A producer freezes a rendered name and a path into the notification when it queues it, and by
/// the time the recipient opens their inbox they may have lost the right to open the thing it
/// names — a grant withdrawn, a caving group left, an object made private. Nothing about the
/// stored row moves when that happens, so a list that simply printed what was frozen would hand
/// out a name and a working link to something the reader may no longer see, outside every filter
/// the ordinary read paths apply.
/// </para>
/// <para>
/// The same rule already runs once, in the pass that decides who is told about a protected row at
/// all: nobody is told about something they could not open, decided from their own access as it
/// stands rather than assumed from their being named on the row. This is that rule applied a
/// second time, at the only other moment it can go stale.
/// </para>
/// <para>
/// A row whose target is withheld is still listed, with its category and its date. Dropping it
/// would be its own kind of leak — a reader could tell that something had happened and been taken
/// away by the gap it left — and it would also hide from somebody that they had lost access at
/// all.
/// </para>
/// </remarks>
public static class NotificationTargets
{
    /// <summary>
    /// Where in this application a reader is sent to see the thing a notification is about, or
    /// null when there is no page for it.
    /// </summary>
    /// <remarks>
    /// Derived from what the notification is about rather than taken from the path the producer
    /// stored, so the link cannot outlive the check: the only reference a reader's access was
    /// tested against is the one the link is built from. Two kinds have no page of their own and
    /// resolve to the list they are read from, which is where somebody following the link would
    /// have to go anyway.
    /// </remarks>
    public static string? RouteTo(NotificationTarget target) => target.Kind switch
    {
        NotificationTargetKind.Feature => $"/features/{target.Id}",
        NotificationTargetKind.TripLog => $"/trip-logs/{target.Id}",
        NotificationTargetKind.Expedition => $"/expeditions/{target.Id}",
        NotificationTargetKind.CavingGroup => "/caving-groups",
        NotificationTargetKind.Geofile or NotificationTargetKind.GeoreferencedMap => "/geodata",
        NotificationTargetKind.MapView => "/map",
        _ => null,
    };

    /// <summary>
    /// Which of these targets the reader may open right now. A target that has since been deleted
    /// is absent for the same reason an unreadable one is: neither can be shown, and the list
    /// never tells the two apart, so it can never be used to find out whether something exists.
    /// </summary>
    public static async Task<HashSet<NotificationTarget>> ReadableAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyCollection<NotificationTarget> targets,
        CancellationToken ct)
    {
        var readable = new HashSet<NotificationTarget>();
        if (targets.Count == 0)
        {
            return readable;
        }

        var protectedRows = new List<(NotificationTargetKind Kind, IProtectedEntity Row)>();

        foreach (var group in targets.GroupBy(target => target.Kind))
        {
            var ids = group.Select(target => target.Id).Distinct().ToList();

            // Caving groups are not protected rows: they carry no owner and no audience, and who
            // may read one is held over the domain and narrowed to the group itself. So the
            // question is asked of the pure rule directly, with the group's own id as the target
            // — which is exactly what the caving-group read endpoint does.
            if (group.Key == NotificationTargetKind.CavingGroup)
            {
                var live = await db.CavingGroups.AsNoTracking()
                    .Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
                foreach (var id in live.Where(id => MayReadCavingGroup(ctx, id)))
                {
                    readable.Add(new NotificationTarget(group.Key, id));
                }

                continue;
            }

            // One query per kind rather than a visibility-filtered listing, because there is no
            // single listing to filter: a page can name several worlds at once.
            protectedRows.AddRange(
                (await LoadAsync(db, group.Key, ids, ct)).Select(row => (group.Key, row)));
        }

        if (protectedRows.Count == 0)
        {
            return readable;
        }

        if (ctx.IsFullAdmin)
        {
            foreach (var (kind, row) in protectedRows)
            {
                readable.Add(new NotificationTarget(kind, row.Id));
            }

            return readable;
        }

        // The facts of a whole kind in one go rather than a decision at a time. Deciding one row at
        // a time costs two extra queries per feature and one per document, and the page size is the
        // caller's to choose — so a full page of notifications about distinct records would be a
        // thousand round trips in one request. The answers are identical either way; only the
        // number of round trips changes, and this way it stays bounded by the number of kinds a
        // page names rather than by how many rows it lists. Per kind rather than all at once
        // because the facts come back keyed by row id alone, and ids are only unique within a
        // table.
        foreach (var byKind in protectedRows.GroupBy(r => r.Kind))
        {
            var rows = byKind.Select(r => r.Row).ToList();
            var facts = await access.FactsOfManyAsync(rows, ct);
            foreach (var row in rows.Where(row => AccessEvaluator.Decide(
                ctx, AccessDomains.Of(row), AccessAction.Read, facts[row.Id]).Allowed))
            {
                readable.Add(new NotificationTarget(byKind.Key, row.Id));
            }
        }

        return readable;
    }

    private static bool MayReadCavingGroup(AccessContext ctx, Guid id) =>
        AccessEvaluator.Decide(
            ctx,
            AccessDomain.CavingGroups,
            AccessAction.Read,
            new AccessTargetFacts { ObjectId = id }).Allowed;

    /// <summary>
    /// The rows one kind names. A kind with no arm here loads nothing and is therefore never
    /// readable, which is the safe direction for a value the vocabulary gains later and this has
    /// not been taught yet.
    /// </summary>
    private static async Task<List<IProtectedEntity>> LoadAsync(
        SilexGisDbContext db, NotificationTargetKind kind, List<Guid> ids, CancellationToken ct) =>
        kind switch
        {
            NotificationTargetKind.Feature =>
                [.. await db.Features.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct)],
            NotificationTargetKind.TripLog =>
                [.. await db.TripLogs.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct)],
            NotificationTargetKind.Geofile =>
                [.. await db.Geofiles.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct)],
            NotificationTargetKind.GeoreferencedMap =>
                [.. await db.GeoreferencedMaps.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct)],
            NotificationTargetKind.MapView =>
                [.. await db.MapViews.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct)],
            NotificationTargetKind.Expedition =>
                [.. await db.Expeditions.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct)],
            _ => [],
        };
}
