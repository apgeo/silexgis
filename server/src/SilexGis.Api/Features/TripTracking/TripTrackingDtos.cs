// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.TripTracking;

// ---- reads ----

public sealed record TrackingTeamDto(Guid Id, string Title);

/// <summary>
/// One participant's current tracking state, folded from the latest event about them.
/// Position fields are null both when nothing places the caver yet and when the caller may
/// not learn the position — a reader without exact-location rights cannot tell the two
/// apart, which is the point.
/// </summary>
public sealed record TrackingParticipantDto(
    Guid CaverId,
    Guid? TeamId,
    TripPositionEventKind? LastKind,
    DateTimeOffset? LastRecordedAt,
    string? StationName,
    decimal? DepthM,
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
    string? Label);

public sealed record TrackingStateDto(
    TripTrackingState State,
    Guid? SurveyModelId,
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
