// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// What an installation has decided about its tracked trips: what a published one may say about
/// the people on it, how long a link to one lasts, and what the coordinator's watch marks.
/// </summary>
/// <remarks>
/// Here rather than in the domain because each is a choice the people running this server make
/// once, not a rule about caves or trips.
/// </remarks>
public sealed class TripTrackingOptions
{
    public const string SectionName = "TripTracking";

    /// <summary>
    /// Whether a published trip names its party for real. <b>On unless an installation turns it
    /// off</b>, which is the one option in this application that defaults to disclosing something.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the owner's decision for their own installation: a club publishes a trip so that
    /// families and friends can watch the party come out, and a page that says "Caver 2 is at
    /// −120 m" answers nobody's question. Written as a setting rather than as the way the software
    /// behaves, because the people named are third parties — club members who are not the
    /// administrator pressing publish — so an installation whose members do not want that has to
    /// be able to say so for everybody at once, in one place, without editing code.
    /// </para>
    /// <para>
    /// What it widens, exactly: the name a published envelope carries for a participant who has no
    /// label of their own becomes that caver's roster name instead of nothing. It changes no other
    /// field, no other route, and nothing at all about what a signed-in caller is told — the
    /// published page has always carried a name where an administrator typed one, and this decides
    /// only what happens where nobody typed one. Turned off, an envelope is byte-for-byte what it
    /// was before this setting existed.
    /// </para>
    /// <para>
    /// <b>A per-participant label still wins, whichever way this is set.</b> Naming one person as a
    /// place in the party is how somebody who does not want to appear is kept off a public page
    /// without the whole installation turning the feature off, so the label is read first and this
    /// setting only decides what an unlabelled participant is called.
    /// </para>
    /// </remarks>
    public bool PublishRealNames { get; set; } = true;

    /// <summary>
    /// Whether a published trip says the hour its party planned to be out by. <b>Off unless an
    /// installation turns it on, and off is a decision rather than an oversight</b> — the owner of
    /// this project was asked whether a published page may show the hour a party planned to be out
    /// and answered no, so nothing here is waiting to be finished and the default is not to be
    /// "corrected" to on. The setting stays because it is how an installation that wants it, or a
    /// later change of mind, grants it without new code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off, because it tells people without an account something about the party's plan that no
    /// published page has said before, and whether a club wants that said in public is its own to
    /// decide. Turned off, the followed page and the list of a cave's followed trips carry the
    /// member empty whatever the trip records, and are otherwise exactly what they were.
    /// </para>
    /// <para>
    /// What it widens, exactly: one instant, on the two reads about trips being followed now —
    /// never on a finished trip's replay or on the list of finished trips, where a plan is history
    /// nobody is waiting on. It is published only when it is later than the moment the watch was
    /// started, which is decided by
    /// <see cref="SilexGis.Domain.Trips.TripTrackingRules.PublishedExpectedReturn"/>.
    /// </para>
    /// <para>
    /// <b>It publishes nothing about an overdue check.</b> The alarm hour, where that check
    /// stands and when it was last looked at stay where they are, behind an account; and switching
    /// this on sends nothing, raises nothing and stands nothing down.
    /// </para>
    /// </remarks>
    public bool PublishExpectedReturn { get; set; }

    /// <summary>
    /// Whether a published trip's survey comes with the names the cave has given its places — the
    /// depths it has declared and the stations it has named without a depth.
    /// <b>On unless an installation switches it off.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, because the names are what makes a published page answer the question it was opened
    /// with: a visitor told that the party is at <c>p.g.7</c> learns nothing, and a visitor told
    /// that <c>p.g.7</c> is the foot of the big pitch knows where the party is. The names are
    /// words a club typed for its own members — what it calls a pitch, where it camps — so an
    /// installation that would rather keep them to its members switches this off. Switched off,
    /// the member is null whatever the cave has declared — never a list with no entries, which a
    /// reader keeping names of its own could take as "there are none" — no declaration is read on
    /// a published route at all, and the answer is byte for byte what it was before this setting
    /// existed.
    /// </para>
    /// <para>
    /// What it carries, exactly: for each place the cave has named — with a declared depth, or
    /// named without one — whose station the survey being handed over holds, that name, the
    /// station, the declared depth where there is one (null where the place was named without a
    /// depth), the short note the cave wrote beside the name where it wrote one, and whether the
    /// cave names the place on the drawing itself (the landmark flag) — on the two reads that hand
    /// a survey over, a trip being followed and a finished trip's replay, in the cave's own list
    /// order. The station and its altitude are already in the survey file those reads serve; the
    /// name, the note and the flag are what is new. Which places qualify is decided by
    /// <see cref="SilexGis.Domain.Trips.TrackingDepthPlacements.PublishedPlaces"/>.
    /// </para>
    /// <para>
    /// <b>It opens nothing about a protected cave.</b> A cave whose position is protected has no
    /// published page for these to appear on, whichever way this is set.
    /// </para>
    /// </remarks>
    public bool PublishDepthPlaces { get; set; } = true;

