// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Uploads;

/// <summary>
/// One drop, as the list of them shows it.
/// </summary>
/// <param name="Label">What the uploader called it, or null.</param>
/// <param name="SourceDescription">
/// The archive's name, or the server path that was walked. Null for a browser drop, where the
/// answer would be "a browser" and would tell nobody anything.
/// </param>
/// <param name="Error">
/// Why the batch as a whole failed, when it did — an archive that would not open, a directory
/// that was not there. Distinct from a batch that ran to the end with some files failing,
/// which has no error and a non-zero failed count.
/// </param>
public sealed record UploadBatchDto(
    Guid Id,
    UploadSource Source,
    UploadBatchStatus Status,
    string? Label,
    Guid? CabinetId,
    long? TagId,
    string? SourceDescription,
    int TotalCount,
    int StoredCount,
    int SkippedCount,
    int FailedCount,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// One line of a batch's report: which source file became which document, or why it did not.
/// </summary>
/// <param name="Reason">
/// A stable code rather than a sentence — the report is translated on the client like every
/// other message.
/// </param>
/// <param name="DuplicateOfDocumentId">
/// The document already holding these bytes. Only ever set when the person reading the report
/// may actually see it.
/// </param>
public sealed record UploadBatchItemDto(
    long Id,
    string SourcePath,
    long SizeBytes,
    UploadItemOutcome Outcome,
    Guid? DocumentId,
    string? DocumentTitle,
    Guid? CabinetId,
    string? Reason,
    Guid? DuplicateOfDocumentId,
    DateTimeOffset CreatedAt);

/// <summary>Opens a drop, so that everything in it can be found together afterwards.</summary>
/// <param name="Label">A name for the drop, shown wherever it is listed.</param>
/// <param name="CabinetId">The shelf everything is aimed at, when the drop names one.</param>
/// <param name="TagName">
/// A tag to apply to everything the drop creates. Named rather than identified, because the
/// point of it is that somebody types "Bulletin scans" — the tag is created if it does not
/// exist. The batch id already identifies the set; this is the part meant for browsing.
/// </param>
public sealed record UploadBatchOpenRequest(string? Label, Guid? CabinetId, string? TagName);

public sealed class UploadBatchOpenRequestValidator : AbstractValidator<UploadBatchOpenRequest>
{
    public UploadBatchOpenRequestValidator()
    {
        RuleFor(x => x.Label).MaximumLength(200);
        RuleFor(x => x.TagName).MaximumLength(100);
    }
}

/// <summary>Starts an import from a directory the server itself can reach.</summary>
/// <param name="Path">
/// The directory to walk. Refused unless it resolves — links and all — inside one of the roots
/// the operator listed when they deployed the installation.
/// </param>
/// <param name="CabinetId">The shelf the mirrored subtree hangs under.</param>
public sealed record DirectoryImportRequest(string Path, string? Label, Guid? CabinetId, string? TagName);

public sealed class DirectoryImportRequestValidator : AbstractValidator<DirectoryImportRequest>
{
    public DirectoryImportRequestValidator()
    {
        RuleFor(x => x.Path).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Label).MaximumLength(200);
        RuleFor(x => x.TagName).MaximumLength(100);
    }
}

/// <summary>
/// Which directories this installation may import from, so the page can offer them rather than
/// asking somebody to type a path and discover it is refused.
/// </summary>
/// <param name="Roots">
/// The configured roots, resolved. Empty means the operator has not switched the feature on,
/// which the page states rather than showing a field that refuses everything.
/// </param>
public sealed record DirectoryImportConfigDto(IReadOnlyList<string> Roots);
