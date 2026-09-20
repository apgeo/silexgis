// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.TripTracking;

// ---- reads ----

public sealed record TrackingTeamDto(Guid Id, string Title);

/// <summary>
/// One participant's current tracking state, folded from every report about them.
/// Position fields are null both when nothing places the caver yet and when the caller may
/// not learn the position — a reader without exact-location rights cannot tell the two
/// apart, which is the point.
/// </summary>
/// <param name="LastKind">
/// The kind of the latest report of <em>any</em> kind. It describes that report and nothing
/// else: it is not the kind the station came from, and standing must not be re-derived from it
/// — <paramref name="In"/> and <paramref name="Out"/> are the answer to that.
/// </param>
/// <param name="LastRecordedAt">
/// When anything was last heard about them, whatever it said. A note and a "come out" are
/// reports too, so this moves on word that carries no position at all.
/// </param>
/// <param name="PositionRecordedAt">
/// When the report that <em>placed</em> them was made — the recorded time of the very row
/// <paramref name="StationName"/> and <paramref name="DepthM"/> were read off.
/// <para>
/// <b>There are two times here because the position and the last word are routinely two
/// different reports.</b> The displayed position stays the latest report that actually claimed a
/// place, while <paramref name="LastRecordedAt"/> follows every report — so one note later, a
/// station heard four hours ago sits beside a timestamp eight minutes old. Anything that ages a
/// position, sorts the party by how fresh their places are, or tells a coordinator how stale a
/// station is must read this one; <paramref name="LastRecordedAt"/> answers only "when was
/// anything last said about this person".
/// </para>
/// <para>
/// Null on exactly the branch that nulls the station, so that nothing downstream can put an age
/// on a place it was refused. <b>Read that as consistency, not as confidentiality</b>, and do not
/// cite it as a protection: <paramref name="LastRecordedAt"/> beside it is unconditional, and
/// whenever the report that placed them is also the latest report — the ordinary case on a live
/// watch — it carries the very same instant, so this null keeps back nothing its sibling has not
/// already given. That is deliberate rather than an oversight. Where this project settles what a
/// tracking report discloses to a reader who may not learn positions, it drops the station, the
/// depth and the model as location vocabulary and keeps the recorded time, because "somebody was
/// heard from eight minutes ago" is exactly what such a reader is meant to keep. Null here
/// therefore means both "nothing has placed them" and "the place may not be told to this caller",
/// the same deliberate ambiguity the position fields carry — it does not additionally mean that
/// the hour is a secret.
/// </para>
/// </param>
/// <param name="In">
/// The last report that <em>stated</em> a standing put them inside the cave, or nothing has
/// stated one and a report has placed them inside it. False together with <paramref name="Out"/>
/// is the third state and a real answer — nobody has said yet that they went in, came out, or
/// were anywhere — and folding that into "not underground" would draw a party who have not set
/// off as one that is already back. Folded in Domain; never re-derive it from
/// <paramref name="LastKind"/>, which is what the two hand-written copies of this used to do.
/// </param>
/// <param name="Out">
/// The last report that stated a standing was that they are out. A later note, and a later report
/// of a place, both leave it standing: an exit ends the watch for a person, and only a recorded
/// entry starts it again.
/// </param>
public sealed record TrackingParticipantDto(
    Guid CaverId,
    Guid? TeamId,
    TripPositionEventKind? LastKind,
    DateTimeOffset? LastRecordedAt,
    DateTimeOffset? PositionRecordedAt,
    string? StationName,
    decimal? DepthM,
    /// <summary>
    /// The survey model the placing report was recorded against, or null where there is no
    /// position to speak of and on exactly the branch that withholds one.
    /// <para>
    /// <b>Carried because a station name alone does not say where somebody is.</b> A station path
    /// is a name inside one survey; the same path in a re-survey of the same cave may be a
    /// different place, or no place at all. A watch can be re-pointed at another model while the
    /// party is underground — a corrected survey mid-trip is a real thing a coordinator does — and
    /// every report already on the log keeps naming the model it was made against. Without this
    /// field the read hands over those names indistinguishable from names measured in the model
    /// now in use, and the surface that draws them puts the party on stations nobody reported.
    /// </para>
    /// <para>
    /// So whoever draws a marker compares this against the model they are drawing, and where the
    /// two differ says the position was recorded elsewhere rather than drawing it or, worse,
    /// leaving the person looking unreported. <see cref="StationName"/> itself is <em>not</em>
    /// blanked for that case: the name is a true record of a report and the log that shows the
    /// coordinator their own history goes on showing it. What changes is only what may be drawn.
    /// </para>
    /// </summary>
    Guid? PositionSurveyModelId,
    bool In,
    bool Out,
    /// <summary>
    /// The caption an administrator chose for this person on the trip's published page, or null
    /// where nobody chose one — which is the ordinary state.
    /// <para>
    /// Null does not say what the page will call them: that is the installation's setting, and it
    /// is answered once for the whole trip by <c>publishesRealNames</c> rather than repeated on
    /// every row. A caption is what the page shows whichever way that setting is set.
    /// </para>
    /// </summary>
    string? Label,
    /// <summary>
    /// The string a published page would print for this person right now, or null where it would
    /// print none and call them by their place in the party.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The server's own answer rather than something a surface derives.</b> A caver whose real
    /// name is on a public page has to be able to find out, and the only honest way to tell them is
    /// to run the very rule the published page runs — the caption first, then the roster's own
    /// name where this installation publishes names, then nothing — against the very rows it reads.
    /// A second implementation on the client would be free to disagree with the page it is
    /// describing, and the one direction it must never be wrong in is saying "a place in the party"
    /// about somebody the page names.
    /// </para>
    /// <para>
    /// It is what the page <em>would</em> print, whether or not a link exists. Said unconditionally
    /// because the question is asked before anybody publishes: whoever is about to press the button
    /// has to read what it will disclose, and somebody already on a published page has to be able
    /// to read what it is disclosing. Whether a page exists is <c>publishedAt</c> on the trip.
    /// </para>
    /// <para>
    /// Deliberately the roster's name and not this account's display name, which is what every
    /// other signed-in surface calls the same person. The two part company the moment a member
    /// chooses a display name, and this field's whole job is to answer "what will a follow link
    /// print" — so it answers with the published string even where that differs from the one beside
    /// it on the screen.
    /// </para>
    /// <para>
    /// <b>That difference is not a widening, and the reason is worth stating rather than assuming.</b>
    /// A caver's roster name is readable by any signed-in caller by design — it is the label every
    /// attribution row needs, and the roster's own protection rule says so; what the shared resolver
    /// does with an account's chosen label is prefer it so that one person is not shown under two
    /// names on one page, which is a consistency rule and never a protection. So this field
    /// discloses nothing a signed-in caller could not already read, and it is sent to a narrower
    /// set than that: only to somebody who may read this trip.
    /// </para>
    /// </remarks>
    string? PublishedAs);

