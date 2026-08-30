// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;

namespace SilexGis.Domain.Messaging;

/// <summary>
/// How many segments a carrier splits one text message into — which is what it bills for, and so
/// what a day of texts actually costs.
/// </summary>
/// <remarks>
/// <para>
/// A pure function over a string, so the rule can be read and tested without a gateway, a
/// database or a rendered template: what goes in is the exact text handed to the transport, and
/// what comes out is the number of pieces it travels in. Nothing here decides <em>whether</em>
/// anything is sent, or what a piece costs in money — only how many pieces there are.
/// </para>
/// <para>
/// A text message travels on one of two alphabets, and which one is not a matter of degree. The
/// seven-bit default alphabet of GSM 03.38 fits 160 characters in a segment, but it holds only
/// the Latin letters of western Europe and a short list of symbols; the moment one character in
/// the message is outside it, the whole message is re-encoded wide, where a segment holds 70.
/// So the cost flips on a single character rather than climbing with length: one Romanian
/// <c>ț</c> in an otherwise plain sentence more than halves what fits, and a wording that is
/// comfortably one segment in English is two in Romanian at the same length. Shortening the
/// wording changes nothing about it until the message crosses 70.
/// </para>
/// <para>
/// A message too long for one segment is sent as several, and every one of them spends room on a
/// header saying which part of what it is — so the room drops to 153 and 67 respectively, and it
/// drops for the first segment too. A message of 161 seven-bit characters therefore costs two
/// segments of 153 rather than one full segment and a short remainder.
/// </para>
/// <para>
/// Ten characters are not in the seven-bit alphabet at all but are still carried on it, written
/// as an escape followed by a substitute, so they spend two of a segment's characters rather than
/// one. Such a pair is never broken across the join between two segments, and neither is the pair
/// of code units that spells one character beyond the basic plane on the wide alphabet: a segment
/// carries one character less instead, and the pair starts the next segment whole.
/// </para>
/// </remarks>
public static class TextMessageSegments
{
    /// <summary>What a lone seven-bit segment holds.</summary>
    public const int SevenBitSegment = 160;

    /// <summary>What each seven-bit segment holds once a message runs to more than one.</summary>
    public const int SevenBitConcatenatedSegment = 153;

    /// <summary>What a lone wide segment holds.</summary>
    public const int WideSegment = 70;

    /// <summary>What each wide segment holds once a message runs to more than one.</summary>
    public const int WideConcatenatedSegment = 67;

    /// <summary>
    /// The least a message may be counted as costing while its text does not exist yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A guard on spending has to answer for work already committed but not yet written out — a
    /// row a fan-out has created, a message queued and not yet routed — and at that moment there
    /// is nothing to count. This is the floor such a message is charged, and it is deliberately
    /// not one: assuming one would be assuming the narrow alphabet, which is the very under-count
    /// this rule exists to correct. Two is what the shortest wording that can leave this
    /// installation as a text costs once it is read in a language whose marks fall outside that
    /// alphabet.
    /// </para>
    /// <para>
    /// <b>It is a floor and not a worst case.</b> A wording carrying a name somebody chose — a
    /// club's, an installation's — is as long as that name makes it, and can cost three pieces or
    /// four; nothing here bounds it. A message whose values are known before it is queued is
    /// therefore weighed rather than assumed, and this stands only for one that was not, until
    /// the hand-over replaces it with what actually left. Raising it would over-charge every short
    /// message to cover a long one, which is why the answer is to weigh the long one.
    /// </para>
    /// </remarks>
    public const int Unrendered = 2;

    /// <summary>
    /// The seven-bit default alphabet, in its own order, less the escape that introduces the
    /// characters below — the escape is not a character anybody writes. Line feed and carriage
    /// return are in it; the tab is not, and neither is any Romanian diacritic.
    /// </summary>
    private const string SevenBitAlphabet =
        "@£$¥èéùìòÇ\nØø\rÅå" +
        "Δ_ΦΓΛΩΠΨΣΘΞÆæßÉ" +
        " !\"#¤%&'()*+,-./0123456789:;<=>?" +
        "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§" +
        "¿abcdefghijklmnopqrstuvwxyzäöñüà";

    /// <summary>
    /// The characters the seven-bit alphabet reaches only through an escape, and which therefore
    /// spend two of a segment's characters each.
    /// </summary>
    private const string SevenBitEscaped = "\f^{}\\[~]|€";

    /// <summary>Everything the seven-bit alphabet can carry, one way or the other.</summary>
    private static readonly SearchValues<char> SevenBit =
        SearchValues.Create(SevenBitAlphabet + SevenBitEscaped);

    private static readonly SearchValues<char> Escaped = SearchValues.Create(SevenBitEscaped);

    /// <summary>
    /// How many segments carry <paramref name="text"/>, which is how many the carrier bills.
    /// </summary>
    /// <remarks>
    /// Never less than one. A message with nothing in it is still a message the gateway takes and
    /// charges for, and answering nothing here would let a wording that rendered empty — or a
    /// fault upstream that passed no text at all — be spent for free by a guard whose whole
    /// purpose is to know what has been spent.
    /// </remarks>
    /// <param name="text">The exact text handed to the transport, after rendering.</param>
    public static int Count(string? text)
    {
        var body = text.AsSpan();
        var wide = body.ContainsAnyExcept(SevenBit);

        var alone = wide ? WideSegment : SevenBitSegment;
        if (Units(body, wide) <= alone)
        {
            return 1;
        }

        return Parts(body, wide, wide ? WideConcatenatedSegment : SevenBitConcatenatedSegment);
    }

    /// <summary>What the whole message spends, before anything is decided about splitting it.</summary>
    private static int Units(ReadOnlySpan<char> body, bool wide)
    {
        var total = 0;

        for (var at = 0; at < body.Length;)
        {
            var (units, chars) = PieceAt(body, at, wide);
            total += units;
            at += chars;
        }

        return total;
    }

    /// <summary>How many segments of <paramref name="room"/> the message is packed into.</summary>
    private static int Parts(ReadOnlySpan<char> body, bool wide, int room)
    {
        var segments = 1;
        var left = room;

        for (var at = 0; at < body.Length;)
        {
            var (units, chars) = PieceAt(body, at, wide);

            // A character that spends two units is never broken across the join. The segment
            // carries one unit less than its room and the character starts the next one whole,
            // which is why a message can need one more segment than dividing its length suggests.
            if (units > left)
            {
                segments++;
                left = room;
            }

            left -= units;
            at += chars;
        }

        return segments;
    }

    /// <summary>
    /// The next thing in the message that may not be broken in half: what it spends, and how many
    /// characters of the string it occupies. The two differ, because an escaped character is one
    /// character spending two units while a character beyond the basic plane is two characters
    /// spending two.
    /// </summary>
    private static (int Units, int Chars) PieceAt(ReadOnlySpan<char> body, int at, bool wide)
    {
        if (!wide)
        {
            return (Escaped.Contains(body[at]) ? 2 : 1, 1);
        }

        return char.IsHighSurrogate(body[at])
            && at + 1 < body.Length
            && char.IsLowSurrogate(body[at + 1])
            ? (2, 2)
            : (1, 1);
    }
}
