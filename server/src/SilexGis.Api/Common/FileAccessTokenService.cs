// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace SilexGis.Api.Common;

/// <summary>
/// Short-lived capability tokens for file content delivery. Browsers load images and
/// COG tiles ambiently (no Authorization header possible), so the API checks the
/// caller's permission once when handing out URLs and encodes (fileId, expiry) into a
/// stateless token the content endpoints verify without a database hit.
/// A revoked permission keeps working until the token expires — accepted staleness.
/// </summary>
public interface IFileAccessTokenService
{
    string CreateToken(Guid fileId);

    bool ValidateToken(string token, Guid fileId);
}

public sealed class FileAccessTokenService : IFileAccessTokenService
{
    /// <summary>Long enough for a gallery/COG session, short enough to limit link sharing.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector protector;

    public FileAccessTokenService(IDataProtectionProvider provider) =>
        protector = provider.CreateProtector("SilexGis.FileAccess").ToTimeLimitedDataProtector();

    public string CreateToken(Guid fileId) =>
        protector.Protect(fileId.ToString("N"), DateTimeOffset.UtcNow.Add(Lifetime));

    public bool ValidateToken(string token, Guid fileId)
    {
        try
        {
            return protector.Unprotect(token) == fileId.ToString("N");
        }
        catch (CryptographicException)
        {
            return false; // tampered or expired
        }
    }
}
