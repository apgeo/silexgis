// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// Uploaded geodata files — GPX tracks, KML, GeoJSON, shapefiles — as a filterable world.
/// </summary>
/// <remarks>
/// The geofile list applies the visibility walk and nothing more, so this world is the walk: a
/// geofile is not tied to a cave, and its list shows every reader of the file its footprint, which
/// is therefore what a position condition is decided on.
/// </remarks>
public sealed class GeofileFilterWorld(SilexGisDbContext db, FeatureFilterWorld anchors) : ProtectedFilterWorld<Geofile>
{
    public const string Key = GeofileFilterFields.World;

    public override string World => Key;

    protected override AccessDomain Domain => AccessDomain.Geofiles;

    protected override IQueryable<Geofile> Rows => db.Geofiles.AsNoTracking();

    protected override Expression<Func<Geofile, string?>> NameOf => g => g.Name;

    protected override Expression<Func<Geofile, string?>> OwnerName =>
        g => db.Users.Where(u => u.Id == g.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault();

    protected override string Symbol => Key;

    protected override string? SubtitleOf(Geofile row) => row.Description;

    protected override IReadOnlyList<FieldDescriptor> OwnFields => GeofileFilterFields.Own;

    protected override async ValueTask<IReadOnlyDictionary<ConditionNode, Guid[]>> PositionsAsync(
        AccessContext caller, FilterNode? where, CancellationToken ct) =>
        await PositionConditions.ResolveAsync(
            anchors,
            caller,
            where,
            async ask => await PositionMembershipSql.FootprintsAsync(db, FootprintTable.Geofiles, ask, ct),
            ct);

    protected override Expression<Func<Geofile, bool>>? OwnLeaf(ConditionNode condition) =>
        condition.Field switch
        {
            GeofileFilterFields.Format => FilterLeaves.EnumField<Geofile, GeofileFormat>(condition, g => g.Format),
            _ => null,
        };
}
