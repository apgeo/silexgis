// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Where an activity — a trip, and later an expedition or a calendar event — has got to, from
/// being written up to being announced or called off.
/// <para>
/// Stored as smallint and append-only: the values are part of the schema contract and travel to a
/// client, so a member keeps the number it was given.
/// </para>
/// <para>
/// One vocabulary rather than one per kind, and an enum rather than an admin-editable table,
/// because every value here has code behind it — notification is suppressed in some states and not
/// others, and later a state decides calendar membership and whether a callout is armed. A row
/// somebody could add through an administration screen would be a state nothing knows how to
/// handle. Which states a given kind of activity may hold is a separate question, answered by
/// <see cref="ActivityStates"/>; the enum carries the whole union so that a kind admitting more of
/// it later is a change to a list, not a schema change.
/// </para>
/// </summary>
public enum ActivityState : short
{
    /// <summary>Being written. Notifies nobody — the author is still deciding what it says.</summary>
    Draft = 0,

    /// <summary>An idea somebody has floated. Reserved for planning; no trip holds it yet.</summary>
    Proposed = 1,

    /// <summary>A more certain intention. Reserved for planning; no trip holds it yet.</summary>
    Planned = 2,

    /// <summary>Going ahead, with the people and the date settled. Reserved for planning.</summary>
    Confirmed = 3,

    /// <summary>It happened. The write-up may still follow.</summary>
    Done = 4,

    /// <summary>Written up and announced — the point at which the people named on it are told.</summary>
    Published = 5,

    /// <summary>Called off.</summary>
    Cancelled = 6,

    /// <summary>Put back to a date not yet chosen. Reserved for planning.</summary>
    Delayed = 7,
}

/// <summary>
/// The lifecycle vocabulary and the rules behind it, in one place.
/// <para>
/// Two questions are asked of it and no others: may this activity move from one state to another,
/// and does a state stop the people named on the activity being told about it. Anything else a
/// state comes to mean belongs to the code that means it.
/// </para>
/// <para>
/// Each kind of activity gets its own admitted-states list and its own transition table beside the
/// trip's below — that is where expeditions and events are added, and where a trip is allowed to
/// hold the planning states once trips can be planned rather than only reported.
/// </para>
/// </summary>
public static class ActivityStates
{
    public static IReadOnlyList<ActivityState> All { get; } = [.. Enum.GetValues<ActivityState>()];

    /// <summary>
    /// Whether a state stops the people named on the activity being notified. A draft has not been
    /// decided on yet and a cancelled activity is not happening, so neither is worth anybody's
    /// mail; publishing is the event that tells them.
    /// </summary>
    /// <remarks>
    /// This is a property of the state itself, not of the kind of activity in it, so every kind
    /// answers it the same way. A state added to the enum and not named here suppresses, rather
    /// than surprising people with mail nobody decided to send.
    /// </remarks>
    public static bool SuppressesParticipantNotification(ActivityState state) => state switch
    {
        ActivityState.Draft => true,
        ActivityState.Cancelled => true,
        ActivityState.Proposed => false,
        ActivityState.Planned => false,
        ActivityState.Confirmed => false,
        ActivityState.Done => false,
        ActivityState.Published => false,
        ActivityState.Delayed => false,
        _ => true,
    };

    // ---- trip logs ----

    /// <summary>Refusal: the activity does not go from the state it is in to the one asked for.</summary>
    public const string TripLogTransitionInvalidCode = "trip_log.state_transition_invalid";

    /// <summary>
    /// The states a trip log may hold. A trip is reported after the fact today, so the four
    /// planning states are deliberately absent and are refused rather than merely unused: a
    /// column that can hold a value no code reads is a defect waiting for somebody to write it.
    /// They join this list, with the transitions that reach them, when trips can be planned.
    /// </summary>
    public static IReadOnlyList<ActivityState> TripLogStates { get; } =
    [
        ActivityState.Draft,
        ActivityState.Done,
        ActivityState.Published,
        ActivityState.Cancelled,
    ];

    /// <summary>Whether a trip log may hold this state at all, whatever it is in now.</summary>
    public static bool IsTripLogState(ActivityState state) => TripLogStates.Contains(state);

    /// <summary>
    /// The moves a trip log may make. Draft is the hub: unpublishing, reinstating a cancelled trip
    /// and reopening a finished one all land there, so "how do I get at this again" has one
    /// answer. Nothing reaches Published or Cancelled from anywhere but the state before it —
    /// de-announcing a trip and declaring it never happened are two decisions, and a table that
    /// let one imply the other would take them both on one click.
    /// </summary>
    /// <remarks>
    /// A state is not a transition to itself, so no pair here repeats a state. Whether asking for
    /// the state an activity is already in is a refusal or a no-op is the caller's to decide; this
    /// table only says it is not a move.
    /// </remarks>
    private static readonly (ActivityState From, ActivityState To)[] TripLogMoves =
    [
        // A draft can be recorded as having happened, announced, or called off.
        (ActivityState.Draft, ActivityState.Done),
        (ActivityState.Draft, ActivityState.Published),
        (ActivityState.Draft, ActivityState.Cancelled),

        // A trip that happened is announced once its write-up is ready, or goes back for more work.
        (ActivityState.Done, ActivityState.Published),
        (ActivityState.Done, ActivityState.Draft),

        // The reverse of publishing, and the only one: an announced trip returns to the workshop.
        (ActivityState.Published, ActivityState.Draft),

        // Reinstating a cancelled trip returns it to the workshop, not straight to announced.
        (ActivityState.Cancelled, ActivityState.Draft),
    ];

    /// <summary>Whether a trip log may move from one state to another.</summary>
    public static bool MayTripLogTransition(ActivityState from, ActivityState to) =>
        TripLogMoves.Any(move => move.From == from && move.To == to);
}
