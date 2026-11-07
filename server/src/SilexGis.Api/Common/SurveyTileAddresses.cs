// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Scans;

namespace SilexGis.Api.Common;

/// <summary>
/// The one way an address of a scan's tiles is written, for every answer that hands one out: the
/// scan card on its upload, and the map listing of placed scans.
/// </summary>
/// <remarks>
/// <para>
/// The address names the survey model and the entry, and carries a file delivery token minted
/// over the model's converted file — the tileset archive — for the whole of the stored bytes and
/// for a session rather than a fetch. A tile reader keeps the root address and derives every
/// child tile's from it, passing the token on, for as long as the scene is open; a token that
/// lapsed under it would refuse the next tile of a cloud that has been drawing for ten minutes,
/// with nothing on screen to say why.
/// </para>
/// <para>
/// Binding the token to the converted file rather than to the model is what makes it stop
/// working the moment the scan is tiled again: the new archive is a new file, and an address
/// that was handed out for the old one opens nothing. It also means the same token would open
/// the archive whole at the file route — the same bytes, under the same rule, so nothing is
/// gained by a caller who notices.
/// </para>
/// </remarks>
public static class SurveyTileAddresses
{
    /// <summary>
    /// The address of the root tileset of this model's archive, signed for the caller of the
    /// current request. Asked only once the caller's right to the scan has been decided: the
    /// tiles carry no coordinates, so the scan's default rules decide it, not the exact-location
    /// one.
    /// </summary>
    public static string Root(IFileAccessTokenService tokens, Guid modelId, Guid convertedFileId) =>
        $"/api/v1/survey-models/{modelId}/tiles/{TilesetEntryNames.Root}"
        + $"?token={Uri.EscapeDataString(tokens.CreateToken(convertedFileId, FileDelivery.Full, FileAccessLifetime.Session))}";
}
