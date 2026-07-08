// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain;

namespace SilexGis.Api.Features.TripLogs;

public sealed record TripParticipantDto(Guid? UserId, string? NameText, string? DisplayName);

public sealed record TripParticipantWrite(Guid? UserId, string? NameText);

public sealed record TripLogDto(
    Guid Id,
    string Title,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    string? Description,
    string? LocationText,
    GeoJsonGeometry? Geom,
    IReadOnlyList<Guid> CaveIds,
    IReadOnlyList<TripParticipantDto> Participants,
    Guid OwnerUserId,
    Guid? TeamId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TripLogWriteRequest(
    string Title,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    string? Description,
    string? LocationText,
    GeoJsonGeometry? Geom,
    IReadOnlyList<Guid> CaveIds,
    IReadOnlyList<TripParticipantWrite> Participants,
    Guid? TeamId,
    Visibility Visibility);

public sealed class TripLogWriteRequestValidator : AbstractValidator<TripLogWriteRequest>
{
    public TripLogWriteRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(10000);
        RuleFor(x => x.LocationText).MaximumLength(300);
        RuleFor(x => x.TripDateEnd)
            .GreaterThanOrEqualTo(x => x.TripDate)
            .When(x => x.TripDateEnd is not null)
            .WithMessage("Trip end date must not precede the start date.");
        RuleFor(x => x.CaveIds).NotNull();
        RuleFor(x => x.Participants).NotNull();
        RuleForEach(x => x.Participants)
            .Must(p => (p.UserId is not null) ^ !string.IsNullOrWhiteSpace(p.NameText))
            .WithMessage("Each participant needs either a user or a name, not both.");
        RuleForEach(x => x.Participants)
            .Must(p => p.NameText is null || p.NameText.Length <= 200)
            .WithMessage("Participant names are limited to 200 characters.");
    }
}
