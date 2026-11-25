// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.FeatureSets;

public sealed record FeatureSetDto(Guid Id, string Name, string Slug, string? Description, int MemberCount);

/// <summary>A member the caller may read, named so the membership editor can show it.</summary>
/// <param name="TypeName">
/// The member's feature type, when it has one — the more particular word, and the one a member
/// just picked in the editor is shown with too. Null for rows that carry only a kind.
/// </param>
public sealed record FeatureSetMemberDto(Guid Id, string? Name, FeatureKind Kind, string? TypeName);

public sealed record FeatureSetWriteRequest(string Name, string? Description);

public sealed record FeatureSetMemberReplaceRequest(IReadOnlyList<Guid> FeatureIds);

public sealed class FeatureSetWriteRequestValidator : AbstractValidator<FeatureSetWriteRequest>
{
    public FeatureSetWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class FeatureSetMemberReplaceRequestValidator
    : AbstractValidator<FeatureSetMemberReplaceRequest>
{
    /// <summary>
    /// The most members one replace names. A set is a hand-picked list a person curates in the
    /// workspace, not a bulk store; the bound keeps one request from writing a membership and an
    /// audit row the size of the whole registry.
    /// </summary>
    public const int MaxFeatureIds = 5_000;

    public FeatureSetMemberReplaceRequestValidator() =>
        RuleFor(x => x.FeatureIds).Cascade(CascadeMode.Stop).NotNull()
            .Must(ids => ids.Count <= MaxFeatureIds)
            .WithMessage($"At most {MaxFeatureIds} features may be named at once.");
}
