// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// Caves, entrances, centrelines and everything else on the map, as a filterable world.
/// </summary>
public sealed class FeatureFilterWorld(SilexGisDbContext db, FeatureProtection protection)
    : FilterWorld<Feature>
{
    public const string Key = "feature";

    public override string World => Key;

    /// <summary>
    /// What a feature can be asked about. The same for every caller today, and asynchronous anyway
    /// because it will not stay that way: this installation's own feature types name typed
    /// properties, and which of those a caller may ask about is a per-caller answer.
    /// </summary>
    public override ValueTask<WorldVocabulary> VocabularyAsync(
        AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(FeatureFilterFields.Vocabulary);

    /// <summary>
    /// What this caller may see: the visibility walk, less any protected centreline they may not
    /// place exactly.
    /// </summary>
    /// <remarks>
    /// The second half is not a refinement of the first. A centreline traces a cave's course
    /// underground, so for somebody without exact view the row is withheld altogether — not shown
    /// without its geometry, not counted, not hinted at by a total that moves. The feature list has
    /// always done this; leaving it out here would have made the filter a second way to ask the
    /// same question with a more generous answer, which is the exact failure this whole design is
    /// arranged to prevent.
    /// </remarks>
    protected override async ValueTask<IQueryable<Feature>> VisibleAsync(
        AccessContext caller, CancellationToken ct)
    {
        var visible = db.Features.AsNoTracking().VisibleTo(caller, db.Features, db.FeatureSetMembers);

        var protectedCenterlineIds = await visible
            .Where(f => f.Kind == FeatureKind.Centerline && f.IsProtectedEffective)
            .Select(f => f.Id)
            .ToListAsync(ct);
        if (protectedCenterlineIds.Count == 0)
        {
            return visible;
        }

        var exact = await protection.ExactViewIdsAsync(caller, protectedCenterlineIds, ct);
        var withheld = protectedCenterlineIds.Where(id => !exact.Contains(id)).ToArray();

        return withheld.Length == 0 ? visible : visible.Where(f => !withheld.Contains(f.Id));
    }

    protected override Expression<Func<Feature, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        f => ids.Contains(f.Id);

    protected override ValueTask<Expression<Func<Feature, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct) =>
        ValueTask.FromResult(new FeatureFilterCompiler(db).Compile(query.Where));

    protected override IQueryable<Feature> Order(IQueryable<Feature> rows, WorldQuery query)
    {
        // Ties are broken by id in every arm. Without it a row can appear on two consecutive pages
        // and another on neither, because the database is free to return equal rows in any order
        // and does — which reads to somebody scrolling as results appearing and vanishing.
        var descending = query.Descending;
        return query.Sort switch
        {
            SortKey.Created => descending
                ? rows.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
                : rows.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id),
            SortKey.Title => descending
                ? rows.OrderByDescending(f => f.Name).ThenByDescending(f => f.Id)
                : rows.OrderBy(f => f.Name).ThenBy(f => f.Id),
            SortKey.Owner => descending
                ? rows.OrderByDescending(f => db.Users
                        .Where(u => u.Id == f.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenByDescending(f => f.Id)
                : rows.OrderBy(f => db.Users
                        .Where(u => u.Id == f.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenBy(f => f.Id),
            _ => descending
                ? rows.OrderByDescending(f => f.UpdatedAt).ThenByDescending(f => f.Id)
                : rows.OrderBy(f => f.UpdatedAt).ThenBy(f => f.Id),
        };
    }

    protected override async ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<Feature> rows, AccessContext caller, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var typeIds = rows.Where(f => f.FeatureTypeId is not null)
            .Select(f => f.FeatureTypeId!.Value).Distinct().ToArray();
        var types = typeIds.Length == 0
            ? []
            : await db.FeatureTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t, ct);

        var exact = await protection.ExactViewIdsAsync(caller, [.. rows.Select(f => f.Id)], ct);

        return
        [
            .. rows.Select(f =>
            {
                var type = f.FeatureTypeId is not null && types.TryGetValue(f.FeatureTypeId.Value, out var t)
                    ? t
                    : null;
                return new FilterHit
                {
                    World = Key,
                    Id = f.Id,
                    // A feature with no name still has to be pickable from a list, so it is named
                    // after what it is rather than shown as a blank row somebody cannot tell apart
                    // from the blank row above it.
                    Title = string.IsNullOrWhiteSpace(f.Name) ? UnnamedTitle(f, type) : f.Name,
                    Subtitle = type?.Name,
                    Symbol = SymbolOf(f, type),
                    Placeable = exact.Contains(f.Id),
                };
            }),
        ];
    }

    /// <summary>
    /// The symbol key for a row: the kind for the built-in ones, the type's own code otherwise.
    /// </summary>
    /// <remarks>
    /// A key rather than anything drawable. The client owns what a key looks like, which is what
    /// lets the same answer be a list row, a map pin and a legend entry without the server having
    /// an opinion about any of the three.
    /// </remarks>
    private static string SymbolOf(Feature feature, FeatureType? type) => feature.Kind switch
    {
        FeatureKind.Cave => "cave",
        FeatureKind.CaveEntrance => "caveEntrance",
        FeatureKind.Centerline => "centerline",
        _ => type?.Code ?? "generic",
    };

    private static string UnnamedTitle(Feature feature, FeatureType? type) =>
        type?.Name ?? feature.Kind.ToString();
}
