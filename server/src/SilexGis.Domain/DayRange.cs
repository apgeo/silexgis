// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// How a span of whole days is stored: a start that is always known and an end that is present
/// only when the span actually ran on past its start.
/// </summary>
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
}
