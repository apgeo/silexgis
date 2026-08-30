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
/// <para>
/// The lifetime is long because mail sits in inboxes for months, and an expired opt-out link is a
/// worse failure than an old one — the recipient's only alternative is to keep receiving mail.
/// </para>
/// <para>
/// The payload names what the link switches off as well as whose account it belongs to, so a
/// summary's link cannot be read as a link for whichever category happened to be first in it.
/// </para>
/// </remarks>
public sealed class UnsubscribeTokenService : IUnsubscribeTokens
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

    private readonly ITimeLimitedDataProtector protector;

    public UnsubscribeTokenService(IDataProtectionProvider provider) =>
        protector = provider.CreateProtector("SilexGis.Unsubscribe").ToTimeLimitedDataProtector();

    public string CreateForCategory(Guid userId, NotificationCategory category) =>
        Protect($"{userId:N}:{(short)UnsubscribeKind.Category}:{(short)category}");

    // The third field is written empty rather than omitted, so every token has the same shape and
    // one parser reads both kinds.
    public string CreateForDigest(Guid userId) =>
        Protect($"{userId:N}:{(short)UnsubscribeKind.DailyDigest}:");

    public bool TryRead(string token, out UnsubscribeSubject subject)
    {
        subject = default;

        try
        {
            var parts = protector.Unprotect(token).Split(':');
            if (parts.Length != 3
                || !Guid.TryParseExact(parts[0], "N", out var userId)
                || !short.TryParse(parts[1], CultureInfo.InvariantCulture, out var rawKind)
                || !Enum.IsDefined(typeof(UnsubscribeKind), rawKind))
            {
                return false;
            }

            var kind = (UnsubscribeKind)rawKind;
            if (kind != UnsubscribeKind.Category)
            {
                subject = new UnsubscribeSubject(userId, kind, default);
                return true;
            }

            if (!short.TryParse(parts[2], CultureInfo.InvariantCulture, out var rawCategory)
                || !Enum.IsDefined(typeof(NotificationCategory), rawCategory))
            {
                return false;
            }

            subject = new UnsubscribeSubject(userId, kind, (NotificationCategory)rawCategory);
            return true;
        }
        catch (CryptographicException)
        {
            // Expired, tampered with, or minted by a different key ring.
            return false;
        }
    }

    private string Protect(string payload) =>
        protector.Protect(payload, DateTimeOffset.UtcNow.Add(Lifetime));
}
