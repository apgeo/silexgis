// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SilexGis.Domain.Features;

/// <summary>
/// The printed-code derivation the cave-navigation app uses, reproduced exactly.
/// </summary>
/// <remarks>
/// <para>
/// A place inside a cave carries a place code somebody assigned it, and the code that goes on the
/// printed label is derived from that place code by hashing it together with a fixed salt and
/// keeping the first few characters of the result in base 36. This is a byte-for-byte
/// reimplementation of that derivation, so that this server and the device that prints labels
/// agree about what a given place code becomes.
/// </para>
/// <para>
/// <b>The salt is a compatibility constant and not a credential.</b> It carries no secret and
/// protects nothing: the whole reason it is fixed is that two datasets which were never in
/// contact, holding the same place code, must derive the same printed code from it — which is
/// what lets a label printed by one of them be recognised by the other. Rotating it does not
/// harden anything, because there is nothing here to harden; it silently stops every code already
/// printed on a wall from matching anything ever derived again. Change it only as a deliberate,
/// coordinated recomputation of every code in every dataset that has to keep agreeing.
/// </para>
/// <para>
/// Nothing resolves a printed code by recomputing it. A stored code may legally disagree with
/// what today's settings would produce — the generating dataset chooses the length, may add a
/// salt of its own, and may not hash at all — so this derivation exists to produce and check
/// codes, never to look one up.
/// </para>
/// </remarks>
public static class QcriHasher
{
    /// <summary>Shortest derived code the generator will produce.</summary>
    public const int MinLength = 4;

    /// <summary>
    /// Longest derived code the generator will produce. The generator lengthens a code by one
    /// when the one it derived is already taken by a different place, and stops here.
    /// </summary>
    public const int MaxLength = 16;

    /// <summary>The length used when nothing asks for another.</summary>
    public const int DefaultLength = 8;

    /// <summary>
    /// The sixteen salt bytes the app compiles in, written as hexadecimal. This value is the
    /// interoperability contract itself — see the type's remarks before changing it.
    /// </summary>
    public const string DefaultBaseSaltHex = "9c42a16f3bd755180eb67ac3e921845d";

    /// <summary>Lowercase base 36: the entire alphabet a derived code can be spelt in.</summary>
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// The derived code for <paramref name="placeCode"/>.
    /// </summary>
    /// <param name="placeCode">The place code the label belongs to; hashed as its UTF-8 bytes.</param>
    /// <param name="baseSalt">
    /// The fixed salt, normally <see cref="DefaultBaseSaltHex"/> decoded. Placed before everything
    /// else in the hashed byte string.
    /// </param>
    /// <param name="length">How many characters to keep, between <see cref="MinLength"/> and <see cref="MaxLength"/>.</param>
    /// <param name="userSalt">
    /// A dataset's own extra salt, if it set one. Sits between the fixed salt and the place code,
    /// and an empty or absent one contributes no bytes at all — so "no salt" and "the empty salt"
    /// are the same thing, which is what the app does.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The requested length is outside the bounds.</exception>
    public static string Hash(
        string placeCode, ReadOnlySpan<byte> baseSalt, int length = DefaultLength, string? userSalt = null)
    {
        ArgumentNullException.ThrowIfNull(placeCode);
        if (length < MinLength || length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, $"A derived code's length is between {MinLength} and {MaxLength}.");
        }

        var userSaltBytes = string.IsNullOrEmpty(userSalt) ? [] : Encoding.UTF8.GetBytes(userSalt);
        var placeCodeBytes = Encoding.UTF8.GetBytes(placeCode);

        var buffer = new byte[baseSalt.Length + userSaltBytes.Length + placeCodeBytes.Length];
        baseSalt.CopyTo(buffer);
        userSaltBytes.CopyTo(buffer, baseSalt.Length);
        placeCodeBytes.CopyTo(buffer, baseSalt.Length + userSaltBytes.Length);

        var encoded = Base36(SHA256.HashData(buffer));

        // Truncation from the front, so asking for one more character extends the same code
        // rather than producing a different one — which is what makes the generator's
        // lengthen-on-collision step safe.
        return encoded.Length >= length ? encoded[..length] : encoded.PadLeft(length, '0');
    }

    /// <summary>
    /// The whole digest read as one unsigned big-endian number, spelt in lowercase base 36 with
    /// no leading zeroes.
    /// </summary>
    /// <remarks>
    /// The absence of padding is load-bearing rather than incidental: a digest whose leading
    /// bytes are small spells out in 49 characters where most spell out in 50, and padding those
    /// to a fixed width would shift every character and change the derived code for exactly those
    /// inputs — while agreeing with the app on all the others, which is the shape of bug that
    /// survives a test suite.
    /// </remarks>
    private static string Base36(ReadOnlySpan<byte> digest)
    {
        var value = new BigInteger(digest, isUnsigned: true, isBigEndian: true);
        if (value.IsZero)
        {
            return "0";
        }

        var digits = new Stack<char>();
        while (value > BigInteger.Zero)
        {
            value = BigInteger.DivRem(value, 36, out var remainder);
            digits.Push(Alphabet[(int)remainder]);
        }

        return new string([.. digits]);
    }
}
