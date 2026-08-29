// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Events;

/// <summary>
/// How often a repeating event comes round. A closed list and a small one: it is read once, by
/// the generator that works out which days a series falls on, and never stored.
/// </summary>
/// <remarks>
/// <para>
/// It is deliberately not a recurrence grammar. A grammar would have to be parsed, and something
/// that parses a stored rule is the first half of an expander — which is the design this one
/// replaces. Four words cover what a club actually runs: the weekly evening, the fortnightly one,
/// the monthly committee and the run of consecutive days a course occupies. Anything else is
/// written by hand as the rows it is, which is what the rest of the application already knows how
/// to edit.
/// </para>
/// <para>
/// The numbers are not a schema contract, because no column holds one: the value is consumed by
/// the create that produced the rows and is gone by the time they are saved. What survives the
/// generation is ordinary rows and the words their author used to describe the pattern.
/// </para>
/// </remarks>
public enum EventRecurrenceFrequency : short
{
    /// <summary>Every day — the consecutive days a course or a working week occupies.</summary>
    Daily = 0,

    /// <summary>The same weekday every week.</summary>
    Weekly = 1,

    /// <summary>The same weekday every other week.</summary>
    Fortnightly = 2,

    /// <summary>The same day of the month, every month.</summary>
    Monthly = 3,
}

/// <summary>
/// The days a series would fall on, or the reason the request for it was refused.
/// </summary>
/// <remarks>
/// One type for both answers rather than an exception for the refusals, because every refusal
/// here is something a person typed rather than something that went wrong: a horizon too far
/// away, a repetition that repeats nothing, a series with no end at all. Each carries the stable
/// code the caller is told, so the words a surface shows are chosen from the code and never
/// parsed out of a sentence.
/// </remarks>
public sealed record EventSeriesPlan
{
    /// <summary>The first day of each occurrence, in order, first occurrence included.</summary>
    public IReadOnlyList<DateOnly> Days { get; init; } = [];

    public string? RefusalCode { get; init; }

    public string? RefusalDetail { get; init; }

    public bool Refused => RefusalCode is not null;
}

/// <summary>
/// Works out which days a repeating event falls on, and refuses to work out too many.
/// </summary>
/// <remarks>
/// <para>
/// A repeating event is not a rule the application evaluates later — it is the rows it becomes,
/// written once when somebody asks for them. That is why this is a generator and not an expander:
/// it runs at the moment of creation, hands back a list of days, and is never consulted again.
/// Everything downstream — who answered, who was reminded, which page a row lands on, what the
/// audit trail hangs the answers off — goes on keying on one row's identifier, unchanged, because
/// each occurrence is one row.
/// </para>
/// <para>
/// <b>It refuses to be unbounded, and that is the point of it.</b> A generator with no ceiling is
/// how one form submission fills a table: "every day, for ever" is four words to type and there
/// is nothing in a database that pushes back. So a request must state where the repetition stops
/// — a number of occurrences, a last day, or both — and both ceilings below are applied whichever
/// of the two was given. A request that asks for more than they allow is <i>refused rather than
/// quietly trimmed</i>: a trimmed series is a lie about what somebody asked for, and they find out
/// months later when the evening they expected is not there.
/// </para>
/// </remarks>
public static class EventRecurrence
{
    /// <summary>
    /// The most occurrences one request may produce. Two years of a weekly club night, which is
    /// the longest thing anybody plans in one sitting; a committee that meets monthly gets more
    /// than eight years out of it.
    /// </summary>
    public const int MaxOccurrences = 104;

    /// <summary>
    /// How far ahead the last occurrence may fall, in days from the first. Two years, matching
    /// the count above rather than being chosen separately: the two ceilings answer different
    /// questions — how many rows, and how far into a future nobody can foresee — and a series
    /// has to satisfy both.
    /// </summary>
    public const int MaxHorizonDays = 730;

