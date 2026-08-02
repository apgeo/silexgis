// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Documents;

/// <summary>
/// A document as its own thing, rather than as whichever file currently represents it:
/// the title and typed metadata that survive a new version, plus the facts read out of the
/// bytes the current version serves.
/// </summary>
/// <remarks>
/// Content facts are columns rather than entries in <see cref="Metadata"/> because document
/// lists filter and order on them; <see cref="Metadata"/> is the per-kind bag whose shape
/// the document type's schema describes. Everything content-derived is null until something
/// has read the file, which for paged and timed formats is text extraction.
/// </remarks>
public sealed record DocumentDto(
    Guid Id,
    string Title,
    long? DocumentTypeId,
    string? DocumentTypeCode,
    JsonElement Metadata,
    int? MetadataSchemaVersion,
    Guid CurrentFileId,
    int CurrentVersionNumber,
    string MimeType,
    long SizeBytes,
    FileKind Kind,
    int? PageCount,
    string? Author,
    string? Producer,
    DateTimeOffset? ContentCreatedAt,
    DateTimeOffset? ContentModifiedAt,
    double? DurationSeconds,
    string? Codec,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The editable document-level fields. A full-DTO update, with one deliberate exception:
/// an absent <see cref="Metadata"/> leaves the stored metadata untouched, so a title can be
/// corrected on a document whose kind has tightened its schema since the document was
/// written. Sending a metadata object always re-validates it against the kind's current
/// schema.
/// </summary>
public sealed record DocumentUpdateRequest(string Title, long? DocumentTypeId, JsonElement? Metadata);