public sealed record TrackingStateDto(
    TripTrackingState State,
    Guid? SurveyModelId,
    /// <summary>
    /// True when the watch names a survey model this server no longer holds — somebody deleted it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A watch in that condition is the one this flag exists for: still <c>Armed</c>, still
    /// accepting notes and entries and exits, and unable to place anybody — the station control
    /// and the party on the model simply are not there, and nothing on the screen says why. Left
    /// to infer it, a surface has only a model id that resolves to nothing, which is also what a
    /// model still being read, a model this caller may not open, and a plain network failure look
    /// like. This is the server saying which of them it is.
    /// </para>
    /// <para>
    /// False whenever the configuration is being withheld from this caller: they are sent
    /// <see cref="SurveyModelId"/> as null, and "the model that watch is on was deleted" is a fact
    /// about a cave they may not be told about. Fail closed, as the rest of this read does.
    /// </para>
    /// </remarks>
    bool SurveyModelMissing,
    string? ReferenceStationName,
    IReadOnlyList<string> DepthFilter,
    DateTimeOffset? ArmedAt,
    DateTimeOffset? ClosedAt,
    /// <summary>True when at least one position existed but was withheld from this caller.</summary>
    bool PositionsWithheld,
    /// <summary>
    /// Whether publishing this trip would put the party's real names on the page. This is the
    /// installation's setting, not a fact about this trip or this caller.
    /// <para>
    /// Carried on the trip's own read because the panel that mints a follow link is drawn on the
    /// same page, and whoever presses that button has to be told what the page will show
    /// <em>before</em> there is a link to hand out. What it discloses is one boolean about how this
    /// server is configured, to a caller who can already read the trip — no name, and nothing about
    /// any person. Somebody an administrator has named as a place in the party is still shown that
    /// way whatever this says.
    /// </para>
    /// </summary>
    bool PublishesRealNames,
    /// <summary>
    /// When this trip was first published by a link that still opens the page, or null when no link
    /// does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The fact a member could not previously learn at all.</b> The list of follow links takes
    /// write access, so somebody who is merely on the trip could not see that one had been minted;
    /// the tracking state said nothing about it; and nothing told them. Their real name could be on
    /// a page on the internet, by the installation's default, with no surface anywhere admitting
    /// it. This is that surface, and it follows the trip's own readability and nothing else — a
    /// caller who may read the trip may know whether the trip is published.
    /// </para>
    /// <para>
    /// <b>It carries no token and no link count, and neither is an oversight.</b> A token is the
    /// whole of a follower's claim and exists in exactly one response; a page that showed one to
    /// every reader of the trip would be handing out the capability rather than reporting it. What
    /// a person needs to know is that a page exists, since when, and until when — not how to open
    /// it.
    /// </para>
    /// <para>
    /// Computed from the same rules the published read answers with, asked at this instant rather
    /// than stored: a link whose watch has closed or whose window has passed is not a publication
    /// any more, and reporting it as one would have this surface disagree with the page itself.
    /// <b>The cave's refusal is one of those rules, not a separate question</b> — a trip whose cave
    /// has since been position-protected, or whose watch has lost the cave it was anchored to, is
    /// refused to every follower while its links sit unrevoked and inside their window, so a
    /// reading that consulted the window alone would report a publication that opens nothing.
    /// </para>
    /// </remarks>
    DateTimeOffset? PublishedAt,
    /// <summary>
    /// When the last still-open link lapses, or null when none is open.
    /// </summary>
    /// <remarks>
    /// The other half of the answer, and the one that makes the first half actionable: "this trip
    /// is published" is a different thing to be told depending on whether it stops on Thursday or
    /// in two weeks. The latest of the open links, because that is when the trip actually stops
    /// being published — an earlier one lapsing changes nothing while another is live. It is an
    /// outer bound rather than a promise: closing the watch ends the publication sooner, and
    /// revoking ends it at once.
    /// </remarks>
    DateTimeOffset? PublishedUntil,
    IReadOnlyList<TrackingTeamDto> Teams,
    IReadOnlyList<TrackingParticipantDto> Participants);

