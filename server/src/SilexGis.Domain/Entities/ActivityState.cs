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

    // ---- expeditions ----

    /// <summary>Refusal: the activity does not go from the state it is in to the one asked for.</summary>
    public const string ExpeditionTransitionInvalidCode = "expedition.state_transition_invalid";

    /// <summary>
    /// The states an expedition may hold — all of them, which is the difference between a camp
    /// and a trip and not an oversight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A trip is written up after it happened, so the four planning states say nothing about one
    /// and are refused. An expedition is the opposite: it exists months before it happens, which
    /// is what it is for. People book leave against it, a club commits money to it and a partner
    /// club is invited to it, and every one of those decisions asks a question only these states
    /// answer — is this somebody's idea, is it being organised, is it going ahead, has it been
    /// put back. Without them a camp being organised would sit in the state that means "nobody
    /// has been told", for half a year, while the whole point of the record is that people have
    /// been told.
    /// </para>
    /// <para>
    /// So every value is one somebody acts on, which is the bar for admitting a state at all.
    /// They are written out rather than taken from the whole vocabulary, so that a state added
    /// to the enum later is not admitted here by accident: whether a camp may hold it is a
    /// decision somebody has to take, the same way it is for a trip.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ActivityState> ExpeditionStates { get; } =
    [
        ActivityState.Draft,
        ActivityState.Proposed,
        ActivityState.Planned,
        ActivityState.Confirmed,
        ActivityState.Done,
        ActivityState.Published,
        ActivityState.Cancelled,
        ActivityState.Delayed,
    ];

    /// <summary>Whether an expedition may hold this state at all, whatever it is in now.</summary>
    public static bool IsExpeditionState(ActivityState state) => ExpeditionStates.Contains(state);

    /// <summary>
    /// The moves an expedition may make. The planning states are a ladder — floated, organised,
    /// going ahead — which may be joined at any rung but is climbed one rung at a time, because
    /// each rung is a decision somebody takes and a table that let one imply the next would take
    /// them both on one click. Draft is the hub the trip's table already makes it: everything
    /// live returns there, so "how do I get at this again" has one answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two differences from a trip's table, both from the camp existing before it happens.
    /// Calling it off is reachable from every state where it has not happened yet, rather than
    /// from the workshop alone — a confirmed camp abandoned three weeks out is the ordinary
    /// cancellation, not an edge case. And it is not reachable from Done or Published: a camp
    /// that happened cannot be made not to have happened, and "delete the record" is a different
    /// act with a different button.
    /// </para>
    /// <para>
    /// Putting it back is reachable only from the two states that had dates to put back. Coming
    /// out of it goes to Planned rather than Confirmed, because new dates have to be settled
    /// before anybody is told the camp is on again.
    /// </para>
    /// <para>
    /// A state is not a transition to itself, so no pair here repeats a state.
    /// </para>
    /// </remarks>
    private static readonly (ActivityState From, ActivityState To)[] ExpeditionMoves =
    [
        // Out of the workshop: floated to the club, or straight to being organised when it has
        // already been agreed elsewhere.
        (ActivityState.Draft, ActivityState.Proposed),
        (ActivityState.Draft, ActivityState.Planned),

        // A camp that already happened is recorded from the workshop — that is how one from
        // before this system existed is entered, write-up and all.
        (ActivityState.Draft, ActivityState.Done),
        (ActivityState.Draft, ActivityState.Published),
        (ActivityState.Draft, ActivityState.Cancelled),

        // An idea is adopted, sent back for more thought, or turned down.
        (ActivityState.Proposed, ActivityState.Planned),
        (ActivityState.Proposed, ActivityState.Draft),
        (ActivityState.Proposed, ActivityState.Cancelled),

        // Being organised: it goes ahead, is put back, or does not happen.
        (ActivityState.Planned, ActivityState.Confirmed),
        (ActivityState.Planned, ActivityState.Delayed),
        (ActivityState.Planned, ActivityState.Draft),
        (ActivityState.Planned, ActivityState.Cancelled),

        // Going ahead: it happens, or it is put back or called off after all.
        (ActivityState.Confirmed, ActivityState.Done),
        (ActivityState.Confirmed, ActivityState.Delayed),
        (ActivityState.Confirmed, ActivityState.Draft),
        (ActivityState.Confirmed, ActivityState.Cancelled),

        // Put back: new dates return it to being organised, not to going ahead.
        (ActivityState.Delayed, ActivityState.Planned),
        (ActivityState.Delayed, ActivityState.Draft),
        (ActivityState.Delayed, ActivityState.Cancelled),

        // It happened: the write-up is announced when it is ready, or it goes back for work.
        (ActivityState.Done, ActivityState.Published),
        (ActivityState.Done, ActivityState.Draft),

        // The reverse of announcing, and the only one.
        (ActivityState.Published, ActivityState.Draft),

        // Reinstating a called-off camp returns it to the workshop, not to the rung it fell from.
        (ActivityState.Cancelled, ActivityState.Draft),
    ];

    /// <summary>Whether an expedition may move from one state to another.</summary>
    public static bool MayExpeditionTransition(ActivityState from, ActivityState to) =>
        ExpeditionMoves.Any(move => move.From == from && move.To == to);
}
