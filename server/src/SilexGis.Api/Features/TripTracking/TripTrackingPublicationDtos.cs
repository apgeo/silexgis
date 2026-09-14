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
/// response is keyed by: a number that means nothing outside this page, so an envelope with no
/// labels set discloses that somebody is at a station and not who.
/// </param>
/// <param name="Label">
/// What an administrator chose to call them, or null where nobody chose — which is the default,
/// and the reason this surface is safe by construction rather than by care. A viewer with no
/// label names them from <paramref name="Ordinal"/> in its own language; the server does not
/// invent a display string, because it has no caller's language to invent one in.
/// </param>
/// <param name="LastRecordedAt">
/// When anything was last heard about them, whatever it said — the same "last word" the
/// signed-in read shows, which can be later than the report that placed them.
/// </param>
public sealed record PublicTripParticipantDto(
    int Ordinal,
    string? Label,
    Guid? TeamId,
    string? StationName,
    decimal? DepthM,
    DateTimeOffset? LastRecordedAt,
    /// <summary>Somebody has been reported underground and not reported out since.</summary>
    bool In,
    /// <summary>The last word about them was that they are out.</summary>
    bool Out);
