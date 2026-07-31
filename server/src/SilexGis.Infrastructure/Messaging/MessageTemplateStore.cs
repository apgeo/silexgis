// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Messaging;

/// <summary>
/// Resolves the wording to use for a template, and records an operator's rewrites.
/// </summary>
/// <remarks>
/// Resolution is deliberately short: the operator's row for the requested language if there is
/// one, otherwise the wording shipped with the product for that language (which itself falls back
/// to English). An operator who rewrites only the English text does not thereby replace the
/// Romanian translation — the branding they were most likely changing travels through the
/// <c>{appName}</c> placeholder either way.
/// </remarks>
public sealed class MessageTemplateStore(SilexGisDbContext db, IMemoryCache cache)
{
    private const string CacheKey = "message-templates";

    /// <summary>Short enough that an edit shows up promptly on another node, long enough to matter.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    public async Task<MessageTemplateText> ResolveAsync(
        MessageTemplateDefinition definition, string? locale, CancellationToken ct = default)
    {
        var normalised = MessageTemplateCatalog.Normalise(locale);
        var overrides = await OverridesAsync(ct);
        return overrides.TryGetValue((definition.Key, normalised), out var custom)
            ? custom
            : MessageTemplateCatalog.Default(definition, normalised);
    }

    /// <summary>Every stored rewrite, for the admin editor to show alongside the defaults.</summary>
    public async Task<IReadOnlyList<MessageTemplate>> AllOverridesAsync(CancellationToken ct = default) =>
        await db.MessageTemplates.AsNoTracking().ToListAsync(ct);

    public async Task SaveAsync(
        MessageTemplateDefinition definition, string locale, string? subject, string body, CancellationToken ct = default)
    {
        var normalised = MessageTemplateCatalog.Normalise(locale);
        var row = await db.MessageTemplates
            .FirstOrDefaultAsync(t => t.Key == definition.Key && t.Locale == normalised, ct);
        if (row is null)
        {
            db.MessageTemplates.Add(new MessageTemplate
            {
                Key = definition.Key,
                Locale = normalised,
                Channel = definition.Channel,
                Subject = subject,
                Body = body,
            });
        }
        else
        {
            row.Subject = subject;
            row.Body = body;
            row.Channel = definition.Channel;
        }

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
    }

    /// <summary>Drops a rewrite so the shipped wording applies again.</summary>
    public async Task<bool> ResetAsync(string key, string locale, CancellationToken ct = default)
    {
        var normalised = MessageTemplateCatalog.Normalise(locale);
        var row = await db.MessageTemplates.FirstOrDefaultAsync(t => t.Key == key && t.Locale == normalised, ct);
        if (row is null)
        {
            return false;
        }

        db.MessageTemplates.Remove(row);
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
        return true;
    }

    private async Task<Dictionary<(string Key, string Locale), MessageTemplateText>> OverridesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out Dictionary<(string, string), MessageTemplateText>? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await db.MessageTemplates.AsNoTracking().ToListAsync(ct);
        var map = rows.ToDictionary(
            row => (row.Key, row.Locale),
            row => new MessageTemplateText(row.Subject, row.Body));
        cache.Set(CacheKey, map, CacheLifetime);
        return map;
    }
}