public sealed record TrackingEventDto(
    Guid Id,
    Guid CaverId,
    Guid? TeamId,
    TripPositionEventKind Kind,
    Guid? SurveyModelId,
    string? StationName,
    decimal? DepthEnteredM,
    string? Note,
    DateTimeOffset RecordedAt);

public sealed record TrackingDepthCandidateDto(
    string StationName,
    string? SurveyName,
    double DepthM,
    double DeltaM);

// ---- writes ----

/// <summary>
/// Config write, merge semantics: an absent field keeps what is stored, so closing tracking
/// cannot silently rewrite the configuration earlier reports were resolved under. Clearing
/// is explicit — an empty string clears the reference station, an empty list the filter.
/// State is always stated.
/// </summary>
public sealed record TrackingConfigRequest(
    TripTrackingState? State,
    Guid? SurveyModelId,
    string? ReferenceStationName,
    IReadOnlyList<string>? DepthFilter);

public sealed class TrackingConfigRequestValidator : AbstractValidator<TrackingConfigRequest>
{
    public TrackingConfigRequestValidator()
    {
        // Nullable so an absent field cannot silently mean the enum's zero value.
        RuleFor(x => x.State).NotNull().IsInEnum();
        RuleFor(x => x.ReferenceStationName).MaximumLength(TripTrackingRules.MaxStationNameLength);
        RuleFor(x => x.DepthFilter!.Count).LessThanOrEqualTo(TripTrackingRules.MaxDepthFilterEntries)
            .When(x => x.DepthFilter is not null);
        RuleForEach(x => x.DepthFilter).NotEmpty().MaximumLength(TripTrackingRules.MaxStationNameLength)
            .When(x => x.DepthFilter is not null);
    }
}

