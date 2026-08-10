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
    /// Problems with the document, as sentences. Empty means it can be compiled.
    /// </summary>
    /// <param name="vocabularies">
    /// The worlds the caller may ask about, by key. A world absent from this is refused rather than
    /// ignored: silently dropping a scope entry would answer a narrower question than the one asked
    /// while looking like it answered the whole of it.
    /// </param>
    public static IReadOnlyList<string> Validate(
        FilterDocument document, IReadOnlyDictionary<string, WorldVocabulary> vocabularies)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(vocabularies);

        var errors = new List<string>();

        if (document.Version > FilterDocument.CurrentVersion)
        {
            errors.Add("This filter was saved by a newer version and cannot be read here.");
            return errors;
        }

        if (document.Scope.Count == 0)
        {
            errors.Add("A filter has to say which kinds of object it is about.");
            return errors;
        }

        if (document.Scope.Count > MaxWorlds)
        {
            errors.Add($"A filter covers at most {MaxWorlds} kinds of object.");
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in document.Scope)
        {
            if (!seen.Add(scope.World))
            {
                errors.Add($"'{scope.World}' is named twice; each kind of object appears once.");
                continue;
            }

            if (!vocabularies.TryGetValue(scope.World, out var vocabulary))
            {
                errors.Add($"There is nothing here called '{scope.World}'.");
                continue;
            }

            if (scope.Where is { } tree)
            {
                var counted = 0;
                Walk(tree, vocabulary, depth: 1, ref counted, errors);
            }
        }

        if (!seen.Any(world => vocabularies.TryGetValue(world, out var v) && v.Supports(document.Sort)))
        {
            // A sort no world in the scope can answer would silently become an arbitrary order,
            // and an arbitrary order paged over reads as rows appearing and vanishing.
            errors.Add("None of the chosen kinds of object can be sorted that way.");
        }

        return errors;
    }

    private static void Walk(
        FilterNode node, WorldVocabulary vocabulary, int depth, ref int counted, List<string> errors)
    {
        if (++counted > MaxNodes)
        {
            if (counted == MaxNodes + 1)
            {
                errors.Add($"A filter holds at most {MaxNodes} conditions and groups.");
            }

            return;
        }

        if (depth > MaxDepth)
        {
            errors.Add($"Groups are nested at most {MaxDepth} deep.");
            return;
        }

        switch (node)
        {
            case ConditionNode condition:
                Check(condition, vocabulary, errors);
                return;

            case NotNode not:
                if (not.Of is null)
                {
                    errors.Add("A negation has nothing to negate.");
                    return;
                }

                Walk(not.Of, vocabulary, depth + 1, ref counted, errors);
                return;

            case AllOfNode or AnyOfNode:
                if (node.Children.Count == 0)
                {
                    // An empty group has no honest meaning: read as "everything" it widens a
                    // filter silently, and read as "nothing" it empties one. Refusing it is the
                    // only reading that cannot surprise somebody.
                    errors.Add("A group needs at least one condition in it.");
                    return;
                }

                foreach (var child in node.Children)
                {
                    if (child is null)
                    {
                        // A hole in a group, which arrives from a document whose list of children
                        // contains a literal null. Refused rather than skipped: skipped, it becomes
                        // a group with fewer conditions than the person wrote, which matches more
                        // than they asked for and says nothing about having done so.
                        errors.Add("A group has a condition missing from it.");
                        continue;
                    }

                    Walk(child, vocabulary, depth + 1, ref counted, errors);
                }

                return;
        }
    }

    private static void Check(ConditionNode condition, WorldVocabulary vocabulary, List<string> errors)
    {
        var field = vocabulary.Field(condition.Field);
        if (field is null)
        {
            // Deliberately does not name what the world does hold. A condition naming a field that
            // exists but is not offerable — somebody's address, or which cave a trip visited — must
            // read exactly like one naming a field that does not exist at all, or refusal becomes
            // the answer to a question that was not supposed to have one.
            errors.Add($"'{vocabulary.World}' cannot be filtered by '{condition.Field}'.");
            return;
        }

        if (!FilterOps.For(field.Kind).Contains(condition.Op))
        {
            errors.Add($"'{condition.Field}' does not support that comparison.");
            return;
        }

        var arity = FilterOps.Arity(condition.Op);
        if (arity is { } exact && condition.Values.Count != exact)
        {
            errors.Add(
                exact == 0
                    ? $"'{condition.Field}' takes no value with that comparison."
                    : $"'{condition.Field}' needs exactly {exact} value(s) with that comparison.");
            return;
        }

        if (arity is null && condition.Values.Count == 0)
        {
            errors.Add($"'{condition.Field}' needs at least one value.");
            return;
        }

        if (condition.Values.Count > MaxValuesPerCondition)
        {
            errors.Add($"One condition takes at most {MaxValuesPerCondition} values.");
            return;
        }

        foreach (var value in condition.Values)
        {
            if (!Matches(field.Kind, value))
            {
                errors.Add($"'{condition.Field}' was given a value of the wrong kind.");
                return;
            }

            if (value is TextValue text && text.Value.Length > MaxTextValueLength)
            {
                errors.Add($"A value is longer than {MaxTextValueLength} characters.");
                return;
            }
        }

        if (condition.Op == FilterOp.Between
            && condition.Values is [NumberValue low, NumberValue high] && low.Value > high.Value)
        {
            errors.Add($"'{condition.Field}' was given a range that ends before it starts.");
        }

        if (condition.Op == FilterOp.Between
            && condition.Values is [InstantValue from, InstantValue to] && from.Value > to.Value)
        {
            errors.Add($"'{condition.Field}' was given a range that ends before it starts.");
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
        // A spatial condition's values are the anchor and the radius, and both are resolved before
        // compilation; what arrives here is a number (metres) or an identity (the anchor object).
        (FieldKind.Spatial, NumberValue or IdValue) => true,
        _ => false,
    };
}
