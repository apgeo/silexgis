// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// Saved map views, as a filterable world.
/// </summary>
/// <remarks>
/// Worth having because "open the view somebody made for the karst survey" is a thing people say,
/// and until now the only way to find one was to scroll the list of every view you can see.
/// </remarks>
public sealed class MapViewFilterWorld(SilexGisDbContext db) : ProtectedFilterWorld<MapView>
{
    public const string Key = "mapView";

    public override string World => Key;

    protected override AccessDomain Domain => AccessDomain.MapViews;

    protected override IQueryable<MapView> Rows => db.MapViews.AsNoTracking();

    protected override Expression<Func<MapView, string?>> NameOf => v => v.Name;

    protected override Expression<Func<MapView, string?>> OwnerName =>
        v => db.Users.Where(u => u.Id == v.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault();

    protected override string Symbol => "mapView";

    protected override string? SubtitleOf(MapView row) => row.Description;
}

/// <summary>
/// Saved filters, as a filterable world, so the object selector can offer them beside the things
/// they find.
/// </summary>
/// <remarks>
/// <para>
/// Visible is exactly what the saved-filter list shows the caller: the ordinary walk over the
/// domain saved filters are read under, which is the map views' one. A filter grants nothing, so
/// offering one here discloses only its name and wording, which its list already shows.
/// </para>
/// <para>
/// The world reads the table directly rather than through the saved-filter reader: that reader
/// needs the registry of worlds to decide whether a document can still run, and the registry is
/// built from the worlds, so this world asking for it would make the container recurse. Whether a
/// picked filter can run is the applying surface's question, and it answers it with the same
/// refusal it gives a link.
/// </para>
/// </remarks>
public sealed class SavedFilterFilterWorld(SilexGisDbContext db) : ProtectedFilterWorld<SavedFilter>
{
    public const string Key = "savedFilter";

    public override string World => Key;

    protected override AccessDomain Domain => AccessDomain.MapViews;

    protected override IQueryable<SavedFilter> Rows => db.SavedFilters.AsNoTracking();

    protected override Expression<Func<SavedFilter, string?>> NameOf => f => f.Name;

    protected override Expression<Func<SavedFilter, string?>> OwnerName =>
        f => db.Users.Where(u => u.Id == f.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault();

    protected override string Symbol => "savedFilter";

    protected override string? SubtitleOf(SavedFilter row) => row.Description;
}
