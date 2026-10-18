// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// A condition on a feature type's own typed property, as the ids of the rows it matches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Typed, and never a fault.</b> A property is read out of the stored document by what it is:
/// a number is compared as a number only where the stored value is a JSON number, a text only where
/// it is a JSON string, a date only where it is a JSON string holding a well-formed ISO date or
/// date-time, a boolean only where it is a JSON boolean. A row holding something else
/// under the key — a label typed where a depth belonged, written before the schema said otherwise —
/// simply does not match. The guard is a <c>CASE</c>, not an <c>AND</c>: PostgreSQL does not promise
/// to evaluate the conjuncts of a <c>WHERE</c> in order, so <c>typeof = 'number' AND value::numeric
/// &lt; 30</c> may cast a string first and fail the whole request; a <c>CASE</c> arm is evaluated only
/// when its test holds.
/// </para>
/// <para>
/// <b>Scoped to its type.</b> The type code travels in the field key and is joined here, so a key
/// another type happens to share is never tested: the condition answers only about rows of the
/// type whose schema declared it, on its own as well as beside the type condition validation
/// insists on.
/// </para>
/// <para>
/// <b>Composed, not run.</b> The answer is a query fragment the caller's predicate holds as an
/// <c>IN</c> subquery, so it runs inside the one statement that also applies the visibility walk
/// around it — it never returns rows, and the set it names is only ever intersected with what the
/// caller may already see. No index serves it; the rows it scans are one type's, and a GIN or
/// expression index over the column is the step to take if that stops being enough.
/// </para>
/// </remarks>
public static class FeaturePropertySql
{
    private const string Head = """
        SELECT f.id AS "Value"
        FROM features f
        JOIN feature_types t ON t.id = f.feature_type_id
        WHERE f.deleted_at IS NULL AND t.code = {0} AND
        """;

    // {1} is always the key. Each guard reads the value only when the stored JSON type agrees.
    private const string Number =
        "(CASE WHEN jsonb_typeof(f.properties -> {1}) = 'number' THEN (f.properties ->> {1})::numeric END)";

    private const string Text =
        "(CASE WHEN jsonb_typeof(f.properties -> {1}) = 'string' THEN f.properties ->> {1} END)";

    // A date or date-time is stored as a JSON string, so its guard is the string's shape: an ISO
    // calendar date, or an ISO date-time that names its offset (a time without one names no single
    // moment). The shape alone does not make a cast safe — 2025-02-30 has it — so
    // pg_input_is_valid, which answers rather than raises, has the last word before the cast.
    //
    // The two are different questions and are asked differently. A date-time is a moment, compared
    // with the moment the bound names. A date with no time is a day, compared with the calendar day
    // the bound names at the offset it was written with — the editor sends the first or last moment
    // of the day the reader picked in their own zone, so "at most the 3rd" picked in Tonga takes a
    // property dated the 3rd in, which no single moment of the 3rd could promise for every zone.
    // This text is formatted once before it joins the query — {0} becomes the date arm's comparison
    // and {1} the date-time arm's — so every brace meant for the query's own format string is
    // doubled once more than elsewhere in this file.
    private const string Dated = """
        (CASE
          WHEN jsonb_typeof(f.properties -> {{1}}) <> 'string' THEN NULL
          WHEN (f.properties ->> {{1}}) ~ '^\d{{{{4}}}}-\d{{{{2}}}}-\d{{{{2}}}}$'
               AND pg_input_is_valid(f.properties ->> {{1}}, 'date')
            THEN {0}
          WHEN (f.properties ->> {{1}}) ~ '^\d{{{{4}}}}-\d{{{{2}}}}-\d{{{{2}}}}[Tt ]\d{{{{2}}}}:\d{{{{2}}}}(:\d{{{{2}}}}(\.\d+)?)?([Zz]|[+-]\d{{{{2}}}}(:?\d{{{{2}}}})?)$'
               AND pg_input_is_valid(f.properties ->> {{1}}, 'timestamptz')
            THEN {1}
        END)
        """;

    private const string Day = "(f.properties ->> {1})::date";

    private const string Moment = "(f.properties ->> {1})::timestamptz";

