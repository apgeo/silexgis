// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Features;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The printed-code derivation, checked against values produced by the cave-navigation app's own
/// implementation rather than by this one.
/// </summary>
/// <remarks>
/// A test that only asserted this code agrees with itself would pass on any reimplementation that
/// hashed the right bytes and then spelt the digest out differently, which is the only part that
/// is easy to get wrong. So the expectations below are literals, generated from the app's
/// algorithm, and each of them fails on a specific plausible mistake: a digest read
/// little-endian, a salt appended rather than prepended, a dataset salt placed after the place
/// code, a number padded to a fixed width, or a truncation taken from the wrong end.
/// </remarks>
public class QcriHasherTests
{
    private static byte[] Salt => Convert.FromHexString(QcriHasher.DefaultBaseSaltHex);

    /// <summary>
    /// Ordinary place codes at the default length. Any of these disagreeing means the byte string
    /// being hashed is not the one the app hashes.
    /// </summary>
    [Theory]
    [InlineData("1", "1ri7jd15")]
    [InlineData("12345", "4p4cdbur")]
    [InlineData("hello", "3pxne2e2")]
    [InlineData("88102820480780005", "5ehxc0qr")]
    [InlineData("abc-def", "36gkw2kh")]
    [InlineData("cave-place-1", "64gj42e4")]
    [InlineData("reproducible", "4ar65mh2")]
    [InlineData("ROU001001001", "56t5uinq")]
    [InlineData("", "29qavfmg")]
    public void Derives_the_code_the_device_derives(string placeCode, string expected) =>
        QcriHasher.Hash(placeCode, Salt).ShouldBe(expected);

    /// <summary>
    /// The inputs whose digest spells out in forty-nine base-36 characters instead of fifty.
    /// These are the whole reason the encoding must not pad to a fixed width: a padded
    /// implementation agrees with the app on every other input in this file and disagrees on
    /// exactly these, which is a bug that ships.
    /// </summary>
    [Theory]
    [InlineData("0", "ubp97e0m")]
    [InlineData("5", "k0io5qsu")]
    [InlineData("7", "6j86olyw")]
    [InlineData("14", "a0ykzedc")]
    [InlineData("pci-x", "lfenogt0")]
    public void Derives_the_same_code_when_the_digest_is_one_character_shorter(string placeCode, string expected) =>
        QcriHasher.Hash(placeCode, Salt).ShouldBe(expected);

    /// <summary>
    /// A dataset's own salt changes the answer, and it is hashed as UTF-8 bytes wherever it came
    /// from — the second case is deliberately outside the Latin alphabet and outside the basic
    /// plane, so an implementation encoding it any other way disagrees here.
    /// </summary>
    [Theory]
    [InlineData("club-A", "1hqedo0x")]
    [InlineData("ő\U0001F600", "x2pmknq2")]
    public void A_datasets_own_salt_is_hashed_as_utf8(string userSalt, string expected) =>
        QcriHasher.Hash("12345", Salt, userSalt: userSalt).ShouldBe(expected);

    /// <summary>An absent salt and an empty one contribute nothing, so they agree.</summary>
    [Fact]
    public void An_empty_dataset_salt_is_no_salt()
    {
        QcriHasher.Hash("12345", Salt, userSalt: "").ShouldBe(QcriHasher.Hash("12345", Salt));
        QcriHasher.Hash("12345", Salt, userSalt: null).ShouldBe(QcriHasher.Hash("12345", Salt));
    }

    /// <summary>
    /// Asking for one more character extends the code rather than replacing it. The generator
    /// lengthens a code when the one it derived is already taken, so a derivation that changed
    /// under it would hand out codes unrelated to the ones already printed.
    /// </summary>
    [Fact]
    public void A_longer_code_starts_with_the_shorter_one()
    {
        for (var length = QcriHasher.MinLength; length < QcriHasher.MaxLength; length++)
        {
            QcriHasher.Hash("cave-place-1", Salt, length + 1)
                .ShouldStartWith(QcriHasher.Hash("cave-place-1", Salt, length));
        }
    }

    /// <summary>The bounds, at both ends and at the full length.</summary>
    [Theory]
    [InlineData(4, "64gj")]
    [InlineData(8, "64gj42e4")]
    [InlineData(16, "64gj42e4ko7njpo2")]
    public void Keeps_exactly_the_characters_asked_for(int length, string expected)
    {
        var code = QcriHasher.Hash("cave-place-1", Salt, length);

        code.ShouldBe(expected);
        code.Length.ShouldBe(length);
    }

    /// <summary>A code is spelt only in lowercase base 36, whatever went into it.</summary>
    [Fact]
    public void A_code_is_lowercase_base36()
    {
        foreach (var placeCode in new[] { "ROU001001001", "Mixed Case Place", "ő\U0001F600", "" })
        {
            QcriHasher.Hash(placeCode, Salt, QcriHasher.MaxLength)
                .ShouldAllBe(c => "0123456789abcdefghijklmnopqrstuvwxyz".Contains(c));
        }
    }

    /// <summary>Outside the bounds is refused rather than clamped — a clamped length would derive a code nothing agrees with.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(17)]
    public void Refuses_a_length_the_generator_would_never_produce(int length) =>
        Should.Throw<ArgumentOutOfRangeException>(() => QcriHasher.Hash("12345", Salt, length));

    /// <summary>
    /// A different salt derives different codes. Stated so the compatibility warning on the
    /// setting has a test behind it rather than only a comment: this is what rotating it does.
    /// </summary>
    [Fact]
    public void A_different_base_salt_derives_a_different_code() =>
        QcriHasher.Hash("12345", Convert.FromHexString("00000000000000000000000000000000"))
            .ShouldNotBe(QcriHasher.Hash("12345", Salt));
}
