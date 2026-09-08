// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace SilexGis.Api.Common;

/// <summary>
/// Short-lived permission to fetch one computed terrain raster, carried in its URL.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the tokens that deliver stored files, and for the same reason: a tile reader
/// in a browser fetches ranges of a raster ambiently, with no way to put a credential on the
/// request, so the permission is decided once when the address is handed out and encoded into the
/// address itself.
/// </para>
/// <para>
/// A separate signature from the one file delivery uses, deliberately. The two name different
/// things — one a row in the catalogue of stored files, this one a raster computed from public
/// elevation and kept in a build's own folder — and a single signature over "some identifier"
/// would mean a token minted for one of them opened the other if the identifiers ever met. They
/// are also gated on different rights, so conflating them would put a file behind whichever of the
/// two checks happened to run.
/// </para>
/// </remarks>
public interface ITerrainRasterTokenService
{
    /// <summary>Mints permission to fetch one computed raster of one layer.</summary>
    string CreateToken(Guid layerId, long rasterId);

    /// <summary>Whether this token opens that raster of that layer.</summary>
    bool Validate(string? token, Guid layerId, long rasterId);
}

public sealed class TerrainRasterTokenService : ITerrainRasterTokenService
{
    /// <summary>Long enough for a map session, short enough to limit a copied link.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector protector;

    public TerrainRasterTokenService(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector("SilexGis.TerrainRaster").ToTimeLimitedDataProtector();
    }

    public string CreateToken(Guid layerId, long rasterId) =>
        protector.Protect(Payload(layerId, rasterId), DateTimeOffset.UtcNow.Add(Lifetime));

    public bool Validate(string? token, Guid layerId, long rasterId)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            return protector.Unprotect(token) == Payload(layerId, rasterId);
        }
        catch (CryptographicException)
        {
            return false; // tampered or expired
        }
    }

    private static string Payload(Guid layerId, long rasterId) =>
        $"{layerId:N}.{rasterId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
