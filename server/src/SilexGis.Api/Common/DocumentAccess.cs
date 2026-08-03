// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Who may read or write a document. A document is content in its own right — it carries
/// an owner, a club binding and a visibility band like any other owned row — so rules
/// written against it are consulted first, and a deny among them is final. A deny that
/// something else could talk past would not be a deny.
/// </summary>
/// <remarks>
/// <para>
/// This is the walk the file routes take as well, not only the document surface. A file is
/// what a document is made of, and every route that hands one over hands over a signed
/// delivery URL with it — so a rule that bound when a document was fetched by name but not
/// when its bytes were asked for would bind nowhere.
/// </para>
/// <para>
/// When nothing written against the document has an opinion, reach through an object the
/// document's current file is attached to answers instead: that is the route every
/// document reached by being attached to a cave or a trip travels, and taking it away
/// would hide content from the people who put it there. That reach is a built-in of the
/// access rule itself, ranked below ownership and the read audience, so this class does
/// not decide precedence — it only resolves the storage-backed fact the rule cannot fetch
/// for itself, and only once the rule has said the question is still open. Resolving it
/// costs a walk over every attached object in every world, which is why it is never paid
/// for a caller an entry already answered.
/// </para>
/// </remarks>
public static class DocumentAccessRules
{
    /// <summary>
    /// The same walk for a document already known to be reached through an attachment —
    /// which is what every row of a listing built from one object's attachments is, since
    /// the object they all name is the one the caller was authorised for a moment ago.
    /// The fact the rule cannot fetch is therefore already in hand, so the whole walk is
    /// arithmetic and a page of rows costs no queries at all.
    /// </summary>
    /// <remarks>
    /// It exists so that the listing and any count of the same listing ask one question
    /// rather than two: a number that disagreed with the rows beside it would announce
    /// exactly what a rule written against a document had declined to show.
    /// </remarks>
    public static bool AllowedByOwnRulesOrAttachment(
        AccessContext ctx, Document document, AccessAction action)
    {
        var facts = AccessTargetFacts.Of(document);
        var decision = AccessEvaluator.Decide(ctx, AccessDomain.Documents, action, facts);
        return AccessEvaluator.AttachmentReachCouldDecide(decision)
            ? AccessEvaluator
                .Decide(ctx, AccessDomain.Documents, action, facts with { ReachedByAttachment = true })
                .Allowed
            : decision.Allowed;
    }

    /// <summary>
    /// Read of a document. <paramref name="content"/> is the file the document currently
    /// serves, or null when it serves none — a document with nothing behind it is
    /// reachable only through its own rules, its owner and its visibility.
    /// </summary>
    public static Task<bool> CanReadAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        Document document,
        StoredFile? content,
        CancellationToken ct) =>
        DecideAsync(
            access,
            ctx,
            AccessAction.Read,
            document,
            content is null
                ? null
                : token => FileAccessRules.CanAccessAsync(db, access, ctx, content, token),
            ct);

    /// <summary>Write of a document: its title, its kind and its typed metadata.</summary>
    public static Task<bool> CanWriteAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        Document document,
        StoredFile? content,
        CancellationToken ct) =>
        DecideAsync(
            access,
            ctx,
            AccessAction.Write,
            document,
            content is null
                ? null
                : token => FileAccessRules.CanWriteFileAsync(db, access, ctx, content, token),
            ct);

    /// <summary>
    /// Read of one particular file of a document — the question every surface that hands
    /// over bytes, or a URL that will, has to ask. It is the document's own walk, decided
    /// against the file the document currently serves because that is the row anything
    /// hangs on, plus the rule that a superseded revision is editor-only.
    /// </summary>
    /// <remarks>
    /// The delivery routes authenticate by signed URL and have no caller to consult, so a
    /// minted URL is a decision already taken: whatever this says is what the bytes do.
    /// That is why it is asked here and not left to the attachment rule alone — a rule
    /// written against a document that only bound when the document was fetched by name
    /// would not bind at all, since the file route hands out the same token.
    /// <para>
    /// Superseded revisions stay editor-only for the reason they always were: a version is
    /// replaced precisely when something in it had to go, so being allowed to read what a
    /// document says now is not being allowed to read what it used to say.
    /// </para>
    /// </remarks>
    public static async Task<bool> CanReadFileAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, FileSubject subject, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!await CanReadAsync(db, access, ctx, subject.Document, subject.CurrentFile, ct))
        {
            return false;
        }

        return subject.Version.IsCurrent
            || await CanWriteAsync(db, access, ctx, subject.Document, subject.CurrentFile, ct);
    }

    /// <summary>
    /// Write of the document behind a file: uploading a revision over it, deleting one,
    /// or correcting the facts a revision states about itself. Decided against the file
    /// the document currently serves, whichever of its files was named.
    /// </summary>
    public static Task<bool> CanWriteFileAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, FileSubject subject, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return CanWriteAsync(db, access, ctx, subject.Document, subject.CurrentFile, ct);
    }

    /// <summary>
    /// The document walk for one action: decide on the document's own facts, and only if
    /// that left the question open resolve whether the caller reaches the document through
    /// something its file is attached to and decide again with that fact in hand. The
    /// second pass runs through the same rule as the first, so the attachment route is a
    /// band of the walk rather than a second answer competing with it.
    /// </summary>
    private static async Task<bool> DecideAsync(
        IAccessService access,
        AccessContext ctx,
        AccessAction action,
        Document document,
        Func<CancellationToken, Task<bool>>? resolveReach,
        CancellationToken ct)
    {
        var facts = await access.FactsOfAsync(document, ct);
        var decision = AccessEvaluator.Decide(ctx, AccessDomain.Documents, action, facts);
        if (!AccessEvaluator.AttachmentReachCouldDecide(decision)
            || resolveReach is null
            || !await resolveReach(ct))
        {
            return decision.Allowed;
        }

        return AccessEvaluator
            .Decide(ctx, AccessDomain.Documents, action, facts with { ReachedByAttachment = true })
            .Allowed;
    }
}
