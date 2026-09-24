// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.TripTracking;

// ---- management ----

/// <summary>
/// Mint response — the only moment the plaintext token exists in a response. It is never stored
/// (only its hash is), so it cannot be shown again; whoever minted it must copy it now.
/// </summary>
/// <param name="ExpiresAt">
/// When this link stops opening the page whatever else happens. Returned with the token because it
/// is part of what was just handed out: whoever is about to paste an address into a club's article
/// has to be able to write "this link works until …" beside it, and this is the only response that
/// can tell them. A publication usually ends earlier than this — closing the watch gets there first
/// — so it is an outer bound rather than a promise about how long the page will answer.
/// </param>
public sealed record TripTrackingShareCreatedDto(
    Guid Id,
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Publication-link metadata for the managing list — deliberately token-free.</summary>
/// <param name="ExpiresAt">
/// When this link lapses. Carried so the list can say which of a trip's links is still live without
/// the reader doing arithmetic, and so that a link that ended on its own is visibly distinguishable
/// <em>here</em> from one somebody revoked — which is a distinction the administrator may see and
/// a follower may not.
/// </param>
public sealed record TripTrackingShareDto(
    Guid Id,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset ExpiresAt);

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
    /// <summary>
    /// The trip this envelope is about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The token's own trip, so this tells its holder nothing they were not already holding</b> —
    /// which is the whole of the argument for it, and it is the argument the archive list next door
    /// already makes for the same disclosure. Every other route that takes a trip id requires an
    /// account and filters by visibility, so the id opens nothing on its own; the token, not the id,
    /// remains the entire secret.
    /// </para>
    /// <para>
    /// <b>Why a page needs it.</b> A single article may follow several parties in one cave at once,
    /// each through its own link, and the same trip appears in this shape while it is being followed
    /// and in the archive list once it is over. Without an id a page can only match the two by
    /// title — and two trips of one cave may share a title, while one trip's title may be corrected
    /// mid-watch. Both produce a page that silently draws the wrong party, which is the one failure
    /// this surface must not have.
    /// </para>
    /// </remarks>
    Guid TripLogId,
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
/// <param name="Pictures">
/// The photographs this page hangs on the drawing's stations, and never anything else. Empty is
/// the ordinary answer and a correct one: a club that has published no photographs has none here.
/// <para>
/// Carried in the envelope for the same reason <paramref name="Proj4"/> is. The route a station's
/// pictures are otherwise read from takes an account, and opening it — or any enumerable route
/// over what is linked to a model — would widen the anonymous surface for every caller on the
/// internet in order to serve one page. A list in this response widens it by nothing at all.
/// </para>
/// </param>
/// <param name="RasterMaps">
/// The scanned map sheets declared on this model, each with the station points defined on the
/// rendering being served — the drawings a followed page shows the party on beside the 3D one.
/// Empty is the ordinary answer: most models have no scanned maps declared, and a page given
/// none shows the 3D drawing exactly as it always has.
/// <para>
/// Carried in the envelope for the reason <paramref name="Proj4"/> and <paramref name="Pictures"/>
/// are: the route the signed-in surfaces read a model's map links from takes an account, and
/// opening it — or any enumerable route over what is linked to a model — would widen the anonymous
/// surface for every caller on the internet in order to serve one page. A list in this response
/// widens it by nothing at all.
/// </para>
/// <para>
/// <b>Provisional pending a publication-consent decision, by owner instruction (2026-09-22).</b>
/// Which declared maps and points a trip's publication covers is a protection question the owner
/// has deferred; until it is decided, every map declared on the published model travels, gated
/// only by what this surface already enforces — the whole-page refusal for a protected cave,
/// the link-level guard on links naming guarded features, and renderings-only delivery.
/// </para>
/// </param>
public sealed record PublicTripSurveyModelDto(
    SurveyModelFormat Format,
    string ModelUrl,
    string? MeshUrl,
    double? AnchorLongitude,
    double? AnchorLatitude,
    double? AnchorHeightM,
    int? SourceEpsg,
    string? Proj4,
    IReadOnlyList<PublicTripStationPictureDto> Pictures,
    IReadOnlyList<PublicTripRasterMapDto> RasterMaps);

/// <summary>
/// The view a published map sheet declares itself to be — its seeded relation code read out,
/// spelled as the envelope's own vocabulary so this shape cannot come to carry a code the
/// page never meant to publish.
/// </summary>
public enum PublicTripMapViewKind
{
    Plan,
    Profile,
    Other,
}

/// <summary>
/// One scanned map sheet a followed page draws the party on.
/// </summary>
/// <remarks>
/// A map is a document whose current file is an image, declared the plan/profile/other view of
/// the survey model by a link under a seeded code; this is that declaration read back for a
/// caller who can read no links. Everything a sheet needs is resolved here — which rendering,
/// which points — because the envelope is the one thing a visitor can read, and a page that
/// filtered or resolved on the client would be trusting the one party this surface exists not
/// to trust.
/// </remarks>
/// <param name="Title">
/// The map document's title, or null where it has none. A map is titled by its document — the
/// same one name every signed-in surface shows — and the tab a follower presses has to say
/// which sheet it opens.
/// </param>
/// <param name="ImageUrl">
/// A short-lived signed URL for a <em>rendering</em> of the map image, and deliberately never
/// for the upload — the exact reach the station pictures above are minted with, for the same
/// reason: a scan's own bytes carry whatever its file format recorded, and the rendering is
/// produced by this application with every metadata profile stripped. The width in the URL is
/// a starting point; a viewer re-points the same URL at another offered width, spending the
/// token it was handed rather than asking for a second one.
/// </param>
/// <param name="Points">
/// The station points defined on the very rendering <paramref name="ImageUrl"/> serves. Points
/// measured against a superseded scan of the same document are resolved out here, server-side:
/// fractions of last year's scan mean nothing on this year's, and an anonymous page has no
/// version history to ask. One point per station — where duplicates exist the newest pin wins,
/// the same rule the signed-in fold applies — so two surfaces showing one map cannot disagree
/// about where a station sits on it.
/// </param>
public sealed record PublicTripRasterMapDto(
    string? Title,
    PublicTripMapViewKind ViewKind,
    string ImageUrl,
    IReadOnlyList<PublicTripMapPointDto> Points);

/// <summary>
/// One station's point on one published map sheet: the station spelled the way the anchor
/// stores it (the viewer's own spelling, passed through exactly as the pictures' station is),
/// at fractions 0–1 of the drawn picture with the origin at its top-left.
/// </summary>
public sealed record PublicTripMapPointDto(
    string Station,
    double X,
    double Y);

/// <summary>
/// One photograph a followed page shows at one station of the drawing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a photograph somebody has already published.</b> A station's pictures are authored as
/// links, by whoever was on the trip, and go on being authored after a follow link is handed out —
/// so publishing all of them on the strength of that link would publish a set nobody reviewed and
/// one that keeps growing under an article already written. The flag that puts a photograph in the
/// installation's public gallery is an act of publication that already exists and already means
/// "anyone may see this", taken deliberately, by a person, about that one picture. It is the whole
/// of the consent this surface asks for, and its consequence is accepted rather than worked around:
/// an installation that has curated no gallery shows no pictures here.
/// </para>
/// <para>
/// <b>It is consent, not a location decision, so the location decision is made separately.</b> Three
/// things stand between a photograph and the position it could give away. The cave the trip is
/// published against carries no protection at all — that is decided before this list is built and
/// again on every read, and a protected cave is refused the whole page rather than this part of it.
/// The link that anchors the picture names no feature that is protected, so a picture cannot arrive
/// here carrying an association with something guarded. And the URL below reaches a rendering only,
/// so the capture point a camera wrote into the upload never leaves, whatever the picture is of.
/// </para>
/// </remarks>
/// <param name="StationName">
/// The station the picture hangs on, spelled the way the viewer addresses stations — which is the
/// string the anchor itself stores, passed through rather than rebuilt, so this page and the
/// signed-in panels place the same picture at the same point.
/// </param>
/// <param name="ThumbnailUrl">
/// A short-lived signed URL for a <em>rendering</em> of the photograph, and deliberately never for
/// the upload. A survey file is only useful as its own bytes, which is why the model above is
/// handed over whole; a photograph is not, and its own bytes carry the fix its camera recorded —
/// a position rather than a fact about one. The rendering is produced by this application with
/// every metadata profile stripped, so it discloses what it depicts and nothing else. The width in
/// the URL is a starting point: a viewer re-points the same URL at whatever width it draws,
/// spending the token it was handed rather than asking for a second one.
/// </param>
/// <param name="Caption">
/// What the picture shows, in the words somebody wrote for it, or the photograph's title where
/// there is no caption. Both are already published verbatim for this same photograph by the
/// curated gallery the flag above put it in, so neither says anything the consent did not cover.
/// </param>
public sealed record PublicTripStationPictureDto(
    string StationName,
    string ThumbnailUrl,
    string? Caption);

public sealed record PublicTripTeamDto(Guid Id, string Title);

/// <summary>
/// A party folded for a published surface — the shape both public reads build their answer from.
/// </summary>
/// <remarks>
/// Not itself a response shape: it is the return of the one routine that decides who is on a trip,
/// what they are called here, and where each may be shown. It exists so that the two routes cannot
/// come to disagree about any of that, and so that the withholding flag travels with the rows it is
/// about rather than being recomputed beside them.
/// </remarks>
/// <param name="PositionsWithheld">
/// True when at least one report placed somebody and its place could not be shown — protection
/// only, never truncation and never a place measured on another survey, each of which the rows say
/// for themselves.
/// </param>
internal sealed record PublicParty(
    IReadOnlyList<PublicTripTeamDto> Teams,
    IReadOnlyList<PublicTripParticipantDto> Participants,
    bool PositionsWithheld);

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
    /// True when the report that placed them was made against a survey other than the one this
    /// envelope hands over — so there is a known place for this person and it cannot honestly be
    /// shown here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A station path is a name inside one survey. A watch re-pointed at a corrected survey while
    /// the party is underground leaves every earlier report naming the survey it was made against,
    /// and the same path in the new one is a different place or no place at all. So the station and
    /// the depth are dropped for such a row rather than drawn — but the fact that somebody
    /// <em>is</em> placed is kept, because the alternative is a page telling the family of a person
    /// underground that nobody has reported where they are, which is false.
    /// </para>
    /// <para>
    /// A boolean rather than the model's id: this envelope deliberately hands over no survey
    /// identifiers, and which survey a report was measured in is a fact about the cave's surveying
    /// that a follower has no use for. What they need is the difference between "not known" and
    /// "known, not shown here", and that is one bit.
    /// </para>
    /// <para>
    /// Never true beside a position that was withheld for protection: that row is refused earlier
    /// and arrives as the ordinary absence, so this bit cannot become a second channel saying
    /// something exists.
    /// </para>
    /// </remarks>
    bool PositionOnOtherModel,
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