    /// <summary>
    /// Whether a published trip says which caving groups each person named on it belongs to.
    /// <b>Off unless an installation turns it on.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off, because it tells people without an account a fact about each person — whom they cave
    /// with — that no published page has said before, and whether a club wants that said in public
    /// is its own to decide. A club that publishes an expedition's journal wants exactly this beside
    /// each name: its figures are counted per club, and without it the article's page has to keep
    /// its own list of who belongs where and be edited every time somebody joins.
    /// </para>
    /// <para>
    /// What it widens, exactly: each person of a published party who is named by their own name
    /// carries the names of the caving groups they are a member of, in the order of those names, on
    /// the followed page, the list of trips being followed and a finished trip's replay. <b>Never
    /// beside a caption</b>: a caption is how an administrator keeps somebody off a public page, and
    /// a club printed beside "a third of the party" would point at who that is. Never where the
    /// installation publishes no names, for the same reason. Nobody's role in a group, and nothing
    /// else about the group, is said. A person in no group carries nothing rather than an empty
    /// list, so that a page keeping a list of its own is never told to replace it with nothing.
    /// </para>
    /// <para>
    /// Read as each answer is made, so a person who joins or leaves a group is shown so on the next
    /// read of every link already handed out.
    /// </para>
    /// </remarks>
    public bool PublishClubs { get; set; }

    /// <summary>
    /// Whether a published trip's page offers its reader the other parties of the same cave that
    /// are being followed at the same time. <b>On unless an installation switches it off.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, because a cave with two parties in it at once is the case a published page is least
    /// able to answer without it: somebody holding one link reads one party's standing and has no
    /// way of learning that the other party exists, let alone that it is out. The list is read
    /// only once a reader asks for it, and it says of each party no more than the page already
    /// says of the one its own link is about.
    /// </para>
    /// <para>
    /// Switched off, a published page offers no such control and asks for no second party at all:
    /// the list of parties being followed is never read on a visitor's behalf, and a page carries
    /// exactly the trip its link was published for — which is what these pages did before the
    /// control was built. The route that answers that list is withdrawn with it: it refuses every
    /// caller as an address nobody published does, before it reads any link, because a switch that
    /// only stopped the page from asking would leave every party of the cave — and, where the
    /// installation publishes names, the names in them — answered to whoever holds any link of
    /// that cave. Nothing else a link opens is touched: the page itself, the cave's past trips and
    /// a replay all answer as they did.
    /// </para>
    /// </remarks>
    public bool OfferSiblingWatch { get; set; } = true;

    /// <summary>
    /// Whether a published trip's page offers its reader the choice between the two languages it
    /// is written in, and honours a language named in its address. <b>On unless an installation
    /// switches it off.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, because one published page is read by two audiences at once — a club's own members and
    /// the families of whoever is visiting — and a page with no way to change its language serves
    /// whichever audience the installation happened to be set up for and nobody else. The choice
    /// is the reader's own and is remembered for published pages alone.
    /// </para>
    /// <para>
    /// Switched off, no such control is drawn, a language named in an address is ignored, and
    /// every published page is in the language the installation opens in — which is what these
    /// pages did before the control was built. It decides nothing about the signed-in
    /// application, where the language is an account's own choice.
    /// </para>
    /// <para>
    /// <b>One screen keeps the choice whatever this says, and it cannot be otherwise.</b> This
    /// setting reaches a page on the followed trip's own answer, so a page whose link opens
    /// nothing — taken back, run out, a cave since protected — is never told it: the answer that
    /// would have carried it is the answer that was refused, and the only other way to tell such a
    /// page would be a second address answering strangers questions about the installation. So
    /// that one screen keeps its button and still honours a language in the address. Deliberate,
    /// and not only unavoidable: its single sentence is the one sentence on any of these pages a
    /// reader most needs to be able to read.
    /// </para>
    /// </remarks>
    public bool OfferLanguageChoice { get; set; } = true;

