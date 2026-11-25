// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace SilexGis.Api.Common;

/// <summary>
/// Short-lived permission to fetch the map of one cave-potential result, carried in its URL.
/// </summary>
/// <remarks>
/// <para>
/// A tile reader in a browser fetches ranges of a raster with no way to put a credential on the
/// request, so who may read the map is decided once — when the address is handed to a reader who
/// has just passed the result's own reading rule — and encoded into the address.
/// </para>
/// <para>
/// A signature of its own, not the one computed terrain pictures use. Those are opened by the
/// terrain right alone; this map can show where recorded features are and is opened by a much
/// narrower rule. One signature over "some identifier" would let an address minted for the one
/// open the other if the identifiers ever met.
/// </para>
/// <para>
/// This is how a map is delivered to the reader it was listed for, not a way of sharing one: the
/// address stops working after a few minutes and is never stored or shown as a link.
/// </para>
/// </remarks>
public interface ICavePotentialRasterTokenService
{
    /// <summary>Mints permission to fetch the map of one result.</summary>
    string CreateToken(Guid resultId);

    /// <summary>Whether this token opens that result's map.</summary>
    bool Validate(string? token, Guid resultId);
}

public sealed class CavePotentialRasterTokenService : ICavePotentialRasterTokenService
{
    /// <summary>Long enough for a map session between two listings, short enough to limit a copied address.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector protector;

    private readonly TimeProvider clock;

    public CavePotentialRasterTokenService(IDataProtectionProvider provider, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        protector = provider.CreateProtector("SilexGis.CavePotentialRaster").ToTimeLimitedDataProtector();
        this.clock = clock;
    }

    public string CreateToken(Guid resultId) =>
        protector.Protect(resultId.ToString("N"), clock.GetUtcNow().Add(Lifetime));

    public bool Validate(string? token, Guid resultId)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            return protector.Unprotect(token) == resultId.ToString("N");
        }
        catch (CryptographicException)
        {
            return false; // tampered or expired
        }
    }
}
