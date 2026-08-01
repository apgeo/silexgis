// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;

namespace SilexGis.Api.Features.Caves;

public sealed class CaveWriteRequestValidator : AbstractValidator<CaveWriteRequest>
{
    public CaveWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.OtherToponyms).MaximumLength(250);
        RuleFor(x => x.IdentificationCode).MaximumLength(50);
        RuleFor(x => x.CaveTypeId).GreaterThan(0);
        RuleFor(x => x.Website).MaximumLength(255);
        RuleFor(x => x.Region).MaximumLength(100);
        RuleFor(x => x.HydrographicBasin).MaximumLength(100);
        RuleFor(x => x.Valley).MaximumLength(100);
        RuleFor(x => x.TributaryRiver).MaximumLength(100);
        RuleFor(x => x.ClosestAddress).MaximumLength(200);
        RuleFor(x => x.LandRegistryNumber).MaximumLength(50);
        RuleFor(x => x.RockAge).MaximumLength(50);
        RuleFor(x => x.ProtectionClass).MaximumLength(50);
        RuleFor(x => x.DiscoveryDate).MaximumLength(50);
        RuleFor(x => x.Discoverer).MaximumLength(255);
        RuleFor(x => x.SurveyedLength).GreaterThanOrEqualTo(0).When(x => x.SurveyedLength.HasValue);
        RuleFor(x => x.EstimatedLength).GreaterThanOrEqualTo(0).When(x => x.EstimatedLength.HasValue);
        RuleFor(x => x.Depth).GreaterThanOrEqualTo(0).When(x => x.Depth.HasValue);
        RuleFor(x => x.Volume).GreaterThanOrEqualTo(0).When(x => x.Volume.HasValue);
        RuleFor(x => x.Area).GreaterThanOrEqualTo(0).When(x => x.Area.HasValue);
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.ExplorationStatus).IsInEnum();
        RuleFor(x => x.Properties)
            .Must(p => p!.Value.ValueKind == JsonValueKind.Object)
            .WithMessage("Properties must be a JSON object.")
            .When(x => x.Properties.HasValue
                && x.Properties.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined));
    }
}
