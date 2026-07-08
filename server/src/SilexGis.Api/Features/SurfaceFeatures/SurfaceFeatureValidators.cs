// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;

namespace SilexGis.Api.Features.SurfaceFeatures;

public sealed class SurfaceFeatureWriteRequestValidator : AbstractValidator<SurfaceFeatureWriteRequest>
{
    public SurfaceFeatureWriteRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.FeatureTypeId).GreaterThan(0);
        RuleFor(x => x.Geometry).NotNull();
        RuleFor(x => x.Properties)
            .Must(p => p is null || p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined)
            .WithMessage("Properties must be a JSON object.");
    }
}
