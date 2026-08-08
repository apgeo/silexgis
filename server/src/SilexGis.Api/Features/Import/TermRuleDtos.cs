// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// A rule set as the list shows it. <c>canEdit</c> and <c>canDelete</c> travel with the row so
/// the screen offers "edit" and "edit a copy" for the right sets without re-deriving the
/// ownership rules in the browser — where they would be a second, drifting copy of them.
/// </summary>
public sealed record TermRuleSetDto(
    Guid Id,
    string Name,
    string? Description,
    TermRuleScope Scope,
    Guid? OwnerUserId,
    Guid? CavingGroupId,
    bool IsDefault,
    bool IsSeeded,
    int RuleCount,
    bool CanEdit,
    bool CanDelete,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A rule set with its rules — what the editor and the dry run read.</summary>
public sealed record TermRuleSetDetailDto(TermRuleSetDto Set, IReadOnlyList<TermRule> Rules);

/// <summary>
/// Creates a set. Rules may be given outright, copied from another set, or left empty for a
/// set built up in the editor.
/// </summary>
public sealed record TermRuleSetCreateRequest(
    string Name,
    string? Description,
    IReadOnlyList<TermRule>? Rules,
    Guid? CopyFromId);

/// <summary>Replaces a set's name, description and rules together — a set is edited as one document.</summary>
public sealed record TermRuleSetWriteRequest(
    string Name,
    string? Description,
    IReadOnlyList<TermRule> Rules);

/// <summary>
/// Moves a set between scopes, or makes it the default of the one it is in. An administrator's
/// action: it changes what everybody else's next import proposes.
/// </summary>
public sealed record TermRuleSetScopeRequest(TermRuleScope Scope, Guid? CavingGroupId, bool IsDefault);

/// <summary>A rule document as a file two installations exchange.</summary>
public sealed record TermRuleSetImportRequest(TermRuleDocument Document, string? Name);

/// <summary>Which set an import would run if the caller named none, and why.</summary>
public sealed record EffectiveTermRuleSetDto(TermRuleSetDetailDto? Set, TermRuleScope? Source);

public sealed class TermRuleSetCreateRequestValidator : AbstractValidator<TermRuleSetCreateRequest>
{
    public TermRuleSetCreateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class TermRuleSetWriteRequestValidator : AbstractValidator<TermRuleSetWriteRequest>
{
    public TermRuleSetWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Rules).NotNull();
    }
}

public sealed class TermRuleSetScopeRequestValidator : AbstractValidator<TermRuleSetScopeRequest>
{
    public TermRuleSetScopeRequestValidator()
    {
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.Scope == TermRuleScope.CavingGroup)
            .WithMessage("A group set names the group it belongs to.");
        RuleFor(x => x.CavingGroupId).Null()
            .When(x => x.Scope != TermRuleScope.CavingGroup)
            .WithMessage("Only a group set names a group.");
    }
}

public sealed class TermRuleSetImportRequestValidator : AbstractValidator<TermRuleSetImportRequest>
{
    public TermRuleSetImportRequestValidator()
    {
        RuleFor(x => x.Document).NotNull();
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Document.Version)
            .LessThanOrEqualTo(TermRuleDocument.CurrentVersion)
            .When(x => x.Document is not null)
            .WithMessage("This file was written by a newer version of the application.");
    }
}

internal static class TermRuleMapping
{
    public static TermRuleSetDto ToDto(this TermRuleSet set, int ruleCount, bool canEdit, bool canDelete) => new(
        set.Id,
        set.Name,
        set.Description,
        set.Scope,
        set.OwnerUserId,
        set.CavingGroupId,
        set.IsDefault,
        set.IsSeeded,
        ruleCount,
        canEdit,
        canDelete,
        set.CreatedAt,
        set.UpdatedAt);
}