    /// <summary>Neither a number of occurrences nor a last day: nothing says where it stops.</summary>
    public const string UnboundedCode = "event.recurrence_unbounded";

    /// <summary>What was asked for happens once, so there is no series to make.</summary>
    public const string NotRepeatingCode = "event.recurrence_not_repeating";

    /// <summary>More occurrences than one request may produce.</summary>
    public const string TooManyCode = "event.recurrence_too_many";

    /// <summary>An occurrence further ahead than a series is written for.</summary>
    public const string HorizonTooFarCode = "event.recurrence_horizon_too_far";

    /// <summary>A repetition this application has no way to step by.</summary>
    public const string FrequencyInvalidCode = "event.recurrence_frequency_invalid";

    /// <summary>
    /// The words behind <see cref="HorizonTooFarCode"/>, in one place because two paths refuse
    /// with it: a repetition that walks past the horizon, and one that walks off the end of the
    /// calendar altogether.
    /// </summary>
    private static readonly string HorizonDetail =
        $"A series is written at most {MaxHorizonDays} days ahead of its first day, and this "
        + "repetition would run past that. Stop it sooner, or make it come round less often.";

    /// <summary>
    /// The days the occurrences of a series fall on, given the day the first one is and where the
    /// repetition stops, or a refusal saying why no series was made.
    /// </summary>
    /// <param name="start">The first occurrence's day. It is always the first day of the list.</param>
    /// <param name="frequency">How often it comes round.</param>
    /// <param name="count">
    /// How many occurrences in total, the first included, or null when the last day is what bounds
    /// it instead.
    /// </param>
    /// <param name="lastDay">
    /// The day past which no occurrence falls, or null when the count is what bounds it instead.
    /// An occurrence landing exactly on it is kept: somebody naming a last day means "up to and
    /// including".
    /// </param>
    public static EventSeriesPlan Plan(
        DateOnly start, EventRecurrenceFrequency frequency, int? count, DateOnly? lastDay)
    {
        // Checked here rather than left to the step below, because a value that is not one of the
        // four arrives over the wire and must be refused, never thrown at. The step's own default
        // arm is then genuinely unreachable and says so.
        if (!Enum.IsDefined(frequency))
        {
            return Refuse(FrequencyInvalidCode, "That is not a repetition this calendar knows.");
        }

        if (count is null && lastDay is null)
        {
            return Refuse(
                UnboundedCode,
                "A repeating event has to say where it stops — how many times it happens, or the "
                + "last day it may fall on.");
        }

        if (count is { } wanted && wanted < 2)
        {
            return Refuse(
                NotRepeatingCode,
                "A single occurrence is an ordinary event rather than a repeating one.");
        }

        if (count is { } asked && asked > MaxOccurrences)
        {
            return Refuse(
                TooManyCode,
                $"That asks for {asked} occurrences and at most {MaxOccurrences} are written at "
                + "once. Make a shorter series now and another one when it runs out.");
        }

        if (lastDay is { } end && end <= start)
        {
            return Refuse(
                NotRepeatingCode,
                "The last day of a repeating event has to be after the first one.");
        }

        // The horizon is measured against the days the series would actually fall on, not against
        // the bounds it was asked for. Both ceilings then hold whichever of the two bounds was
        // named: a last day a fortnight away is within the horizon but with a daily repetition
        // still describes a fortnight of rows, which the count ceiling catches; and a count of a
        // hundred monthly occurrences names no date at all and still runs eight years out, which
        // this one catches. Measuring the bounds instead would miss the second entirely and would
        // refuse a request whose count stops it well short of a far-off last day.
        //
        // Clamped rather than added to, because a first day near the end of the calendar would
        // otherwise overflow while working out where the ceiling is — a refusal is an answer a
        // person is shown, never a throw.
        var horizon = start.DayNumber <= DateOnly.MaxValue.DayNumber - MaxHorizonDays
            ? start.AddDays(MaxHorizonDays)
            : DateOnly.MaxValue;

        var wantedCount = count ?? int.MaxValue;
        var days = new List<DateOnly>();
        for (var index = 0; days.Count < wantedCount; index++)
        {
            // A step that runs off the end of the calendar is past every bound there is, so it
            // ends a series that named a last day and is refused as a distance for one that did
            // not — which is what it is: further ahead than a series is written for, by a very
            // long way.
            if (!TryStep(start, frequency, index, out var day))
            {
                if (lastDay is not null)
                {
                    break;
                }

                return Refuse(HorizonTooFarCode, HorizonDetail);
            }

            // The last day is read before the horizon, because an occurrence past it is not part
            // of the series at all: a run that stops on the day somebody named is inside every
            // ceiling however far the step after it would have fallen.
            if (lastDay is { } stop && day > stop)
            {
                break;
            }

            if (day > horizon)
            {
                return Refuse(HorizonTooFarCode, HorizonDetail);
            }

            days.Add(day);
            if (days.Count > MaxOccurrences)
            {
                return Refuse(
                    TooManyCode,
                    $"That asks for more than {MaxOccurrences} occurrences and at most "
                    + $"{MaxOccurrences} are written at once. Bring the last day nearer, or "
                    + "make the event come round less often.");
            }
        }

        // A bound that leaves one day describes one event, not a run of them — and one row
        // carrying a grouping key would offer every surface the acts that reach a series and give
        // them nothing to reach. Caught here rather than by the count check above because a last
        // day falling between the first occurrence and the second passes every check on the
        // inputs and only shows up in what was produced.
        if (days.Count < 2)
        {
            return Refuse(
                NotRepeatingCode,
                "That last day leaves a single occurrence, which is an ordinary event rather "
                + "than a repeating one.");
        }

        return new EventSeriesPlan { Days = days };
    }

