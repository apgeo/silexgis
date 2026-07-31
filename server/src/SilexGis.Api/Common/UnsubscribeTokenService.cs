// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Notifications;

namespace SilexGis.Api.Common;

/// <summary>
/// Signed, expiring, stateless opt-out tokens, protected by the host's data-protection key ring —
/// the same mechanism the file delivery tokens use, and for the same reason: nothing is stored,
/// and a token cannot be forged or edited.
/// </summary>
/// <remarks>
/// The lifetime is long because mail sits in inboxes for months, and an expired opt-out link is a
/// worse failure than an old one — the recipient's only alternative is to keep receiving mail.
/// </remarks>
public sealed class UnsubscribeTokenService : IUnsubscribeTokens
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

    private readonly ITimeLimitedDataProtector protector;

    public UnsubscribeTokenService(IDataProtectionProvider provider) =>
        protector = provider.CreateProtector("SilexGis.Unsubscribe").ToTimeLimitedDataProtector();

    public string Create(Guid userId, NotificationCategory category) =>
        protector.Protect($"{userId:N}:{(short)category}", DateTimeOffset.UtcNow.Add(Lifetime));

    public bool TryRead(string token, out Guid userId, out NotificationCategory category)
    {
        userId = Guid.Empty;
        category = default;

        try
        {
            var parts = protector.Unprotect(token).Split(':');
            if (parts.Length != 2
                || !Guid.TryParseExact(parts[0], "N", out userId)
                || !short.TryParse(parts[1], CultureInfo.InvariantCulture, out var raw)
                || !Enum.IsDefined(typeof(NotificationCategory), raw))
            {
                return false;
            }

            category = (NotificationCategory)raw;
            return true;
        }
        catch (CryptographicException)
        {
            // Expired, tampered with, or minted by a different key ring.
            return false;
        }
    }
}
