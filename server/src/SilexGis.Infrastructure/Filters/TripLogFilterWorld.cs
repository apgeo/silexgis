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
/// Trip logs, as a filterable world.
/// </summary>
/// <remarks>
/// The second world, and the one that shows whether the first was built as a world or as a feature
/// list wearing one. Everything load-bearing is inherited: the composition order, the meaning of
/// each operator, the shape of a row. What is written here is only what a trip actually is.
/// </remarks>
public sealed class TripLogFilterWorld(SilexGisDbContext db) : FilterWorld<TripLog>
{
    public const string Key = "tripLog";

    public override string World => Key;

    public override ValueTask<WorldVocabulary> VocabularyAsync(
        AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(TripLogFilterFields.Vocabulary);

    /// <summary>
    /// What this caller may see. A trip has no second rule the way a feature does — nothing about
    /// a trip is withheld from somebody who may read the trip.
    /// </summary>
    /// <remarks>
    /// A draft is not a hidden trip. Where the write-up has got to is deliberately not consulted
    /// here: who may read a trip is settled in one place, and a draft the trip's own list endpoint
    /// hands to this caller must be handed over here too, or the filter becomes a second way to ask
    /// the same question with a different answer.
    /// </remarks>
    protected override ValueTask<IQueryable<TripLog>> VisibleAsync(
        AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(db.TripLogs.AsNoTracking().VisibleTo(caller, AccessDomain.TripLogs));

    protected override Expression<Func<TripLog, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        t => ids.Contains(t.Id);

    protected override ValueTask<Expression<Func<TripLog, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Compile(query.Where));

    private static Expression<Func<TripLog, bool>> Compile(FilterNode? node) => node switch
    {
        null => _ => true,
        AllOfNode all => all.Of.Count == 0
            ? _ => true
            : all.Of.Select(Compile).Aggregate((left, right) => left.And(right)),
        // An OR of nothing matches nothing, which is the reading that keeps "any of these" from
        // widening to everything when a person clears the last choice out of a group.
        AnyOfNode any => any.Of.Count == 0
            ? _ => false
            : any.Of.Select(Compile).Aggregate((left, right) => left.Or(right)),
        NotNode not => Compile(not.Of).Not(),
        ConditionNode condition => Leaf(condition),
        _ => throw new InvalidOperationException($"No compiler arm for {node.GetType().Name}."),
    };

    private static Expression<Func<TripLog, bool>> Leaf(ConditionNode condition) => condition.Field switch
    {
        TripLogFilterFields.Title => FilterLeaves.Text<TripLog>(condition, t => t.Title),
        TripLogFilterFields.Type => FilterLeaves.NullableEnum<TripLog, TripType>(condition, t => t.Type),
        TripLogFilterFields.State =>
            FilterLeaves.EnumField<TripLog, ActivityState>(condition, t => t.State),
        TripLogFilterFields.TripDate => FilterLeaves.Date<TripLog>(condition, t => t.TripDate),
        TripLogFilterFields.OwnerId => FilterLeaves.Guids<TripLog>(condition, t => t.OwnerUserId),
        TripLogFilterFields.CavingGroupId => FilterLeaves.Guids<TripLog>(condition, t => t.CavingGroupId),
        TripLogFilterFields.OrganizingCavingGroupId =>
            FilterLeaves.Guids<TripLog>(condition, t => t.OrganizingCavingGroupId),
        TripLogFilterFields.Visibility =>
            FilterLeaves.EnumField<TripLog, Visibility>(condition, t => t.Visibility),
        TripLogFilterFields.CreatedAt => FilterLeaves.Instant<TripLog>(condition, t => t.CreatedAt),
        TripLogFilterFields.UpdatedAt => FilterLeaves.Instant<TripLog>(condition, t => t.UpdatedAt),
        _ => throw new InvalidOperationException(
            $"No compiler arm for '{condition.Field}'. A field the vocabulary declares must have one "
            + "here, or a validated filter would silently match everything."),
    };

    protected override IQueryable<TripLog> Order(IQueryable<TripLog> rows, WorldQuery query)
    {
        // Tied rows are broken by id in every arm, or a row can appear on two consecutive pages
        // while another appears on neither.
        var descending = query.Descending;
        return query.Sort switch
        {
            SortKey.Occurred => descending
                ? rows.OrderByDescending(t => t.TripDate).ThenByDescending(t => t.Id)
                : rows.OrderBy(t => t.TripDate).ThenBy(t => t.Id),
            SortKey.Created => descending
                ? rows.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                : rows.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
            SortKey.Title => descending
                ? rows.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id)
                : rows.OrderBy(t => t.Title).ThenBy(t => t.Id),
            SortKey.Owner => descending
                ? rows.OrderByDescending(t => db.Users
                        .Where(u => u.Id == t.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenByDescending(t => t.Id)
                : rows.OrderBy(t => db.Users
                        .Where(u => u.Id == t.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenBy(t => t.Id),
            _ => descending
                ? rows.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id)
                : rows.OrderBy(t => t.UpdatedAt).ThenBy(t => t.Id),
        };
    }

    protected override ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<TripLog> rows, AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<FilterHit>>(
        [
            .. rows.Select(t => new FilterHit
            {
                World = Key,
                Id = t.Id,
                Title = t.Title,
                // The day, which is what tells two trips to the same place apart in a list.
                Subtitle = t.TripDate.ToString("yyyy-MM-dd"),
                Symbol = "tripLog",
                // A trip carries a geometry, but nothing here decides who may be shown it, and a
                // world that guessed would be a second place answering a question that has one home.
                // Until a trip's own protection rule is written, no trip is offered as placeable.
                Placeable = false,
            }),
        ]);
}
