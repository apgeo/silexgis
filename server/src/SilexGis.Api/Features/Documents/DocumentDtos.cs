// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain;
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
    Visibility Visibility,
    Guid? CavingGroupId,

    /// <summary>
    /// The cabinets this document is filed in — where it lives, and therefore which
    /// cabinet-scoped rules reach it. Served because filing is edited from the document
    /// side as well as from the tree, and a control that could file but not show what is
    /// already filed would be a one-way door. No disclosure of its own: the filing tree is
    /// readable by anyone signed in, and this document has already been read-gated.
    /// </summary>
    IReadOnlyList<Guid> CabinetIds,
    Guid CurrentFileId,
    int CurrentVersionNumber,
    string MimeType,
    long SizeBytes,
    FileKind Kind,
    int? PageCount,

    /// <summary>
    /// How far reading this document's text has got. Served so the interface can say which
    /// of the two silences it is looking at: a document nothing has read yet will have text
    /// shortly, and a scanned one never will, and both otherwise show as a document with no
    /// words in it. No disclosure of its own — it is a fact about the document, and the
    /// document has already been read-gated.
    /// </summary>
    TextExtractionState TextExtraction,

    /// <summary>
    /// The language its text was read as, or null when nothing has said. Served because it is
    /// correctable, and a control that could set it but not show the current value would be
    /// asking the reader to guess whether detection had run. No disclosure of its own.
    /// </summary>
    string? Language,
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
/// <para>
/// <see cref="Visibility"/> and <see cref="CavingGroupId"/> are the document's read
/// audience and its club binding — the facts the access rule falls back on when no rule
/// names the document. They are ordinary fields of this request, so changing them is a
/// write on the document like any other; binding to a club additionally requires belonging
/// to it or holding a rule that names its content.
/// </para>
/// </summary>
/// <param name="Language">
/// The language the document is written in, as a language subtag; correcting it re-indexes
/// every page of every revision under the stemmer that code names. Shares the metadata
/// carve-out: absent leaves the stored code alone, because the code is detected when the text
/// is read and a title correction that did not mention it must not undo that. Sending an empty
/// string — or anything else that is not a language subtag — clears it back to unstated, which
/// indexes language-neutrally.
/// </param>
public sealed record DocumentUpdateRequest(
    string Title,
    long? DocumentTypeId,
    JsonElement? Metadata,
    Visibility Visibility,
    Guid? CavingGroupId,
    string? Language);
