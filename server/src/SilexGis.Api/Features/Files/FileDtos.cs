// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Features.Files;

/// <summary>
/// Upload limits this installation applies, and how much room the caller has left.
/// Published so a client checks a file before transferring it rather than after, and so no
/// client build carries a number that could disagree with the server's.
/// </summary>
/// <param name="MaxUploadBytes">Largest single file accepted.</param>
/// <param name="RemainingBytes">
/// How much this caller may still store, or null when nothing limits them. Stated up front
/// because a quota discovered at the end of a 400 MB transfer is the same as no warning at
/// all — and because "you have 2 GB left" is a thing somebody can act on before they start.
/// </param>
/// <param name="QuotaBytes">Their total allowance, or null when they have none.</param>
/// <param name="UsedBytes">How much they already hold. Null when nothing is counting.</param>
/// <param name="AcceptedExtensions">
/// What this installation takes, lower-case and dotted. Empty means everything, which is the
/// default: an archive whose purpose is holding whatever a caving club has accumulated should
/// not have a list of formats somebody has to remember to extend.
/// </param>
/// <param name="RefusedExtensions">What it will not take, whatever the list above says.</param>
/// <param name="ChunkBytes">
/// The piece size a resumable upload should send. Advice — the server accepts whatever
/// arrives — but following it keeps a dropped connection cheap.
/// </param>
/// <param name="ResumableThresholdBytes">
/// Above this size a client should open a resumable session rather than send one request.
/// Below it the extra round trips cost more than the resumption is worth.
/// </param>
/// <param name="ArchiveExtensions">
/// Archives this installation can expand server-side. Empty would mean the feature is off;
/// it is what the upload dialog offers "expand this" for.
/// </param>
/// <param name="ConversionAvailable">
/// Whether this installation runs the optional service that lays an office document out as a
/// portable one. A standing fact about the installation, false on most small ones, and said
/// here so a page can leave out a choice that could only be refused — a generated write-up
/// offered as a PDF, for one — rather than offer it and apologise.
/// </param>
public sealed record FileConfigDto(
    long MaxUploadBytes,
    long? RemainingBytes,
    long? QuotaBytes,
    long? UsedBytes,
    IReadOnlyList<string> AcceptedExtensions,
    IReadOnlyList<string> RefusedExtensions,
    int ChunkBytes,
    long ResumableThresholdBytes,
    IReadOnlyList<string> ArchiveExtensions,
    bool ConversionAvailable);

/// <summary>
/// Whether content with this hash is already here, as far as this caller is concerned.
/// </summary>
/// <param name="Duplicate">
/// True only when a document holding exactly these bytes exists <em>and</em> this caller may
/// read it. A copy they may not read is reported as no copy at all — telling them it exists
/// is the disclosure the whole rule is there to prevent — and they get their own copy, with
/// the pair recorded for an administrator who can see both.
/// </param>
/// <param name="DocumentId">The document already holding it, when there is one to name.</param>
/// <param name="Title">Its title, so the warning can say what it collides with.</param>
public sealed record DuplicateCheckDto(bool Duplicate, Guid? DocumentId, string? Title);

/// <summary>How far a resumable upload has got.</summary>
/// <param name="Id">The session, named in every later piece.</param>
/// <param name="ReceivedBytes">
/// The offset to send from. A resuming client asks for this and continues; it is read back
/// from the stored bytes rather than remembered, so it never describes bytes that did not
/// survive.
/// </param>
/// <param name="DeclaredSizeBytes">What the file was said to be, and what completion measures against.</param>
/// <param name="ChunkBytes">The piece size to send.</param>
/// <param name="ExpiresAt">
/// When the session stops being resumable and its bytes are swept up. For staged bytes, when
/// they go if no sync upload has adopted them.
/// </param>
/// <param name="StagedForSync">Whether the transfer was opened to stage bytes for a sync upload.</param>
/// <param name="StagedAt">
/// When staged bytes were completed and described; absent while they are still arriving. A
/// phone asking whether its staged bytes are still waiting reads this, and an answer of
/// "not found" means they are gone and must be sent again.
/// </param>
/// <param name="Sha256">The hash the server computed for staged bytes, once they are staged.</param>
public sealed record UploadSessionDto(
    Guid Id,
    long ReceivedBytes,
    long DeclaredSizeBytes,
    int ChunkBytes,
    DateTimeOffset ExpiresAt,
    bool StagedForSync,
    DateTimeOffset? StagedAt,
    string? Sha256);

/// <summary>
/// Bytes a phone sent ahead of the sync upload that will name them, completed and described
/// but not yet anybody's document.
/// </summary>
/// <param name="Id">What the upload names to adopt them — the transfer's own id.</param>
/// <param name="FileName">The name the transfer was opened with.</param>
/// <param name="SizeBytes">How many bytes are held.</param>
/// <param name="Sha256">
/// The lowercase hex SHA-256 the server computed. The upload's document row must state the
/// same hash, so a phone can compare it with its own before it sends the row.
/// </param>
/// <param name="MimeType">The type sniffed from the content, not taken from the name.</param>
/// <param name="Kind">What kind of file the content is.</param>
/// <param name="StagedAt">When the bytes were staged.</param>
/// <param name="ExpiresAt">When they are swept if no upload has adopted them.</param>
public sealed record StagedUploadDto(
    Guid Id,
    string FileName,
    long SizeBytes,
    string Sha256,
    string MimeType,
    FileKind Kind,
    DateTimeOffset StagedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// The extracted words of one page. The text is what a reader wrote into the search index,
/// character for character — a selection is measured against this, so anything that
/// normalised it here would move every offset composed from it.
/// </summary>
public sealed record PageTextDto(int Page, string Text);

/// <summary>One revision of a document (newest first). Superseded ones are editor-only.</summary>
public sealed record FileVersionDto(
    Guid Id,
    int VersionNumber,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    Guid? UploadedBy,
    string? UploaderName,
    DateTimeOffset CreatedAt,
    string ContentUrl,
    bool IsHead);
