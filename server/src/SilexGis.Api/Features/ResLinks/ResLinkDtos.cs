// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;

namespace SilexGis.Api.Features.ResLinks;

/// <summary>
/// How well a member's anchor still fits its target. Anchors address content that can be
/// re-imported or re-versioned underneath them, so the fit is a fact of the read, not of
/// the row. Only <see cref="Exact"/> and <see cref="Degraded"/> are produced today —
/// <see cref="Degraded"/> when the file the anchor was measured against is no longer what
/// the document currently serves, so following the anchor against the current content
/// could silently highlight the wrong thing. The richer states arrive with re-anchoring.
/// </summary>
public enum ResLinkAnchorState
{
    /// <summary>The anchor addresses the content it was authored against.</summary>
    Exact = 0,

    /// <summary>Re-computed against newer content; the meaning was preserved.</summary>
    Reanchored = 1,

    /// <summary>The measured-against content moved on; the anchor may no longer point
    /// where its author meant.</summary>
    Degraded = 2,

    /// <summary>The addressed part no longer exists in the target at all.</summary>
    Unresolvable = 3,
}

/// <summary>Relation vocabulary row. Seeded rows (<paramref name="Seeded"/>) are the
/// exchange vocabulary — clients translate their labels by <paramref name="Code"/>;
/// custom rows are shown as written.</summary>
public sealed record ResLinkRelationTypeDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool Directed,
    string? InverseName,
    bool Seeded);

/// <summary>What a caller may see of a member's target, from its own domain's rules —
/// null on the member when the target is unreadable, and then nothing here (name, route,
/// thumbnail) leaks around that answer.</summary>
public sealed record ResLinkTargetDisplayDto(
    string Title, string? Subtitle, string? Route, string? ThumbnailUrl);

/// <summary>One row of the target picker feed — deliberately uniform across types so the
/// picker stays type-agnostic.</summary>
public sealed record ResLinkTargetHitDto(Guid Id, string Title, string? Subtitle);

public sealed record ResLinkMemberDto(
    Guid Id,
    string TargetType,
    Guid TargetId,
    bool IsMain,
    int SortOrder,
    string? Note,
    AnchorKind AnchorKind,
    /// <summary>The anchor payload as authored; withheld (null) when the caller may not
    /// read the target, because a payload can quote what it anchors to.</summary>
    JsonElement? Anchor,
    Guid? AnchorFileId,
    ResLinkAnchorState AnchorState,
    ResLinkTargetDisplayDto? Display);

public sealed record ResLinkDto(
    Guid Id,
    string ShortCode,
    ResLinkRelationTypeDto? RelationType,
    string? Description,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ResLinkMemberDto> Members);

/// <summary>A member of a link being created: a typed target reference plus its anchor.
/// The GPS-point convenience exists only on the member-add endpoint — a link is created
/// against things that already exist.</summary>
public sealed record ResLinkMemberInput(
    string TargetType,
    Guid TargetId,
    bool IsMain,
    int SortOrder,
    string? Note,
    AnchorKind AnchorKind,
    JsonElement? Anchor,
    Guid? AnchorFileId);

public sealed record ResLinkCreateRequest(
    long? RelationTypeId,
    string? Description,
    IReadOnlyList<ResLinkMemberInput> Members);

/// <summary>
/// Replaces the link's own mutable facts: description and relation type (null clears
/// either). <paramref name="MainMemberId"/> lets the relation change and the main marker
/// move in one act — switching an established link to a directed relation needs both at
/// once, because a main marker is refused while the relation is undirected and a directed
/// relation with two or more members refuses to exist without one.
/// </summary>
public sealed record ResLinkUpdateRequest(
    string? Description,
    long? RelationTypeId,
    Guid? MainMemberId);

/// <summary>The GPS-point convenience: a new caller-owned generic point feature created
/// and joined as a whole member in one transaction. Visibility defaults to Public —
/// deliberately, so the point is visible to whoever sees the link.</summary>
public sealed record ResLinkNewGeoPointInput(
    double Lon, double Lat, double? Z, string? Name, Visibility? Visibility);

/// <summary>Adds a member: EITHER a target reference (type + id) OR
/// <paramref name="NewGeoPoint"/> — never both. A new point is always a whole member, so
/// anchor fields must stay empty alongside it.</summary>
public sealed record ResLinkMemberAddRequest(
    string? TargetType,
    Guid? TargetId,
    ResLinkNewGeoPointInput? NewGeoPoint,
    bool IsMain,
    int SortOrder,
    string? Note,
    AnchorKind AnchorKind,
    JsonElement? Anchor,
    Guid? AnchorFileId);

