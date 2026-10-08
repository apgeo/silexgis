// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Messaging;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Notifications;

/// <summary>
/// The language a line the server words for one account is written in.
/// </summary>
/// <remarks>
/// <para>
/// One order, in one place: what the request asked for, then the language the account last saved,
/// then English. The request comes first because somebody who has just switched the application to
/// another language wants that one now and not at their next sign-in; the account comes second
/// because a caller that is not a browser — or a browser asking only for languages this
/// installation has no words in — has said nothing, and the account has.
/// </para>
/// <para>
/// It is written down once because more than one thing words a line for a reader — an inbox, a
/// document — and each deciding the order for itself is how one person comes to read one of them
/// in a language and the other in another.
/// </para>
/// </remarks>
public static class ReadingLanguage
{
    /// <summary>The language, given the request's header and what the account has stored.</summary>
    public static string Of(string? acceptLanguage, string? storedLocale) =>
        AcceptLanguage.Preferred(acceptLanguage) ?? MessageTemplateCatalog.Normalise(storedLocale);

    /// <summary>
    /// The language for an account known only by its identifier. The account is read only when
    /// the request did not settle it; with no account there is nothing stored to ask.
    /// </summary>
    public static async Task<string> ForAccountAsync(
        SilexGisDbContext db, Guid? userId, string? acceptLanguage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (AcceptLanguage.Preferred(acceptLanguage) is { } asked)
        {
            return asked;
        }

        var stored = userId is { } id
            ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Locale).FirstOrDefaultAsync(ct)
            : null;
        return MessageTemplateCatalog.Normalise(stored);
    }
}
