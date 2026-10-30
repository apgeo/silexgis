// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// Trip logs, as a filterable world.
/// </summary>
/// <remarks>
/// The second world, and the one that shows whether the first was built as a world or as a feature
/// list wearing one. Everything load-bearing is inherited: the composition order, the meaning of
/// each operator, the shape of a row. What is written here is only what a trip actually is.
/// </remarks>
public sealed class TripLogFilterWorld(
    SilexGisDbContext db, FeatureProtection protection, FeatureFilterWorld anchors) : FilterWorld<TripLog>
{
    public const string Key = "tripLog";

    public override string World => Key;

    public override ValueTask<WorldVocabulary> VocabularyAsync(
        AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(TripLogFilterFields.Vocabulary);

    /// <summary>
    /// What this caller may see. No <em>row</em> is withheld beyond the visibility walk, so this
    /// world has no second rule to compose the way a feature world does.
    /// </summary>
    /// <remarks>
    /// A trip does hold one part that answers to a narrower audience than the row: the account of
    /// what went wrong is disclosed only to a caller who may change the trip, because it names
    /// identifiable people making mistakes. That is a decision about a property, taken per caller
    /// when the row is mapped — it never changes which rows exist, which rows this world returns,
    /// or what it counts, which is why nothing here has to weigh it. What it does mean is that a
    /// filter field over that part would be a second way to ask a question the record withholds,
    /// and so the vocabulary declares none: only whether something went wrong, which every reader
    /// of the trip is told anyway.
    /// <para>
    /// A draft is not a hidden trip. Where the write-up has got to is deliberately not consulted
    /// here: who may read a trip is settled in one place, and a draft the trip's own list endpoint
    /// hands to this caller must be handed over here too, or the filter becomes a second way to ask
    /// the same question with a different answer.
    /// </para>
    /// </remarks>
    protected override ValueTask<IQueryable<TripLog>> VisibleAsync(
        AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(db.TripLogs.AsNoTracking().VisibleTo(caller, AccessDomain.TripLogs));

    protected override Expression<Func<TripLog, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        t => ids.Contains(t.Id);

    protected override async ValueTask<Expression<Func<TripLog, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct)
    {
        var positions = await PositionConditions.ResolveAsync(
            anchors, query.Caller, query.Where, ask => VisitedAsync(query.Caller, ask, ct), ct);
        return Compile(query.Where, positions);
    }

    /// <summary>
    /// The trips that named a cave answering the question, counting only the caves a trip's cave
    /// list shows this caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caves are decided as the feature world decides them — by an entrance's shown position —
    /// and then through the trip cave list's own rule, readable and placeable exactly. A trip carries
    /// its own exact geometry, so "this trip reached that place" places a guarded cave by proximity:
    /// a cave the list withholds must not be what makes a trip match, or the condition would be the
    /// list's question asked with its position check missing. Such a cave contributes nothing, and a
    /// trip that visited only withheld caves answers no position condition, as a trip that named no
    /// cave at all.
    /// </para>
    /// <para>
    /// Caves only, because that is what the list holds. A trip naming a spring or a shaft by itself
    /// is not placed by it here; naming it on the cave it belongs to is what places the trip.
    /// </para>
    /// </remarks>
    private async Task<IEnumerable<Guid>> VisitedAsync(AccessContext caller, SpatialAsk ask, CancellationToken ct)
    {
        var answering = await anchors.MatchingFeatureIdsAsync(caller, ask, ct);
        if (answering.Count == 0)
        {
            return [];
        }

        var listed = (await TripCaveDisclosure.DisclosableCaveIdsAsync(db, protection, caller, answering, ct)).ToArray();
        if (listed.Length == 0)
        {
            return [];
        }

        var caves = db.Features.Where(f => listed.Contains(f.Id)).Select(f => f.Id);
        return await TripRoleLinks.TripIdsNamingAny(db, caves).Distinct().ToListAsync(ct);
    }

    private static Expression<Func<TripLog, bool>> Compile(
        FilterNode? node, IReadOnlyDictionary<ConditionNode, Guid[]> positions) => node switch
    {
        null => _ => true,
        AllOfNode all => all.Of.Count == 0
            ? _ => true
            : all.Of.Select(n => Compile(n, positions)).Aggregate((left, right) => left.And(right)),
        // An OR of nothing matches nothing, which is the reading that keeps "any of these" from
        // widening to everything when a person clears the last choice out of a group.
        AnyOfNode any => any.Of.Count == 0
            ? _ => false
            : any.Of.Select(n => Compile(n, positions)).Aggregate((left, right) => left.Or(right)),
        NotNode not => Compile(not.Of, positions).Not(),
        ConditionNode condition => Leaf(condition, positions),
        _ => throw new InvalidOperationException($"No compiler arm for {node.GetType().Name}."),
    };

    private static Expression<Func<TripLog, bool>> Leaf(
        ConditionNode condition, IReadOnlyDictionary<ConditionNode, Guid[]> positions) => condition.Field switch
    {
        SpatialConditions.PositionField => TripIn(PositionConditions.Of(positions, condition)),
        TripLogFilterFields.Title => FilterLeaves.Text<TripLog>(condition, t => t.Title),
        TripLogFilterFields.Type => FilterLeaves.Longs<TripLog>(condition, t => t.TripTypeId),
        TripLogFilterFields.State =>
            FilterLeaves.EnumField<TripLog, ActivityState>(condition, t => t.State),
        TripLogFilterFields.TripDate => FilterLeaves.DateSpan<TripLog>(condition, t => t.TripDate, t => t.TripDateEnd),
        TripLogFilterFields.HadIncident => FilterLeaves.Boolean<TripLog>(condition, t => t.HadIncident),
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

    private static Expression<Func<TripLog, bool>> TripIn(Guid[] trips) => t => trips.Contains(t.Id);

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
