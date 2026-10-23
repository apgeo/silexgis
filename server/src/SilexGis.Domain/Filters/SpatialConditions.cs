// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Filters;

/// <summary>
/// What a spatial condition may hold, and the refusals a run can give when its anchor cannot be
/// used. The single home of those rules: the validator, the saved-filter rules and the resolver
/// all read them from here.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shapes.</b> <see cref="FilterOp.Within"/> takes a radius in metres and an anchor — a
/// <see cref="PointValue"/>, an <see cref="ObjectValue"/> or a <see cref="SavedFilterValue"/>.
/// <see cref="FilterOp.Inside"/> takes one outline — a <see cref="PolygonValue"/> or an
/// <see cref="ObjectValue"/> naming an area.
/// </para>
/// <para>
/// <b>Why there is no radius floor tied to the protection grid.</b> A row the caller may not place
/// exactly is measured by the position the caller is shown — its snapped lattice point — never by
/// its stored one, so shrinking a radius around it can only ever tell somebody where that lattice
/// point is, which every surface already shows them. A floor would refuse honest small questions
/// about exactly-placed rows and protect nothing. The floor here only refuses a radius that is not
/// a distance at all.
/// </para>
/// </remarks>
public static class SpatialConditions
{
    /// <summary>The smallest radius a condition may ask about, in metres.</summary>
    public const double MinRadiusMeters = 1;

    /// <summary>
    /// The largest radius, in metres, unless the installation sets another. Past this the question
    /// is better asked as an area, and the window the database prefilters on stops being a window:
    /// a cost guard, not a protection — every row is measured on the position its reader is shown
    /// whatever the radius.
    /// </summary>
    public const double DefaultMaxRadiusMeters = 200_000;

    /// <summary>
    /// The most an installation may raise <see cref="DefaultMaxRadiusMeters"/> to: a thousand
    /// kilometres, the width of a country. Past it the prefilter's box covers a continent and every
    /// distance question visits the whole table.
    /// </summary>
    public const double LargestMaxRadiusMeters = 1_000_000;

    /// <summary>Whether a configured ceiling is one the conditions can be asked under.</summary>
    public static bool IsUsableMaxRadius(double meters) =>
        double.IsFinite(meters) && meters >= MinRadiusMeters && meters <= LargestMaxRadiusMeters;

    /// <summary>
    /// The one world whose rows an object anchor or a saved-filter anchor names, whichever world the
    /// condition filters: a feature is the only row whose position the placement gate decides. A
    /// photograph within 500 m of a cave is measured from the cave, not from another photograph.
    /// </summary>
    public const string AnchorWorld = "feature";

    /// <summary>The field every world with a shown position declares its spatial conditions on.</summary>
    public const string PositionField = "position";

    /// <summary>Positions in one outline, over all its rings — far past anything somebody draws.</summary>
    public const int MaxPolygonPositions = 2_000;

    /// <summary>Rings in one outline: the outer one and its holes.</summary>
    public const int MaxPolygonRings = 50;

    /// <summary>
    /// The most rows another saved filter's result may hold when a distance is measured from it.
    /// Counted over the rows the runner may place exactly, so the number says nothing about rows
    /// they are not shown.
    /// </summary>
    public const int MaxAnchorRows = 1_000;

    /// <summary>
    /// An anchor this caller cannot use: an object or a saved filter that does not exist, that they
    /// may not read, or — for an object — whose position they may not see exactly. One code and one
    /// sentence for all of them: a different answer for "exists but withheld" would turn the refusal
    /// into the very answer the withholding exists to avoid.
    /// </summary>
    public const string AnchorNotFoundCode = "filter.anchor_not_found";

    /// <summary>
    /// An area object asked to enclose something, that is not an outline — the not-an-outline code
    /// every route that takes a study area answers with, under the filter's own prefix.
    /// </summary>
    public const string AnchorNotAnAreaCode = "filter." + StudyArea.NotAnOutlineCodeSuffix;

    /// <summary>A saved filter measured from, that itself measures from a saved filter.</summary>
    public const string AnchorNestedCode = "filter.anchor_nested";

    /// <summary>A saved filter measured from, whose result is larger than <see cref="MaxAnchorRows"/>.</summary>
    public const string AnchorTooLargeCode = "filter.anchor_too_large";

    public const string AnchorNotFoundDetail = "What this filter measures from is not available.";