    /// <summary>
    /// Where the occurrence at <paramref name="index"/> falls, counted from the first one, or
    /// false when that day would be past the end of the calendar.
    /// </summary>
    /// <remarks>
    /// Every occurrence is measured from the start rather than from the one before it, so nothing
    /// drifts: a monthly series beginning on the 31st gives 31 January, 28 February, 31 March —
    /// each month's own reading of "the 31st", instead of the 28th for ever after the first short
    /// month. Stepping from the previous occurrence would lose the day of the month permanently
    /// the first time it could not be honoured.
    /// </remarks>
    private static bool TryStep(
        DateOnly start, EventRecurrenceFrequency frequency, int index, out DateOnly day)
    {
        day = default;

        // The arithmetic is done in day and month numbers and checked before the date is built,
        // because DateOnly's own AddDays and AddMonths throw past the end of the calendar. A
        // first day the caller typed is not bounded anywhere upstream, so that throw is reachable
        // from a request and would surface as a failure rather than as one of the refusals above.
        if (frequency == EventRecurrenceFrequency.Monthly)
        {
            var months = ((long)start.Year * 12) + (start.Month - 1) + index;
            if (months > ((long)DateOnly.MaxValue.Year * 12) + (DateOnly.MaxValue.Month - 1))
            {
                return false;
            }

            day = start.AddMonths(index);
            return true;
        }

        var stride = frequency switch
        {
            EventRecurrenceFrequency.Daily => 1,
            EventRecurrenceFrequency.Weekly => 7,
            EventRecurrenceFrequency.Fortnightly => 14,

            // Unreachable: the planner refuses a frequency outside the four before it steps at
            // all. Thrown rather than defaulted, because a fifth value added to the vocabulary
            // and not given a step here has no sensible spacing to fall back on, and picking one
            // would generate a series nobody asked for.
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null),
        };

        var number = (long)start.DayNumber + ((long)stride * index);
        if (number > DateOnly.MaxValue.DayNumber)
        {
            return false;
        }

        day = DateOnly.FromDayNumber((int)number);
        return true;
    }

    private static EventSeriesPlan Refuse(string code, string detail) =>
        new() { RefusalCode = code, RefusalDetail = detail };
}
