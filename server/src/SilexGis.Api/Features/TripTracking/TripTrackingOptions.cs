// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// What an installation has decided a published trip may say about the people on it.
/// </summary>
/// <remarks>
/// One setting, and it is here rather than in the domain because it is a choice the people running
/// this server make once, not a rule about caves or trips.
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
    public TimeSpan ShareLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How long a followed page keeps answering after the watch is closed. <b>Two days.</b>
    /// </summary>
    /// <remarks>
    /// Not zero, and that is the whole reason it is a setting rather than an absence. The moment a
    /// coordinator closes the watch is the moment the page has its most important thing to say —
    /// everybody is out — and it is the moment the people who have been refreshing it all evening
    /// are looking at it. A page that answered nothing the instant the party came out would fail
    /// precisely the readers it exists for, and would look exactly like the party having stopped
    /// being reported. Two days lets anybody who was following read the ending, and then it stops.
    /// Never longer than <see cref="ShareLifetime"/> allows: the expiry is the outer bound and this
    /// window cannot push past it.
    /// </remarks>
    public TimeSpan ShareGraceAfterClose { get; set; } = TimeSpan.FromDays(2);

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
}
