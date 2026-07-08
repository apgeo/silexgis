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
    DateTimeOffset CreatedAt,
    string ContentUrl,
    string? ThumbnailUrl);

internal static class FileMapping
{
    public static FileDto ToDto(this StoredFile f, IFileAccessTokenService tokens)
    {
        var token = tokens.CreateToken(f.Id);
        return new FileDto(
            f.Id,
            f.OriginalName,
            f.MimeType,
            f.SizeBytes,
            f.Sha256,
            f.Kind,
            f.CreatedAt,
            $"/api/v1/files/{f.Id}/content?token={Uri.EscapeDataString(token)}",
            f.Kind == FileKind.Image
                ? $"/api/v1/files/{f.Id}/thumbnail?size=480&token={Uri.EscapeDataString(token)}"
                : null);
    }
}
