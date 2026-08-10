// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// What a spatial condition became once its anchor was resolved.
/// </summary>
/// <remarks>
/// The resolution happens before compilation and only over rows the caller may place exactly, so
/// what arrives here is an ordinary set of ids. That is the whole reason the spatial family is
/// safe to negate: for a row the caller cannot place, the condition answers the same way whatever
/// the row's true geometry is, so <c>not near</c> cannot be turned into a measurement.
/// </remarks>
public sealed record ResolvedSpatial(IReadOnlyCollection<Guid> Ids);

/// <summary>
/// Turns a validated filter tree into one expression a database can run.
/// </summary>
/// <remarks>
/// <para>
/// There is no fallback. Every condition reaching this class has already been proved by
/// <see cref="FilterValidation"/> to name a declared field, use an operator that field admits and
/// carry the right values — so an unknown field here is a programming error rather than a caller's
/// mistake, and it throws rather than quietly matching everything. A filter that silently widened
/// on a field nobody recognised would be the worst kind of wrong: more rows than asked for, with
/// nothing on screen to say so.
/// </para>
/// <para>
/// Nothing here consults the caller. This builds the predicate the caller authored and nothing
/// else; who may see which row is composed around it, once, by the query that owns that rule.
/// Keeping the two apart is what stops a filter condition from ever being able to widen the
/// visible set.
/// </para>
/// </remarks>
public sealed class FeatureFilterCompiler(SilexGisDbContext db)
{
    /// <summary>
    /// The tree as one predicate. A null tree means "everything", which composes as a constant
    /// rather than as a special case every caller has to remember.
    /// </summary>
    /// <param name="spatial">
    /// The resolved id set for each spatial condition, keyed by the condition it came from.
    /// Absent for a condition means it resolved to nothing — which is a real answer (no anchor the
    /// caller may place, or nothing within the radius) and compiles to "matches no row".
    /// </param>
    public Expression<Func<Feature, bool>> Compile(
        FilterNode? node, IReadOnlyDictionary<ConditionNode, ResolvedSpatial>? spatial = null)
    {
        if (node is null)
        {
            return _ => true;
        }

        return Build(node, spatial ?? new Dictionary<ConditionNode, ResolvedSpatial>());
    }

    private Expression<Func<Feature, bool>> Build(
        FilterNode node, IReadOnlyDictionary<ConditionNode, ResolvedSpatial> spatial) => node switch
    {
        AllOfNode all => all.Of.Select(child => Build(child, spatial))
            .Aggregate((left, right) => left.And(right)),
        AnyOfNode any => any.Of.Select(child => Build(child, spatial))
            .Aggregate((left, right) => left.Or(right)),
        NotNode not => Build(not.Of, spatial).Not(),
        ConditionNode condition => Leaf(condition, spatial),
        _ => throw new InvalidOperationException($"Unknown filter node {node.GetType().Name}."),
    };

    private Expression<Func<Feature, bool>> Leaf(
        ConditionNode condition, IReadOnlyDictionary<ConditionNode, ResolvedSpatial> spatial)
    {
        if (condition.Field == FeatureFilterFields.Position)
        {
            // Resolved elsewhere; here it is only ever a set membership.
            var ids = spatial.TryGetValue(condition, out var resolved) ? resolved.Ids : [];
            return ids.Count == 0 ? _ => false : f => ids.Contains(f.Id);
        }

        if (FeatureFilterFields.TryReadProperty(condition.Field, out _, out var key))
        {
            return PropertyLeaf(key, condition);
        }

        return condition.Field switch
        {
            FeatureFilterFields.Name => FilterLeaves.Text<Feature>(condition, f => f.Name),
            FeatureFilterFields.Kind =>
                FilterLeaves.EnumField<Feature, FeatureKind>(condition, f => f.Kind),
            FeatureFilterFields.Category =>
                FilterLeaves.EnumField<Feature, FeatureCategory>(condition, f => f.Category),
            FeatureFilterFields.TypeId => FilterLeaves.Longs<Feature>(condition, f => f.FeatureTypeId),
            FeatureFilterFields.Tag => TagLeaf(condition),
            // The owner is never null, so it is read through a nullable selector to reach the same
            // leaf every other identity field uses rather than having one of its own.
            FeatureFilterFields.OwnerId => FilterLeaves.Guids<Feature>(condition, f => f.OwnerUserId),
            FeatureFilterFields.CavingGroupId =>
                FilterLeaves.Guids<Feature>(condition, f => f.CavingGroupId),
            FeatureFilterFields.Visibility =>
                FilterLeaves.EnumField<Feature, Visibility>(condition, f => f.Visibility),
            FeatureFilterFields.LocationProtected =>
                FilterLeaves.Boolean<Feature>(condition, f => f.LocationProtected),
            FeatureFilterFields.CreatedAt => FilterLeaves.Instant<Feature>(condition, f => f.CreatedAt),
            FeatureFilterFields.UpdatedAt => FilterLeaves.Instant<Feature>(condition, f => f.UpdatedAt),
            _ => throw new InvalidOperationException(
                $"No compiler arm for '{condition.Field}'. A field the vocabulary declares must have "
                + "one here, or a validated filter would silently match everything."),
        };
    }

