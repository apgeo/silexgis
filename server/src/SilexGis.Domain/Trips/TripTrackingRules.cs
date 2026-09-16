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
