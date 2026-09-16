// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.TripTracking;

// ---- management ----

/// <summary>
/// Mint response — the only moment the plaintext token exists in a response. It is never stored
/// (only its hash is), so it cannot be shown again; whoever minted it must copy it now.
/// </summary>
public sealed record TripTrackingShareCreatedDto(Guid Id, string Token, DateTimeOffset CreatedAt);

/// <summary>Publication-link metadata for the managing list — deliberately token-free.</summary>
public sealed record TripTrackingShareDto(
    Guid Id,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

// ---- the published page ----

/// <summary>
/// A tracked trip as somebody holding its link sees it, carrying nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Declared here rather than shared with the authenticated state read, and that is the whole
/// point of it. <see cref="TrackingParticipantDto"/> is keyed by <c>caverId</c> — a real
/// identity, and the one thing this response must never carry — so the two shapes are kept
/// apart by construction: a field added to the signed-in surface cannot arrive on this one by
/// accident of another slice's evolution, which is the way a public response actually grows a
/// leak.
/// </para>
/// <para>
/// Nothing here is optional-by-protection. A trip whose cave carries any protection is not
/// published at all, so there is no obfuscated form of this envelope to get wrong;
/// <see cref="PositionsWithheld"/> covers only the narrower case of a position row whose own
/// anchor has since gone or names a different, protected cave.
/// </para>
/// </remarks>
public sealed record PublicTripTrackingEnvelopeDto(
    string Title,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    TripTrackingState State,
    DateTimeOffset? ArmedAt,
    DateTimeOffset? ClosedAt,
    /// <summary>True when at least one position existed but could not be shown here.</summary>
    bool PositionsWithheld,
    PublicTripSurveyModelDto? Model,
    IReadOnlyList<PublicTripTeamDto> Teams,
    IReadOnlyList<PublicTripParticipantDto> Participants);

/// <summary>
/// Enough of the survey model for the embedded viewer to draw it, and no more: no name, no
/// description, no processing state, nothing about who uploaded it.
/// </summary>
/// <param name="ModelUrl">
/// A short-lived signed delivery URL for the survey file. It expires well before a followed page
/// is closed, so re-reading this envelope is how a page gets a live one — there is deliberately
/// no route that re-mints it on its own.
/// <para>
/// A URL handed out is a decision already taken and the route that redeems it re-decides
/// nothing, so a publication refused after this envelope was built closes the page but not a
/// URL already in somebody's hands: that one keeps working until it expires. The window is what
/// the signing lifetime says and no longer, which is why the envelope hands out nothing else and
/// why a protected cave is refused publication rather than published without its drawing.
/// </para>
/// </param>
/// <param name="Proj4">
/// The PROJ.4 definition of <paramref name="SourceEpsg"/>, resolved from the database that ships
/// with this application and carried here rather than fetched. The route that answers CRS
/// definitions is not on the anonymous allow-list and is not being put there for one page: a
/// string in the envelope widens the anonymous surface by nothing at all, where opening an
/// enumerable route widens it for every caller on the internet.
/// </param>
public sealed record PublicTripSurveyModelDto(
    SurveyModelFormat Format,
    string ModelUrl,
    string? MeshUrl,
    double? AnchorLongitude,
    double? AnchorLatitude,
    double? AnchorHeightM,
    int? SourceEpsg,
    string? Proj4);

public sealed record PublicTripTeamDto(Guid Id, string Title);

/// <summary>
/// One member of the party as followers see them.
/// </summary>
/// <param name="Ordinal">
/// Their place in the party, counted in the order the roster was written. The identity this
/// response is keyed by — a number that means nothing outside this page — and the only thing a
/// follower is told about somebody the envelope carries no name for.
/// </param>
/// <param name="Label">
/// What this page calls them: the label an administrator typed, failing that the roster's own
/// name for them where the installation publishes names, and null where neither applies.
/// <para>
/// <b>Null is a real answer and stays one.</b> It is what an installation that does not publish
/// names sends for everybody, what a participant an administrator has deliberately named as a
/// place in the party sends, and what a roster row with a blank name sends. A viewer given null
/// names them from <paramref name="Ordinal"/> in its own language; the server still does not
/// invent a display string, because it has no caller's language to invent one in — what it sends
/// where it sends a name is a name somebody wrote down, never a rendering of a number.
/// </para>
/// </param>
/// <param name="LastRecordedAt">
/// When anything was last heard about them, whatever it said — the same "last word" the
/// signed-in read shows, which can be later than the report that placed them.
/// </param>
/// <param name="PositionRecordedAt">
/// When the report that <em>placed</em> them was made — the recorded time of the very row
/// <paramref name="StationName"/> and <paramref name="DepthM"/> were read off.
/// <para>
/// <b>Two times, because the position and the last word are routinely two different reports.</b>
/// The position stays the latest report that actually claimed a place, while
/// <paramref name="LastRecordedAt"/> follows every report — so one note later, a station heard
/// four hours ago would sit beside a timestamp eight minutes old. This page is read by the
/// families of people underground, who are reading the time to judge how old the place beside it
/// is: that reading is only true of this field. Anything ageing a position reads this one.
/// </para>
/// <para>
/// Null on exactly the branch that nulls the station, so that no surface can put an age on a
/// place this page was refused. <b>Consistency, not confidentiality</b> — do not cite it as a
/// protection: <paramref name="LastRecordedAt"/> is unconditional, and whenever the placing
/// report is also the latest report it carries the same instant. Keeping the last-heard time for
/// a follower who cannot be shown a position is the deliberate choice, here and in the audit
/// timeline's rule: a page whose whole purpose is that somebody is still being heard from must
/// keep saying when. Null here means "nothing has placed them" and "the place cannot be shown
/// here" alike.
/// </para>
/// </param>
public sealed record PublicTripParticipantDto(
    int Ordinal,
    string? Label,
    Guid? TeamId,
    string? StationName,
    decimal? DepthM,
    DateTimeOffset? LastRecordedAt,
    DateTimeOffset? PositionRecordedAt,
    /// <summary>
    /// The last report that <em>stated</em> a standing put them inside the cave, or nothing has
    /// stated one and a report has placed them inside it. False together with <see cref="Out"/> is
    /// the third state and a real answer: nobody has said yet that they went in, came out, or were
    /// anywhere.
    /// </summary>
    bool In,
    /// <summary>
    /// The last report that stated a standing was that they are out. A later note, and a later
    /// report of a place, both leave it standing — only a recorded entry puts somebody back
    /// underground, so this page never moves a person between the counts unexplained.
    /// </summary>
    bool Out);
