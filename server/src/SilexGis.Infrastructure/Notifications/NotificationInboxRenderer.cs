// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// One page of notifications turned into the text a reader sees, and the language it was written
/// in.
/// </summary>
/// <param name="Language">
/// What the page was rendered in, so a caller can say so on the wire rather than leaving a client
/// to guess whether it got what it asked for.
/// </param>
/// <param name="Titles">
/// One line per notification, keyed by its id. A notification withheld from this reader has no
/// entry at all: its wording is exactly the thing being withheld.
/// </param>
public sealed record NotificationRendering(
    string Language, IReadOnlyDictionary<long, string> Titles);

/// <summary>
/// Writes out what a notification says, for the person reading their own inbox.
/// </summary>
/// <remarks>
/// <para>
/// Rendered here rather than shipped to a client as a template key and a bag of values, for three
/// reasons that all point the same way: the wording has one home, an operator who rewrites a
/// message sees the rewrite in the inbox and not only in the mail, and the catalogue is already
/// the only thing that knows both languages. A client that would rather write its own is not shut
/// out — the row carries its category and its template key as well.
/// </para>
/// <para>
/// <b>A line is the message's own subject.</b> The body is written for an email: it greets the
/// reader, gives the installation's address and carries an opt-out line, none of which belong on a
/// row in a list inside the application the reader is already signed into. The subject is that
/// message's own one-line summary of what happened — already translated, already editable by the
/// operator — which is why the daily summary uses it the same way.
/// </para>
/// <para>
/// In Infrastructure because it reads the reader's account row for the language to fall back to,
/// and reading user rows is what this layer does on a feature slice's behalf.
/// </para>
/// </remarks>
public sealed class NotificationInboxRenderer(
    SilexGisDbContext db,
    IMessageDispatcher dispatcher,
    ILogger<NotificationInboxRenderer> logger)
{
    /// <summary>
    /// Renders every row except those withheld from this reader.
    /// </summary>
    /// <param name="acceptLanguage">
    /// What the reader's browser asked for. It wins over the language stored on the account: a
    /// person reading the site in one language should not be handed a list written in another
    /// because their profile remembers an older choice.
    /// </param>
    /// <param name="withheld">
    /// Notifications whose target this reader may no longer open. They are still listed — hiding
    /// that something happened is its own kind of leak — but nothing is rendered for them, because
    /// the name a producer froze into the row is exactly what the reader has lost the right to see.
    /// </param>
    public async Task<NotificationRendering> RenderAsync(
        Guid readerId,
        string? acceptLanguage,
        IReadOnlyList<Notification> rows,
        IReadOnlySet<long> withheld,
        CancellationToken ct)
    {
        var reader = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == readerId, ct);
        var language = AcceptLanguage.Preferred(acceptLanguage)
            ?? MessageTemplateCatalog.Normalise(reader?.Locale);

        var titles = new Dictionary<long, string>();
        foreach (var row in rows)
        {
            if (withheld.Contains(row.Id) || MessageTemplateCatalog.Find(row.TemplateKey) is null)
            {
                // An unknown key cannot be rendered at all. It is already recorded as a dead
                // delivery for the operator to see; the reader gets the row's category and date,
                // which is the same thing a withheld row gets and needs no second shape.
                continue;
            }

            try
            {
                titles[row.Id] = await dispatcher.RenderSubjectAsync(
                    row.TemplateKey, language, ValuesFor(row, reader), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Could not render an inbox line for {TemplateKey}", row.TemplateKey);
            }
        }

        return new NotificationRendering(language, titles);
    }

    /// <summary>
    /// What the producer recorded, plus the one thing only this layer knows: how to address the
    /// reader.
    /// </summary>
    /// <remarks>
    /// Deliberately without the three values a message carries when it leaves the system. The
    /// installation's own address and the opt-out link are there so somebody reading mail can get
    /// back and can stop it; a reader already inside the application needs neither. And the path
    /// the producer froze is dropped rather than passed through: where a notification points is
    /// re-derived from what it is about, which is the reference the reader's access was actually
    /// checked against, so a stale or unchecked path can never reach the page through the wording.
    /// </remarks>
    private static Dictionary<string, string> ValuesFor(Notification row, SilexGisUser? reader)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["displayName"] = reader is null ? string.Empty : RecipientGreeting.For(reader),
        };

        var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(
            row.Placeholders, JsonSerializerOptions.Web);
        if (stored is not null)
        {
            foreach (var (key, value) in stored)
            {
                values[key] = value;
            }
        }

        values.Remove("url");
        return values;
    }
}
