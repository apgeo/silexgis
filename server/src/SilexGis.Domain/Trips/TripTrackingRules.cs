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
    /// A reported depth in the form the log keeps it: one decimal, half away from zero, sign kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column a report's depth is stored in holds one decimal and rounds a finer value on the
    /// way in, silently. A depth placed, stored and shown from three different numbers is how two
    /// rows that read as the same depth come to stand at two stations: a report of 120.04 placed
    /// on the value as typed, stored as 120.0 and listed beside a report of 120, could have measured
    /// to a different station from it — or missed the cave's declaration at 120 altogether — while
    /// every reader sees "120.0 m" twice. So a depth is brought to this form before it is placed,
    /// before it is stored and before it is echoed, and the three cannot come apart.
    /// </para>
    /// <para>
    /// Half away from zero because that is what the database does to the value it stores; the
    /// sign is kept because the log keeps it, and a declaration's key takes the magnitude of this.
    /// </para>
    /// </remarks>
    public static decimal RecordedDepthM(decimal depthM) =>
        decimal.Round(depthM, 1, MidpointRounding.AwayFromZero);

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
    /// Whether a watch is running now and has been for longer than <paramref name="longerThan"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing closes a watch but a person, on purpose: "the party is out" is a fact somebody has
    /// to state, and a watch closed by a timer would move a party that is overdue — which is
    /// exactly a party that departed from its plan — off the page that follows it. The other side
    /// of that choice is that a watch somebody forgot runs for ever, so the people who answer for
    /// an installation need to be able to find the ones that have run suspiciously long. This is
    /// the one reading of "long" they are found by.
    /// </para>
    /// <para>
    /// Measured from the moment the watch was last started, not from the trip's dates: the dates
    /// are a plan, and a watch started again for a party still underground is long-running from
    /// when it was started again. A watch that is not running is never long-running, however long
    /// it once ran; one recorded as running with no moment it started at cannot be measured and is
    /// not counted.
    /// </para>
    /// </remarks>
    public static bool ArmedForLongerThan(
        TripTrackingState state, DateTimeOffset? armedAt, DateTimeOffset now, TimeSpan longerThan) =>
        state == TripTrackingState.Armed && armedAt is { } since && now - since > longerThan;

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
    /// Whether a watch in this state may have its log written to at all — a report recorded,
    /// corrected or taken off it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A closed watch's log is still editable, and that is the whole of this rule.</b> Recording
    /// used to be allowed only on an armed watch while deleting was allowed on any, which left a
    /// finished trip in the one state nobody wants: its log could be destroyed and could not be
    /// repaired. Since the way to correct a report is to put a right one where a wrong one was,
    /// half of that being refused made the correction path unusable on exactly the trips most
    /// likely to need it — the ones somebody is writing up afterwards, from notes, days later.
    /// </para>
    /// <para>
    /// <b>Off is refused, and for a different reason from the one it looks like.</b> An off watch is
    /// not a finished trip, it is a trip nobody is following: it names no survey, so a report
    /// claiming a station has nothing to resolve that station against and nothing to protect the
    /// position by. A watch is armed before it is written to, and stays writable once closed.
    /// </para>
    /// <para>
    /// Asked of the state a watch is <em>in</em>, unlike the rule above it, which is asked of the
    /// state a write would leave it in. Nothing here moves the watch; it only decides whether its
    /// log may be touched.
    /// </para>
    /// </remarks>
    public static bool MayWriteLog(TripTrackingState state) =>
        state is TripTrackingState.Armed or TripTrackingState.Closed;

    /// <summary>
    /// Whether somebody may be taken off a trip's roster altogether, given the state of the trip's
    /// watch and whether its log holds a report about them.
    /// </summary>
    /// <param name="state">
    /// The state the trip's watch is in now; a trip that never had a watch is
    /// <see cref="TripTrackingState.Off"/>.
    /// </param>
    /// <param name="hasReports">Whether the watch's log holds at least one report about them.</param>
    /// <remarks>
    /// <para>
    /// <b>Refused in exactly one case: the watch is running and the person has been reported.</b>
    /// A running watch is read to answer who is still inside, and the roster is what its published
    /// page counts the party from. Somebody who went in and then disappears from the list is the
    /// one error such a page cannot afford — the count of people underground drops by one with
    /// nobody having come out — and it is reached by an ordinary act: the trip's form sends the
    /// whole roster back, so a form that was opened before the person was added, or an edit made
    /// for another reason, removes them without anybody having decided to.
    /// </para>
    /// <para>
    /// <b>Allowed once the watch has closed, and on a trip that never had one.</b> A finished trip
    /// is a record somebody is writing up, and its list of people has to stay correctable: a guest
    /// entered under the wrong trip is taken off it. The reports about them remain in the log, and
    /// the watch's own read goes on listing everybody its log speaks of, so nothing recorded is
    /// lost from sight by the removal.
    /// </para>
    /// <para>
    /// <b>Somebody nobody has reported on may leave at any time</b>, a running watch included:
    /// there is nothing about them for the watch to lose, and a party that changes at the entrance
    /// is the commonest roster edit there is.
    /// </para>
    /// <para>
    /// <b>What this does not promise: that a running watch never speaks of somebody off the
    /// roster.</b> It refuses the ordinary way there, a roster edit, and two others remain. A
    /// watch that was closed, had a reported person taken off, and is then started again is
    /// running with that person off its list; and the answer here is given from what the caller
    /// read a moment earlier, with nothing holding the log still until the roster is saved, so a
    /// first report recorded in that moment lands after the departure it should have refused.
    /// Every reader of a watch therefore still has to cope with a reported person who is not on
    /// the roster — the watch's own read lists them and says so — and must not take this rule as
    /// leave to assume otherwise.
    /// </para>
    /// <para>
    /// <b>This is asked about leaving the trip, never about leaving a job on it.</b> One person can
    /// hold several jobs on a trip, each its own roster row; giving one up, or exchanging one for
    /// another, leaves them on the trip and is not a departure. The caller decides who is leaving
    /// by comparing people, not rows, and asks this only of those named in no job afterwards.
    /// </para>
    /// </remarks>
    public static bool MayLeaveRoster(TripTrackingState state, bool hasReports) =>
        !(state == TripTrackingState.Armed && hasReports);

    /// <summary>
    /// Whether a stored report has been changed since it was first written down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off the row's own two stamps, which one clock writes together when the row is first
    /// saved and of which only the second moves afterwards — and only when a save actually changed
    /// something, so a correction that altered nothing, or a sheet imported twice over the same
    /// rows, leaves a report reading as it was written.
    /// </para>
    /// <para>
    /// <b>A yes or a no, and deliberately not the two moments.</b> A log is what somebody said at a
    /// moment, and a reader of it is owed knowing that a row no longer says what was first taken
    /// down. When it was typed and by whom is the audit timeline's to say, to those who may read
    /// that; putting the stamps beside the report would publish a second, coarser history to
    /// everybody who reads the trip.
    /// </para>
    /// <para>
    /// Anything that rewrites the row counts, not only the correction dialog: a sheet that replaced
    /// it, and folding one roster entry into another, which changes whom the report is about.
    /// </para>
    /// </remarks>
    public static bool ChangedSinceWritten(DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        updatedAt > createdAt;

    /// <summary>
    /// What a send answers when the key it carries may already be on the trip's log: the stored
    /// reports to answer with, or null when the log has never heard of this act and the send is
    /// to be written.
    /// </summary>
    /// <param name="askedCaverIds">The people the send names, in the order it names them.</param>
    /// <param name="storedUnderKey">
    /// Every report of the trip that carries the send's key — <b>those taken off the log
    /// included</b>, which is the point of asking for them.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The act is either on record or it is not; it is never completed.</b> Any stored report
    /// under the key makes the send a repeat, and a repeat writes nothing — not even for a person
    /// the first send wrote and somebody has since taken off the log. That report was removed on
    /// purpose by a person looking at it; a re-send is a machine that never heard its answer, and
    /// it must not undo the person. So a partly removed act answers what is left of it, and an act
    /// removed entirely answers an empty list, which is still success: the sender's report was
    /// received, and what became of it afterwards is on the log for anybody to read.
    /// </para>
    /// <para>
    /// What the repeat says is not compared with what was written. The key names the act; a
    /// sender that reuses one for different content is answered with the first content, and can
    /// see that it was.
    /// </para>
    /// <para>
    /// The answer is ordered as the send names its people, so a repeat reads like the answer it
    /// replaces; a stored report about somebody the repeat does not name comes after those, in
    /// the order the reports were written.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TripPositionEvent>? ReplayAnswer(
        IReadOnlyList<Guid> askedCaverIds, IReadOnlyCollection<TripPositionEvent> storedUnderKey)
    {
        if (storedUnderKey.Count == 0) return null;

        var asked = new Dictionary<Guid, int>();
        for (var index = 0; index < askedCaverIds.Count; index++)
        {
            asked.TryAdd(askedCaverIds[index], index);
        }

        return [.. storedUnderKey
            .Where(e => e.RemovedAt is null)
            .OrderBy(e => asked.GetValueOrDefault(e.CaverId, int.MaxValue))
            .ThenBy(e => e.Id)];
    }

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

    /// <summary>
    /// Whether somebody on a running watch has gone unreported for longer than the installation's
    /// threshold: underground by the log, and no word of any kind since.
    /// </summary>
    /// <param name="state">Where the watch itself stands. Only a running watch has quiet people.</param>
    /// <param name="standing">What <see cref="StandingOf"/> made of the person's reports.</param>
    /// <param name="lastHeardAt">The moment of their latest report of any kind, or null.</param>
    /// <param name="now">The moment the question is asked at.</param>
    /// <param name="quietAfter">
    /// How long a silence has to last before it is marked. Zero switches the mark off, and so does
    /// anything below zero: a threshold nobody could have meant marks nobody, which is the reading
    /// that cannot cry wolf.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>It is a mark on a screen and nothing else.</b> It is derived at the moment of reading and
    /// stored nowhere; nothing is sent, raised or stood down because of it. A watch records what
    /// it is told and watches no clock on anybody's behalf — whether a silence matters is for the
    /// person reading the screen, who knows whether the party was expected to be out of reach.
    /// </para>
    /// <para>
    /// <b>Only somebody underground can be quiet.</b> Somebody out has been accounted for and has
    /// nothing further to report; somebody never heard from already has a state of their own that
    /// says exactly that, and a second mark on them would say the same thing twice in a louder
    /// voice. And once the watch is closed nobody is expected to report at all, so the silence
    /// that follows a closed watch is the ordinary one.
    /// </para>
    /// <para>
    /// <b>Any word resets it, a note included.</b> The question is how long it has been since
    /// anything was heard about this person, not since they last moved: "radio contact, all well"
    /// names no place and is precisely the report that ends a silence.
    /// </para>
    /// <para>
    /// Strictly longer than the threshold, so a report made exactly that long ago is not yet
    /// quiet; and a last word dated after <paramref name="now"/> — a clock a little ahead — is a
    /// silence of no length.
    /// </para>
    /// </remarks>
    public static bool IsQuiet(
        TripTrackingState state, TripStanding standing, DateTimeOffset? lastHeardAt,
        DateTimeOffset now, TimeSpan quietAfter) =>
        quietAfter > TimeSpan.Zero
        && state == TripTrackingState.Armed
        && standing == TripStanding.Underground
        && lastHeardAt is { } heard
        && now - heard > quietAfter;
}
