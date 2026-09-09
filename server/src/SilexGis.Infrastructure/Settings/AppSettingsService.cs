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

    /// <summary>
    /// The brakes on the neighbouring photo libraries.
    /// </summary>
    /// <remarks>
    /// The configuration section it falls back to is deliberately not the one holding each
    /// library's address and credential: that one contains an object per product, and binding a
    /// pair of switches onto it would either fail or silently read nothing. Keeping them apart also
    /// keeps the two kinds of value apart — what the deployment <em>is</em>, and what an operator
    /// has decided about it while it is running.
    /// </remarks>
    public ValueTask<PhotoLibrarySuspensionSettings> GetPhotoLibrarySuspensionAsync(CancellationToken ct = default) =>
        GetAsync(
            AppSettingSections.PhotoLibrarySuspension,
            "PhotoLibrarySuspension",
            // The one section whose unreadable row does not fall back to configuration. Every other
            // section here describes how the installation would like to work, and the configured
            // values are a working answer for it; this one is a brake somebody pulled during an
            // incident, and resolving "the decision cannot be read" as "carry on" would resume
            // talking to a library an operator stopped and leave only a log line behind. So an
            // unreadable or drifted row means everything stopped, and saving the section again is
            // what recovers it.
            () => PhotoLibrarySuspensionSettings.EverythingStopped,
            ct);

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

    /// <param name="unreadable">
    /// What a stored row that cannot be deserialised resolves to, when that is not the configured
    /// default. Null for every section whose configuration is a safe answer; supplied by the one
    /// section where "the stored decision is unreadable" has a safer reading of its own.
    /// </param>
    private async ValueTask<T> GetAsync<T>(
        string section, string configurationSection, Func<T>? unreadable, CancellationToken ct)
        where T : class, new()
    {
        if (cache.TryGetValue(CacheKey(section), out T? cached) && cached is not null)
        {
            return cached;
        }

        var resolved = await ResolveAsync(section, configurationSection, unreadable, ct);
        cache.Set(CacheKey(section), resolved, CacheLifetime);
        return resolved;
    }

    private ValueTask<T> GetAsync<T>(string section, string configurationSection, CancellationToken ct)
        where T : class, new() =>
        GetAsync<T>(section, configurationSection, unreadable: null, ct);

    private async Task<T> ResolveAsync<T>(
        string section, string configurationSection, Func<T>? unreadable, CancellationToken ct)
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
            return JsonSerializer.Deserialize<T>(stored, SerializerOptions) ?? Unreadable();
        }
        catch (JsonException ex)
        {
            // A row that cannot be read must not take the installation down with it. Which answer
            // is the safe one is the section's own business and not this method's: for most of them
            // the configured values are a working answer, and for a section that stores a decision
            // to stop doing something the safe answer is to go on not doing it.
            logger.LogError(
                ex,
                "Stored settings for section {Section} could not be read; using the section's fallback",
                section);
            return Unreadable();
        }

        T Unreadable() => unreadable is null ? fromConfiguration : unreadable();
    }

    private static string CacheKey(string section) => $"app-settings:{section}";
}
