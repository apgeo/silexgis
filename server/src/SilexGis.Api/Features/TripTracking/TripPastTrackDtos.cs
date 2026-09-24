// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.TripTracking;

// ---- the list of past trips ----

/// <summary>
/// The trips of one cave that are over, published, and still readable — as somebody holding a
/// link to any published trip of that cave sees them.
/// </summary>
/// <remarks>
/// <para>
/// Declared apart from both the signed-in shapes and the live public envelope, for the reason the
/// live envelope already gives: a field added to a neighbouring surface must not arrive on an
/// anonymous one by accident of another slice's evolution. That is the way a public response
/// actually grows a leak.
/// </para>
/// <para>
/// <b>What this list says, in full: that a club published a trip to this cave on these dates.</b>
/// It names nobody, carries no position, and holds no trip that was not published, none whose
/// links have all been revoked, and none that is still being followed.
/// </para>
/// </remarks>
/// <param name="More">
/// True when older past trips exist that this response does not carry — and deliberately not a
/// count. How many times a club has been into one cave is a disclosure; that there is something
/// older is not, and it is all a picker needs in order to say so.
/// </param>
public sealed record PublicPastTripListDto(
    IReadOnlyList<PublicPastTripDto> Trips,
    bool More);

/// <summary>
/// One past trip, as a row somebody picks from.
/// </summary>
/// <param name="TripLogId">
/// The trip's own identifier, which is what the playback route takes.
/// <para>
/// A deliberate departure from the live page's "the URL identity is an opaque token, never the
/// trip id", so the argument is written down. This list has already disclosed that the trip
/// exists, its title and its dates, so the id adds no fact to that; every other route that takes a
/// trip id requires an account and filters by visibility, so the id opens nothing on its own; and
/// the token, not the id, is still the whole of the secret. Guessing one buys nothing: the
/// playback route refuses any trip that is not of this token's cave and not readable as past,
/// with the same 404 an unknown token gets.
/// </para>
/// </param>
/// <param name="ClosedAt">Where the watch was closed, and null for a watch nobody closed.</param>
/// <param name="ParticipantCount">
/// How many people the trip's roster holds. A number, and it names nobody — the party is named, as
/// far as it is named at all, only inside a track somebody has chosen to open.
/// </param>
/// <param name="Playable">
/// At least one report placed somebody inside this cave, on the survey the playback of this trip
/// would hand over — so there is a track that will actually draw. Both halves are load-bearing:
/// a report anchored to another cave is withheld per row, and a report measured against a survey
/// that was later replaced is known but not drawable on the one handed over, and either kind alone
/// makes a playback that opens empty. False is an ordinary answer: a trip can be published,
/// followed and closed with nothing but entries and exits recorded.
/// </param>
public sealed record PublicPastTripDto(
    Guid TripLogId,
    string Title,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    DateTimeOffset? ClosedAt,
    int ParticipantCount,
    bool Playable);

// ---- one past track, played back ----

