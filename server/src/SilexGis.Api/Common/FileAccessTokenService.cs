// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace SilexGis.Api.Common;

/// <summary>
/// How much of a file a delivery token opens. Everything a token can be used for is
/// decided when it is minted, because the routes that redeem it have no caller to ask.
/// </summary>
public enum FileDelivery
{
    /// <summary>
    /// Generated renderings only — thumbnails and the like. Those are produced by this
    /// application and carry no metadata, so they disclose only what they depict.
    /// </summary>
    DerivativesOnly = 0,

    /// <summary>The stored bytes exactly as uploaded, plus everything a derivative opens.</summary>
    Full = 1,
}

/// <summary>
/// Short-lived capability tokens for file content delivery. Browsers load images and
/// COG tiles ambiently (no Authorization header possible), so the API checks the
/// caller's permission once when handing out URLs and encodes (fileId, what it opens,
/// expiry) into a stateless token the content endpoints verify without a database hit.
/// A revoked permission keeps working until the token expires — accepted staleness.
/// </summary>
/// <remarks>
/// The token carries no identity, so nothing downstream of it can re-decide anything: a
/// URL handed out is a decision already taken. That is why the reach is part of what is
/// signed. A photo's own bytes hold the GPS fix its camera wrote, which is a position and
/// not a fact about one, so a caller who may see the picture but not place what it shows
/// gets a token good for the stripped rendering and nothing else.
/// </remarks>
public interface IFileAccessTokenService
{
    /// <summary>
    /// Mints a token. There is deliberately no default reach — a mint site that has not
    /// thought about whether the caller may have the original bytes has not thought about
    /// the question this type exists to answer.
    /// </summary>
    string CreateToken(Guid fileId, FileDelivery delivery);

    /// <summary>
    /// What the token opens for this file, or null when it is not a valid token for it.
    /// </summary>
    FileDelivery? Validate(string token, Guid fileId);
}

public sealed class FileAccessTokenService : IFileAccessTokenService
{
    /// <summary>Long enough for a gallery/COG session, short enough to limit link sharing.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector protector;

    public FileAccessTokenService(IDataProtectionProvider provider) =>
        protector = provider.CreateProtector("SilexGis.FileAccess").ToTimeLimitedDataProtector();

    public string CreateToken(Guid fileId, FileDelivery delivery) =>
        protector.Protect(Payload(fileId, delivery), DateTimeOffset.UtcNow.Add(Lifetime));

    public FileDelivery? Validate(string token, Guid fileId)
    {
        string plain;
        try
        {
            plain = protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return null; // tampered or expired
        }

        // Both reaches are spelled out rather than one being the absence of a marker, so a
        // payload this version does not understand cannot be read as the permissive one.
        foreach (var delivery in new[] { FileDelivery.Full, FileDelivery.DerivativesOnly })
        {
            if (plain == Payload(fileId, delivery))
            {
                return delivery;
            }
        }

        return null;
    }

    private static string Payload(Guid fileId, FileDelivery delivery) =>
        $"{fileId:N}.{(delivery == FileDelivery.Full ? "f" : "d")}";
}
