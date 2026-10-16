// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// The cabinets of the filing scheme, as a filterable world.
/// </summary>
/// <remarks>
/// <para>
/// The cabinet tree is shown whole to every signed-in account — it is a filing scheme, small by
/// construction, and a reader who may open none of a shelf's documents still sees the shelf. So
/// this world's visibility is the whole table, exactly as the cabinet list's is. What the list
/// counts per reader (the documents on each shelf this reader may open) is not offered here at all.
/// </para>
/// <para>
/// Not built on the protected-row base: a cabinet has no owner, no caving group and no visibility,
/// so the fields and the owner sort that base offers would be questions with no answer.
/// </para>
/// </remarks>
public sealed class CabinetFilterWorld(SilexGisDbContext db) : FilterWorld<Cabinet>
{
    public const string Key = CabinetFilterFields.World;

    public override string World => Key;

    public override ValueTask<WorldVocabulary> VocabularyAsync(AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(CabinetFilterFields.Vocabulary);

    protected override ValueTask<IQueryable<Cabinet>> VisibleAsync(AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult<IQueryable<Cabinet>>(db.Cabinets.AsNoTracking());

    protected override Expression<Func<Cabinet, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        c => ids.Contains(c.Id);

    protected override ValueTask<Expression<Func<Cabinet, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Compile(query.Where));

    private static Expression<Func<Cabinet, bool>> Compile(FilterNode? node) => node switch
    {
        null => _ => true,
        AllOfNode all => all.Of.Count == 0
            ? _ => true
            : all.Of.Select(Compile).Aggregate((left, right) => left.And(right)),
        // An OR of nothing matches nothing, so clearing the last choice narrows rather than widens.
        AnyOfNode any => any.Of.Count == 0
            ? _ => false
            : any.Of.Select(Compile).Aggregate((left, right) => left.Or(right)),
        NotNode not => Compile(not.Of).Not(),
        ConditionNode condition => Leaf(condition),
        _ => throw new InvalidOperationException($"No compiler arm for {node.GetType().Name}."),
    };

    private static Expression<Func<Cabinet, bool>> Leaf(ConditionNode condition) => condition.Field switch
    {
        CommonFilterFields.Name => FilterLeaves.Text<Cabinet>(condition, c => c.Name),
        CommonFilterFields.CreatedAt => FilterLeaves.Instant<Cabinet>(condition, c => c.CreatedAt),
        CommonFilterFields.UpdatedAt => FilterLeaves.Instant<Cabinet>(condition, c => c.UpdatedAt),
        _ => throw new InvalidOperationException(
            $"No compiler arm for '{condition.Field}'. A field the vocabulary declares must have one "
            + "here, or a validated filter would silently match everything."),
    };

    protected override IQueryable<Cabinet> Order(IQueryable<Cabinet> rows, WorldQuery query)
    {
        // Ties broken by id in every arm, or equal keys page unstably.
        var descending = query.Descending;
        return query.Sort switch
        {
            SortKey.Created => descending
                ? rows.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                : rows.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            SortKey.Title => descending
                ? rows.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id)
                : rows.OrderBy(c => c.Name).ThenBy(c => c.Id),
            _ => descending
                ? rows.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.Id)
                : rows.OrderBy(c => c.UpdatedAt).ThenBy(c => c.Id),
        };
    }

    protected override async ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<Cabinet> rows, AccessContext caller, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        // The parent's name is the quieter line: two shelves called "Surveys" under different
        // cabinets are told apart by where they sit, which every reader of the tree already sees.
        var parentIds = rows.Where(c => c.ParentId is not null).Select(c => c.ParentId!.Value).Distinct().ToArray();
        var parents = parentIds.Length == 0
            ? []
            : await db.Cabinets.AsNoTracking()
                .Where(c => parentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return
        [
            .. rows.Select(c => new FilterHit
            {
                World = Key,
                Id = c.Id,
                Title = string.IsNullOrWhiteSpace(c.Name) ? Key : c.Name,
                Subtitle = c.ParentId is not null && parents.TryGetValue(c.ParentId.Value, out var parent)
                    ? parent
                    : null,
                Symbol = Key,
                // A cabinet is a shelf, not a place.
                Placeable = false,
            }),
        ];
    }
}
