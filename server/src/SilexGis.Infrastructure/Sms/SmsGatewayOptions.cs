// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Sms;

/// <summary>
/// Installation-level limits on the SMS gateway address, which the settings page itself cannot
/// change: the page is where the address is typed, so the page cannot be what lifts the limit.
/// </summary>
public sealed class SmsGatewayOptions
{
    public const string SectionName = "SmsGateway";

    /// <summary>
    /// Allows a gateway on this machine or on a private, shared or link-local network (a gateway
    /// run beside the installation). Off by default: then such an address is refused when the
    /// settings are saved and again when a message is sent.
    /// </summary>
    public bool AllowInternalAddresses { get; set; }
}
