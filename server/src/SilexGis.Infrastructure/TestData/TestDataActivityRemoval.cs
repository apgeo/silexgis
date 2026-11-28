// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// The part of a run's removal that is what its people did: resource links (the trips' links to
/// caves among them), trips with everything that hangs on them, camps and events. They go first,
/// so a run's own trip never counts as somebody's use of a generated cave or account. They are
/// removed through the change tracker, so the access rules and history that name them follow.
/// </summary>
public static class TestDataActivityRemoval
{
    public static async Task RemoveAsync(SilexGisDbContext db, ILookup<string, string> listed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(listed);
        var links = Guids(listed["res_links"]);
        foreach (var chunk in links.Chunk(500))
        {
            db.ResLinks.RemoveRange(await db.ResLinks.IgnoreQueryFilters().Where(l => chunk.Contains(l.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }

        var trips = Guids(listed["trip_logs"]);
        foreach (var chunk in trips.Chunk(500))
        {
            db.TripLogs.RemoveRange(await db.TripLogs.IgnoreQueryFilters().Where(t => chunk.Contains(t.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }

        var expeditions = Guids(listed["expeditions"]);
        db.Expeditions.RemoveRange(await db.Expeditions.IgnoreQueryFilters().Where(e => expeditions.Contains(e.Id)).ToListAsync(ct));
        var events = Guids(listed["events"]);
        foreach (var chunk in events.Chunk(500))
        {
            db.Events.RemoveRange(await db.Events.IgnoreQueryFilters().Where(e => chunk.Contains(e.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }

        var views = Guids(listed["map_views"]);
        db.MapViews.RemoveRange(await db.MapViews.Where(v => views.Contains(v.Id)).ToListAsync(ct));
        var filters = Guids(listed["saved_filters"]);
        db.SavedFilters.RemoveRange(await db.SavedFilters.Where(f => filters.Contains(f.Id)).ToListAsync(ct));
        var templates = Guids(listed["report_templates"]);
        db.ReportTemplates.RemoveRange(await db.ReportTemplates.Where(t => templates.Contains(t.Id)).ToListAsync(ct));
        var models = Guids(listed["cave_potential_models"]);
        db.CavePotentialModels.RemoveRange(await db.CavePotentialModels.Where(m => models.Contains(m.Id)).ToListAsync(ct));
        var announcements = Guids(listed["caving_group_announcements"]);
        db.CavingGroupAnnouncements.RemoveRange(await db.CavingGroupAnnouncements.Where(a => announcements.Contains(a.Id)).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    private static List<Guid> Guids(IEnumerable<string> ids) => [.. ids.Select(id => Guid.Parse(id, CultureInfo.InvariantCulture))];
}
