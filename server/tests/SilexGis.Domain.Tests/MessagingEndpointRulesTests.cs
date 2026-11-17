// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Shouldly;
using SilexGis.Domain.Map;
using SilexGis.Domain.Settings;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which addresses the SMS gateway may not be, when a stored secret is dropped, and whose home a
/// saved view is.
/// </summary>
public sealed class MessagingEndpointRulesTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.8.9.10")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00::5")]
    [InlineData("fc12::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.1.2.3")]
    public void Internal_addresses_are_internal(string address) =>
        MessagingEndpointRules.IsInternal(IPAddress.Parse(address)).ShouldBeTrue();

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("1.1.1.1")]
    [InlineData("100.128.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:93.184.215.14")]
    public void Public_addresses_are_not(string address) =>
        MessagingEndpointRules.IsInternal(IPAddress.Parse(address)).ShouldBeFalse();

    [Fact]
    public void A_mail_server_moves_with_its_host_or_its_port_but_not_with_the_hosts_case()
    {
        var current = new MailSettings { Host = "smtp.example.org", Port = 587 };

        MessagingEndpointRules.MailServerMoved(current, "SMTP.example.org", 587).ShouldBeFalse();
        MessagingEndpointRules.MailServerMoved(current, "smtp.elsewhere.example.org", 587).ShouldBeTrue();
        MessagingEndpointRules.MailServerMoved(current, "smtp.example.org", 465).ShouldBeTrue();
    }

    [Fact]
    public void A_gateway_moves_with_any_change_of_its_address()
    {
        var current = new SmsSettings { Url = "https://gateway.example.org/send" };

        MessagingEndpointRules.SmsGatewayMoved(current, "https://gateway.example.org/send").ShouldBeFalse();
        MessagingEndpointRules.SmsGatewayMoved(current, "https://gateway.example.org/other").ShouldBeTrue();
        MessagingEndpointRules.SmsGatewayMoved(current, "https://elsewhere.example.org/send").ShouldBeTrue();
    }

    [Fact]
    public void A_view_is_home_only_to_its_owner()
    {
        var owner = Guid.NewGuid();
        var reader = Guid.NewGuid();

        SavedViewRules.IsHomeOf(isHome: true, owner, owner).ShouldBeTrue();
        SavedViewRules.IsHomeOf(isHome: true, owner, reader).ShouldBeFalse();
        SavedViewRules.IsHomeOf(isHome: false, owner, owner).ShouldBeFalse();
    }
}