    // ---------- leaves ----------

    /// <summary>
    /// Tags, spelled exactly as the feature list already spells them — an EXISTS over the
    /// taggings joined to the tag's slug. Copied rather than improved on purpose: the emitted SQL
    /// keeps the shape the existing plans were measured against, so moving the list onto this
    /// compiler changes what can be asked without changing what the database does about it.
    /// </summary>
    private Expression<Func<Feature, bool>> TagLeaf(ConditionNode condition)
    {
        if (condition.Op is FilterOp.IsEmpty)
        {
            return f => !db.Taggings.Any(tg => tg.FeatureId == f.Id);
        }

        if (condition.Op is FilterOp.IsNotEmpty)
        {
            return f => db.Taggings.Any(tg => tg.FeatureId == f.Id);
        }

        var slugs = condition.Values.OfType<IdValue>().Select(v => v.Value).ToArray();
        return f => db.Taggings.Any(tg =>
            tg.FeatureId == f.Id && db.Tags.Any(t => t.Id == tg.TagId && slugs.Contains(t.Slug)));
    }

    private Expression<Func<Feature, bool>> PropertyLeaf(string key, ConditionNode condition)
    {
        // Containment against the stored document. Proved against the database rather than
        // assumed: this reaches PostgreSQL as `@>` and is served by the index over the column,
        // and it distinguishes the number 4.5 from the string "4.5" — which is why the values
        // carried a type all the way from the browser.
        if (condition.Op is FilterOp.IsEmpty or FilterOp.IsNotEmpty)
        {
            return condition.Op == FilterOp.IsEmpty
                ? f => !EF.Functions.JsonExists(f.Properties, key)
                : f => EF.Functions.JsonExists(f.Properties, key);
        }

        var json = PropertyProbe(key, condition.Values[0]);
        return f => EF.Functions.JsonContains(f.Properties, json);
    }

    /// <summary>One key and one typed value, as the containment document to compare against.</summary>
    private static string PropertyProbe(string key, FilterValue value)
    {
        var encoded = value switch
        {
            NumberValue number => System.Text.Json.JsonSerializer.Serialize(number.Value),
            BooleanValue boolean => boolean.Value ? "true" : "false",
            TextValue text => System.Text.Json.JsonSerializer.Serialize(text.Value),
            IdValue id => System.Text.Json.JsonSerializer.Serialize(id.Value),
            InstantValue instant => System.Text.Json.JsonSerializer.Serialize(instant.Value),
            _ => "null",
        };

        return $"{{{System.Text.Json.JsonSerializer.Serialize(key)}:{encoded}}}";
    }

    private static Func<string, Expression<Func<Feature, bool>>> EnumMatcher<TEnum>(
        Expression<Func<Feature, TEnum>> selector)
        where TEnum : struct, Enum =>
        raw =>
        {
            if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value) || !Enum.IsDefined(value))
            {
                // Validation checks shape, not vocabulary membership of every enum name, so an
                // unknown name lands here. Matching nothing is the honest answer: it is a value no
                // row has.
                return _ => false;
            }

            var body = Expression.Equal(selector.Body, Expression.Constant(value));
            return Expression.Lambda<Func<Feature, bool>>(body, selector.Parameters);
        };
}
