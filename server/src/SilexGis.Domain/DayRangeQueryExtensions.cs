// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;

namespace SilexGis.Domain;

/// <summary>
/// The one place that decides whether a span of whole days overlaps a window of them. Every
/// listing that narrows dated rows to a period asks this and nothing else; the storage twin is
/// <see cref="DayRange"/>.
/// </summary>
/// <remarks>
/// <para>
/// The window asks whether the span overlapped it rather than whether it started inside it,
/// which is what somebody looking at a season means: a fortnight camp running across the end of
/// July is part of both halves of the summer, and a trip that ran 27 February to 2 March belongs
/// in March as much as in February. A span with no end date ran for one day, so its end is its
/// start — reading the stored end alone would drop every single-day row out of every window.
/// </para>
/// <para>
/// It lives here, with one home, because the surfaces that ask it read more than one family of
/// dated rows at once. Two copies that disagree about which rows fall in March present as "the
/// list is missing a trip" on one surface and not the other, which is the least diagnosable
/// symptom this codebase produces — and a shared test over several copies pins their agreement
/// today without preventing the next copy.
/// </para>
/// <para>
/// Both bounds are inclusive, and each is applied only when the caller supplies it, as its own
/// clause: the start column is indexed and the effective end is a computed expression that is
/// not, so folding the two into one predicate would change the plan the query provider picks.
/// A caller that resolves its own default for a bound passes the resolved day, which is never
/// absent and is therefore always applied.
/// </para>
/// </remarks>
public static class DayRangeQueryExtensions
{
    /// <summary>
    /// Narrows <paramref name="query"/> to the rows whose span of days overlaps
    /// <paramref name="from"/>..<paramref name="to"/>, either bound inclusive and either
    /// absent meaning unbounded on that side.
    /// </summary>
    /// <param name="firstDay">The column holding the span's first day, which is always known.</param>
    /// <param name="lastDay">
    /// The column holding the span's last day, which is absent when the span lasted a single day.
    /// </param>
    public static IQueryable<T> OverlappingDays<T>(
        this IQueryable<T> query,
        Expression<Func<T, DateOnly>> firstDay,
        Expression<Func<T, DateOnly?>> lastDay,
        DateOnly? from,
        DateOnly? to)
    {
        if (from is { } windowStart)
        {
            query = query.Where(RanOnOrAfter(firstDay, lastDay, windowStart));
        }

        if (to is { } windowEnd)
        {
            query = query.Where(BeganOnOrBefore(firstDay, windowEnd));
        }

        return query;
    }

    /// <summary>The span had not finished before <paramref name="day"/>.</summary>
    private static Expression<Func<T, bool>> RanOnOrAfter<T>(
        Expression<Func<T, DateOnly>> firstDay,
        Expression<Func<T, DateOnly?>> lastDay,
        DateOnly day)
    {
        var row = firstDay.Parameters[0];
        var storedEnd = Replace(lastDay.Body, lastDay.Parameters[0], row);

        // The rule is written as an ordinary lambda over the two dates and then grafted onto the
        // columns, rather than assembled node by node: the compiler checks it, it reads as the
        // sentence it is, and the day stays a captured variable the query provider turns into a
        // parameter instead of a constant it would inline into the SQL.
        Expression<Func<DateOnly, DateOnly?, bool>> ranOnOrAfter =
            (start, end) => (end ?? start) >= day;

        var body = Replace(ranOnOrAfter.Body, ranOnOrAfter.Parameters[0], firstDay.Body);
        body = Replace(body, ranOnOrAfter.Parameters[1], storedEnd);
        return Expression.Lambda<Func<T, bool>>(body, row);
    }

    /// <summary>The span had begun by <paramref name="day"/>.</summary>
    private static Expression<Func<T, bool>> BeganOnOrBefore<T>(
        Expression<Func<T, DateOnly>> firstDay,
        DateOnly day)
    {
        Expression<Func<DateOnly, bool>> beganOnOrBefore = start => start <= day;
        return Expression.Lambda<Func<T, bool>>(
            Replace(beganOnOrBefore.Body, beganOnOrBefore.Parameters[0], firstDay.Body),
            firstDay.Parameters[0]);
    }

    private static Expression Replace(
        Expression body, ParameterExpression parameter, Expression replacement) =>
        new ParameterReplacer(parameter, replacement).Visit(body);

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }
}
