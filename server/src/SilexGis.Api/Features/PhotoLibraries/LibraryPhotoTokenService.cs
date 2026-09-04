// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SilexGis.Domain;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// Mints and checks the short-lived credential a browser carries when it asks this application for
/// a picture held in a neighbouring photo library.
/// </summary>
public interface ILibraryPhotoTokenService
{
    /// <summary>
    /// A token good for one library's pictures, for the account this request belongs to, for the
    /// next few minutes.
    /// </summary>
    string CreateToken(PhotoLibrarySource source);

    /// <summary>Whether this token was minted by this installation for this library and has not expired.</summary>
    bool Validate(string? token, PhotoLibrarySource source);
}

/// <summary>
/// Short-lived capability tokens for pictures fetched from a neighbouring photo library. A browser
/// loads an image ambiently and cannot attach a bearer token, so the caller's right to see the
/// layer is checked once — where the layer is served — and encoded into a token the delivery route
/// verifies without a database hit.
/// </summary>
/// <remarks>
/// <para>
/// <b>The token is per library, per viewer, per ten minutes — not per photograph.</b> That is
/// deliberate and it is the honest shape for the decision behind it: every account that may see
/// this layer may see every photograph in it, so a per-photograph token would be per-photograph
/// precision over a judgment nobody makes per photograph, and it would put a hundred and fifty
/// bytes of ciphertext on every one of thousands of features. When a rule that differs per
/// photograph arrives, this is the thing that becomes per-photograph, and the delivery route is
/// where it would be re-asked.
/// </para>
/// <para>
/// Its protection purpose is its own. A token minted for a neighbouring library must not be
/// redeemable against this application's own stored files, and separate purposes make that
/// impossible rather than merely unlikely.
/// </para>
/// <para>
/// A right taken away keeps working until the token expires — the same staleness this application's
/// own delivery tokens already accept, and the reason the lifetime is ten minutes rather than an
/// hour.
/// </para>
/// </remarks>
public sealed class LibraryPhotoTokenService : ILibraryPhotoTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const string Anonymous = "-";

    private readonly ITimeLimitedDataProtector protector;
    private readonly ICurrentUser currentUser;

    public LibraryPhotoTokenService(IDataProtectionProvider provider, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(provider);

        protector = provider.CreateProtector("SilexGis.PhotoLibraryAccess").ToTimeLimitedDataProtector();
        this.currentUser = currentUser;
    }

    public string CreateToken(PhotoLibrarySource source) =>
        protector.Protect(
            $"{(int)source}.{(currentUser.UserId is { } id ? id.ToString("N") : Anonymous)}",
            DateTimeOffset.UtcNow.Add(Lifetime));

    public bool Validate(string? token, PhotoLibrarySource source)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        string plain;
        try
        {
            plain = protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return false; // tampered, or minted for something else, or expired
        }

        // The library is part of what was signed rather than taken from the address: a token for
        // one library must not open another, or adding a second product would silently widen the
        // first one's tokens.
        var parts = plain.Split('.');
        return parts.Length == 2
            && int.TryParse(parts[0], out var minted)
            && minted == (int)source;
    }
}
