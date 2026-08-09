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
/// How many documents filed directly here this caller may read — decided by the same read
/// rule as the listing that opens when the shelf is picked, so the number and the listing
/// agree. A count taken over everything filed would state precisely how much the listing
/// withheld, and on an installation using cabinets to keep one club's archive from
/// another's it would report the size of that archive and every time it grew.
/// <para>
/// Directly, because that is what the listing shows by default. Asking a listing for the
/// whole subtree asks a different question — of that cabinet and every one below it — and
/// the number on the tree keeps answering the one the tree is drawn from, since a client
/// that wants the subtree total has every cabinet's count in the same response to add up.
/// </para>
/// </param>
/// <param name="Defaults">
/// What this shelf says about whatever lands on it — its own settings, and the ones it
/// inherits from the shelves above. Sent with the tree because the upload dialog has to show
/// what is about to be applied before anything is uploaded, and because the shelf editor has
/// to be able to say which values on screen are this shelf's own.
/// </param>
public sealed record CabinetDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string? Description,
    IReadOnlyList<Guid> AncestorIds,
    int DocumentCount,
    CabinetDefaultsDto Defaults);

/// <summary>
/// A shelf's defaults, both as written on it and as they come out after inheritance.
/// </summary>
/// <param name="DocumentTypeId">This shelf's own setting; null means "ask the shelf above".</param>
/// <param name="Visibility">This shelf's own setting; null means "ask the shelf above".</param>
/// <param name="TagIds">Tags this shelf itself applies.</param>
/// <param name="RequiredMetadataKeys">
/// Metadata keys this shelf itself expects. Expected, not enforced: an upload missing them
/// still succeeds and the document is marked incomplete, because a shelf that refused an
/// upload for missing metadata would break dropping four hundred scans in and filing them
/// later — and would do it halfway through, for a field nobody can fill in until they have
/// opened the file.
/// </param>
/// <param name="EffectiveDocumentTypeId">What actually applies here, inheritance resolved.</param>
/// <param name="EffectiveVisibility">What actually applies here, inheritance resolved.</param>
/// <param name="EffectiveTagIds">Every tag that applies here, nearest shelf first.</param>
/// <param name="EffectiveRequiredMetadataKeys">
/// Every key expected here. These accumulate down the tree rather than being answered by the
/// nearest shelf: a requirement written on the archive governs everything inside it.
/// </param>
public sealed record CabinetDefaultsDto(
    long? DocumentTypeId,
    Visibility? Visibility,
    IReadOnlyList<long> TagIds,
    IReadOnlyList<string> RequiredMetadataKeys,
    long? EffectiveDocumentTypeId,
    Visibility? EffectiveVisibility,
    IReadOnlyList<long> EffectiveTagIds,
    IReadOnlyList<string> EffectiveRequiredMetadataKeys);

/// <summary>
/// Creating or editing a cabinet. <paramref name="ParentId"/> is the whole placement:
/// sending a different one on an update moves the cabinet and everything below it, since
/// a cabinet sits in exactly one place.
/// </summary>
/// <param name="DefaultDocumentTypeId">Null clears this shelf's own setting.</param>
/// <param name="DefaultVisibility">Null clears this shelf's own setting.</param>
/// <param name="DefaultTagIds">Empty clears them.</param>
/// <param name="RequiredMetadataKeys">Empty clears them.</param>
public sealed record CabinetWriteRequest(
    string Name,
    string? Description,
    Guid? ParentId,
    long? DefaultDocumentTypeId = null,
    Visibility? DefaultVisibility = null,
    IReadOnlyList<long>? DefaultTagIds = null,
    IReadOnlyList<string>? RequiredMetadataKeys = null);

/// <summary>
/// Files and unfiles many documents in one request.
/// </summary>
/// <remarks>
/// Filing is many-to-many, so "copy" is free and "move" is a file plus an unfile — which is
/// why this takes both lists rather than a source and a destination. Doing them in one request
/// is what makes a move atomic from the caller's point of view: two requests can half-succeed
/// and leave a document on both shelves or on neither.
/// </remarks>
/// <param name="DocumentIds">The documents to refile.</param>
/// <param name="FileIntoCabinetIds">Shelves to add them to.</param>
/// <param name="UnfileFromCabinetIds">Shelves to take them off.</param>
public sealed record BulkFilingRequest(
    IReadOnlyList<Guid> DocumentIds,
    IReadOnlyList<Guid>? FileIntoCabinetIds,
    IReadOnlyList<Guid>? UnfileFromCabinetIds);

/// <summary>
/// What a bulk refile did, per document. A partial result rather than all-or-nothing: refusing
/// the whole request because one of two hundred documents is not writable by this caller would
/// make the feature unusable on exactly the archives it exists for.
/// </summary>
/// <param name="Refused">
/// Documents that were not moved, each with the code saying why. A document the caller may not
/// read is absent from both lists — its existence is not disclosed by a refusal.
/// </param>
public sealed record BulkFilingResultDto(
    IReadOnlyList<Guid> Filed,
    IReadOnlyDictionary<Guid, string> Refused);

/// <summary>A document as a filing tree lists it: enough to draw a row, no file bytes.</summary>
/// <param name="MissingMetadataKeys">
/// Which of the keys this shelf expects the document does not answer. Empty means complete,
/// and also means the shelf expects nothing.
/// <para>
/// Measured against the shelf being listed. On a subtree listing that is the shelf above
/// rather than the document's own, so the answer is a true subset of what is really missing —
/// requirements accumulate downwards, and a document deeper in the tree is subject to at least
/// these. A mark that is sometimes incomplete is worth having; one that could be wrong in the
/// other direction would not be.
/// </para>
/// </param>
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
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> MissingMetadataKeys);

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

        RuleFor(x => x.DefaultVisibility).IsInEnum();
        RuleForEach(x => x.RequiredMetadataKeys).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RequiredMetadataKeys)
            .Must(keys => keys is null || keys.Count <= 50)
            .WithMessage("A cabinet may expect at most 50 metadata keys.");
        RuleFor(x => x.DefaultTagIds)
            .Must(tags => tags is null || tags.Count <= 50)
            .WithMessage("A cabinet may apply at most 50 tags.");
    }
}

public sealed class BulkFilingRequestValidator : AbstractValidator<BulkFilingRequest>
{
    /// <summary>
    /// How many documents one request may move. A bound rather than a policy: every document
    /// costs an access walk, and a selection larger than this is a job rather than a request.
    /// </summary>
    public const int MaxDocuments = 500;

    public BulkFilingRequestValidator()
    {
        RuleFor(x => x.DocumentIds).NotEmpty();
        RuleFor(x => x.DocumentIds).Must(ids => ids is null || ids.Count <= MaxDocuments)
            .WithMessage($"At most {MaxDocuments} documents can be refiled in one request.");

        // A request naming no shelf on either side would do nothing and report success, which
        // reads as a defect rather than as a no-op.
        RuleFor(x => x)
            .Must(x => x.FileIntoCabinetIds is { Count: > 0 } || x.UnfileFromCabinetIds is { Count: > 0 })
            .WithMessage("Name at least one cabinet to file into or to unfile from.");
    }
}
