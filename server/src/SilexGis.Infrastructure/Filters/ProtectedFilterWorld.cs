// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Filters;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// A world over rows the ordinary protection walk decides, with a name and two timestamps.
/// </summary>
/// <remarks>
/// <para>
/// Map views are the first of several the same shape — geofiles, georeferenced maps and survey
/// models are all a row somebody owns, which a group may share, with a name to search and dates to
/// sort by. Written out separately they would be four copies of one paragraph, and within a year
/// one of them would have a subtly different idea of what "contains" means, or would have forgotten
/// to break a sort tie.
/// </para>
/// <para>
/// Features and trip logs do not use this, and should not be made to. A feature has a second
/// visibility rule about centrelines and a taxonomy of its own; a trip has a date that is not a
/// timestamp. Bending either into this shape would cost more than the paragraph it saved.
/// </para>
/// </remarks>
public abstract class ProtectedFilterWorld<TEntity> : FilterWorld<TEntity>
    where TEntity : class, IProtectedEntity, ITimestamped
{
    /// <summary>Which domain's read entries decide this world.</summary>
    protected abstract AccessDomain Domain { get; }

    /// <summary>The untracked table.</summary>
    protected abstract IQueryable<TEntity> Rows { get; }

    /// <summary>The column the world calls its name — one says name, another says title.</summary>
    protected abstract Expression<Func<TEntity, string?>> NameOf { get; }

    /// <summary>The symbol key every row of this world draws with.</summary>
    protected abstract string Symbol { get; }

    /// <summary>The quieter second line, if the world has one worth saying.</summary>
    protected virtual string? SubtitleOf(TEntity row) => null;

    /// <summary>
    /// Nothing in these worlds is withheld beyond what the walk decides. A world that grows such a
    /// rule overrides this rather than adding it to its filter, where it would stop being
    /// visibility.
    /// </summary>
    protected override ValueTask<IQueryable<TEntity>> VisibleAsync(
        AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(Rows.VisibleTo(caller, Domain));

    public override ValueTask<WorldVocabulary> VocabularyAsync(
        AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(
            CommonFilterFields.Vocabulary(World, $"filters.worlds.{World}"));

    protected override Expression<Func<TEntity, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        e => ids.Contains(e.Id);

    protected override ValueTask<Expression<Func<TEntity, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Compile(query.Where));

    private Expression<Func<TEntity, bool>> Compile(FilterNode? node) => node switch
    {
        null => _ => true,
        AllOfNode all => all.Of.Count == 0
            ? _ => true
            : all.Of.Select(Compile).Aggregate((left, right) => left.And(right)),
        // An OR of nothing matches nothing: clearing the last choice out of a group narrows to
        // nothing rather than quietly widening to everything.
        AnyOfNode any => any.Of.Count == 0
            ? _ => false
            : any.Of.Select(Compile).Aggregate((left, right) => left.Or(right)),
        NotNode not => Compile(not.Of).Not(),
        ConditionNode condition => Leaf(condition),
        _ => throw new InvalidOperationException($"No compiler arm for {node.GetType().Name}."),
    };

    private Expression<Func<TEntity, bool>> Leaf(ConditionNode condition) =>
        condition.Field switch
        {
            CommonFilterFields.Name => FilterLeaves.Text(condition, NameOf),
            CommonFilterFields.OwnerId => FilterLeaves.Guids<TEntity>(condition, e => e.OwnerUserId),
            CommonFilterFields.CavingGroupId =>
                FilterLeaves.Guids<TEntity>(condition, e => e.CavingGroupId),
            CommonFilterFields.Visibility =>
                FilterLeaves.Ids(condition, FilterLeaves.Enum<TEntity, Visibility>(e => e.Visibility)),
            CommonFilterFields.CreatedAt => FilterLeaves.Instant<TEntity>(condition, e => e.CreatedAt),
            CommonFilterFields.UpdatedAt => FilterLeaves.Instant<TEntity>(condition, e => e.UpdatedAt),
            _ => throw new InvalidOperationException(
                $"No compiler arm for '{condition.Field}'. A field the vocabulary declares must have "
                + "one here, or a validated filter would silently match everything."),
        };

    protected override IQueryable<TEntity> Order(IQueryable<TEntity> rows, WorldQuery query)
    {
        // Every arm breaks ties by id. Without it a row can appear on two consecutive pages while
        // another appears on neither, which reads to somebody scrolling as results flickering.
        var descending = query.Descending;
        return query.Sort switch
        {
            SortKey.Created => descending
                ? rows.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                : rows.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id),
            SortKey.Title => descending
                ? rows.OrderByDescending(NameOf).ThenByDescending(e => e.Id)
                : rows.OrderBy(NameOf).ThenBy(e => e.Id),
            SortKey.Owner => descending
                ? rows.OrderByDescending(OwnerName).ThenByDescending(e => e.Id)
                : rows.OrderBy(OwnerName).ThenBy(e => e.Id),
            _ => descending
                ? rows.OrderByDescending(e => e.UpdatedAt).ThenByDescending(e => e.Id)
                : rows.OrderBy(e => e.UpdatedAt).ThenBy(e => e.Id),
        };
    }

    /// <summary>
    /// The owner's display name, so an ordering by owner reads as names rather than identifiers.
    /// </summary>
    protected abstract Expression<Func<TEntity, string?>> OwnerName { get; }

    protected override ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<TEntity> rows, AccessContext caller, CancellationToken ct)
    {
        var name = NameOf.Compile();
        return ValueTask.FromResult<IReadOnlyList<FilterHit>>(
        [
            .. rows.Select(row => new FilterHit
            {
                World = World,
                Id = row.Id,
                // A row with no name is still pickable: it is named after what it is rather than
                // drawn as a blank somebody cannot tell from the blank above it.
                Title = string.IsNullOrWhiteSpace(name(row)) ? World : name(row)!,
                Subtitle = SubtitleOf(row),
                Symbol = Symbol,
                // These worlds hold no position anybody is shown through a filter. A geofile has a
                // bounding box and a map view has a camera, and neither is this route's to disclose.
                Placeable = false,
            }),
        ]);
    }
}
