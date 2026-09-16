// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// Where one member of a tracked party stands: the three states the reports actually
/// distinguish, and the answer every surface that counts the party has to reach the same way.
/// </summary>
public enum TripStanding
{
    /// <summary>
    /// Nothing has yet said they went in, come out, or been anywhere. A state of its own and
    /// never a synonym for "not underground": somebody still in the car park and somebody safely
    /// back out are the two answers a reader most needs told apart.
    /// </summary>
    Unheard = 0,

    /// <summary>The last word that spoke to presence put them inside the cave.</summary>
    Underground = 1,

    /// <summary>The last word that spoke to presence was that they are out.</summary>
    Out = 2,
}

/// <summary>
/// The tracking lifecycle's legality table and the slice's shared limits — one home, so the
/// API and its tests cannot drift apart on what a tracking state may become.
/// </summary>
public static class TripTrackingRules
{
    public const int MaxTitleLength = 200;
    public const int MaxNoteLength = 2000;
    public const int MaxStationNameLength = 400;
    public const int MaxDepthFilterEntries = 200;
    public const decimal MaxDepthAbsM = 5000m;
    public const int MaxCaversPerWrite = 100;

    /// <summary>How long a published participant's display label may be — a caption, not a bio.</summary>
    public const int MaxLabelLength = 200;

    /// <summary>
    /// Longest publication token this application will even hash. One it mints is 43 characters;
    /// the bound keeps an unbounded string out of the lookup, and a token over it is answered
    /// exactly as an unknown one is.
    /// </summary>
    public const int MaxShareTokenLength = 100;

    /// <summary>How far ahead of the server clock a report may claim to be — clock skew, not planning.</summary>
    public static readonly TimeSpan RecordedAtSkew = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How many pictures one write may hang on the trip's moments. Sized for the act it exists
    /// for — somebody emptying a memory card after the trip — and bounded because each one costs
    /// a document read and a membership row.
    /// </summary>
    public const int MaxPicturesPerWrite = 100;

    /// <summary>How long a caption on one of those pictures may be. A line, not an account.</summary>
    public const int MaxPictureCaptionLength = 1000;

    /// <summary>
    /// Whether a moment claims to be later than the clock allows — the same skew a report is held
    /// to, applied to the same kind of claim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Said here rather than at the endpoint because two write paths now make the same claim about
    /// the same trip ("this happened at 14:05") and a second reading of the allowance is how they
    /// come to disagree by a minute.
    /// </para>
    /// <para>
    /// Note what is deliberately <em>not</em> checked, here or anywhere: that the moment falls
    /// inside the stretch of time the watch covers. A camera clock set to the wrong hour is the
    /// same class of thing as a station anchor naming a station its model does not have — it is
    /// accepted and degrades where it is drawn, because a refusal would throw away the one record
    /// of a picture on the grounds of a number the person attaching it can fix afterwards. The
    /// surface that offers the moment warns; the server does not refuse.
    /// </para>
    /// </remarks>
    public static bool MomentIsInFuture(DateTimeOffset at, DateTimeOffset now) =>
        at > now + RecordedAtSkew;

    /// <summary>
    /// Off arms, Armed closes, Closed re-arms (a party that turns out to still be underground),
    /// and any state restates itself. Nothing returns to Off: history exists, and "we never
    /// tracked this trip" would be a lie the moment one event row is on the timeline.
    /// </summary>
    public static bool MayTransition(TripTrackingState from, TripTrackingState to) => (from, to) switch
    {
        _ when from == to => true,
        (TripTrackingState.Off, TripTrackingState.Armed) => true,
        (TripTrackingState.Armed, TripTrackingState.Closed) => true,
        (TripTrackingState.Closed, TripTrackingState.Armed) => true,
        _ => false,
    };

