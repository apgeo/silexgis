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
/// Survey models — the line plots and wall meshes of a cave — as a filterable world.
/// </summary>
/// <remarks>
/// <para>
/// A survey model carries no rights of its own; its cave answers for it, twice. The reader must be
/// able to read the cave, and must be able to place it exactly: a model is a measured drawing of
/// the passages, georeferenced, so serving one to somebody who may not place the cave would hand
/// over the position the protection exists to withhold. A cave's model list answers an empty list
/// to a reader who may read the cave but not place it, and this world leaves those models out the
/// same way — before the filter, the count and the page, so a total never counts a model the
/// reader could not open.
/// </para>
/// <para>
/// Not built on the protected-row base: a model has no owner, group or visibility, so the fields
/// and the owner sort that base offers would be questions with no answer.
/// </para>
/// </remarks>
public sealed class SurveyModelFilterWorld(SilexGisDbContext db, FeatureProtection protection)
    : FilterWorld<SurveyModel>
{
    public const string Key = SurveyModelFilterFields.World;

    public override string World => Key;

    public override ValueTask<WorldVocabulary> VocabularyAsync(AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(SurveyModelFilterFields.Vocabulary);

    protected override async ValueTask<IQueryable<SurveyModel>> VisibleAsync(
        AccessContext caller, CancellationToken ct)
    {
        // The caves the reader may read, by the feature walk every cave read uses.
        var caves = db.Features.AsNoTracking()
            .VisibleTo(caller, db.Features, db.FeatureSetMembers)
            .Where(f => f.Kind == FeatureKind.Cave)
            .Select(f => f.Id);
        var readable = db.SurveyModels.AsNoTracking().Where(m => caves.Contains(m.CaveFeatureId));

        // The exact-view half cannot be composed into a query, so it is asked once over every cave
        // with a model among the readable ones, and the caves it admits go back in as a parameter.
        // The admitted set rather than its complement: the page is a second query, and a model
        // committed on another cave between the two reads must be left out, not let through.
        var linked = await readable.Select(m => m.CaveFeatureId).Distinct().ToListAsync(ct);
        Guid[] exactIds = linked.Count == 0
            ? []
            : (await protection.ExactViewIdsAsync(caller, linked, ct)).ToArray();
        return readable.Where(m => exactIds.Contains(m.CaveFeatureId));
    }

    protected override Expression<Func<SurveyModel, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        m => ids.Contains(m.Id);

    protected override ValueTask<Expression<Func<SurveyModel, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Compile(query.Where));

    private static Expression<Func<SurveyModel, bool>> Compile(FilterNode? node) => node switch
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

    private static Expression<Func<SurveyModel, bool>> Leaf(ConditionNode condition) => condition.Field switch
    {
        CommonFilterFields.Name => FilterLeaves.Text<SurveyModel>(condition, m => m.Name),
        SurveyModelFilterFields.Format =>
            FilterLeaves.EnumField<SurveyModel, SurveyModelFormat>(condition, m => m.Format),
        SurveyModelFilterFields.SurveyedAt => FilterLeaves.NullableDate<SurveyModel>(condition, m => m.SurveyedAt),
        SurveyModelFilterFields.IsCurrent => FilterLeaves.Boolean<SurveyModel>(condition, m => m.IsCurrent),
        CommonFilterFields.CreatedAt => FilterLeaves.Instant<SurveyModel>(condition, m => m.CreatedAt),
        CommonFilterFields.UpdatedAt => FilterLeaves.Instant<SurveyModel>(condition, m => m.UpdatedAt),
        _ => throw new InvalidOperationException(
            $"No compiler arm for '{condition.Field}'. A field the vocabulary declares must have one "
            + "here, or a validated filter would silently match everything."),
    };

    protected override IQueryable<SurveyModel> Order(IQueryable<SurveyModel> rows, WorldQuery query)
    {
        // Ties broken by id in every arm, or equal keys page unstably.
        var descending = query.Descending;
        return query.Sort switch
        {
            SortKey.Created => descending
                ? rows.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                : rows.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id),
            SortKey.Title => descending
                ? rows.OrderByDescending(m => m.Name).ThenByDescending(m => m.Id)
                : rows.OrderBy(m => m.Name).ThenBy(m => m.Id),
            _ => descending
                ? rows.OrderByDescending(m => m.UpdatedAt).ThenByDescending(m => m.Id)
                : rows.OrderBy(m => m.UpdatedAt).ThenBy(m => m.Id),
        };
    }

    protected override async ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<SurveyModel> rows, AccessContext caller, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        // The cave's name tells two models called "Main survey" apart. Every row here belongs to a
        // cave the reader may read and place, so the name is one they are already shown.
        var caveIds = rows.Select(m => m.CaveFeatureId).Distinct().ToArray();
        var caves = await db.Features.AsNoTracking()
            .Where(f => caveIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        return
        [
            .. rows.Select(m => new FilterHit
            {
                World = Key,
                Id = m.Id,
                Title = string.IsNullOrWhiteSpace(m.Name) ? Key : m.Name,
                Subtitle = caves.TryGetValue(m.CaveFeatureId, out var cave) ? cave : null,
                Symbol = Key,
                // A model's anchor is its cave's position by another name, and placing it is the
                // cave's business, not this answer's.
                Placeable = false,
            }),
        ];
    }
}
