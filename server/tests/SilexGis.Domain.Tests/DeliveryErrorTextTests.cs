// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a failed delivery may be shown as. The operator page that reads these lists other
/// people's messages, and the text is whatever somebody else's mail server said — which is
/// routinely the address it refused.
/// </summary>
public class DeliveryErrorTextTests
{
    [Fact]
    public void A_rejection_quoting_the_address_does_not_carry_it_out()
    {
        var shown = DeliveryErrorText.ForOperator("550 5.1.1 <ana@example.org>: Recipient address rejected");

        shown.ShouldNotBeNull();
        shown.ShouldNotContain("@");
        shown.ShouldNotContain("ana");
        shown.ShouldContain(DeliveryErrorText.RedactedAddress);
        // The diagnostic survives the redaction — an operator still learns it was the recipient
        // the far end objected to, and with what code.
        shown.ShouldContain("550");
        shown.ShouldContain("Recipient address rejected");
    }

    [Fact]
    public void Several_addresses_in_one_message_all_go()
    {
        var shown = DeliveryErrorText.ForOperator("relay denied from bot@spam.test to ana@example.org");

        shown.ShouldNotBeNull();
        shown.ShouldNotContain("@");
        shown.Split(DeliveryErrorText.RedactedAddress).Length.ShouldBe(3);
    }

    [Fact]
    public void An_address_with_a_diacritic_in_it_goes_like_any_other()
    {
        // A Romanian-facing product has these, and a pattern written as an ASCII alphabet does not
        // merely trim such an address — it fails to see one at all, and prints it whole.
        var shown = DeliveryErrorText.ForOperator("550 5.1.1 <ană@example.ro> User unknown");

        shown.ShouldNotBeNull();
        shown.ShouldNotContain("@");
        shown.ShouldNotContain("ană");
        shown.ShouldContain(DeliveryErrorText.RedactedAddress);
        shown.ShouldContain("User unknown");
    }

    [Fact]
    public void An_address_at_a_host_with_no_dot_in_it_goes_too()
    {
        // What a self-hosted relay or an intranet installation answers with. A pattern that
        // insists on a dotted domain lets the whole thing through.
        var shown = DeliveryErrorText.ForOperator("relay refused ana@mailhost");

        shown.ShouldNotBeNull();
        shown.ShouldNotContain("@");
        shown.ShouldNotContain("ana");
        shown.ShouldBe($"relay refused {DeliveryErrorText.RedactedAddress}");
    }

    [Fact]
    public void An_address_is_removed_before_the_text_is_shortened()
    {
        // Shortening first would leave the front half of the address behind, which still names
        // somebody. The address is placed past the cut on purpose.
        var padding = new string('x', DeliveryErrorText.MaxLength);
        var shown = DeliveryErrorText.ForOperator($"{padding} ana@example.org");

        shown.ShouldNotBeNull();
        shown.ShouldNotContain("ana");
        shown.Length.ShouldBeLessThanOrEqualTo(DeliveryErrorText.MaxLength + 1);
    }

    [Fact]
    public void A_long_failure_is_shortened_and_says_so()
    {
        var shown = DeliveryErrorText.ForOperator(new string('e', DeliveryErrorText.MaxLength + 50));

        shown!.Length.ShouldBe(DeliveryErrorText.MaxLength + 1);
        shown.ShouldEndWith("…");
    }

    [Fact]
    public void A_short_failure_is_left_exactly_as_it_was()
    {
        DeliveryErrorText.ForOperator("Connection refused").ShouldBe("Connection refused");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_say_stays_nothing(string? error)
    {
        // Null rather than an empty string, so the answer distinguishes "no failure recorded"
        // from "a failure whose text was blank".
        DeliveryErrorText.ForOperator(error).ShouldBeNull();
    }
}
