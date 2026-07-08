// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;

namespace SilexGis.Api.Features.Geofiles;

public sealed class GeofileUpdateRequestValidator : AbstractValidator<GeofileUpdateRequest>
{
    public GeofileUpdateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Style)
            .Must(s => s is null || s.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined)
            .WithMessage("Style must be a JSON object.");
    }
}