    /// <summary>
    /// Problems with a spatial condition's values, each a code from <see cref="FilterProblemCodes"/>
    /// with an English sentence and the limit it is about. Called once its arity and the kind of
    /// each value are known to be right.
    /// </summary>
    /// <param name="maxRadiusMeters">The installation's ceiling, from the vocabulary the condition is read with.</param>
    public static IReadOnlyList<(string Code, string Detail, int? Limit)> Problems(
        ConditionNode condition, double maxRadiusMeters = DefaultMaxRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var field = condition.Field;
        switch (condition.Op, condition.Values)
        {
            case (FilterOp.Within, [NumberValue radius, var anchor]):
                if (!double.IsFinite(radius.Value)
                    || radius.Value < MinRadiusMeters
                    || radius.Value > maxRadiusMeters)
                {
                    return [(FilterProblemCodes.RadiusOutOfRange,
                        $"'{field}' takes a distance from {MinRadiusMeters:0} to {maxRadiusMeters:0} metres.",
                        (int)maxRadiusMeters)];
                }

                return anchor switch
                {
                    PointValue point => PointProblems(field, point),
                    ObjectValue target => ObjectProblems(field, target),
                    SavedFilterValue => [],
                    _ => [(FilterProblemCodes.ValueKind, $"'{field}' was given a value of the wrong kind.", null)],
                };

            case (FilterOp.Inside, [var outline]):
                return outline switch
                {
                    PolygonValue polygon => PolygonProblems(field, polygon),
                    ObjectValue target => ObjectProblems(field, target),
                    _ => [(FilterProblemCodes.ValueKind, $"'{field}' was given a value of the wrong kind.", null)],
                };

            default:
                return [(FilterProblemCodes.ValueKind, $"'{field}' was given a value of the wrong kind.", null)];
        }
    }

    /// <summary>
    /// Problems with what a request's proximity sort measures from, placed at the document's
    /// <c>/anchor</c> — asked of a
    /// request about to run, never of a saved document, which holds no anchor and is given one by
    /// whoever runs it.
    /// </summary>
    /// <remarks>
    /// A nearest-first request with nothing to measure from is refused rather than answered in
    /// another order: an order that is not the one asked for looks exactly like the one asked for.
    /// An anchor beside any other sort is not read at all.
    /// </remarks>
    public static IReadOnlyList<FilterProblem> ProximityProblems(
        FilterDocument document, IReadOnlyDictionary<string, WorldVocabulary> vocabularies)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(vocabularies);
        if (document.Sort != SortKey.Proximity)
        {
            return [];
        }

