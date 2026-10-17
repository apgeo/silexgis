// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What makes a filter acceptable, checked before anything touches the database.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of "untranslatable means invalid". A condition that reaches the compiler has
/// already been proved to name a field its world declares, to use an operator that field admits,
/// and to carry the number and kinds of values that operator takes — so the compiler has one job
/// and no fallback. The alternative, evaluating what will not translate in memory, is where a
/// filter's count and its rows stop agreeing.
/// </para>
/// <para>
/// The ceilings are guards rather than design. A document past any of them is not a filter
/// somebody drew; it is a document somebody generated, and the cost of compiling it lands on a
/// shared database.
/// </para>
/// </remarks>
public static class FilterValidation
{
    /// <summary>Every node in the document, of any kind. Past this it is machine-written.</summary>
    public const int MaxNodes = 200;

    /// <summary>
    /// How deeply groups may nest. Three is already more than a person can read at a glance, and
    /// the bound is what lets the compiler walk a document with ordinary recursion.
    /// </summary>
    public const int MaxDepth = 6;

    /// <summary>Values in one condition — what a multi-select can hold before it stops being one.</summary>
    public const int MaxValuesPerCondition = 200;

    /// <summary>Worlds in one request. Past a handful the answer stops being a list anybody reads.</summary>
    public const int MaxWorlds = 12;

    public const int MaxTextValueLength = 200;

    /// <summary>
    /// How large a number may be before it stops being comparable to a stored measurement.
    /// </summary>
    /// <remarks>
    /// A numeric condition arrives as a floating-point value and is compared against a fixed-point
    /// column, so the comparison is only meaningful over the range both can hold. Past this the
    /// conversion either overflows — which would be a server error for what is really a bad
    /// request — or silently drops the digits that made the value different from its neighbour. The
    /// bound is far beyond any length, depth or radius in metres that a real record carries, so it
    /// refuses only documents nobody drew.
    /// </remarks>
    public const double MaxNumberMagnitude = 1e15;

    /// <summary>
    /// Problems with the document, each with a code and the place in the document it is about.
    /// Empty means it can be compiled.
    /// </summary>
    /// <param name="vocabularies">
    /// The worlds the caller may ask about, by key. A world absent from this is refused rather than
    /// ignored: silently dropping a scope entry would answer a narrower question than the one asked
    /// while looking like it answered the whole of it.
    /// </param>
    public static IReadOnlyList<FilterProblem> Validate(
        FilterDocument document, IReadOnlyDictionary<string, WorldVocabulary> vocabularies)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(vocabularies);

        var errors = new List<FilterProblem>();

        if (document.Version > FilterDocument.CurrentVersion)
        {
            errors.Add(Whole(FilterProblemCodes.NewerVersion,
                "This filter was saved by a newer version and cannot be read here."));
            return errors;
        }

        if (!IsWellFormed(document))
        {
            errors.Add(Whole(FilterProblemCodes.Unreadable, "This filter has a part missing and cannot be read."));
            return errors;
        }

        if (document.Scope.Count == 0)
        {
            errors.Add(Whole(FilterProblemCodes.NoScope, "A filter has to say which kinds of object it is about."));
            return errors;
        }

        if (document.Scope.Count > MaxWorlds)
        {
            errors.Add(Whole(FilterProblemCodes.TooManyWorlds,
                $"A filter covers at most {MaxWorlds} kinds of object.", MaxWorlds));
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < document.Scope.Count; index++)
        {
            var scope = document.Scope[index];
            var at = $"/scope/{index}";
            if (!seen.Add(scope.World))
            {
                errors.Add(new FilterProblem(FilterProblemCodes.WorldRepeated, at, null, null, null,
                    $"'{scope.World}' is named twice; each kind of object appears once."));
                continue;
            }

            if (!vocabularies.TryGetValue(scope.World, out var vocabulary))
            {
                errors.Add(new FilterProblem(FilterProblemCodes.UnknownWorld, at, null, null, null,
                    $"There is nothing here called '{scope.World}'."));
                continue;
            }

            if (scope.Where is { } tree)
            {
                var counted = 0;
                Walk(tree, $"{at}/where", vocabulary, depth: 1, NoGuards, ref counted, errors);
            }
        }

        if (!seen.Any(world => vocabularies.TryGetValue(world, out var v) && v.Supports(document.Sort)))
        {
            // A sort no world in the scope can answer would silently become an arbitrary order,
            // and an arbitrary order paged over reads as rows appearing and vanishing.
            errors.Add(Whole(FilterProblemCodes.SortUnsupported,
                "None of the chosen kinds of object can be sorted that way."));
        }

