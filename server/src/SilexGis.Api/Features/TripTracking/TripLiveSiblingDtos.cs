// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// The trips of one cave that are being followed right now — as somebody holding a link to any
/// published trip of that cave sees them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The other half of a pair, and it is worth saying which half.</b> The archive route beside
/// this one answers what this cave's finished trips were; this one answers who is in it now. The
/// two rules underneath them partition a published trip's life — followable while its publication
/// window is open, readable as past once that window has shut, never both and never neither while
/// the retention allows — so a page may read both lists and trust that nothing is counted twice and
/// nothing falls between them. A trip moving from this list to that one as its grace window runs
/// out is the ordinary course of events, not a gap, and a page matching rows across the two must do
/// it by <c>tripLogId</c> rather than by title.
/// </para>
/// <para>
/// <b>Why this exists at all, stated plainly.</b> A share token names exactly one trip, so a club
/// running two parties into the same cave on the same day had no way to show both on one page: each
/// link followed its own party and could see the other only once it was over. A page could be given
/// both tokens, and for a fixed pair of trips that works — but a camp gains trips while it is
/// running, and configuration that has to be edited every time is configuration that will be
/// out of date on the day it matters. This list is how one link keeps up.
/// </para>
/// <para>
/// <b>What it discloses, in full: that a club is in this cave now, under these trip titles, with
/// these parties.</b> That is the same class of fact the archive list already publishes about
/// finished trips, decided by the same publication rules, and about a cave the holder of this token
/// is already following a party in. It carries no cave identity, no survey identity and no caver
/// identity, and every position in it has been through the same per-row withholding the followed
/// page applies.
/// </para>
/// </remarks>
/// <param name="More">
/// True when more followed trips exist than this response carries — and deliberately not a count,
/// for the reason the archive list gives the same field: how much a club is doing is itself a
/// disclosure, and a picker only needs to know it is not seeing everything.
/// </param>
public sealed record PublicLiveTripListDto(
    IReadOnlyList<PublicLiveTripDto> Trips,
    bool More);

/// <summary>
/// One trip being followed right now, with its party as this page may show it.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no model on this shape, and its absence is the design.</b> The token's own followed
/// route hands over exactly one survey, and that is the survey a page has loaded. So every position
/// here has been decided against <em>that</em> survey rather than against each trip's own: a trip
/// whose reports were measured on a different one comes back placeless, with
/// <see cref="PublicTripParticipantDto.PositionOnOtherModel"/> set, which is the rule the followed
/// page already applies to a watch re-pointed mid-trip, applied across trips instead of within one.
/// A reader is therefore never shown a station name from one survey drawn on another — and a page
/// needs no survey identity to protect itself, because it was never given a position it cannot
/// honestly draw.
/// </para>
/// <para>
/// <b>The token's own trip is in this list.</b> One uniform shape, no special case for the row the
/// caller happens to hold the link to — which is what <see cref="TripLogId"/> on the followed
/// envelope is for. After the first read, which is where the survey comes from, a page can poll
/// this route alone.
/// </para>
/// </remarks>
/// <param name="State">
/// The watch. <b>Both values are ordinary answers and a page must tell them apart:</b> a trip
/// whose watch is armed has a party underground, and one recorded as closed is inside the short
/// grace window after "everybody out" — still the live thing a reader came for, for a few hours,
/// and then gone from this list into the archive. A page that drew the second as though it were the
/// first would say somebody is underground who came out this morning, which is the one false
/// statement this surface can make.
/// </param>
public sealed record PublicLiveTripDto(
    Guid TripLogId,
    string Title,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    SilexGis.Domain.Entities.TripTrackingState State,
    DateTimeOffset? ArmedAt,
    DateTimeOffset? ClosedAt,
    bool PositionsWithheld,
    IReadOnlyList<PublicTripTeamDto> Teams,
    IReadOnlyList<PublicTripParticipantDto> Participants);