        switch (document.Anchor)
        {
            case null:
                return [AtAnchor(FilterProblemCodes.AnchorMissing, "Nearest first needs a place to measure from.")];

            case PointAnchor point:
                return [.. PointProblems("nearest first", new PointValue(point.Longitude, point.Latitude))
                    .Select(p => AtAnchor(p.Code, p.Detail))];

            case ObjectAnchor target:
                // Only a world in the request that ranks by distance can name the object: it is the
                // world whose rows carry a position the runner may be allowed to see exactly.
                var ranks = target.World is not null
                    && target.Id != Guid.Empty
                    && document.Scope.Any(s => string.Equals(s.World, target.World, StringComparison.Ordinal))
                    && vocabularies.TryGetValue(target.World, out var vocabulary)
                    && vocabulary.Supports(SortKey.Proximity);
                return ranks ? [] : [AtAnchor(FilterProblemCodes.AnchorKind, "Nearest first cannot be measured from that kind of object.")];

            default:
                return [AtAnchor(FilterProblemCodes.AnchorKind, "Nearest first was given a place of the wrong kind.")];
        }
    }

    private static FilterProblem AtAnchor(string code, string detail) => new(code, "/anchor", null, null, null, detail);

    /// <summary>Whether a value can stand in a spatial condition at all.</summary>
    public static bool IsSpatialValue(FilterValue value) =>
        value is NumberValue or PointValue or PolygonValue or ObjectValue or SavedFilterValue;

    /// <summary>
    /// Whether any condition in the document carries a position of its own — a point or an
    /// outline — rather than naming something to look up. A saved filter refuses one.
    /// </summary>
    public static bool HoldsPosition(FilterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Scope?.Any(s => s?.Where is { } where && Any(where, v => v is PointValue or PolygonValue)) == true;
    }

    /// <summary>Every spatial condition under this node, in document order.</summary>
    public static IEnumerable<ConditionNode> ConditionsOf(FilterNode? node)
    {
        if (node is ConditionNode { Field: PositionField } condition)
        {
            yield return condition;
            yield break;
        }

        if (node is null)
        {
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var found in ConditionsOf(child))
            {
                yield return found;
            }
        }
    }

    /// <summary>
    /// The refusal for an anchor this caller cannot use — missing, unreadable or not placeable
    /// exactly — in the one code and sentence all three share.
    /// </summary>
    public static FilterRefusedException AnchorNotFound() => new(AnchorNotFoundCode, AnchorNotFoundDetail);

    /// <summary>Whether any condition under this node measures from another saved filter.</summary>
    public static bool MeasuresFromSavedFilter(FilterNode? node) =>
        node is not null && Any(node, v => v is SavedFilterValue);

    /// <summary>
    /// The outline as a geometry, or null when it is not one: rings that do not close, positions
    /// off the globe, edges that cross. Shared by the validator and by whatever compiles the
    /// outline, so the two cannot disagree about what was asked.
    /// </summary>
    public static Polygon? ToPolygon(PolygonValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Coordinates is not { Length: > 0 and <= MaxPolygonRings } rings
            || rings.Sum(r => r?.Length ?? 0) > MaxPolygonPositions)
        {
            return null;
        }

        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var built = new List<LinearRing>(rings.Length);
        foreach (var ring in rings)
        {
            if (ring is not { Length: >= 4 })
            {
                return null;
            }

            var coordinates = new Coordinate[ring.Length];
            for (var i = 0; i < ring.Length; i++)
            {
                if (ring[i] is not [var lon, var lat]
                    || !double.IsFinite(lon) || !double.IsFinite(lat)
                    || lon < -180 || lon > 180 || lat < -90 || lat > 90)
                {
                    return null;
                }

                coordinates[i] = new Coordinate(lon, lat);
            }

            if (!coordinates[0].Equals2D(coordinates[^1]))
            {
                return null;
            }

            built.Add(factory.CreateLinearRing(coordinates));
        }

        var polygon = factory.CreatePolygon(built[0], [.. built.Skip(1)]);
        return polygon.IsValid && polygon.Area > 0 ? polygon : null;
    }

    private static IReadOnlyList<(string Code, string Detail, int? Limit)> PointProblems(string field, PointValue point) =>
        double.IsFinite(point.Longitude) && double.IsFinite(point.Latitude)
        && point.Longitude is >= -180 and <= 180 && point.Latitude is >= -90 and <= 90
            ? []
            : [(FilterProblemCodes.PointOffGlobe, $"'{field}' was given a point that is not on the globe.", null)];

    private static IReadOnlyList<(string Code, string Detail, int? Limit)> PolygonProblems(string field, PolygonValue polygon) =>
        ToPolygon(polygon) is null
            ? [(FilterProblemCodes.OutlineInvalid,
                $"'{field}' was given an outline that is not a simple polygon of at most {MaxPolygonPositions} positions.",
                MaxPolygonPositions)]
            : [];

    // Only a feature can be an anchor, whatever the condition filters: it is the one world whose
    // rows carry a position the runner may be allowed to see exactly, decided by one gate.
    private static IReadOnlyList<(string Code, string Detail, int? Limit)> ObjectProblems(string field, ObjectValue target) =>
        string.Equals(target.World, AnchorWorld, StringComparison.Ordinal) && target.Id != Guid.Empty
            ? []
            : [(FilterProblemCodes.AnchorKind, $"'{field}' cannot be measured from that kind of object.", null)];

    private static bool Any(FilterNode node, Func<FilterValue, bool> test) => node switch
    {
        ConditionNode condition => condition.Values?.Any(v => v is not null && test(v)) == true,
        NotNode not => not.Of is not null && Any(not.Of, test),
        AllOfNode all => all.Of?.Any(child => child is not null && Any(child, test)) == true,
        AnyOfNode any => any.Of?.Any(child => child is not null && Any(child, test)) == true,
        _ => false,
    };
}

/// <summary>
/// A filter that was valid but cannot be run for this caller as written — an anchor they cannot
/// use. Carries the stable code and the sentence the answer gives.
/// </summary>
public sealed class FilterRefusedException(string code, string detail) : Exception(detail)
{
    public string Code { get; } = code;

    public string Detail { get; } = detail;
}
