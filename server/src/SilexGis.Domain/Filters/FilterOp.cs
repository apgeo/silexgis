// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a condition does with its values.
///
/// <para>
/// Deliberately small. Every operator here is one a person can pick from a list and one the
/// compiler can turn into SQL; there is no third state where a condition is accepted and then
/// evaluated in memory, because a filter that pages and counts over a partly in-memory predicate
/// reports numbers that disagree with its own rows.
/// </para>
/// <para>
/// Stored in saved documents, so the names are a contract: a member is added at the end and never
/// renamed or renumbered.
/// </para>
/// </summary>
public enum FilterOp
{
    /// <summary>Exactly this value.</summary>
    Equals = 0,

    /// <summary>Any of the given values — what a multi-select produces.</summary>
    In = 1,

    /// <summary>
    /// The text contains this, compared without regard to case or diacritics.
    /// A field GPS writes "Pestera" and a person writes "Peștera"; a search that told them apart
    /// would find nothing either way.
    /// </summary>
    Contains = 2,

    /// <summary>The text begins with this, folded the same way.</summary>
    StartsWith = 3,

    LessThan = 4,

    GreaterThan = 5,

    /// <summary>Between two values, both ends included. Takes exactly two values, low first.</summary>
    Between = 6,

    /// <summary>The field holds nothing. Takes no values.</summary>
    IsEmpty = 7,

    /// <summary>The field holds something. Takes no values.</summary>
    IsNotEmpty = 8,

    /// <summary>
    /// Within a distance of an anchor. The values carry the anchor and the radius, and the anchor
    /// is resolved before compilation — see <see cref="NotNode"/> for why that resolution is what
    /// makes the whole spatial family safe to negate.
    /// </summary>
    Within = 9,

    /// <summary>Inside a shape. Resolved the same way, and safe for the same reason.</summary>
    Inside = 10,
}

/// <summary>What kind of value a field takes, and therefore which operators can apply to it.</summary>
public enum FieldKind
{
    Text = 0,
    Number = 1,
    Boolean = 2,
    Instant = 3,

    /// <summary>An identity chosen from a list — a tag, a type, an owner, a caving group.</summary>
    Id = 4,

    /// <summary>A place. Only the spatial operators apply, and only through a resolved anchor.</summary>
    Spatial = 5,
}

/// <summary>Which operators a field of each kind may take.</summary>
/// <remarks>
/// One table rather than a rule per field, so a world declaring a new field cannot accidentally
/// admit an operator nothing can compile. The validator reads this and the builder is generated
/// from it, which is what keeps the two from drifting into disagreement about what is offerable.
/// </remarks>
public static class FilterOps
{
    public static IReadOnlyList<FilterOp> For(FieldKind kind) => kind switch
    {
        FieldKind.Text =>
            [FilterOp.Equals, FilterOp.Contains, FilterOp.StartsWith, FilterOp.IsEmpty, FilterOp.IsNotEmpty],
        FieldKind.Number =>
            [FilterOp.Equals, FilterOp.LessThan, FilterOp.GreaterThan, FilterOp.Between,
             FilterOp.IsEmpty, FilterOp.IsNotEmpty],
        FieldKind.Boolean => [FilterOp.Equals],
        FieldKind.Instant =>
            [FilterOp.LessThan, FilterOp.GreaterThan, FilterOp.Between, FilterOp.IsEmpty, FilterOp.IsNotEmpty],
        FieldKind.Id => [FilterOp.Equals, FilterOp.In, FilterOp.IsEmpty, FilterOp.IsNotEmpty],
        FieldKind.Spatial => [FilterOp.Within, FilterOp.Inside],
        _ => [],
    };

    /// <summary>How many values an operator takes: null means one or more.</summary>
    public static int? Arity(FilterOp op) => op switch
    {
        FilterOp.IsEmpty or FilterOp.IsNotEmpty => 0,
        FilterOp.Between => 2,
        FilterOp.In => null,
        _ => 1,
    };
}
