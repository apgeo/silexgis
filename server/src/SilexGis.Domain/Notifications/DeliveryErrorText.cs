// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Notifications;

/// <summary>
/// What a delivery's failure may be shown as, on a page that lists other people's messages.
/// </summary>
/// <remarks>
/// <para>
/// The stored text is whatever somebody else's server said, and a mail server routinely quotes
/// the address it is refusing — <c>550 5.1.1 &lt;ana@example.org&gt; User unknown</c>. Every other
/// path that names a recipient on this page resolves a label instead of an address, and an
/// unedited error string would walk straight past that rule while looking like a diagnostic. So
/// anything address-shaped is replaced before the text leaves the server.
/// </para>
/// <para>
/// Replaced rather than dropped: an operator reading "the server refused [address]" learns that
/// the refusal was about the recipient, which is the diagnostic. Dropping it silently would leave
/// a sentence with a hole in it and no way to tell a redaction from a truncation.
/// </para>
/// </remarks>
public static partial class DeliveryErrorText
{
    /// <summary>What an address is replaced by, so a reader can tell one was there.</summary>
    public const string RedactedAddress = "[address]";

    /// <summary>What a telephone number is replaced by, for the same reason.</summary>
    public const string RedactedNumber = "[number]";

    /// <summary>
    /// How much of a failure is worth a table cell. Long enough for the status line a server
    /// answers with, short enough that one row cannot push the rest of the page off the screen;
    /// the whole text is never needed to tell one kind of failure from another.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Anything address-shaped: a run of characters, an at sign, another run. Deliberately far
    /// broader than a validator would be — this decides what to hide, so matching something that
    /// is not an address costs a redaction, while missing one costs a leak.
    /// </summary>
    /// <remarks>
    /// Both halves are "anything that is not whitespace and not a character that quotes or
    /// separates a value", which is why they are written as exclusions rather than as an alphabet.
    /// An alphabet gets two realistic shapes wrong and both of them silently: a local part with a
    /// diacritic in it, ordinary in a product used in Romanian, and a single-label domain such as
    /// the one a self-hosted relay or an intranet installation answers with. Neither would match a
    /// dotted-ASCII pattern, and the failure of a redaction is invisible — the address simply
    /// appears on the page. Excluding the quoting characters keeps the surrounding punctuation of
    /// a status line intact, so what is left still reads as a diagnostic.
    /// </remarks>
    [GeneratedRegex(@"[^\s<>,;:""()\[\]@]+@[^\s<>,;:""()\[\]@]+", RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    /// <summary>
    /// Anything telephone-shaped: an optional plus, then seven to fifteen digits, which may be
    /// broken up by the spacing and bracketing people and gateways write numbers with.
    /// </summary>
    /// <remarks>
    /// An address is not the only recipient a far side quotes back. A gateway that refuses a
    /// message routinely names the destination it refused — <c>invalid destination
    /// +40721234567</c> — and a number here is a sign-in credential as well as a way to reach
    /// somebody, so it belongs on this page no more than an email address does.
    /// <para>
    /// Seven digits at the least, because that is the shortest thing anybody dials, and shorter
    /// runs are the numbers an error text is actually made of: a status line's code, a message
    /// id's counter, an attempt number. Fifteen at the most, which is the longest number the
    /// international plan allows. The same trade-off as above decides the width: redacting
    /// something that was not a number costs a redaction and a reader who can still tell what
    /// kind of failure it was, while missing one costs the number.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"\+?\d(?:[\s\-.()]?\d){6,14}", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    /// <summary>
    /// The failure as an operator may read it: addresses redacted, then shortened.
    /// </summary>
    /// <remarks>
    /// Redacted before shortened, and not the other way round: cutting the text first can leave
    /// the front half of an address behind, which is still enough to name somebody.
    /// </remarks>
    public static string? ForOperator(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        // Addresses first: an address may have digits in it, and redacting it whole leaves
        // nothing telephone-shaped behind for the second pass to find inside it.
        var redacted = AddressPattern().Replace(error, RedactedAddress);
        redacted = NumberPattern().Replace(redacted, RedactedNumber);
        return redacted.Length <= MaxLength ? redacted : redacted[..MaxLength] + "…";
    }
}
