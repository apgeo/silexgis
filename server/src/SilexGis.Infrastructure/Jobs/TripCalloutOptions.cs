// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// How often the installation checks whether a party is overdue.
/// </summary>
/// <remarks>
/// <para>
/// The check is a scheduled pass over the trips that arranged one, not an alarm armed ahead of
/// time on the queue that carries the message. A queued message cannot be recalled — its wording
/// is fixed the moment it is written and nothing outside the delivery machinery can find it again
/// by the trip it is about — so a party that came back early, a trip put back, and a trip called
/// off would all still raise the alarm, naming a date that is no longer true. The pass reads the
/// trip as it stands each time it runs, which is what makes standing an alarm down possible at all.
/// </para>
/// <para>
/// The interval is also the alarm's worst-case lateness, which is why it is minutes rather than the
/// hours the housekeeping passes use: a party reported half a day after they were due has been
/// reported by somebody else already.
/// </para>
/// </remarks>
public sealed class TripCalloutOptions
{
    public const string SectionName = "TripCallout";

    /// <summary>
    /// How often to queue a pass. Zero or negative switches the schedule off entirely.
    /// </summary>
    /// <remarks>
    /// A real operator setting, for an installation whose members do not use the callout and would
    /// rather not have a pass running — and the setting the automated tests rely on, because they
    /// share one database and a pass left running under one of them would move another's trips out
    /// of the state that test had just put them in.
    /// </remarks>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether there is a schedule at all. Named rather than compared at each call site so that
    /// "switched off" is one fact with one definition, and so a caller cannot accidentally read a
    /// negative interval as a very frequent one.
    /// </summary>
    public bool SweepIsScheduled => SweepInterval > TimeSpan.Zero;

    /// <summary>
    /// The longest gap this installation will actually leave between passes, whatever is
    /// configured. Not a guard against a typo — a deliberate ceiling, because the number has a
    /// second reader.
    /// </summary>
    /// <remarks>
    /// A trip showing an armed check reports how long ago the last completed pass was, and treats
    /// a gap of an hour as meaning nobody has checked — it says so in as many words, and tells the
    /// reader to reach the party another way. That warning is only worth anything if it is
    /// impossible to reach while the installation is healthy: a warning every armed trip shows all
    /// the time is a warning nobody reads, on the one feature where being ignored costs the most.
    /// The page cannot know what this is set to, so the two numbers can only agree by being made
    /// to. Half of the hour leaves room for a pass that starts late or takes a while.
    /// </remarks>
    public static readonly TimeSpan MaxSweepInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The interval the schedule actually uses: what was configured, never longer than
    /// <see cref="MaxSweepInterval"/>. Meaningless when there is no schedule at all.
    /// </summary>
    public TimeSpan EffectiveSweepInterval =>
        SweepInterval > MaxSweepInterval ? MaxSweepInterval : SweepInterval;

    /// <summary>
    /// How far ahead of a trip the people on it are reminded that it is coming up. Zero or negative
    /// sends no reminders, leaving the overdue check running — they are two different promises and
    /// an installation may well want one without the other.
    /// </summary>
    /// <remarks>
    /// The reminder rides the same pass rather than being written onto the queue when the trip is
    /// arranged, for the same reason the alarm does: a message set weeks ahead cannot be recalled
    /// when the trip moves or is called off, and would arrive naming a date nobody is going on.
    /// </remarks>
    public TimeSpan ReminderLead { get; set; } = TimeSpan.FromDays(2);
}
