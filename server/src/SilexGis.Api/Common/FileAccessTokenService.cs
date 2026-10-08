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
/// <summary>
/// How long a delivery token stays good for, decided by the mint site from how the address will
/// be used.
/// </summary>
public enum FileAccessLifetime
{
    /// <summary>
    /// Minutes: a picture, a document, a mesh — one fetch, made soon after the address was handed
    /// out. Short enough that a copied link stops working before it has gone far.
    /// </summary>
    Fetch = 0,

    /// <summary>
    /// Hours: an address a tile reader keeps open and asks ranges of for as long as a map is on
    /// screen. A reader holds the address it was created with, so a token that expires under it
    /// refuses the next range request on a layer that has been drawing perfectly well for ten
    /// minutes — a session that ends in the middle of a pan, with nothing on screen to say why.
    /// Measured in hours because that is the length of a working session, and because the listing
    /// that mints these is re-read while the page is open, so a page left open gets a fresh one.
    /// </summary>
    Session = 1,
}

/// <summary>
/// Whether a delivery of a file's stored bytes says what the upload was called.
/// </summary>
/// <remarks>
/// Decided at the mint like everything else a token carries, and for the same reason: the route
/// that hands the bytes over has no caller to ask and re-decides nothing. It is not a reach — both
/// values open exactly the same bytes — but a file's name is something its uploader wrote, and
/// people name files after what is in them, so whether it travels is a disclosure of its own.
/// </remarks>
public enum FileNaming
{
    /// <summary>
    /// The download is called what the upload was called. What a member fetching a file to work on
    /// it needs, and what every mint site gets unless it says otherwise.
    /// </summary>
    AsUploaded = 0,

    /// <summary>
    /// The download carries a fixed name that says nothing. For an address handed to somebody who
    /// was given the file's contents and nothing about where they came from.
    /// </summary>
    Withheld = 1,
}

public interface IFileAccessTokenService
{
    /// <summary>
    /// Mints a token for the caller of the current request. There is deliberately no
    /// default reach — a mint site that has not thought about whether the caller may have
    /// the original bytes has not thought about the question this type exists to answer.
    /// </summary>
    /// <param name="naming">
    /// Whether a delivery of the stored bytes under this token is called what the upload was
    /// called. A token that says nothing about it names the file, as every delivery always did.
    /// </param>
    string CreateToken(
        Guid fileId,
        FileDelivery delivery,
        FileAccessLifetime lifetime = FileAccessLifetime.Fetch,
        FileNaming naming = FileNaming.AsUploaded);

    /// <summary>
    /// What the token opens for this file and who it was minted for, or null when it is
    /// not a valid token for that file — including when there is no token at all, which is
    /// the ordinary case of somebody typing a delivery route into a browser.
    /// </summary>
    FileAccessGrant? Validate(string? token, Guid fileId);
}

/// <summary>
/// A redeemed delivery token: how far it reaches, the person it was handed to (null for
/// a token minted outside any authenticated request, or one whose subject came back
/// unreadable), and whether the stored bytes go out under the name they were uploaded with. A
/// payload that does not carry a subject at all is not a grant of any reach — it is refused,
/// which is what happens to every token minted by a build older than this one.
/// </summary>
public sealed record FileAccessGrant(FileDelivery Delivery, Guid? UserId, FileNaming Naming);

public sealed class FileAccessTokenService : IFileAccessTokenService
{
    /// <summary>Long enough for a gallery page, short enough to limit link sharing.</summary>
    /// <remarks>
    /// Readable by the rest of the application because an answer that hands such an address out
    /// may be kept by its reader, and whatever decides how long a kept copy may go on being
    /// confirmed has to be sized against this and not against a second copy of the number.
    /// </remarks>
    internal static readonly TimeSpan FetchLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Long enough for a map session over a raster read in ranges. Four hours and not a day: the
    /// address is still a bearer of the file, and what it bears is a georeferenced map somebody
    /// chose to share with this caller, so the revocation staleness this service accepts is
    /// stretched to one sitting, not removed.
    /// </summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(4);

    private const string Anonymous = "-";

    private readonly ITimeLimitedDataProtector protector;
    private readonly ICurrentUser currentUser;

    public FileAccessTokenService(IDataProtectionProvider provider, ICurrentUser currentUser)
    {
        protector = provider.CreateProtector("SilexGis.FileAccess").ToTimeLimitedDataProtector();
        this.currentUser = currentUser;
    }

    public string CreateToken(
        Guid fileId,
        FileDelivery delivery,
        FileAccessLifetime lifetime = FileAccessLifetime.Fetch,
        FileNaming naming = FileNaming.AsUploaded) =>
        protector.Protect(
            Payload(fileId, delivery, naming, currentUser.UserId),
            DateTimeOffset.UtcNow.Add(lifetime == FileAccessLifetime.Session ? SessionLifetime : FetchLifetime));

    public FileAccessGrant? Validate(string? token, Guid fileId)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

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
        // payload this version does not understand cannot be read as the permissive one. The
        // same goes for the name: a token that withholds it says so in a word of its own, and a
        // word this version does not know is no grant at all rather than a grant that names.
        (FileDelivery Delivery, FileNaming Naming)? reach = parts[1] switch
        {
            "f" => (FileDelivery.Full, FileNaming.AsUploaded),
            "fn" => (FileDelivery.Full, FileNaming.Withheld),
            "d" => (FileDelivery.DerivativesOnly, FileNaming.AsUploaded),
            "dn" => (FileDelivery.DerivativesOnly, FileNaming.Withheld),
            _ => null,
        };
        if (reach is null)
        {
            return null;
        }

        // An unreadable subject is treated as no subject rather than as a bad token: who
        // the bytes were promised to is bookkeeping, and losing it must not turn a valid
        // grant into a refusal.
        var subject = Guid.TryParse(parts[2], out var userId) ? userId : (Guid?)null;
        return new FileAccessGrant(reach.Value.Delivery, subject, reach.Value.Naming);
    }

    private static string Payload(Guid fileId, FileDelivery delivery, FileNaming naming, Guid? userId) =>
        $"{fileId:N}.{(delivery == FileDelivery.Full ? "f" : "d")}{(naming == FileNaming.Withheld ? "n" : "")}"
        + $".{(userId is { } id ? id.ToString("N") : Anonymous)}";
}
