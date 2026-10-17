// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Filters;

/// <summary>
/// One node of a filter: either a leaf condition, or a boolean combination of other nodes.
///
/// <para>
/// A tree rather than a list of rows, because the owner asked for more than one AND-group and
/// there is no honest way to express "(a and b) or (c and (d or e))" without one. The shape is
/// recursive but the depth is bounded — see <see cref="FilterValidation"/> — so a document is
/// always something a person could have drawn and always something the compiler can walk without
/// a stack of unknown depth.
/// </para>
/// <para>
/// The type discriminator is written into the document, so a saved filter read back by a later
/// version arrives as the node it was saved as rather than as whichever shape happens to
/// deserialise. Names are part of the stored contract: renaming one silently changes what every
/// saved filter means.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "node")]
[JsonDerivedType(typeof(AllOfNode), "allOf")]
[JsonDerivedType(typeof(AnyOfNode), "anyOf")]
[JsonDerivedType(typeof(NotNode), "not")]
[JsonDerivedType(typeof(ConditionNode), "condition")]
public abstract record FilterNode
{
    /// <summary>
    /// Every node this one contains, for walking a document without knowing its shape.
    /// A leaf contains nothing, which is what ends the walk.
    /// </summary>
    /// <remarks>
    /// Never written to the wire. These are the same nodes the concrete shape already carries under
    /// its own name, so serialising them puts every subtree into the document twice — and again
    /// inside each copy, which at the depth a filter may reach turns a small document into a large
    /// one. A filter is saved and put into links, so that cost would have been paid on every save
    /// and every URL for a convenience that only exists in memory.
    /// </remarks>
    [JsonIgnore]
    public virtual IReadOnlyList<FilterNode> Children => [];
}

/// <summary>Every child must match.</summary>
public sealed record AllOfNode(IReadOnlyList<FilterNode> Of) : FilterNode
{
    [JsonIgnore]
    public override IReadOnlyList<FilterNode> Children => Of;
}

/// <summary>At least one child must match.</summary>
public sealed record AnyOfNode(IReadOnlyList<FilterNode> Of) : FilterNode
{
    [JsonIgnore]
    public override IReadOnlyList<FilterNode> Children => Of;
}

/// <summary>
/// The child must not match.
/// </summary>
/// <remarks>
/// Negation is safe here for a reason worth writing down. A precise spatial condition is resolved
/// to a set of ids <em>before</em> the tree is compiled, and that set is drawn only from rows the
/// caller may place exactly. So for a row they may not place, the leaf answers the same way
/// whatever the row's true geometry is — which means negating it cannot turn "not near" into a
/// measurement. The safety is structural rather than a rule each operator has to remember.
/// </remarks>
public sealed record NotNode(FilterNode Of) : FilterNode
{
    [JsonIgnore]
    public override IReadOnlyList<FilterNode> Children => [Of];
}

/// <summary>
/// One condition: a field of the world being filtered, an operator, and the values it takes.
/// </summary>
/// <param name="Field">
/// A field the world's own vocabulary declares. Never a free-text path into stored JSON: a
/// condition that could name any key would also be a way to discover which keys exist.
/// </param>
/// <param name="Values">
/// What the operator compares against. Its length is the operator's business —
/// <see cref="FilterOp.Between"/> takes two, <see cref="FilterOp.In"/> takes many, and
/// <see cref="FilterOp.IsEmpty"/> takes none — and the validator is where that is settled, so the
/// compiler can assume a well-formed condition.
/// </param>
public sealed record ConditionNode(string Field, FilterOp Op, IReadOnlyList<FilterValue> Values)
    : FilterNode;

/// <summary>
/// One value in a condition, carrying its own type.
/// </summary>
/// <remarks>
/// Typed rather than a string that the compiler parses by looking at the field. Containment
/// against stored JSON distinguishes the number 4.5 from the string "4.5" — proved against the
/// database rather than assumed — so a filter that quoted every value would silently match
/// nothing, which is the worst way for a filter to be wrong.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextValue), "text")]
[JsonDerivedType(typeof(NumberValue), "number")]
[JsonDerivedType(typeof(BooleanValue), "boolean")]
[JsonDerivedType(typeof(InstantValue), "instant")]
[JsonDerivedType(typeof(IdValue), "id")]
[JsonDerivedType(typeof(PointValue), "point")]
[JsonDerivedType(typeof(PolygonValue), "polygon")]
[JsonDerivedType(typeof(ObjectValue), "object")]
[JsonDerivedType(typeof(SavedFilterValue), "savedFilter")]
public abstract record FilterValue;

public sealed record TextValue(string Value) : FilterValue;

public sealed record NumberValue(double Value) : FilterValue;

public sealed record BooleanValue(bool Value) : FilterValue;

/// <summary>A moment on the universal axis; the client sends what the person picked in their zone.</summary>
public sealed record InstantValue(DateTimeOffset Value) : FilterValue;

/// <summary>An identity — a tag, a type, an owner, a group, an object.</summary>
public sealed record IdValue(string Value) : FilterValue;

/// <summary>
/// A place given as longitude and latitude (WGS 84, degrees) — what a spatial condition measures
/// from when the person typed or clicked a point.
/// </summary>
/// <remarks>
/// A request may carry one; a saved filter never does. A stored point is a statement about a
/// position that outlives the request and is handed to whoever the filter is shared with, which is
/// not what anybody asked for when they measured from a spot on the map.
/// </remarks>
public sealed record PointValue(double Longitude, double Latitude) : FilterValue;

/// <summary>
/// An outline sent with the request, as the coordinates of a GeoJSON polygon: the outer ring first,
/// then any holes, each ring a closed list of <c>[longitude, latitude]</c> positions.
/// </summary>
/// <remarks>Request-only for the same reason as <see cref="PointValue"/>.</remarks>
public sealed record PolygonValue(double[][][] Coordinates) : FilterValue;

/// <summary>
/// An object somebody picked — a cave entrance, an area — named by its world and identity. Its
/// geometry is looked up afresh on every run, under the access of whoever runs the filter, so a
/// saved filter may hold one without holding a position.
/// </summary>
public sealed record ObjectValue(string World, Guid Id) : FilterValue;

/// <summary>
/// Another saved filter, whose result is what a distance is measured from. Run under the access of
/// whoever runs the outer filter, every time, and never nested further.
/// </summary>
public sealed record SavedFilterValue(Guid Id) : FilterValue;
