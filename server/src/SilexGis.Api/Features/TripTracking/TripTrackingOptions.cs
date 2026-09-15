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
}