    /// <summary>
    /// Whether a position recorded against <paramref name="recordedOn"/> may be drawn on the model
    /// <paramref name="modelInUse"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A station path is a name inside one survey and means whatever that survey says it means, so
    /// <c>sala-mare.4</c> of a re-survey is not necessarily the place <c>sala-mare.4</c> of the
    /// survey before it was. Drawing a report from one model on another is therefore a confident
    /// statement about where somebody is, assembled out of a collision of names — the one kind of
    /// wrong answer a surface read during a rescue must never produce.
    /// </para>
    /// <para>
    /// Both nulls answer false, and that is the fail-closed half. A row with no model claims no
    /// place worth drawing; a panel that does not know which model it is showing has nothing to
    /// compare against. Neither is an invitation to guess.
    /// </para>
    /// <para>
    /// <b>What follows from a false is "say so", not "say nothing".</b> The position exists and is
    /// known — it is only unplaceable <em>here</em> — so the surfaces that cannot draw it report it
    /// as recorded elsewhere rather than as a person nobody has reported, which would be a false
    /// statement about somebody underground.
    /// </para>
    /// </remarks>
    public static bool DrawableOn(Guid? recordedOn, Guid? modelInUse) =>
        recordedOn is not null && modelInUse is not null && recordedOn == modelInUse;

    /// <summary>
    /// Whether a watch that will be in <paramref name="state"/>, anchored to
    /// <paramref name="anchoredCave"/>, may be pointed at a model belonging to
    /// <paramref name="newCave"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replacing the model of a watch that is <em>armed</em> is a legitimate act and is allowed: a
    /// survey corrected or re-imported while a party is underground is exactly the situation a
    /// coordinator may have to follow, and refusing it would leave them arguing with the
    /// application during a callout. What is refused is the narrower thing — moving an armed watch
    /// to a model of a <b>different cave</b>. A party is in one cave, and the cave is also the
    /// anchor every position on the log is protected by, so a swap that crosses caves either
    /// re-points a live watch at a place the party is not, or moves the protection anchor of the
    /// config's own station vocabulary to a cave nobody decided that about. Neither is recoverable
    /// by reading the screen afterwards.
    /// </para>
    /// <para>
    /// A watch that is off or closed may be pointed anywhere: nothing is being followed, and
    /// history keeps its own per-row anchor whatever the configuration later says.
    /// </para>
    /// <para>
    /// <b>The state to ask about is the one the watch will be in, not the one it is in.</b> A
    /// single write says both things at once, and which of the two is read decides the answer to
    /// two real acts. Ending a watch and re-pointing it in one act is free by the paragraph above
    /// — the party is no longer being followed by the time the new model applies — and asking
    /// about the state it was in refuses it for a fact about the past. Arming a closed watch
    /// directly onto another cave's survey is the very thing the paragraph before that refuses,
    /// and asking about the state it was in permits it: the watch arms, and the anchor protecting
    /// its station vocabulary moves to a cave nobody decided that about. Both are the same
    /// mistake, in opposite directions, and the fix for both is to ask about the outcome.
    /// </para>
    /// </remarks>
    public static bool MayPointAtCave(TripTrackingState state, Guid? anchoredCave, Guid newCave) =>
        state != TripTrackingState.Armed || anchoredCave is null || anchoredCave == newCave;

