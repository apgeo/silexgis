// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SilexGis.Domain.Auth;

/// <summary>
/// What a personal access key may be used for. A key opens only the routes that declare its
/// purpose, so the purpose is what keeps a pasted key from being a whole-account credential.
/// Stored as smallint; values are part of the schema contract — append, never renumber.
/// </summary>
public enum AccessKeyPurpose : short
{
    /// <summary>
    /// Sending surveys from a field device: the survey-upload routes, the caller's own profile and
    /// the cave search a device needs to pick where a survey goes.
    /// </summary>
    SurveyUpload = 0,
}

/// <summary>Where a key stands at a given moment, derived from its stamps and never stored.</summary>
public enum AccessKeyState
{
    Active = 0,
    Expired = 1,
    Revoked = 2,
}

/// <summary>
/// How a personal access key's text is made, recognised and stored — one home for all three,
/// because the mint and the lookup live in different layers and a key hashed two ways is a key
/// that never opens anything.
/// </summary>
/// <remarks>
/// <para>
/// The text is a fixed marker followed by 32 random bytes in base64url. The marker is what lets
/// the authentication layer tell a key from a sign-in token by looking at the bearer value alone,
/// and what lets a secret scanner recognise one pasted where it should not be.
/// </para>
/// <para>
/// Only the SHA-256 of the whole text is stored. The secret is 32 random bytes, so a plain hash
/// is enough: there is nothing to guess, and a slow password hash would only add cost to every
/// request a device makes. Lookup is by the hash itself, which is why no constant-time comparison
/// is needed anywhere.
/// </para>
/// </remarks>
public static class AccessKeyText
{
    /// <summary>The marker every key starts with.</summary>
    public const string Marker = "sgx_";

    /// <summary>How many characters of a key are kept in clear and shown in lists and logs.</summary>
    public const int PrefixLength = 12;

    /// <summary>The length of a minted key: the marker and 43 characters of base64url.</summary>
    public const int Length = 47;

    /// <summary>
    /// The longest value the authentication layer will even hash. Anything far past a minted key's
    /// length is not a key somebody was given, and refusing it before the hash keeps a kilobyte of
    /// header from costing a kilobyte of hashing.
    /// </summary>
    public const int MaxPlausibleLength = 100;

    /// <summary>
    /// A fresh key, the form of it that is stored, and the part of it kept in clear. The first is
    /// shown to its holder exactly once; the database never holds it.
    /// </summary>
    public static (string Key, byte[] Hash, string Prefix) Mint()
    {
        var key = Marker + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (key, Hash(key), PrefixOf(key));
    }

    /// <summary>SHA-256 of the whole key text — the stored and looked-up form.</summary>
    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    /// <summary>The leading characters kept in clear, so a holder can tell their keys apart.</summary>
    public static string PrefixOf(string key) => key.Length <= PrefixLength ? key : key[..PrefixLength];

    /// <summary>
    /// Whether a bearer value is a personal access key rather than a sign-in token. Decides only
    /// which check the value is sent to — never whether it is accepted.
    /// </summary>
    public static bool IsKey(string? value) =>
        value is not null && value.StartsWith(Marker, StringComparison.Ordinal);

    /// <summary>
    /// Whether a value that looks like a key is even worth hashing. Malformed and oversized values
    /// are refused exactly as unknown ones are, by the caller, so nothing here says why.
    /// </summary>
    public static bool IsPlausible(string? value) =>
        IsKey(value) && value!.Length <= MaxPlausibleLength;
}

/// <summary>How long a key may live, and the lifetimes the creation form offers.</summary>
public static class AccessKeyLifetime
{
    /// <summary>The lifetime a key gets when its creator does not choose one.</summary>
    public const int DefaultDays = 365;

    /// <summary>The installation ceiling's floor and ceiling — a safety net against a mistyped setting.</summary>
    public const int MinCeilingDays = 1;

    public const int MaxCeilingDays = 3650;

    /// <summary>The lifetimes a person is offered when making a key; there is deliberately no "never".</summary>
    public static readonly IReadOnlyList<int> OfferedDays = [30, 90, 365];

    /// <summary>The installation's configured ceiling, brought inside one day to ten years.</summary>
    public static int Ceiling(int configuredMaxDays) =>
        Math.Clamp(configuredMaxDays, MinCeilingDays, MaxCeilingDays);

    /// <summary>
    /// The lifetimes to offer under the installation's ceiling — the usual three where they fit,
    /// and the ceiling itself when none of them does, so the form always has a choice to offer.
    /// </summary>
    public static IReadOnlyList<int> Offered(int configuredMaxDays)
    {
        var ceiling = Ceiling(configuredMaxDays);
        var fitting = OfferedDays.Where(d => d <= ceiling).ToList();
        return fitting.Count > 0 ? fitting : [ceiling];
    }

    /// <summary>
    /// The lifetime a new key gets: the one asked for, or the default brought under the ceiling
    /// when none was asked for. Null when the one asked for is outside one day to the ceiling.
    /// </summary>
    public static int? Resolve(int? requestedDays, int configuredMaxDays)
    {
        var ceiling = Ceiling(configuredMaxDays);
        if (requestedDays is null)
        {
            return Math.Min(DefaultDays, ceiling);
        }

        return requestedDays.Value >= 1 && requestedDays.Value <= ceiling ? requestedDays.Value : null;
    }
}
