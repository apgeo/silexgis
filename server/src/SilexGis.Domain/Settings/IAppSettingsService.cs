// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Settings;

/// <summary>
/// Reads and writes the administrator-editable sections.
/// </summary>
/// <remarks>
/// Every section resolves the same way: the deployment's configuration supplies the values, and a
/// saved row replaces them wholesale. Configuration is therefore the installation default rather
/// than a competing source of truth — an operator who prefers environment variables never saves a
/// section, and one who prefers the admin page stops thinking about environment variables.
/// </remarks>
public interface IAppSettingsService
{
    ValueTask<MailSettings> GetMailAsync(CancellationToken ct = default);

    ValueTask<SmsSettings> GetSmsAsync(CancellationToken ct = default);

    ValueTask<SecuritySettings> GetSecurityAsync(CancellationToken ct = default);

    /// <summary>Replaces a section and drops the cached copy across the process.</summary>
    Task SaveAsync<T>(string section, T value, CancellationToken ct = default)
        where T : class;
}