    /// <summary>
    /// Where one member of the party stands, folded from every report about them.
    /// </summary>
    /// <param name="reports">
    /// That person's reports <b>oldest first, by recorded time</b>, which is the order both reads
    /// already fold in. Note what that order is and is not: the recorded time is what the reporter
    /// gave <em>or the clock at the moment the report was written</em>, so it sorts the reports but
    /// does not reliably date the moments they describe — which is the reason the rule below turns
    /// on what a report says rather than only on which one is last. Null or empty is somebody
    /// nobody has said anything about yet. Reports about other people must not be in here; this
    /// answers about one person and cannot tell whose row is whose.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The rule, stated in full so that nobody has to derive it again.</b> Of the five report
    /// kinds, two <em>state</em> a standing, two only <em>imply</em> one, and one says nothing
    /// about it at all.
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <c>Entered</c> and <c>Exited</c> state it. Each says in so many words where the person now
    /// is, and the later of the two is always the answer.
    /// </item>
    /// <item>
    /// <c>AtStation</c> and <c>AtDepth</c> imply it: a place inside the cave is a claim that the
    /// person is inside the cave. They can raise somebody from <see cref="TripStanding.Unheard"/>
    /// to <see cref="TripStanding.Underground"/>, and they never overturn an <c>Entered</c> or an
    /// <c>Exited</c> that has already spoken.
    /// </item>
    /// <item>
    /// <c>Note</c> is defined as a note about the caver with no position claim. It moves nothing,
    /// in either direction, ever.
    /// </item>
    /// </list>
    /// <para>
    /// Said the other way round, which is the same rule: the standing is the last <c>Entered</c>
    /// or <c>Exited</c>; where there has been neither, any report that claims a place makes it
    /// <see cref="TripStanding.Underground"/>; where there has been neither of those either, it
    /// is <see cref="TripStanding.Unheard"/>.
    /// </para>
    /// <para>
    /// <b>Why a place implies presence at all.</b> A station is a named point of the cave's own
    /// measured survey, and a depth is resolved to one; being reported at either is a statement
    /// that the person is inside the cave, made by somebody who had to name a real station of a
    /// real model to make it. Word here arrives by relayed phone call, and what gets relayed
    /// first about a team is routinely where they are rather than that they went in — so a rule
    /// reading only <c>Entered</c> and <c>Exited</c> would leave a party whose station is on the
    /// screen counted as never heard from. On a surface that exists to say who is still inside,
    /// that is the reading most likely to be acted on wrongly.
    /// </para>
    /// <para>
    /// <b>Why it must not overturn a standing that was stated.</b> The tempting version of this
    /// rule — last report of any presence-bearing kind wins — rests on the premise that a report
    /// carries the moment it is <em>about</em>, so late word about an earlier moment sorts before
    /// the exit on its own. That premise does not hold. A report's time is optional and defaults
    /// to the clock at the moment it is written, so completing the log after the fact ("last seen
    /// at the bottom pitch", typed at 17:20 about 14:00) lands stamped 17:20 and sorts after a
    /// 17:00 exit. Rows also arrive from a device-export import, stamped with the recording
    /// device's own clock: a cave affords no fix to correct that clock against, and an archive
    /// from Saturday is routinely imported on Monday, with nobody watching at the moment the fold
    /// is recomputed. Under the tempting rule each of those moves a caver from <c>Out</c> back to
    /// <c>Underground</c> — silently, on a page the families of people underground are reading,
    /// and with nothing on that page to explain it.
    /// </para>
    /// <para>
    /// An exit is the most deliberate report in the system: it is the one that ends the watch for
    /// a person. Undoing it should be deliberate too, and there is exactly one way to do it —
    /// record that they went in again. That path is not a workaround; it is the same act the
    /// lifecycle already expects of a party that turns out to still be underground, which is why
    /// closed tracking re-arms.
    /// </para>
    /// <para>
    /// <b>What this rule still gets wrong.</b> Somebody who genuinely re-enters, and whose
    /// re-entry is relayed only as a place, reads <c>Out</c> until an <c>Entered</c> is recorded
    /// — the dangerous direction, a person inside shown as one who is safe, so it is named here
    /// rather than left to be discovered. Two things bound it. The station and the hour it was
    /// reported at are still shown beside that person, so a place timed after their exit is on
    /// the screen for a coordinator to act on. And the correction is one report, by the same
    /// person who recorded the exit and therefore has the context to notice. Weighed against it,
    /// the rule this replaces produced a false resurrection out of ordinary log-keeping and out
    /// of an unattended import — far more often, and with nobody present to catch it.
    /// </para>
    /// </remarks>
    public static TripStanding StandingOf(IEnumerable<TripPositionEvent>? reports)
    {
        if (reports is null) return TripStanding.Unheard;

        var standing = TripStanding.Unheard;
        foreach (var report in reports)
        {
            switch (report.Kind)
            {
                case TripPositionEventKind.Entered:
                    standing = TripStanding.Underground;
                    break;
                case TripPositionEventKind.Exited:
                    standing = TripStanding.Out;
                    break;
                case TripPositionEventKind.AtStation:
                case TripPositionEventKind.AtDepth:
                    // A place answers where nothing has stated an answer — and only there. Once
                    // Entered or Exited has spoken the standing is no longer Unheard, so this
                    // leaves it exactly as it found it.
                    if (standing == TripStanding.Unheard) standing = TripStanding.Underground;
                    break;
                default:
                    // Note — something happened, not where and not whether. A kind added later
                    // lands here too and moves nothing until somebody decides what it means,
                    // which is the right way round for a rule that counts who is still inside.
                    break;
            }
        }
        return standing;
    }
}
