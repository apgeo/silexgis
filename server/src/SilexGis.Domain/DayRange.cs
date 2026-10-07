// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// How a span of whole days is stored. There are two kinds of span and each has one rule, both
/// stated here and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// A span that is over the moment it is written down — a trip, a camp, an event — has a start
/// that is always known and an end that is present only when the span actually ran on past its
/// start. No end means one day. <see cref="EndForStorage"/> is that rule.
/// </para>
/// <para>
/// A span that can be written down while it is still going on — somebody's stay at a camp — needs
/// a third thing to say: that it has not ended. There is one empty value and it cannot mean both
/// "one day" and "still running", so for this kind it means only the second, and a single day
/// stores its last day like any other length does. <see cref="OpenEndForStorage"/> is that rule.
/// </para>
/// </remarks>
public static class DayRange
{
    /// <summary>
    /// The end date as it should be stored, given what a writer supplied. An end equal to the
    /// start is nothing: a stored end is the "and it ran on to" fact, so keeping it would make
    /// every single-day span read as a range of itself and force every reader to compare the two
    /// columns before it could say how long the span was.
    /// </summary>
    /// <remarks>
    /// An end that precedes its start is returned unchanged rather than silently dropped —
    /// quietly turning a mistyped date into "one day" hides it. Refusing it belongs to the
    /// validation the write goes through, with the database constraint behind that.
    /// </remarks>
    public static DateOnly? EndForStorage(DateOnly start, DateOnly? end) =>
        end == start ? null : end;

    /// <summary>
    /// The end date as it should be stored for a span that may still be going on: exactly what
    /// the writer supplied. A last day equal to the first is kept, because here an absent end is
    /// the statement "has not ended" and a one-day span folded into it would be read, by every
    /// reader, as somebody who never left.
    /// </summary>
    /// <remarks>
    /// It changes nothing, and is here all the same: the difference between the two kinds of span
    /// is precisely that this one is not normalised, and a write path that simply assigned the
    /// column would look like one that forgot to call <see cref="EndForStorage"/> — the next
    /// person to tidy it would put the ambiguity back.
    /// </remarks>
    public static DateOnly? OpenEndForStorage(DateOnly start, DateOnly? end) => end;

    /// <summary>
    /// Whether a supplied end comes before its start — a mistyped date, under either rule. The
    /// storage rules hand such an end back untouched so that it reaches something that can
    /// refuse it; this is the question that something asks.
    /// </summary>
    public static bool EndsBeforeItStarts(DateOnly start, DateOnly? end) =>
        end is { } last && last < start;
}