/// <summary>Replaces a member's mutable facts. Anchors are deliberately not here: an
/// anchor edit is a change of meaning, so it is always a delete + re-add, never a silent
/// mutation of an existing row.</summary>
public sealed record ResLinkMemberUpdateRequest(bool IsMain, int SortOrder, string? Note);

public sealed record ResLinkRelationTypeWriteRequest(
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool Directed,
    string? InverseName);

public sealed class ResLinkCreateRequestValidator : AbstractValidator<ResLinkCreateRequest>
{
    public ResLinkCreateRequestValidator()
    {
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Members).NotEmpty().WithMessage("A link needs at least one member.");
        RuleFor(x => x.Members)
            .Must(m => m is null || m.Count <= ResLinkRules.MaxMembers)
            .WithMessage($"A link holds at most {ResLinkRules.MaxMembers} members.");
        RuleForEach(x => x.Members).ChildRules(member =>
        {
            member.RuleFor(m => m.TargetType).NotEmpty();
            member.RuleFor(m => m.TargetId).NotEmpty();
            member.RuleFor(m => m.Note).MaximumLength(2000);
            member.RuleFor(m => m.Anchor).Must(BeAJsonObjectOrAbsent)
                .WithMessage("The anchor payload must be a JSON object.");
            member.RuleFor(m => m.Anchor).Must(BeWithinAnchorSize)
                .WithMessage("The anchor payload is too large.");
        });
    }

    internal static bool BeAJsonObjectOrAbsent(JsonElement? anchor) =>
        anchor is null
        || anchor.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined;

    /// <summary>Payloads are stored and echoed verbatim to every reader, so their size
    /// is bounded like any other user-authored field.</summary>
    internal static bool BeWithinAnchorSize(JsonElement? anchor) =>
        anchor is not { ValueKind: JsonValueKind.Object } payload
        || payload.GetRawText().Length <= ResLinkRules.MaxAnchorLength;
}

public sealed class ResLinkUpdateRequestValidator : AbstractValidator<ResLinkUpdateRequest>
{
    public ResLinkUpdateRequestValidator()
    {
        RuleFor(x => x.Description).MaximumLength(4000);
    }
}

public sealed class ResLinkMemberAddRequestValidator : AbstractValidator<ResLinkMemberAddRequest>
{
    public ResLinkMemberAddRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.NewGeoPoint is not null
                ? x.TargetType is null && x.TargetId is null
                : !string.IsNullOrWhiteSpace(x.TargetType) && x.TargetId is { } id && id != Guid.Empty)
            .WithMessage("Provide either a target (type + id) or newGeoPoint, not both.");
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.Anchor).Must(ResLinkCreateRequestValidator.BeAJsonObjectOrAbsent)
            .WithMessage("The anchor payload must be a JSON object.");
        RuleFor(x => x.Anchor).Must(ResLinkCreateRequestValidator.BeWithinAnchorSize)
            .WithMessage("The anchor payload is too large.");

        When(x => x.NewGeoPoint is not null, () =>
        {
            // A freshly created point has no parts to anchor into.
            RuleFor(x => x.AnchorKind).Equal(AnchorKind.Whole)
                .WithMessage("A new GPS point joins as a whole member.");
            RuleFor(x => x.Anchor).Null();
            RuleFor(x => x.AnchorFileId).Null();
            RuleFor(x => x.NewGeoPoint!.Lon).InclusiveBetween(-180, 180);
            RuleFor(x => x.NewGeoPoint!.Lat).InclusiveBetween(-90, 90);
            RuleFor(x => x.NewGeoPoint!.Name).MaximumLength(200);
        });
    }
}

public sealed class ResLinkMemberUpdateRequestValidator : AbstractValidator<ResLinkMemberUpdateRequest>
{
    public ResLinkMemberUpdateRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}

public sealed class ResLinkRelationTypeWriteRequestValidator
    : AbstractValidator<ResLinkRelationTypeWriteRequest>
{
    public ResLinkRelationTypeWriteRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.InverseName).MaximumLength(100);
        // An inverse reading only exists where there are two distinguishable sides.
        RuleFor(x => x.InverseName).Null().When(x => !x.Directed)
            .WithMessage("An undirected relation has no inverse name.");
    }
}
