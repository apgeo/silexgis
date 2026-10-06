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

/// <summary>A member the caller may read, named so the membership editor can show it.</summary>
public sealed record FeatureSetMemberDto(Guid Id, string? Name, FeatureKind Kind);

public sealed record FeatureSetWriteRequest(string Name, string? Description);

public sealed record FeatureSetMemberReplaceRequest(IReadOnlyList<Guid> FeatureIds);

/// <summary>
/// What the caller may do in each domain, with no particular row in view — the shape UI
/// gating needs. Row-level answers come from the per-object access routes instead.
/// </summary>
/// <param name="IsFullAdmin">
/// Whether the caller holds the installation-wide role, which a handful of decisions turn on
/// directly rather than through a domain right — publishing a photograph to the public gallery
/// is one. Stated rather than inferred: a client guessing it from an unrelated domain would
/// offer the control to the wrong people, and hide it from the right ones.
/// </param>
/// <param name="CreateInCavingGroups">
/// For each domain whose rows can belong to a caving group, the caller's own caving groups in
/// which they may create a row bound to that group. A second answer beside the map, and not a
/// correction to it: the map is decided with no row in view, so a right held only at one caving
/// group's scope is absent from it by construction — a member whose club lets them record the
/// club's trips holds no <c>create</c> there, though a trip bound to the club is accepted. This
/// says where such a right does reach, by the rule the create itself is decided by, so a deny or
/// a ruleset edit moves this answer and the write together. It is read to offer a create door
/// and to bound the groups a create form offers, and it speaks of creating only: writing,
/// deleting or sharing under a right held at a group's scope stay questions about a row.
/// Every such domain is a key, with an empty list where the caller may create in none of their
/// groups, so an absent key always means a domain that has no group binding at all.
/// <para>
/// One place where the answer runs ahead of the routes, stated so that nobody draws a door from
/// it there: a vector file and a raster map are created by uploading a file, and neither upload
/// takes a group — each asks for the right with none named and the row is bound afterwards by an
/// edit. A group listed under those two domains is one the rules would admit a bound create in,
/// and no route can make one yet.
/// </para>
/// </param>
public sealed record CapabilitiesDto(
    IReadOnlyDictionary<string, AccessAction> Domains,
    bool IsFullAdmin,
    IReadOnlyDictionary<string, IReadOnlyList<CreatableCavingGroupDto>> CreateInCavingGroups);

/// <summary>
/// One of the caller's own caving groups, named so a form can say which group a new row will
/// belong to rather than calling it "your group".
/// </summary>
public sealed record CreatableCavingGroupDto(Guid Id, string Name);

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

/// <summary>
/// The preview's answer: the subject's domain-level rights, plus the server's own
/// explanation of what decided each one — rendered verbatim by the editor, never
/// re-derived, so what the preview claims and what the evaluator does cannot drift.
/// </summary>
public sealed record AccessPreviewDto(
    IReadOnlyDictionary<string, AccessAction> Domains,
    IReadOnlyList<AccessPreviewExplanationDto> Explanations);

/// <summary>One (domain, action) verdict for the previewed subject, with its reason.</summary>
public sealed record AccessPreviewExplanationDto(
    string Domain,
    AccessAction Action,
    bool Allowed,
    string Source,
    AccessLevel? Level,
    string? RuleName,
    bool Redacted);

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
