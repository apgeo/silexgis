// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Filters;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// What each kind of condition means, once, for every world.
/// </summary>
/// <remarks>
/// <para>
/// A world differs from another in which fields it has, not in what "contains" means. Whether a
/// comparison folds diacritics, whether a range includes both ends, what an empty value list
/// matches — those are answers this application gives once. A second world with its own copy would
/// not be a second implementation for long; it would be a second implementation that drifted, and
/// the drift would show up as one screen finding a cave another could not.
/// </para>
/// <para>
/// Each takes the column as an expression and returns a predicate over the same parameter, so a
/// world spells out only the part that is genuinely its own: which column a field key names.
/// </para>
/// </remarks>
internal static class FilterLeaves
{
    /// <summary>
    /// Text, compared without regard to case or diacritics.
    /// </summary>
    /// <remarks>
    /// A handheld writes "Pestera" and a person writes "Peștera"; a comparison that told them apart
    /// would find neither. Spelled as the existing list screens already spell it, so moving a screen
    /// onto this changes what can be asked without changing what the database does about it.
    /// </remarks>
    public static Expression<Func<T, bool>> Text<T>(
        ConditionNode condition, Expression<Func<T, string?>> selector)
    {
        var parameter = selector.Parameters[0];
        var column = selector.Body;
        var empty = Expression.Constant(string.Empty);
        var nothing = Expression.Constant(null, typeof(string));

        if (condition.Op is FilterOp.IsEmpty or FilterOp.IsNotEmpty)
        {
            var blank = Expression.OrElse(
                Expression.Equal(column, nothing), Expression.Equal(column, empty));
            return Expression.Lambda<Func<T, bool>>(
                condition.Op == FilterOp.IsEmpty ? blank : Expression.Not(blank), parameter);
        }

        var text = ((TextValue)condition.Values[0]).Value;
        var pattern = condition.Op switch
        {
            FilterOp.Contains => $"%{text}%",
            FilterOp.StartsWith => $"{text}%",
            _ => text,
        };

        // Written as a lambda over one string and then grafted onto the column, rather than built
        // by naming the provider's methods through reflection. The compiler checks it, so a rename
        // upstream is a build error here instead of a request that fails at run time.
        Expression<Func<string, bool>> matches = value =>
            EF.Functions.ILike(EF.Functions.Unaccent(value), EF.Functions.Unaccent(pattern));

        var applied = Substitution.Apply(matches.Body, matches.Parameters[0], column);
        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(Expression.NotEqual(column, nothing), applied), parameter);
    }

    /// <summary>
    /// Identity values matched one at a time and joined with OR, through a per-value matcher.
    /// </summary>
    /// <remarks>
    /// An empty list matches nothing rather than everything. That is the honest reading of "any of
    /// none", and the alternative is the dangerous one: a multi-select somebody cleared would
    /// silently widen the answer instead of narrowing it to nothing.
    /// </remarks>
    public static Expression<Func<T, bool>> Ids<T>(
        ConditionNode condition, Func<string, Expression<Func<T, bool>>> matcher)
    {
        var values = condition.Values.OfType<IdValue>().Select(v => v.Value).ToList();
        return values.Count == 0
            ? _ => false
            : values.Select(matcher).Aggregate((left, right) => left.Or(right));
    }

    /// <summary>An enum column matched by name, matching nothing when the name is not one.</summary>
    public static Func<string, Expression<Func<T, bool>>> Enum<T, TEnum>(
        Expression<Func<T, TEnum>> selector)
        where TEnum : struct, Enum =>
        raw =>
        {
            if (!System.Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value)
                || !System.Enum.IsDefined(value))
            {
                // Validation checks shape, not membership of every enum name, so an unknown name
                // lands here. Matching nothing is honest: it is a value no row has.
                return _ => false;
            }

            return Expression.Lambda<Func<T, bool>>(
                Expression.Equal(selector.Body, Expression.Constant(value)), selector.Parameters);
        };

    /// <summary>A nullable enum column matched by name.</summary>
    /// <remarks>
    /// Emptiness is answered here rather than by the caller, because "has no type" is a question
    /// people genuinely ask of a half-filled record and there is nothing to match it against.
    /// </remarks>
    public static Expression<Func<T, bool>> NullableEnum<T, TEnum>(
        ConditionNode condition, Expression<Func<T, TEnum?>> selector)
        where TEnum : struct, Enum
    {
        if (condition.Op is FilterOp.IsEmpty or FilterOp.IsNotEmpty)
        {
            return NullCheck(selector, condition.Op == FilterOp.IsNotEmpty);
        }

        return Ids<T>(condition, raw =>
        {
            if (!System.Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value)
                || !System.Enum.IsDefined(value))
            {
                return _ => false;
            }

            return Expression.Lambda<Func<T, bool>>(
                Expression.Equal(
                    selector.Body, Expression.Constant(value, typeof(TEnum?))),
                selector.Parameters);
        });
    }

    /// <summary>
    /// A calendar date — the day a trip happened, rather than the instant a row was written.
    /// </summary>
    /// <remarks>
    /// Kept apart from the timestamp leaf because the two are not the same question. A trip on the
    /// third of May happened on the third of May in the valley it happened in; reading it as an
    /// instant would put an evening trip on the fourth for anybody east of here, and a filter for
    /// "trips in May" would quietly lose the last one.
    /// </remarks>
    public static Expression<Func<T, bool>> Date<T>(
        ConditionNode condition, Expression<Func<T, DateOnly>> selector)
    {
        if (condition.Op is FilterOp.IsEmpty)
        {
            return _ => false;
        }

        if (condition.Op is FilterOp.IsNotEmpty)
        {
            return _ => true;
        }

        var days = condition.Values.OfType<InstantValue>()
            .Select(v => DateOnly.FromDateTime(v.Value.UtcDateTime))
            .ToList();

        var body = condition.Op switch
        {
            FilterOp.LessThan => Expression.LessThan(selector.Body, Expression.Constant(days[0])),
            FilterOp.GreaterThan => Expression.GreaterThan(selector.Body, Expression.Constant(days[0])),
            FilterOp.Between => Expression.AndAlso(
                Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(days[0])),
                Expression.LessThanOrEqual(selector.Body, Expression.Constant(days[1]))),
            _ => Expression.Equal(selector.Body, Expression.Constant(days[0])),
        };

        return Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }

    /// <summary>
    /// An enum column the schema never leaves empty.
    /// </summary>
    /// <remarks>
    /// The emptiness pair is answered here rather than falling through to the identity leaf, which
    /// reads "no values given" as "matches nothing" — correct for a cleared multi-select, and wrong
    /// for these two operators, where no values is what they mean. Left as it was, a vocabulary that
    /// offers "kind is not empty" answered it with nothing at all, and its negation with everything.
    /// </remarks>
    public static Expression<Func<T, bool>> EnumField<T, TEnum>(
        ConditionNode condition, Expression<Func<T, TEnum>> selector)
        where TEnum : struct, Enum
    {
        if (condition.Op is FilterOp.IsEmpty)
        {
            return _ => false;
        }

        if (condition.Op is FilterOp.IsNotEmpty)
        {
            return _ => true;
        }

        return Ids(condition, Enum(selector));
    }

    public static Expression<Func<T, bool>> Longs<T>(
        ConditionNode condition, Expression<Func<T, long?>> selector) =>
        Nullable<T, long>(condition, selector, v => long.TryParse(v, out var p) ? p : null);

    public static Expression<Func<T, bool>> Guids<T>(
        ConditionNode condition, Expression<Func<T, Guid?>> selector) =>
        Nullable<T, Guid>(condition, selector, v => Guid.TryParse(v, out var p) ? p : null);

    public static Expression<Func<T, bool>> Boolean<T>(
        ConditionNode condition, Expression<Func<T, bool>> selector)
    {
        var wanted = ((BooleanValue)condition.Values[0]).Value;
        return Expression.Lambda<Func<T, bool>>(
            Expression.Equal(selector.Body, Expression.Constant(wanted)), selector.Parameters);
    }

    /// <summary>A timestamp column that is never null.</summary>
    public static Expression<Func<T, bool>> Instant<T>(
        ConditionNode condition, Expression<Func<T, DateTimeOffset>> selector)
    {
        // Emptiness has a definite answer for a column the schema never leaves null, and giving it
        // is better than producing an expression the provider cannot translate.
        if (condition.Op is FilterOp.IsEmpty)
        {
            return _ => false;
        }

        if (condition.Op is FilterOp.IsNotEmpty)
        {
            return _ => true;
        }

        var values = condition.Values.OfType<InstantValue>().Select(v => v.Value).ToList();
        var body = condition.Op switch
        {
            FilterOp.LessThan => Expression.LessThan(selector.Body, Expression.Constant(values[0])),
            FilterOp.GreaterThan => Expression.GreaterThan(selector.Body, Expression.Constant(values[0])),
            // Both ends included, which is what somebody picking two dates means by "between".
            FilterOp.Between => Expression.AndAlso(
                Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(values[0])),
                Expression.LessThanOrEqual(selector.Body, Expression.Constant(values[1]))),
            _ => Expression.Equal(selector.Body, Expression.Constant(values[0])),
        };

        return Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }

    /// <summary>A timestamp column that may be null — a trip's date, a survey's.</summary>
    public static Expression<Func<T, bool>> NullableInstant<T>(
        ConditionNode condition, Expression<Func<T, DateTimeOffset?>> selector)
    {
        if (condition.Op is FilterOp.IsEmpty or FilterOp.IsNotEmpty)
        {
            return NullCheck(selector, condition.Op == FilterOp.IsNotEmpty);
        }

        var values = condition.Values.OfType<InstantValue>().Select(v => v.Value).ToList();
        var column = Expression.Convert(selector.Body, typeof(DateTimeOffset));
        var present = Expression.NotEqual(
            selector.Body, Expression.Constant(null, typeof(DateTimeOffset?)));

        var body = condition.Op switch
        {
            FilterOp.LessThan => Expression.LessThan(column, Expression.Constant(values[0])),
            FilterOp.GreaterThan => Expression.GreaterThan(column, Expression.Constant(values[0])),
            FilterOp.Between => Expression.AndAlso(
                Expression.GreaterThanOrEqual(column, Expression.Constant(values[0])),
                Expression.LessThanOrEqual(column, Expression.Constant(values[1]))),
            _ => Expression.Equal(column, Expression.Constant(values[0])),
        };

        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(present, body), selector.Parameters);
    }

    /// <summary>
    /// A measured quantity — a length, a depth — held as a fixed-point column that may be null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value arrives as a floating-point number, because that is what a browser sends and what
    /// the stored document holds, and it is converted here on the <em>constant</em> side. Casting
    /// the column instead would read the same and behave differently in the two ways that matter:
    /// the comparison would stop being served by any index over the column, and it would round the
    /// stored value to the caller's type rather than the caller's value to the stored one — so a
    /// length recorded as 1234.5 would answer differently depending on which side was widened.
    /// </para>
    /// <para>
    /// The conversion is safe because a number past what a fixed-point column can hold is refused
    /// before it reaches here; overflowing it would turn a bad request into a server error.
    /// </para>
    /// <para>
    /// Both ends of a range are included, which is what somebody picking a low and a high means by
    /// "between", and is the same reading the timestamp leaves give it.
    /// </para>
    /// </remarks>
    public static Expression<Func<T, bool>> Decimals<T>(
        ConditionNode condition, Expression<Func<T, decimal?>> selector)
    {
        if (condition.Op is FilterOp.IsEmpty or FilterOp.IsNotEmpty)
        {
            return NullCheck(selector, condition.Op == FilterOp.IsNotEmpty);
        }

        var values = condition.Values.OfType<NumberValue>()
            .Select(v => (decimal)v.Value)
            .ToList();

        // Unwrapping the nullable rather than casting the column: the provider reads this as the
        // column itself, guarded by the presence test beside it, so nothing is computed per row.
        var column = Expression.Convert(selector.Body, typeof(decimal));
        var present = Expression.NotEqual(
            selector.Body, Expression.Constant(null, typeof(decimal?)));

        var body = condition.Op switch
        {
            FilterOp.LessThan => Expression.LessThan(column, Expression.Constant(values[0])),
            FilterOp.GreaterThan => Expression.GreaterThan(column, Expression.Constant(values[0])),
            FilterOp.Between => Expression.AndAlso(
                Expression.GreaterThanOrEqual(column, Expression.Constant(values[0])),
                Expression.LessThanOrEqual(column, Expression.Constant(values[1]))),
            _ => Expression.Equal(column, Expression.Constant(values[0])),
        };

        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(present, body), selector.Parameters);
    }

    public static Expression<Func<T, bool>> NullCheck<T, TValue>(
        Expression<Func<T, TValue>> selector, bool wantPresent)
    {
        var nothing = Expression.Constant(null, typeof(TValue));
        return Expression.Lambda<Func<T, bool>>(
            wantPresent
                ? Expression.NotEqual(selector.Body, nothing)
                : Expression.Equal(selector.Body, nothing),
            selector.Parameters);
    }

    private static Expression<Func<T, bool>> Nullable<T, TValue>(
        ConditionNode condition,
        Expression<Func<T, TValue?>> selector,
        Func<string, TValue?> parse)
        where TValue : struct
    {
        if (condition.Op is FilterOp.IsEmpty or FilterOp.IsNotEmpty)
        {
            return NullCheck(selector, condition.Op == FilterOp.IsNotEmpty);
        }

        var wanted = condition.Values.OfType<IdValue>()
            .Select(v => parse(v.Value))
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToArray();

        var contains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(TValue)],
            Expression.Constant(wanted),
            Expression.Convert(selector.Body, typeof(TValue)));
        var present = Expression.NotEqual(
            selector.Body, Expression.Constant(null, typeof(TValue?)));

        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(present, contains), selector.Parameters);
    }
}

/// <summary>
/// Combining predicates over the same parameter.
/// </summary>
/// <remarks>
/// Written out rather than taken from a library because a filter tree composes predicates in
/// exactly three ways and each is four lines. The parameter rebinding is the whole content: two
/// lambdas built separately have different parameter instances, and combining their bodies without
/// rewriting one would produce an expression the provider cannot translate.
/// </remarks>
internal static class PredicateComposition
{
    public static Expression<Func<T, bool>> And<T>(
        this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Compose(left, right, Expression.AndAlso);

    public static Expression<Func<T, bool>> Or<T>(
        this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Compose(left, right, Expression.OrElse);

    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> inner) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(inner.Body), inner.Parameters);

    private static Expression<Func<T, bool>> Compose<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> join)
    {
        var parameter = left.Parameters[0];
        var rebound = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(join(left.Body, rebound), parameter);
    }

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>Grafting a lambda written about one value onto the column it is asked about.</summary>
internal static class Substitution
{
    public static Expression Apply(Expression body, ParameterExpression from, Expression to) =>
        new Visitor(from, to).Visit(body)
        ?? throw new InvalidOperationException("A lambda body cannot visit to nothing.");

    private sealed class Visitor(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) =>
            node == from ? to : base.Visit(node);
    }
}
