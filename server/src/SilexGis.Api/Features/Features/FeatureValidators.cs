// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;

namespace SilexGis.Api.Features.Features;

public sealed class FeatureCreateRequestValidator : AbstractValidator<FeatureCreateRequest>
{
    public FeatureCreateRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Name).MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.FeatureTypeId).GreaterThan(0);
        RuleFor(x => x.Properties)
            .Must(BeAJsonObjectOrAbsent)
            .WithMessage("Properties must be a JSON object.");
        RuleForEach(x => x.Parents).ChildRules(parent =>
            parent.RuleFor(p => p.ParentId).NotEmpty());
    }

    internal static bool BeAJsonObjectOrAbsent(JsonElement? properties) =>
        properties is null
        || properties.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined;
}

public sealed class FeatureUpdateRequestValidator : AbstractValidator<FeatureUpdateRequest>
{
    public FeatureUpdateRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.FeatureTypeId).GreaterThan(0);
        RuleFor(x => x.Properties)
            .Must(FeatureCreateRequestValidator.BeAJsonObjectOrAbsent)
            .WithMessage("Properties must be a JSON object.");
    }
}

public sealed class SetParentsRequestValidator : AbstractValidator<SetParentsRequest>
{
    public SetParentsRequestValidator()
    {
        RuleFor(x => x.Parents).NotNull();
        RuleForEach(x => x.Parents).ChildRules(parent =>
            parent.RuleFor(p => p.ParentId).NotEmpty());
    }
}

public sealed class SetLinksRequestValidator : AbstractValidator<SetLinksRequest>
{
    public SetLinksRequestValidator()
    {
        RuleFor(x => x.Links).NotNull();
        RuleForEach(x => x.Links).ChildRules(link =>
        {
            link.RuleFor(l => l.ToId).NotEmpty();
            link.RuleFor(l => l.LinkKindCode).NotEmpty().MaximumLength(100);
            link.RuleFor(l => l.Note).MaximumLength(1000);
        });
    }
}