/// <summary>
/// The name a published page gives one participant. An absent, empty or blank label clears the
/// choice and returns them to the non-identifying default — there is no separate route for
/// that, because "call them nothing in particular" is a value this field can hold.
/// </summary>
public sealed record TrackingParticipantLabelRequest(string? Label);

public sealed class TrackingParticipantLabelRequestValidator : AbstractValidator<TrackingParticipantLabelRequest>
{
    public TrackingParticipantLabelRequestValidator()
    {
        RuleFor(x => x.Label).MaximumLength(TripTrackingRules.MaxLabelLength);
    }
}

/// <summary>The label as stored after the write; null when the choice was cleared.</summary>
public sealed record TrackingParticipantLabelDto(Guid CaverId, string? Label);

public sealed record TrackingTeamRequest(string? Title);

public sealed class TrackingTeamRequestValidator : AbstractValidator<TrackingTeamRequest>
{
    public TrackingTeamRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TripTrackingRules.MaxTitleLength);
    }
}

public sealed record TrackingEventRequest(
    IReadOnlyList<Guid>? CaverIds,
    TripPositionEventKind? Kind,
    string? StationName,
    decimal? DepthM,
    Guid? TeamId,
    string? Note,
    DateTimeOffset? RecordedAt);

public sealed class TrackingEventRequestValidator : AbstractValidator<TrackingEventRequest>
{
    public TrackingEventRequestValidator()
    {
        RuleFor(x => x.CaverIds).NotEmpty();
        RuleFor(x => x.CaverIds!.Count).LessThanOrEqualTo(TripTrackingRules.MaxCaversPerWrite)
            .When(x => x.CaverIds is not null);
        RuleFor(x => x.Kind).NotNull().IsInEnum();
        RuleFor(x => x.Note).MaximumLength(TripTrackingRules.MaxNoteLength);

        // A station name belongs to a station report and a depth to a depth report — a request
        // carrying the wrong one is a confused caller, not a permissive default.
        RuleFor(x => x.StationName).NotEmpty().MaximumLength(TripTrackingRules.MaxStationNameLength)
            .When(x => x.Kind == TripPositionEventKind.AtStation);
        RuleFor(x => x.StationName).Null()
            .When(x => x.Kind is not null && x.Kind != TripPositionEventKind.AtStation);
        RuleFor(x => x.DepthM).NotNull()
            .When(x => x.Kind == TripPositionEventKind.AtDepth);
        RuleFor(x => x.DepthM!.Value).InclusiveBetween(-TripTrackingRules.MaxDepthAbsM, TripTrackingRules.MaxDepthAbsM)
            .When(x => x.DepthM is not null);
        RuleFor(x => x.DepthM).Null()
            .When(x => x.Kind is not null && x.Kind != TripPositionEventKind.AtDepth);
    }
}

public sealed record TrackingResolveDepthRequest(decimal? DepthM, int? Take);

public sealed class TrackingResolveDepthRequestValidator : AbstractValidator<TrackingResolveDepthRequest>
{
    public TrackingResolveDepthRequestValidator()
    {
        RuleFor(x => x.DepthM).NotNull();
        RuleFor(x => x.DepthM!.Value).InclusiveBetween(-TripTrackingRules.MaxDepthAbsM, TripTrackingRules.MaxDepthAbsM)
            .When(x => x.DepthM is not null);
        RuleFor(x => x.Take!.Value).InclusiveBetween(1, 25).When(x => x.Take is not null);
    }
}
