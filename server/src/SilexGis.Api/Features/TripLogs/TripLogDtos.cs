// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.TripLogs;

public sealed record TripParticipantDto(Guid? UserId, string? NameText, string? DisplayName);

public sealed record TripParticipantWrite(Guid? UserId, string? NameText);

public sealed record TripLogDto(
    Guid Id,
    string Title,
    TripType? Type,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    TimeOnly? EntryTime,
    TimeOnly? ExitTime,
    string? Description,
    string? Results,
    string? WeatherConditions,
    string? LocationText,
    string? OrganizingClub,
    GeoJsonGeometry? Geom,
    IReadOnlyList<Guid> CaveIds,
    IReadOnlyList<TripParticipantDto> Participants,
    IReadOnlyList<TripParticipantDto> Proposers,
    Guid OwnerUserId,
    Guid? CavingGroupId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TripLogWriteRequest(
    string Title,
    TripType? Type,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    TimeOnly? EntryTime,
    TimeOnly? ExitTime,
    string? Description,
    string? Results,
    string? WeatherConditions,
    string? LocationText,
    string? OrganizingClub,
    GeoJsonGeometry? Geom,
    IReadOnlyList<Guid> CaveIds,
    IReadOnlyList<TripParticipantWrite> Participants,
    IReadOnlyList<TripParticipantWrite>? Proposers,
    Guid? CavingGroupId,
    Visibility Visibility);

public sealed class TripLogWriteRequestValidator : AbstractValidator<TripLogWriteRequest>
{
    public TripLogWriteRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type is not null);
        RuleFor(x => x.Description).MaximumLength(10000);
        RuleFor(x => x.Results).MaximumLength(10000);
        RuleFor(x => x.WeatherConditions).MaximumLength(300);
        RuleFor(x => x.LocationText).MaximumLength(300);
        RuleFor(x => x.OrganizingClub).MaximumLength(200);
        RuleFor(x => x.TripDateEnd)
            .GreaterThanOrEqualTo(x => x.TripDate)
            .When(x => x.TripDateEnd is not null)
            .WithMessage("Trip end date must not precede the start date.");
        RuleFor(x => x.CaveIds).NotNull();
        RuleFor(x => x.Participants).NotNull();
        // Proposers are optional (a trip needn't record who proposed it); a null list is
        // treated as empty. Each supplied entry still follows the shared identity rules.
        RuleForEach(x => x.Participants).SetValidator(new TripParticipantValidator());
        RuleForEach(x => x.Proposers).SetValidator(new TripParticipantValidator());
    }
}

/// <summary>Shared identity rules for both attendee and proposer entries.</summary>
public sealed class TripParticipantValidator : AbstractValidator<TripParticipantWrite>
{
    public TripParticipantValidator()
    {
        RuleFor(p => p)
            .Must(p => (p.UserId is not null) ^ !string.IsNullOrWhiteSpace(p.NameText))
            .WithMessage("Each person needs either a user or a name, not both.");
        RuleFor(p => p.NameText)
            .MaximumLength(200)
            .WithMessage("Names are limited to 200 characters.");
    }
}