    /// <summary>The ids of the rows of type <paramref name="typeCode"/> the condition matches.</summary>
    public static IQueryable<Guid> Matching(
        SilexGisDbContext db, string typeCode, string key, ConditionNode condition)
    {
        var (predicate, values) = Predicate(condition);
        object[] arguments = [typeCode, key, .. values];
        return db.Database.SqlQuery<Guid>(FormattableStringFactory.Create(Head + " " + predicate, arguments));
    }

    /// <summary>
    /// The SQL test for one condition, from a closed set of spellings; every value it compares
    /// against is a parameter from <c>{2}</c> on.
    /// </summary>
    private static (string Sql, object[] Values) Predicate(ConditionNode condition) =>
        (condition.Op, condition.Values) switch
        {
            // Present means a value is recorded: the key is there, it is not JSON null, and it is
            // not the empty string a cleared text box leaves behind.
            (FilterOp.IsNotEmpty, _) => (Present, []),
            (FilterOp.IsEmpty, _) => ($"NOT {Present}", []),

            (FilterOp.Equals, [NumberValue n]) => ($"{Number} = {{2}}", [ToNumeric(n)]),
            (FilterOp.LessThan, [NumberValue n]) => ($"{Number} < {{2}}", [ToNumeric(n)]),
            (FilterOp.LessOrEqual, [NumberValue n]) => ($"{Number} <= {{2}}", [ToNumeric(n)]),
            (FilterOp.GreaterThan, [NumberValue n]) => ($"{Number} > {{2}}", [ToNumeric(n)]),
            (FilterOp.GreaterOrEqual, [NumberValue n]) => ($"{Number} >= {{2}}", [ToNumeric(n)]),
            (FilterOp.Between, [NumberValue low, NumberValue high]) =>
                ($"{Number} BETWEEN {{2}} AND {{3}}", [ToNumeric(low), ToNumeric(high)]),

            // A day arm's bound at {2} (and {3}), the moment arm's after it.
            (FilterOp.LessThan, [InstantValue i]) => Compared("<", i),
            (FilterOp.LessOrEqual, [InstantValue i]) => Compared("<=", i),
            (FilterOp.GreaterThan, [InstantValue i]) => Compared(">", i),
            (FilterOp.GreaterOrEqual, [InstantValue i]) => Compared(">=", i),
            (FilterOp.Between, [InstantValue low, InstantValue high]) => (
                string.Format(CultureInfo.InvariantCulture, Dated, $"{Day} BETWEEN {{2}} AND {{3}}", $"{Moment} BETWEEN {{4}} AND {{5}}"),
                [FilterLeaves.DayNamed(low), FilterLeaves.DayNamed(high), ToUtc(low), ToUtc(high)]),

            // Text is matched the way every other text field is: accent- and case-blind, with the
            // value used as the pattern it already is on those fields.
            (FilterOp.Equals, [TextValue s]) => (TextLike, [s.Value]),
            (FilterOp.Contains, [TextValue s]) => (TextLike, [$"%{s.Value}%"]),
            (FilterOp.StartsWith, [TextValue s]) => (TextLike, [$"{s.Value}%"]),

            (FilterOp.Equals, [BooleanValue b]) =>
                (b.Value ? "f.properties -> {1} = 'true'::jsonb" : "f.properties -> {1} = 'false'::jsonb", []),

            _ => throw new InvalidOperationException(
                $"No typed-property arm for {condition.Op} over {condition.Values.Count} value(s). A "
                + "property field the vocabulary offers must have one here."),
        };

    private static (string Sql, object[] Values) Compared(string comparison, InstantValue bound) => (
        string.Format(CultureInfo.InvariantCulture, Dated, $"{Day} {comparison} {{2}}", $"{Moment} {comparison} {{3}}"),
        [FilterLeaves.DayNamed(bound), ToUtc(bound)]);

    private const string Present =
        "(COALESCE(jsonb_typeof(f.properties -> {1}), 'null') <> 'null' AND f.properties -> {1} <> '\"\"'::jsonb)";

    private const string TextLike = $"unaccent({Text}) ILIKE unaccent({{2}})";

    // The magnitude bound validation applies keeps this conversion exact for anything a person typed.
    private static decimal ToNumeric(NumberValue value) => (decimal)value.Value;

    // The provider writes a timestamptz parameter only from a UTC offset.
    private static DateTimeOffset ToUtc(InstantValue value) => value.Value.ToUniversalTime();
}
