// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// Georeferenced raster maps, as a filterable world.
/// </summary>
/// <remarks>
/// A map linked to a cave is drawn over the cave's ground, so its footprint is the cave's location
/// by another name. The map list leaves such a map out entirely for a reader who may not place that
/// cave exactly — not listed without its footprint, not counted — and this world leaves it out the
/// same way, before the filter, the count and the page, so the filter is never a second way to ask
/// the list's question with a more generous answer.
/// </remarks>
public sealed class GeoreferencedMapFilterWorld(
    SilexGisDbContext db, FeatureProtection protection, FeatureFilterWorld anchors)
    : ProtectedFilterWorld<GeoreferencedMap>
{
    public const string Key = GeoreferencedMapFilterFields.World;

    public override string World => Key;

    protected override AccessDomain Domain => AccessDomain.GeoreferencedMaps;

    protected override IQueryable<GeoreferencedMap> Rows => db.GeoreferencedMaps.AsNoTracking();

    protected override Expression<Func<GeoreferencedMap, string?>> NameOf => m => m.Name;

    protected override Expression<Func<GeoreferencedMap, string?>> OwnerName =>
        m => db.Users.Where(u => u.Id == m.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault();

    protected override string Symbol => Key;

    protected override string? SubtitleOf(GeoreferencedMap row) => row.Description;

    protected override IReadOnlyList<FieldDescriptor> OwnFields => GeoreferencedMapFilterFields.Own;

    protected override Expression<Func<GeoreferencedMap, bool>>? OwnLeaf(ConditionNode condition) =>
        condition.Field switch
        {
            GeoreferencedMapFilterFields.MapKind =>
                FilterLeaves.EnumField<GeoreferencedMap, MapKind>(condition, m => m.MapKind),
            _ => null,
        };

    // Every map this world returns shows its footprint to its reader whole, so the footprint itself
    // is what a position condition is decided on.
    protected override async ValueTask<IReadOnlyDictionary<ConditionNode, Guid[]>> PositionsAsync(
        AccessContext caller, FilterNode? where, CancellationToken ct) =>
        await PositionConditions.ResolveAsync(
            anchors,
            caller,
            where,
            async ask => await PositionMembershipSql.FootprintsAsync(db, FootprintTable.GeoreferencedMaps, ask, ct),
            ct);

    // The map list's own rule, from its one home, so the filter is never a second way to ask the
    // list's question with a more generous answer.
    protected override async ValueTask<IQueryable<GeoreferencedMap>> VisibleAsync(
        AccessContext caller, CancellationToken ct) =>
        await GeoreferencedMapReads.ShownAsync(db, protection, caller, ct);
}
