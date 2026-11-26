// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SilexGis.Domain.Auth;

/// <summary>
/// The text of a render node's token: a fixed marker and 32 random bytes in base64url.
/// </summary>
/// <remarks>
/// Stored as its SHA-256 and looked up by it, as a personal access key is: the secret has nothing
/// to guess, so a slow hash would only cost every call a node makes. Its marker differs from a
/// personal access key's, so the sign-in machinery never mistakes one for the other — a node token
/// is not an account's credential and opens no route an account opens.
/// </remarks>
public static class RenderNodeTokens
{
    /// <summary>The marker every node token starts with.</summary>
    public const string Marker = "sgn_";

    /// <summary>How many characters of a token are kept in clear and shown in the node list.</summary>
    public const int PrefixLength = 12;

    /// <summary>The longest value worth hashing; a minted token is 47 characters.</summary>
    public const int MaxPlausibleLength = 100;

    /// <summary>A fresh token, the hash that is stored, and the part kept in clear.</summary>
    public static (string Token, byte[] Hash, string Prefix) Mint()
    {
        var token = Marker + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token), token[..PrefixLength]);
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>Whether a bearer value is worth looking up at all; anything else is refused as unknown.</summary>
    public static bool IsPlausible(string? value) =>
        value is not null && value.StartsWith(Marker, StringComparison.Ordinal) && value.Length <= MaxPlausibleLength;
}