    /// <summary>
    /// Whether a published read may answer a reader that already holds the answer by saying so,
    /// instead of sending it again. <b>On unless an installation switches it off.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, because a page watching a party underground asks the same question every few seconds
    /// for hours while the answer changes only when somebody reports. The short "nothing changed"
    /// answer is what keeps a family's phone usable on a cave-mouth connection, and what keeps a
    /// link printed in a club's article from costing the installation a survey's worth of reading
    /// per reader per minute.
    /// </para>
    /// <para>
    /// Switched off, no answer carries a validator, no read is ever answered with "nothing
    /// changed", and every read is answered in full and kept by nobody — which is what these
    /// routes did before this was built. An installation sitting behind a cache it does not
    /// control, or chasing a reader that was shown a page out of date, switches it off and pays
    /// the difference in bytes.
    /// </para>
    /// <para>
    /// It covers the published reads together rather than one at a time: a reader holding a kept
    /// copy of one of them and none of the others would be the worst of both.
    /// </para>
    /// </remarks>
    public bool TellReadersNothingChanged { get; set; } = true;

    /// <summary>
    /// How long a follow link goes on working after the trip it is about. <b>Two weeks unless an
    /// installation says otherwise.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted from the end of the trip's last day rather than from the moment of minting, so the
    /// same number means the same thing for an afternoon in a local cave and for a three-week
    /// expedition — see
    /// <see cref="SilexGis.Domain.Trips.TripPublicationWindow.ExpiresAtFor"/>.
    /// </para>
    /// <para>
    /// Two weeks is chosen against what the link is for rather than against a sense of tidiness. A
    /// published page is written for the people waiting while a party is underground; by a
    /// fortnight afterwards every one of them has long since read whatever it had to say, and what
    /// remains is an address sitting in a club's article, indexed and archived, answering strangers
    /// with a party's names and positions. The cost of being wrong in that direction falls on
    /// people who did not choose to be on the page, so the default is the shorter one and lengthening
    /// it is a decision an installation takes out loud.
    /// </para>
    /// <para>
    /// It is a backstop and not the usual end of a publication — the watch closing gets there first
    /// on nearly every real trip. What this covers is the watch that is never closed at all.
    /// </para>
    /// </remarks>
    public TimeSpan ShareLifetime { get; set; } = TripPublicationWindow.DefaultShareLifetime;

