// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Who may read or write a document. A document is content in its own right — it carries
/// an owner, a club binding and a visibility band like any other owned row — so rules
/// written against it are consulted first, and a deny among them is final. A deny that
/// something else could talk past would not be a deny.
/// </summary>
/// <remarks>
/// When nothing written against the document has an opinion, the file the document
/// currently serves answers instead. That is the route every document reached by being
/// attached to a cave or a trip travels today, and taking it away here would hide
/// content from the people who put it there. The two answers are separate on purpose
/// while only one of them is expressible as rules; unifying them into a single walk is
/// the next step, not this one.
/// </remarks>
public static class DocumentAccessRules
{
    /// <summary>
    /// Read of a document. <paramref name="content"/> is the file the document currently
    /// serves, or null when it serves none — a document with nothing behind it is
    /// reachable only through its own rules, its owner and its visibility.
    /// </summary>
    public static async Task<bool> CanReadAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        Document document,
        StoredFile? content,
        CancellationToken ct) =>
        await OwnRulesAsync(access, ctx, AccessAction.Read, document, ct)
        ?? (content is not null && await FileAccessRules.CanAccessAsync(db, access, ctx, content, ct));

    /// <summary>Write of a document: its title, its kind and its typed metadata.</summary>
    public static async Task<bool> CanWriteAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        Document document,
        StoredFile? content,
        CancellationToken ct) =>
        await OwnRulesAsync(access, ctx, AccessAction.Write, document, ct)
        ?? (content is not null && await FileAccessRules.CanWriteFileAsync(db, access, ctx, content, ct));

    /// <summary>
    /// What the document's own domain says, or null when it says nothing. A rule naming
    /// the document (or full administration) is the whole answer either way; the owner
    /// and visibility built-ins only ever admit, so failing them leaves the question open
    /// for whatever else can answer it.
    /// </summary>
    private static async Task<bool?> OwnRulesAsync(
        IAccessService access,
        AccessContext ctx,
        AccessAction action,
        Document document,
        CancellationToken ct)
    {
        var decision = await access.DecideAsync(ctx, action, document, ct);
        return decision.Source switch
        {
            AccessDecisionSource.FullAdministrators or AccessDecisionSource.Entries => decision.Allowed,
            _ => decision.Allowed ? true : null,
        };
    }
}
