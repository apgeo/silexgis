// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// What an installation has decided about its own archive of past tracks.
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate class and a separate configuration section from
/// <see cref="TripTrackingOptions"/>, deliberately.</b> These govern the second of the two
/// lifetimes a published trip has — how long its track stays readable <em>after</em> it is over —
/// and the settings of the first one govern whether a party can be followed while they are
/// underground. Putting them in one class would leave the two a careless read apart, and the
/// careless read is the one where a live follow window quietly extends itself.
/// </para>
/// <para>
/// Nothing here can widen the live window, and nothing in the live window's settings can shorten
/// this one. They are asked by different rules about different questions.
/// </para>
/// </remarks>
public sealed class TripPastTrackOptions
{
    public const string SectionName = "TripPastTracks";

    /// <summary>
    /// The largest list this surface will serve however it is configured.
    /// </summary>
    /// <remarks>
    /// A published page is a picker, not a bulk export, and the list is anonymous: a bound an
    /// operator cannot raise is what keeps a link somebody put in an article from becoming the
    /// cheapest way to read a club's whole register in one request.
    /// </remarks>
    public const int MaxListSize = 200;

    /// <summary>
    /// Whether past tracks are readable at all on this installation. <b>On unless it says
    /// otherwise.</b>
    /// </summary>
    /// <remarks>
    /// Off, both past-track routes answer the same 404 an unknown token gets — the feature is not
    /// there rather than there and empty. It is the one lever an installation has over the
    /// archive's existence as a whole, which is why it is a real setting and not a speculative
    /// flag: there is no per-trip archive act, so without this there would be no way to say no.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a trip stays readable as a past track after it is over. <b>No limit unless an
    /// installation sets one.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted from the end of the trip's last day, through the same reading of "when was the trip
    /// over" the live link's expiry counts from — see
    /// <see cref="SilexGis.Domain.Trips.TripPublicationWindow.EndOfTrip"/>.
    /// </para>
    /// <para>
    /// <b>No limit is the default, and it is the one default here that runs against this
    /// application's usual instinct</b>, so the argument is written down rather than left to be
    /// re-derived. The live link's own lifetime defaults to the short value because the cost of
    /// being wrong falls on people who did not choose to be on the page. That reasoning still
    /// applies — but a finite default here would silently discard the whole point of an archive,
    /// which was asked for so that a club's published history can be read over time; to a club it
    /// would look like their history disappearing for no stated reason. The disclosure this
    /// governs is also narrower: an archived trip is over, nobody is being followed, and a
    /// participant kept off the live page by a caption is kept off this one by the same caption.
    /// Setting a finite value is one line of configuration and the code path is identical either
    /// way.
    /// </para>
    /// </remarks>
    public TimeSpan? Retention { get; set; }

    /// <summary>
    /// How many past trips one response carries. Clamped to <see cref="MaxListSize"/>.
    /// </summary>
    /// <remarks>
    /// Enough to pick from. The response says whether older ones exist and deliberately never says
    /// how many: "this club has been here 412 times" is a disclosure that a bit saying "there is
    /// more" is not.
    /// </remarks>
    public int ListSize { get; set; } = 50;

    /// <summary>
    /// The list size actually served: what the operator asked for, held between one and the bound
    /// above.
    /// </summary>
    /// <remarks>
    /// Clamped where it is read rather than validated at startup, because a mistyped number here
    /// should serve a sane list rather than refuse to start an installation whose live tracking is
    /// working — and because zero would otherwise be a way to turn the feature half off, which is
    /// what <see cref="Enabled"/> is for.
    /// </remarks>
    public int EffectiveListSize => Math.Clamp(ListSize, 1, MaxListSize);
}
