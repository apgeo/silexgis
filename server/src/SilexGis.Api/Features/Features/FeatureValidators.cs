// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using SilexGis.Domain;

namespace SilexGis.Api.Features.Features;

public sealed class FeatureCreateRequestValidator : AbstractValidator<FeatureCreateRequest>
{
    public FeatureCreateRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        // An enum binds any number of its underlying type, and the audience checks compare
        // against the defined values, so a number outside them would be stored as an audience
        // nobody defined and read differently by different readers.
        RuleFor(x => x.Visibility).IsInEnum();
        // Club visibility says "the club this belongs to may read it", so it needs a club to
        // name; without one it would be an audience that admits nobody, silently.
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.Visibility == Visibility.CavingGroup)
            .WithMessage("Caving-group visibility needs a caving group.");
        RuleFor(x => x.Name).MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.FeatureTypeId).GreaterThan(0);
        RuleFor(x => x.Properties)
            .Must(BeAJsonObjectOrAbsent)
            .WithMessage("Properties must be a JSON object.");
        RuleFor(x => x.Properties)
            .Must(FitProperties)
            .WithMessage($"Properties are limited to {MaxPropertiesLength} characters.");
        RuleFor(x => x.Parents!)
            .Must(parents => parents.Count <= MaxParents)
            .When(x => x.Parents is not null)
            .WithMessage($"A feature has at most {MaxParents} parents.");
        RuleForEach(x => x.Parents).ChildRules(parent =>
            parent.RuleFor(p => p.ParentId).NotEmpty());
    }

    /// <summary>
    /// The longest a feature's free-form properties may be, as written. Far past what a form or an
    /// imported record carries; the properties are sent with every read of the feature.
    /// </summary>
    public const int MaxPropertiesLength = 262_144;

    /// <summary>The most parents one feature may be placed under.</summary>
    public const int MaxParents = 100;

    /// <summary>The most typed links one feature may hold.</summary>
    public const int MaxLinks = 1_000;

    internal static bool BeAJsonObjectOrAbsent(JsonElement? properties) =>
        properties is null
        || properties.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined;

    internal static bool FitProperties(JsonElement? properties) =>
        properties is not { ValueKind: JsonValueKind.Object } value || value.GetRawText().Length <= MaxPropertiesLength;
}

public sealed class FeatureUpdateRequestValidator : AbstractValidator<FeatureUpdateRequest>
{
    public FeatureUpdateRequestValidator()
    {
        RuleFor(x => x.Visibility).IsInEnum();
        // Club visibility says "the club this belongs to may read it", so it needs a club to
        // name; without one it would be an audience that admits nobody, silently.
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.Visibility == Visibility.CavingGroup)
            .WithMessage("Caving-group visibility needs a caving group.");
        RuleFor(x => x.Name).MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.FeatureTypeId).GreaterThan(0);
        RuleFor(x => x.Properties)
            .Must(FeatureCreateRequestValidator.BeAJsonObjectOrAbsent)
            .WithMessage("Properties must be a JSON object.");
        RuleFor(x => x.Properties)
            .Must(FeatureCreateRequestValidator.FitProperties)
            .WithMessage($"Properties are limited to {FeatureCreateRequestValidator.MaxPropertiesLength} characters.");
    }
}

public sealed class SetParentsRequestValidator : AbstractValidator<SetParentsRequest>
{
    public SetParentsRequestValidator()
    {
        RuleFor(x => x.Parents).NotNull();
        RuleFor(x => x.Parents!)
            .Must(parents => parents.Count <= FeatureCreateRequestValidator.MaxParents)
            .When(x => x.Parents is not null)
            .WithMessage($"A feature has at most {FeatureCreateRequestValidator.MaxParents} parents.");
        RuleForEach(x => x.Parents).ChildRules(parent =>
            parent.RuleFor(p => p.ParentId).NotEmpty());
    }
}

public sealed class SetLinksRequestValidator : AbstractValidator<SetLinksRequest>
{
    public SetLinksRequestValidator()
    {
        RuleFor(x => x.Links).NotNull();
        RuleFor(x => x.Links!)
            .Must(links => links.Count <= FeatureCreateRequestValidator.MaxLinks)
            .When(x => x.Links is not null)
            .WithMessage($"A feature holds at most {FeatureCreateRequestValidator.MaxLinks} links.");
        RuleForEach(x => x.Links).ChildRules(link =>
        {
            link.RuleFor(l => l.ToId).NotEmpty();
            link.RuleFor(l => l.LinkKindCode).NotEmpty().MaximumLength(100);
            link.RuleFor(l => l.Note).MaximumLength(1000);
        });
    }
}