    /// <summary>
    /// How long a followed page keeps answering after the watch is closed. <b>No time at all unless
    /// an installation sets one.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// While it lasts, a closed trip is still on its own page — saying that everybody is out — and
    /// still in the list of trips being followed, and it is <em>not</em> yet among the cave's past
    /// trips: the archive begins exactly where the live page ends, so no trip is ever both.
    /// </para>
    /// <para>
    /// <b>Zero is the default because of what the gap costs.</b> It used to be two days, argued
    /// from the people who had been refreshing the page all evening: the moment the watch closes
    /// is the moment the page has its most important thing to say. But for those two days the trip
    /// could not be played back from anywhere a reader without an account could reach, and a club
    /// that enters a finished expedition's trips after the fact — every one of them closed long
    /// after its last day — published an archive that stayed empty for two days with nothing
    /// saying why. With no grace a closed trip is a past trip at once, and the same link that
    /// followed the party opens it there, where it reads as finished and can be replayed. That
    /// needs the archive of past trips to be on, which it is unless an installation turns it off;
    /// with the archive off and no grace, a link answers nothing from the moment its watch closes.
    /// </para>
    /// <para>
    /// An installation that wants the followed page itself to say "everybody is out" for a while
    /// sets a period here. Never longer than <see cref="ShareLifetime"/> allows: the expiry is the
    /// outer bound and this window cannot push past it.
    /// </para>
    /// </remarks>
    public TimeSpan ShareGraceAfterClose { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// How long after its own trip is over a link goes on listing the parties being followed in
    /// its cave right now. <b>Unset, which means no limit.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A link is minted for one trip and lives in an article. When that trip is over the link
    /// still opens the cave's past trips and, beside them, who is underground in that cave at this
    /// moment — which is what lets one article show a whole camp while it runs. Left without a
    /// limit, an article from years ago goes on naming tonight's party. This is the period after
    /// which it stops, counted from the end of the link's own trip; the past trips themselves stay
    /// readable for as long as the archive keeps them.
    /// </para>
    /// <para>
    /// <b>Why unset is the default.</b> How long an old address should go on saying who is in a
    /// cave is a question about a club's readers and the people it names, and no number chosen
    /// here would be right for both a club that publishes one camp a year and one that wants its
    /// journal page to work indefinitely. Unset changes nothing for an installation that has not
    /// decided.
    /// </para>
    /// <para>
    /// A link still following its own party is never affected, whatever this says. Zero is
    /// allowed: a link then stops listing other parties once its own trip's last day is over and
    /// its own page has ended.
    /// </para>
    /// </remarks>
    public TimeSpan? SiblingWindowAfterLapse { get; set; }

    /// <summary>
    /// The largest list of concurrently-followed trips this surface will serve however it is
    /// configured.
    /// </summary>
    /// <remarks>
    /// Lower than the archive's bound, and for a reason that is about cost rather than about
    /// disclosure: a row of the archive is a title and a headcount, while a row here is a whole
    /// party with a position each, folded from that trip's own report log. Fifty parties in one cave
    /// at one moment is already far past anything a club does; a bound an operator cannot raise is
    /// what keeps an anonymous, unauthenticated read from being asked to fold an unbounded number of
    /// them.
    /// </remarks>
    public const int MaxFollowedListSize = 50;

    /// <summary>
    /// How many concurrently-followed trips of one cave a list response carries. Clamped to
    /// <see cref="MaxFollowedListSize"/>.
    /// </summary>
    /// <remarks>
    /// Twenty, because the real number is one or two and the value of a larger default is only that
    /// a club running an unusually busy camp is not quietly told a lie about who is underground.
    /// The response says whether there are more and never how many, for the reason the archive list
    /// gives: how much a club is doing is itself a disclosure.
    /// </remarks>
    public int FollowedListSize { get; set; } = 20;

    /// <summary>
    /// The followed-list size actually served: what the operator asked for, held between one and the
    /// bound above.
    /// </summary>
    /// <remarks>
    /// Clamped where it is read rather than validated at startup, matching the archive's own
    /// handling: a mistyped number should serve a sane list rather than refuse to start an
    /// installation whose live tracking is otherwise working.
    /// </remarks>
    public int EffectiveFollowedListSize => Math.Clamp(FollowedListSize, 1, MaxFollowedListSize);

    /// <summary>
    /// How long somebody underground may go unreported before the coordinator's watch marks them
    /// as not heard from. <b>Three hours; zero switches the mark off.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a mark for the signed-in people running the watch and nothing more: nothing is sent,
    /// no alarm is raised or stood down, and no page a visitor without an account reads says
    /// anything about it. So the number is a matter of what a club finds worth a glance, which is
    /// why an installation sets it — a club whose trips are all in one short cave and a club
    /// running multi-day pushes below a camp do not mean the same thing by a long silence.
    /// </para>
    /// <para>
    /// Three hours because word from underground arrives by relay, at the pace of somebody
    /// climbing to where a telephone works: an hour without a report is ordinary on every trip,
    /// and a mark that is lit most of the time is a mark nobody reads.
    /// </para>
    /// </remarks>
    public TimeSpan QuietAfter { get; set; } = TimeSpan.FromHours(3);

    /// <summary>
    /// The threshold actually applied: what the operator asked for, with anything below zero read
    /// as zero — the mark switched off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A negative duration is held to zero where it is read rather than refused at startup: off
    /// is the side to fail to, because the other reading of a nonsense threshold marks everybody
    /// at once.
    /// </para>
    /// <para>
    /// <b>That is the only mistake this absorbs.</b> A value that is not a duration at all
    /// (<c>3h</c>) never gets here: it cannot be read, and the application refuses to start on
    /// it, naming the setting — as it does for an unreadable link lifetime, grace window or period
    /// after a lapse above (see <see cref="TripTrackingOptionsValidator.RefuseUnreadablePeriods"/>).
    /// And a bare number is a number of days, so <c>3</c> is three days and in practice the mark
    /// switched off. The accepted form is <c>[d.]hh:mm:ss</c>.
    /// </para>
    /// </remarks>
    public TimeSpan EffectiveQuietAfter => QuietAfter > TimeSpan.Zero ? QuietAfter : TimeSpan.Zero;
}
