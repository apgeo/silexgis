// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Permissions;

/// <summary>
/// One rule as the wire carries it. The two anchor columns are served as a single
/// <see cref="ScopeId"/>: which column a scope uses is a storage detail (features get a
/// real FK, everything else an id), and an editor should not have to know it.
/// </summary>
public sealed record AccessEntryDto(
    long Id,
    AccessEffect Effect,
    AccessDomain Domain,
    AccessAction Actions,
    AccessScopeKind ScopeKind,
    Guid? ScopeId,
    string? ScopeLabel,
    FeatureKind? FeatureKind,
    long? FeatureTypeId);

public sealed record AccessEntryWrite(
    AccessEffect Effect,
    AccessDomain Domain,
    AccessAction Actions,
    AccessScopeKind ScopeKind,
    Guid? ScopeId,
    FeatureKind? FeatureKind,
    long? FeatureTypeId);

/// <summary>A ruleset's whole content — replaced in one call, as ACLs always were.</summary>
public sealed record AccessEntryReplaceRequest(IReadOnlyList<AccessEntryWrite> Entries);

public sealed record PermissionGroupDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    bool IsProtected,
    bool IsSeeded,
    int MemberCount,
    int EntryCount);

public sealed record PermissionGroupWriteRequest(string Name, string? Description);

/// <summary>A trustee: a user, or a whole caving group whose account-holding members inherit.</summary>
public sealed record PermissionGroupMemberDto(
    AccessSubjectKind MemberKind, Guid MemberId, string? MemberName);

public sealed record PermissionGroupMemberWriteRequest(AccessSubjectKind MemberKind, Guid MemberId);

public sealed record FeatureSetDto(Guid Id, string Name, string Slug, string? Description, int MemberCount);

public sealed record FeatureSetWriteRequest(string Name, string? Description);

public sealed record FeatureSetMemberReplaceRequest(IReadOnlyList<Guid> FeatureIds);

/// <summary>
/// What the caller may do in each domain, with no particular row in view — the shape UI
/// gating needs. Row-level answers come from the per-object access routes instead.
/// </summary>
public sealed record CapabilitiesDto(IReadOnlyDictionary<string, AccessAction> Domains);

/// <summary>The valid vocabulary, generated from the rule that validates writes.</summary>
public sealed record AccessCatalogDto(
    IReadOnlyList<AccessCatalogDomainDto> Domains,
    IReadOnlyList<AccessCatalogFeatureSetDto> FeatureSets);

public sealed record AccessCatalogDomainDto(
    AccessDomain Domain,
    string Name,
    bool SupportsKindNarrowing,
    IReadOnlyList<AccessCatalogScopeDto> Scopes);

/// <summary>A scope this domain accepts, with the actions valid inside it.</summary>
public sealed record AccessCatalogScopeDto(
    AccessScopeKind ScopeKind, bool RequiresAnchor, IReadOnlyList<AccessAction> Actions);

public sealed record AccessCatalogFeatureSetDto(Guid Id, string Name);

/// <summary>
/// Why the caller does or does not hold one action on one object. The anchor is named
/// only when the caller may read it — a denied child must not disclose a hidden
/// ancestor's name, or the shape of the installation's policy.
/// </summary>
public sealed record AccessExplanationDto(
    AccessAction Action,
    bool Allowed,
    string Source,
    AccessLevel? Level,
    string? RuleName,
    bool Redacted);

public sealed record EffectiveAccessDto(
    AccessAction Actions, IReadOnlyList<AccessExplanationDto>? Explain);

/// <summary>Evaluate the model as somebody else would see it, before saving a rule.</summary>
public sealed record AccessPreviewRequest(AccessSubjectKind SubjectKind, Guid SubjectId);

public sealed class AccessEntryWriteValidator : AbstractValidator<AccessEntryWrite>
{
    public AccessEntryWriteValidator()
    {
        RuleFor(x => x.Effect).IsInEnum();
        RuleFor(x => x.Domain).IsInEnum();
        RuleFor(x => x.ScopeKind).IsInEnum();
        RuleFor(x => x.Actions)
            .Must(a => a != AccessAction.None)
            .WithMessage("A rule needs at least one action.");
    }
}

public sealed class AccessEntryReplaceRequestValidator : AbstractValidator<AccessEntryReplaceRequest>
{
    public AccessEntryReplaceRequestValidator()
    {
        RuleFor(x => x.Entries).NotNull();
        RuleForEach(x => x.Entries).SetValidator(new AccessEntryWriteValidator());
    }
}

public sealed class PermissionGroupWriteRequestValidator : AbstractValidator<PermissionGroupWriteRequest>
{
    public PermissionGroupWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class PermissionGroupMemberWriteRequestValidator
    : AbstractValidator<PermissionGroupMemberWriteRequest>
{
    public PermissionGroupMemberWriteRequestValidator()
    {
        RuleFor(x => x.MemberKind).IsInEnum();
        RuleFor(x => x.MemberId).NotEmpty();
    }
}

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
    public FeatureSetMemberReplaceRequestValidator() => RuleFor(x => x.FeatureIds).NotNull();
}

public sealed class AccessPreviewRequestValidator : AbstractValidator<AccessPreviewRequest>
{
    public AccessPreviewRequestValidator()
    {
        RuleFor(x => x.SubjectKind).IsInEnum();
        RuleFor(x => x.SubjectId).NotEmpty();
    }
}
