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
    /// </summary>
    public static FileDto ToDto(this StoredFile f, DocumentVersion version, IFileAccessTokenService tokens)
    {
        var token = tokens.CreateToken(f.Id);
        return new FileDto(
            f.Id,
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
