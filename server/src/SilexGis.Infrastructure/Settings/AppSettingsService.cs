// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Settings;

/// <summary>
/// Resolves a settings section as "the deployment's configuration, replaced wholesale by the saved
/// row if there is one", and caches the answer.
/// </summary>
/// <remarks>
/// <para>
/// Sections are read on nearly every sign-in, so they are cached in memory. The cache is process
/// local and expires on a short window as well as on write; a two-node installation therefore
/// picks up an administrator's change within that window rather than instantly. That is the right
/// trade for settings which change a few times in an installation's life, and it avoids a
/// distributed cache the project does not otherwise need.
/// </para>
/// <para>
/// Secrets are the one place where "replaced wholesale" is softened: saving a section with its
/// secret left blank keeps the stored one, because the admin API never sends a secret to the
/// browser and so can never send it back.
/// </para>
/// </remarks>
public sealed class AppSettingsService(
    SilexGisDbContext db,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<AppSettingsService> logger) : IAppSettingsService
{
    /// <summary>Bounds how long a second node keeps serving settings an administrator has changed.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<MailSettings> GetMailAsync(CancellationToken ct = default) =>
        GetAsync<MailSettings>(AppSettingSections.Mail, "Mail", ct);

    public ValueTask<SmsSettings> GetSmsAsync(CancellationToken ct = default) =>
        GetAsync<SmsSettings>(AppSettingSections.Sms, "Sms", ct);

    public ValueTask<SecuritySettings> GetSecurityAsync(CancellationToken ct = default) =>
        GetAsync<SecuritySettings>(AppSettingSections.Security, "Security", ct);

    public ValueTask<ProtectionSettings> GetProtectionAsync(CancellationToken ct = default) =>
        GetAsync<ProtectionSettings>(AppSettingSections.Protection, "Protection", ct);

    public ValueTask<ImportSettings> GetImportAsync(CancellationToken ct = default) =>
        GetAsync<ImportSettings>(AppSettingSections.Import, "Import", ct);

    public ValueTask<InterfaceSettings> GetInterfaceAsync(CancellationToken ct = default) =>
        GetAsync<InterfaceSettings>(AppSettingSections.Interface, "Interface", ct);

    public ValueTask<NotificationSettings> GetNotificationsAsync(CancellationToken ct = default) =>
        GetAsync<NotificationSettings>(AppSettingSections.Notifications, "Notifications", ct);

    public ValueTask<AnnouncementSettings> GetAnnouncementsAsync(CancellationToken ct = default) =>
        GetAsync<AnnouncementSettings>(AppSettingSections.Announcements, "Announcements", ct);

    public async Task SaveAsync<T>(string section, T value, CancellationToken ct = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == section, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = section, Value = json });
        }
        else
        {
            row.Value = json;
        }

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey(section));
    }

    private async ValueTask<T> GetAsync<T>(string section, string configurationSection, CancellationToken ct)
        where T : class, new()
    {
        if (cache.TryGetValue(CacheKey(section), out T? cached) && cached is not null)
        {
            return cached;
        }

        var resolved = await ResolveAsync<T>(section, configurationSection, ct);
        cache.Set(CacheKey(section), resolved, CacheLifetime);
        return resolved;
    }

    private async Task<T> ResolveAsync<T>(string section, string configurationSection, CancellationToken ct)
        where T : class, new()
    {
        // Configuration first: it is the installation's stated default and the only source before
        // anyone opens the admin page.
        var fromConfiguration = configuration.GetSection(configurationSection).Get<T>() ?? new T();

        var stored = await db.AppSettings
            .AsNoTracking()
            .Where(s => s.Key == section)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (stored is null)
        {
            return fromConfiguration;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(stored, SerializerOptions) ?? fromConfiguration;
        }
        catch (JsonException ex)
        {
            // A row that cannot be read must not take the installation down with it; the
            // configured values are a working answer and the log says which section to fix.
            logger.LogError(ex, "Stored settings for section {Section} could not be read; using configuration", section);
            return fromConfiguration;
        }
    }

    private static string CacheKey(string section) => $"app-settings:{section}";
}
