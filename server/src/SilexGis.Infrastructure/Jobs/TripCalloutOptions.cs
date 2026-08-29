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
    /// How far ahead of a trip, or of a club event, the people it concerns are reminded that it is
    /// coming up. Zero or negative sends no reminders at all, leaving the overdue check running —
    /// they are two different promises and an installation may well want one without the other.
    /// <para>
    /// One setting for both kinds of row rather than one each. It is an installation's answer to
    /// "how much notice do people here want", which is a fact about the club and not about what is
    /// written in the diary; and a reader who wants different notice for the two would be asking
    /// for a setting of their own, which is a per-account preference and not this.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reminder rides the same pass rather than being written onto the queue when the trip is
    /// arranged, for the same reason the alarm does: a message set weeks ahead cannot be recalled
    /// when the trip moves or is called off, and would arrive naming a date nobody is going on.
    /// </para>
    /// <para>
    /// Why this is one number for the installation and not one per reader, written down here
    /// because the obvious way to make it per-reader loses messages silently. What stops a
    /// reminder arriving on every pass through the run-up is a single nullable instant on the
    /// subject itself, claimed by an update that only sends when it finds that instant still
    /// unset. Two readers wanting different notice are two sends about one subject on two
    /// different days, and one column records at most one of them: the earlier send sets it, the
    /// guarded update then finds nothing to claim on the later day, and the second reader is
    /// simply never told — with nothing anywhere recording that a reminder was owed. Widening the
    /// window to the longest notice does not help, because it sends earlier for everybody and
    /// still stamps once; dropping the stamp restores the reminder-every-quarter-hour it exists
    /// to prevent.
    /// </para>
    /// <para>
    /// So a per-reader notice needs two things this does not have: somewhere to record each
    /// reader's chosen notice, and a marker per subject-and-reader rather than per subject. And if
    /// it is built, it should offer a small closed set of choices — the same day, a day, three
    /// days, a week — rather than a free-form duration. With a closed set the pass still selects
    /// subjects, taking everything inside the longest choice and sorting each subject's readers
    /// into the choice each of them made, so the bound on how many rows one pass takes goes on
    /// bounding subjects. A free-form duration gives every subject-and-reader pair its own due
    /// date and inverts the selection to pairs, so the same bound silently starts bounding
    /// something that grows with how many people a subject concerns.
    /// </para>
    /// </remarks>
    public TimeSpan ReminderLead { get; set; } = TimeSpan.FromDays(2);
}
