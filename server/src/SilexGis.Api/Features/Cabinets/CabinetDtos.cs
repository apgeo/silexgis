// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Cabinets;

/// <summary>
/// One node of the filing tree. <paramref name="AncestorIds"/> runs root first and ends
/// with the cabinet itself, so a client draws a breadcrumb from the flat list it already
/// has instead of asking per level.
/// </summary>
/// <param name="DocumentCount">
/// How many documents are filed here — all of them, not the caller's readable subset. A
/// cabinet is a security anchor, so this count and the document listing can legitimately
/// differ; the count says how full a shelf is, the listing says what is on it for you.
/// </param>
public sealed record CabinetDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string? Description,
    IReadOnlyList<Guid> AncestorIds,
    int DocumentCount);

/// <summary>
/// Creating or editing a cabinet. <paramref name="ParentId"/> is the whole placement:
/// sending a different one on an update moves the cabinet and everything below it, since
/// a cabinet sits in exactly one place.
/// </summary>
public sealed record CabinetWriteRequest(string Name, string? Description, Guid? ParentId);

/// <summary>A document as a filing tree lists it: enough to draw a row, no file bytes.</summary>
public sealed record CabinetDocumentDto(
    Guid Id,
    string Title,
    long? DocumentTypeId,
    Visibility Visibility,
    Guid? CavingGroupId,
    Guid? CurrentFileId,
    FileKind? Kind,
    string? MimeType,
    long? SizeBytes,
    DateTimeOffset UpdatedAt);

public sealed class CabinetWriteRequestValidator : AbstractValidator<CabinetWriteRequest>
{
    public CabinetWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);

        // A path label is a cabinet id, never the name, so the name is free text — except
        // for the separator characters a breadcrumb would read as another level.
        RuleFor(x => x.Name).Must(n => n is null || !n.Contains('/', StringComparison.Ordinal))
            .WithMessage("A cabinet name may not contain '/'.");
    }
}
