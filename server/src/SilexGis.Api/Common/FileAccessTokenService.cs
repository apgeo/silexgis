// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SilexGis.Domain;

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
/// <para>
/// Nothing downstream of a token may re-decide anything: a URL handed out is a decision
/// already taken. That is why the reach is part of what is signed. A photo's own bytes hold
/// the GPS fix its camera wrote, which is a position and not a fact about one, so a caller
/// who may see the picture but not place what it shows gets a token good for the stripped
/// rendering and nothing else.
/// </para>
/// <para>
/// The token also names who it was minted for. That is not an input to any decision — the
/// reach still is, and the delivery routes still ask nothing else — it is so that a
/// delivery of original bytes can be recorded against a person. The alternative was to
/// record at every place a URL is built, but those are listing endpoints: a gallery of
/// forty photos mints forty tokens and a page left open re-mints them every few minutes, so
/// counting mints would count having a page on screen as having taken forty copies. The
/// subject travels inside the protected payload, so it is ciphertext to everything that
/// handles the URL, including the browser history and any log the URL lands in.
/// </para>
/// </remarks>
public interface IFileAccessTokenService
{
    /// <summary>
    /// Mints a token for the caller of the current request. There is deliberately no
    /// default reach — a mint site that has not thought about whether the caller may have
    /// the original bytes has not thought about the question this type exists to answer.
    /// </summary>
    string CreateToken(Guid fileId, FileDelivery delivery);

    /// <summary>
    /// What the token opens for this file and who it was minted for, or null when it is
    /// not a valid token for that file.
    /// </summary>
    FileAccessGrant? Validate(string token, Guid fileId);
}

/// <summary>
/// A redeemed delivery token: how far it reaches, and the person it was handed to (null for
/// a token minted outside any authenticated request, or one whose subject came back
/// unreadable). A payload that does not carry a subject at all is not a grant of any reach —
/// it is refused, which is what happens to every token minted by a build older than this one.
/// </summary>
public sealed record FileAccessGrant(FileDelivery Delivery, Guid? UserId);

public sealed class FileAccessTokenService : IFileAccessTokenService
{
    /// <summary>Long enough for a gallery/COG session, short enough to limit link sharing.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const string Anonymous = "-";

    private readonly ITimeLimitedDataProtector protector;
    private readonly ICurrentUser currentUser;

    public FileAccessTokenService(IDataProtectionProvider provider, ICurrentUser currentUser)
    {
        protector = provider.CreateProtector("SilexGis.FileAccess").ToTimeLimitedDataProtector();
        this.currentUser = currentUser;
    }

    public string CreateToken(Guid fileId, FileDelivery delivery) =>
        protector.Protect(
            Payload(fileId, delivery, currentUser.UserId), DateTimeOffset.UtcNow.Add(Lifetime));

    public FileAccessGrant? Validate(string token, Guid fileId)
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

        var parts = plain.Split('.');
        if (parts.Length != 3 || parts[0] != fileId.ToString("N"))
        {
            return null;
        }

        // Both reaches are spelled out rather than one being the absence of a marker, so a
        // payload this version does not understand cannot be read as the permissive one.
        var delivery = parts[1] switch
        {
            "f" => FileDelivery.Full,
            "d" => FileDelivery.DerivativesOnly,
            _ => (FileDelivery?)null,
        };
        if (delivery is null)
        {
            return null;
        }

        // An unreadable subject is treated as no subject rather than as a bad token: who
        // the bytes were promised to is bookkeeping, and losing it must not turn a valid
        // grant into a refusal.
        var subject = Guid.TryParse(parts[2], out var userId) ? userId : (Guid?)null;
        return new FileAccessGrant(delivery.Value, subject);
    }

    private static string Payload(Guid fileId, FileDelivery delivery, Guid? userId) =>
        $"{fileId:N}.{(delivery == FileDelivery.Full ? "f" : "d")}.{(userId is { } id ? id.ToString("N") : Anonymous)}";
}
