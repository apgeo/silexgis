// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Files;

/// <summary>
/// File metadata plus freshly minted delivery URLs. The URLs embed a short-lived
/// capability token — clients use them as-is in img/src and download links and refetch
/// the metadata when a token expires.
/// </summary>
public sealed record FileDto(
    Guid Id,
    Guid DocumentId,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    string Sha256,
    FileKind Kind,
    int VersionNumber,
    DateOnly? DocumentDate,
    DateTimeOffset CreatedAt,
    string ContentUrl,
    string? ThumbnailUrl);

/// <summary>
/// Upload limits this installation applies. Published so a client checks a file before
/// transferring it rather than after, and so no client build carries a number that could
/// disagree with the server's.
/// </summary>
public sealed record FileConfigDto(long MaxUploadBytes);

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

internal static class FileMapping
{
    /// <summary>
    /// A file plus the revision it belongs to: version number and document date are
    /// version detail, so they are read from there rather than duplicated per file.
    /// The document id comes from the same revision — a file id changes with every new
    /// version, so it is the only identifier that can name the thing being looked at.
    /// </summary>
    /// <param name="mayHaveOriginal">
    /// Whether this caller may be handed the stored bytes of a photo that records where it
    /// was taken. Defaults to no, and the default is the point: a photo's GPS fix is a
    /// position rather than a fact about one, so a mint site that has not resolved the
    /// caller's right to place what the photo shows must not hand out the original — while
    /// everything without a position of its own is unaffected, whatever this says.
    /// </param>
    public static FileDto ToDto(
        this StoredFile f, DocumentVersion version, IFileAccessTokenService tokens, bool mayHaveOriginal = false)
    {
        var delivery = f.Geom is null || mayHaveOriginal ? FileDelivery.Full : FileDelivery.DerivativesOnly;
        var token = tokens.CreateToken(f.Id, delivery);
        return new FileDto(
            f.Id,
            version.DocumentId,
            f.OriginalName,
            f.MimeType,
            f.SizeBytes,
            f.Sha256,
            f.Kind,
            version.VersionNumber,
            version.DocumentDate,
            f.CreatedAt,
            ContentUrl(f.Id, token),
            f.Kind == FileKind.Image
                ? $"/api/v1/files/{f.Id}/thumbnail?size=480&token={Uri.EscapeDataString(token)}"
                : null);
    }

    public static string ContentUrl(Guid fileId, string token) =>
        $"/api/v1/files/{fileId}/content?token={Uri.EscapeDataString(token)}";
}
