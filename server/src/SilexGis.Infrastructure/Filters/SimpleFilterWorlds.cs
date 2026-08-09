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