        return errors;
    }

    private static FilterProblem Whole(string code, string detail, int? limit = null) =>
        new(code, "", null, null, limit, detail);

    private static FilterProblem About(
        ConditionNode condition, string path, string code, string detail, int? limit = null) =>
        new(code, path, condition.Field, condition.Op, limit, detail);

    private static readonly IReadOnlyList<FieldRequirement> NoGuards = [];

    /// <param name="guards">
    /// The conditions that fix a field to one value for every row this node can match: the
    /// equality conditions standing directly in an enclosing all-of group. A field that
    /// <see cref="FieldDescriptor.Requires"/> a companion is valid only where its companion is
    /// among these. Nothing else counts — a companion under an any-of could be the branch not
    /// taken, and one under a negation says the opposite.
    /// </param>
    /// <param name="path">The node's place in the document, as a JSON Pointer.</param>
    private static void Walk(
        FilterNode node,
        string path,
        WorldVocabulary vocabulary,
        int depth,
        IReadOnlyList<FieldRequirement> guards,
        ref int counted,
        List<FilterProblem> errors)
    {
        if (++counted > MaxNodes)
        {
            if (counted == MaxNodes + 1)
            {
                errors.Add(new FilterProblem(FilterProblemCodes.TooManyNodes, path, null, null, MaxNodes,
                    $"A filter holds at most {MaxNodes} conditions and groups."));
            }

            return;
        }

        if (depth > MaxDepth)
        {
            errors.Add(new FilterProblem(FilterProblemCodes.TooDeep, path, null, null, MaxDepth,
                $"Groups are nested at most {MaxDepth} deep."));
            return;
        }

        switch (node)
        {
            case ConditionNode condition:
                Check(condition, path, vocabulary, guards, errors);
                return;

            case NotNode not:
                Walk(not.Of, $"{path}/of", vocabulary, depth + 1, guards, ref counted, errors);
                return;

            case AllOfNode or AnyOfNode:
                if (node.Children.Count == 0)
                {
                    // An empty group has no honest meaning: read as "everything" it widens a
                    // filter silently, and read as "nothing" it empties one. Refusing it is the
                    // only reading that cannot surprise somebody.
                    errors.Add(new FilterProblem(FilterProblemCodes.EmptyGroup, path, null, null, null,
                        "A group needs at least one condition in it."));
                    return;
                }

                var inner = node is AllOfNode ? WithGuards(guards, node.Children) : guards;
                for (var i = 0; i < node.Children.Count; i++)
                {
                    Walk(node.Children[i], $"{path}/of/{i}", vocabulary, depth + 1, inner, ref counted, errors);
                }

                return;
        }
    }

    /// <summary>
    /// Whether every member the document's shape needs is there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deserialiser fills a member a document leaves out, or spells as null, with null rather
    /// than refusing it, so a document can parse and still have holes in it: no scope list, an
    /// entry of one with no world, a group with no list of children, a negation of nothing, a
    /// condition with no field or no values, a value with no payload. Nothing downstream can read
    /// such a document, and each road one arrives by — a link token, a request body, a stored row —
    /// asks this before anything else is asked of it, so a hole is a refusal and never a fault.
    /// </para>
    /// <para>
    /// A hole is refused rather than skipped. A group read with its missing member left out is a
    /// group with fewer conditions than the person wrote, which matches more than they asked for
    /// and says nothing about having done so.
    /// </para>
    /// <para>
    /// The walk recurses without a bound of its own: a document off the wire is no deeper than the
    /// reader's nesting limit, and its depth is judged, with a sentence, by <see cref="Validate"/>.
    /// </para>
    /// </remarks>
    public static bool IsWellFormed(FilterDocument? document) =>
        document?.Scope is { } scope
        && scope.All(s => s?.World is not null && (s.Where is null || IsWellFormed(s.Where)));

    private static bool IsWellFormed(FilterNode? node) => node switch
    {
        AllOfNode all => all.Of is { } children && children.All(IsWellFormed),
        AnyOfNode any => any.Of is { } children && children.All(IsWellFormed),
        NotNode not => IsWellFormed(not.Of),
        ConditionNode condition =>
            condition.Field is not null && condition.Values is { } values && values.All(IsWellFormed),
        _ => false,
    };

    private static bool IsWellFormed(FilterValue? value) => value switch
    {
        TextValue text => text.Value is not null,
        IdValue id => id.Value is not null,
        NumberValue or BooleanValue or InstantValue or SavedFilterValue => true,
        PointValue => true,
        PolygonValue polygon => polygon.Coordinates is { } rings
            && rings.All(ring => ring is not null && ring.All(position => position is not null)),
        ObjectValue target => target.World is not null,
        _ => false,
    };

    /// <summary>
    /// The guards in force inside an all-of group: those inherited, plus each direct member that
    /// pins a field to exactly one identity — an equality, or a one-valued (or all-equal) "in".
    /// </summary>
    private static IReadOnlyList<FieldRequirement> WithGuards(
        IReadOnlyList<FieldRequirement> inherited, IReadOnlyList<FilterNode> members)
    {
        List<FieldRequirement>? added = null;
        foreach (var member in members)
        {
            if (member is ConditionNode { Op: FilterOp.Equals or FilterOp.In } pin
                && pin.Values is { Count: > 0 } values
                && values.All(v => v is IdValue)
                && values.Select(v => ((IdValue)v).Value).Distinct(StringComparer.Ordinal).ToList() is [var only])
            {
                (added ??= [.. inherited]).Add(new FieldRequirement(pin.Field, only));
            }
        }

        return added ?? inherited;
    }

    private static void Check(
        ConditionNode condition,
        string path,
        WorldVocabulary vocabulary,
        IReadOnlyList<FieldRequirement> guards,
        List<FilterProblem> errors)
    {
        var field = vocabulary.Field(condition.Field);
        if (field is null || (field.Requires is { } companion && !guards.Contains(companion)))
        {
            // Deliberately does not name what the world does hold. A condition naming a field that
            // exists but is not offerable — somebody's address, or which cave a trip visited — must
            // read exactly like one naming a field that does not exist at all, or refusal becomes
            // the answer to a question that was not supposed to have one. A typed property asked
            // without the type that declares it is refused in the same words, for the same reason:
            // a different sentence would tell somebody probing keys that this one exists somewhere.
            // The same holds for the code and every other member: only what the caller sent is
            // echoed back.
            errors.Add(About(condition, path, FilterProblemCodes.UnknownField,
                $"'{vocabulary.World}' cannot be filtered by '{condition.Field}'."));
            return;
        }

        if (!FilterOps.For(field.Kind).Contains(condition.Op))
        {
            errors.Add(About(condition, path, FilterProblemCodes.OpUnsupported,
                $"'{condition.Field}' does not support that comparison."));
            return;
        }

        var arity = FilterOps.Arity(condition.Op);
        if (arity is { } exact && condition.Values.Count != exact)
        {
            errors.Add(
                exact == 0
                    ? About(condition, path, FilterProblemCodes.TakesNoValue,
                        $"'{condition.Field}' takes no value with that comparison.")
                    : About(condition, path, FilterProblemCodes.ValueCount,
                        $"'{condition.Field}' needs exactly {exact} value(s) with that comparison.", exact));
            return;
        }

        if (arity is null && condition.Values.Count == 0)
        {
            errors.Add(About(condition, path, FilterProblemCodes.ValueMissing,
                $"'{condition.Field}' needs at least one value."));
            return;
        }

        if (condition.Values.Count > MaxValuesPerCondition)
        {
            errors.Add(About(condition, path, FilterProblemCodes.TooManyValues,
                $"One condition takes at most {MaxValuesPerCondition} values.", MaxValuesPerCondition));
            return;
        }

        foreach (var value in condition.Values)
        {
            if (!Matches(field.Kind, value))
            {
                errors.Add(About(condition, path, FilterProblemCodes.ValueKind,
                    $"'{condition.Field}' was given a value of the wrong kind."));
                return;
            }

            if (value is TextValue text && text.Value.Length > MaxTextValueLength)
            {
                errors.Add(About(condition, path, FilterProblemCodes.TextTooLong,
                    $"A value is longer than {MaxTextValueLength} characters.", MaxTextValueLength));
                return;
            }

            if (value is NumberValue number
                && (!double.IsFinite(number.Value) || Math.Abs(number.Value) > MaxNumberMagnitude))
            {
                errors.Add(About(condition, path, FilterProblemCodes.NumberOutOfRange,
                    $"'{condition.Field}' was given a number it cannot be compared against."));
                return;
            }
        }

        if (field.Kind == FieldKind.Spatial
            && SpatialConditions.Problems(condition, vocabulary.World) is { Count: > 0 } spatial)
        {
            errors.AddRange(spatial);
            return;
        }

        if (condition.Op == FilterOp.Between
            && condition.Values is [NumberValue low, NumberValue high] && low.Value > high.Value)
        {
            errors.Add(About(condition, path, FilterProblemCodes.RangeReversed,
                $"'{condition.Field}' was given a range that ends before it starts."));
        }

        if (condition.Op == FilterOp.Between
            && condition.Values is [InstantValue from, InstantValue to] && from.Value > to.Value)
        {
            errors.Add(About(condition, path, FilterProblemCodes.RangeReversed,
                $"'{condition.Field}' was given a range that ends before it starts."));
        }
    }

    /// <summary>
    /// Whether a value is the kind its field takes.
    /// </summary>
    /// <remarks>
    /// Strict, and deliberately so where it would be convenient not to be: stored JSON tells the
    /// number 4.5 from the string "4.5", so a text value quietly accepted for a number field would
    /// produce a condition that matches nothing while looking correct — the worst way for a filter
    /// to be wrong, because there is nothing on screen to notice.
    /// </remarks>
    private static bool Matches(FieldKind kind, FilterValue value) => (kind, value) switch
    {
        (FieldKind.Text, TextValue) => true,
        (FieldKind.Number, NumberValue) => true,
        (FieldKind.Boolean, BooleanValue) => true,
        (FieldKind.Instant, InstantValue) => true,
        (FieldKind.Id, IdValue) => true,
        // A spatial condition's values are a radius and what it is measured from, or an outline;
        // which of them may stand in which place is checked with the condition as a whole.
        (FieldKind.Spatial, var spatial) => SpatialConditions.IsSpatialValue(spatial),
        _ => false,
    };
}
