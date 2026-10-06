// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SilexGis.Domain.Calendar;

/// <summary>
/// How a calendar feed address is minted and how it is looked up — one home for both halves,
/// because the mint and the feed live in different slices and a hash computed two ways is a feed
/// that never answers.
/// </summary>
public static class CalendarFeedTokens
{
    /// <summary>
    /// The longest token the feed will even hash. A minted token is 43 characters; anything far
    /// past that is not a token somebody was given, and refusing it before the hash keeps a
    /// kilobyte of path from costing a kilobyte of hashing.
    /// </summary>
    public const int MaxTokenLength = 100;

    /// <summary>
    /// A fresh address and the form of it that is stored. The first is shown to the holder exactly
    /// once; the second is all the database ever holds.
    /// </summary>
    public static (string Token, string Hash) Mint()
    {
        // 32 random bytes, base64url-encoded, become the URL token; only its SHA-256 is stored,
        // so a database leak cannot resurrect live feeds and the token cannot be shown twice.
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    /// <summary>SHA-256 of the URL token, base64url — the stored and looked-up form.</summary>
    public static string Hash(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Whether a value from an address is even worth hashing. Malformed and oversized tokens are
    /// refused exactly as unknown ones are, by the caller, so nothing here says why.
    /// </summary>
    public static bool IsPlausible(string? token) =>
        !string.IsNullOrEmpty(token) && token.Length <= MaxTokenLength;
}