/// <summary>
/// One past trip's whole track: the party by their place in it, and where each of them was
/// reported over time, drawn on the survey their reports were actually measured in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the past trip's own, never the live page's.</b> A trip tracked against a survey
/// that has since been superseded must be drawn on that survey or not at all: handing over the
/// current model would draw one survey's station paths on another, which is a confident statement
/// about where somebody was assembled out of a collision of names.
/// </para>
/// <para>
/// <b>What is absent here is absent on purpose.</b> No caver id, ever — the party is keyed by a
/// place in it, exactly as the live page keys them. No note, because a note is a coordinator's
/// free text about a person during a callout and has never been on a public surface. No report
/// kind and no provenance: "this one came out of a phone" is a fact about the party's movements,
/// and what a reader needs is whether somebody was in or out, not the log's vocabulary.
/// </para>
/// </remarks>
/// <param name="PositionsWithheld">
/// True when at least one report placed somebody and its place could not be shown here — the same
/// meaning the live envelope gives the same name. It covers protection and nothing else;
/// truncation is <paramref name="TrackTruncated"/>.
/// </param>
/// <param name="TrackTruncated">
/// True when the log is longer than one response carries, so the end of the track is missing. Said
/// out loud because a playback that simply stopped would read as a party who stopped being heard
/// from, which is a different and much worse statement.
/// </param>
public sealed record PublicPastTrackDto(
    /// <summary>
    /// The trip this track is of — the same id the caller named to ask for it, answered back so
    /// that a page folding this into an envelope can carry it there rather than losing it.
    /// </summary>
    /// <remarks>
    /// The list that leads here already disclosed this id, and the caller supplied it in the
    /// address; saying it again adds no fact. What it buys is that every envelope-shaped answer on
    /// this surface — followed and replayed alike — is identified the same way, so a page holding
    /// several of them cannot come to match them by title.
    /// </remarks>
    Guid TripLogId,
    string Title,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    DateTimeOffset? ArmedAt,
    DateTimeOffset? ClosedAt,
    bool PositionsWithheld,
    bool TrackTruncated,
    PublicTripSurveyModelDto? Model,
    IReadOnlyList<PublicTripTeamDto> Teams,
    IReadOnlyList<PublicPastTrackParticipantDto> Participants);

/// <summary>
/// One member of a past party and everything that was reported about them, oldest first.
/// </summary>
/// <param name="Ordinal">
/// Their place in the party, counted in the order the roster was written — the same number the
/// live page shows for the same person on the same trip, because both surfaces derive it in one
/// place. Across different trips it means nothing and a reader should not be told otherwise.
/// </param>
/// <param name="Label">
/// What this page calls them: the caption an administrator typed, failing that the roster's own
/// name where the installation publishes names, and null where neither applies. The same resolver
/// the live page calls, with the same inputs — so somebody kept off a live page by a caption is
/// kept off this one, and a caption added after the trip takes effect here immediately.
/// </param>
public sealed record PublicPastTrackParticipantDto(
    int Ordinal,
    string? Label,
    IReadOnlyList<PublicPastTrackFixDto> Track);

/// <summary>
/// One moment of one person's track.
/// </summary>
/// <remarks>
/// Only moments a playback can show: a report that claimed a place, or one that said somebody went
/// in or came out. A note moves nothing and shows nothing, so it is not emitted — and because the
/// rule that folds a standing is the one that says a note moves nothing, dropping notes changes no
/// value below.
/// </remarks>
/// <param name="RecordedAt">
/// When this report was made — the per-row form of the live page's "last heard from", which that
/// page also sends unconditionally, and deliberately not the live page's second instant that dates
/// a shown position and is withheld with it. There is no single station here to misdate: this row
/// carries its own hour beside its own <paramref name="StationName"/>, null or not. Nor would
/// dropping it hide anything, the track being ordered: a placeless row sits between the fixes
/// either side of it with or without a clock.
/// </param>
/// <param name="StationName">
/// The station this report placed them at, in the viewer's own spelling, or null — which means
/// "no place was claimed", "the place cannot be shown here" and "the place was measured on another
/// survey" alike, the last of those being what <paramref name="PositionOnOtherModel"/> is for.
/// </param>
/// <param name="PositionOnOtherModel">
/// True when this report was measured against a survey other than the one handed over, so the
/// place is known and cannot honestly be drawn here. Never true beside a place withheld for
/// protection: a second bit explaining that absence would give back exactly what the withholding
/// kept.
/// </param>
/// <param name="In">Where this person stood after this report: inside the cave.</param>
/// <param name="Out">
/// Where this person stood after this report: out. Both false is the third state and a real
/// answer — nothing had yet said they went in, came out, or were anywhere.
/// </param>
public sealed record PublicPastTrackFixDto(
    DateTimeOffset RecordedAt,
    Guid? TeamId,
    string? StationName,
    decimal? DepthM,
    bool PositionOnOtherModel,
    bool In,
    bool Out);
