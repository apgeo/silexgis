// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
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
    /// <param name="cabinetReach">
    /// Which cabinets reach each document, from <see cref="CabinetReachAsync"/> — the one
    /// fact of the walk that lives in another table and so cannot be read off the row. It
    /// is a required argument rather than an optional one because leaving it out would not
    /// narrow the answer, it would flip it: a rule denying a shelf would go unseen and the
    /// document would list.
    /// </param>
    public static bool AllowedByOwnRulesOrAttachment(
        AccessContext ctx,
        Document document,
        AccessAction action,
        IReadOnlyDictionary<Guid, Guid[]> cabinetReach)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cabinetReach);

        var facts = AccessTargetFacts.Of(document) with
        {
            CabinetIds = cabinetReach.TryGetValue(document.Id, out var cabinets) ? cabinets : [],
        };
        var decision = AccessEvaluator.Decide(ctx, AccessDomain.Documents, action, facts);
        return AccessEvaluator.AttachmentReachCouldDecide(decision)
            ? AccessEvaluator
                .Decide(ctx, AccessDomain.Documents, action, facts with { ReachedByAttachment = true })
                .Allowed
            : decision.Allowed;
    }

    /// <summary>
    /// The cabinets that reach each of a page of documents — every cabinet a document is
    /// filed in, plus each of their ancestors, because a rule on a cabinet covers what is
    /// filed below it. One query for the whole page, which is what keeps a listing's
    /// per-row walk free of queries.
    /// </summary>
    /// <remarks>
    /// Nothing is asked at all unless a cabinet-scoped rule for this action actually
    /// reaches this caller, which for most callers is never: with no such rule the answer
    /// cannot depend on where anything is filed, so the lookup would be paid for an
    /// outcome that is already known.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<Guid, Guid[]>> CabinetReachAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        AccessAction action,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(documentIds);

        var set = ctx.For(AccessDomain.Documents, action);
        if (documentIds.Count == 0 || (set.DenyCabinetIds.Length == 0 && set.AllowCabinetIds.Length == 0))
        {
            return new Dictionary<Guid, Guid[]>();
        }

        var ids = documentIds.Distinct().ToList();
        var filings = await db.CabinetDocuments.AsNoTracking()
            .Where(m => ids.Contains(m.DocumentId))
            .Join(
                db.Cabinets.AsNoTracking(),
                m => m.CabinetId,
                c => c.Id,
                (m, c) => new { m.DocumentId, c.AncestorIds })
            .ToListAsync(ct);

        return filings
            .GroupBy(f => f.DocumentId)
            .ToDictionary(g => g.Key, g => g.SelectMany(f => f.AncestorIds).Distinct().ToArray());
    }

    /// <summary>
    /// Which of a set of candidate documents this caller reaches through an object the
    /// document's current file hangs on — the batch form of the fact
    /// <see cref="CanReadAsync"/> resolves one document at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists so that a listing and a search can hand the answer to the query that
    /// decides which rows exist, instead of fetching rows and dropping some afterwards.
    /// Dropping afterwards is not a narrower listing, it is a listing whose count, ranking
    /// and page boundaries were computed over rows the caller never sees — which discloses
    /// them as surely as printing them would.
    /// </para>
    /// <para>
    /// The set it returns only ever widens what the query admits: it is handed to the walk
    /// as the attachment built-in, which sits on the same arm as ownership and the read
    /// audience, below every entry. A document denied by a rule stays denied even if it
    /// appears here, so the candidate set may safely be wider than the answer.
    /// </para>
    /// <para>
    /// Candidates are narrowed to the documents where the question can have a positive
    /// answer at all — the ones whose current file hangs on something, plus the ones whose
    /// revision this caller uploaded — because everything else costs a walk over every
    /// world a file can hang in for an answer that is already known to be no.
    /// </para>
    /// </remarks>
    public static Task<IReadOnlyCollection<Guid>> ReachedByAttachmentAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IQueryable<Guid> candidateDocumentIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(candidateDocumentIds);
        return ReachedAsync(
            db,
            ctx,
            db.DocumentVersions.AsNoTracking()
                .Where(v => v.IsCurrent && candidateDocumentIds.Contains(v.DocumentId)),
            ct);
    }

    /// <summary>
    /// The same question over an id set already in hand — for a caller that had to compute
    /// its candidates in a statement of its own rather than as a subquery.
    /// </summary>
    public static Task<IReadOnlyCollection<Guid>> ReachedByAttachmentAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IReadOnlyCollection<Guid> candidateDocumentIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(candidateDocumentIds);
        if (candidateDocumentIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<Guid>>([]);
        }

        var ids = candidateDocumentIds.Distinct().ToList();
        return ReachedAsync(
            db,
            ctx,
            db.DocumentVersions.AsNoTracking().Where(v => v.IsCurrent && ids.Contains(v.DocumentId)),
            ct);
    }

    private static async Task<IReadOnlyCollection<Guid>> ReachedAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        IQueryable<DocumentVersion> currentVersions,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // The file a document serves is the first of its current revision, which is the row
        // attachments point at — spelled here the way every other read of it is spelled, so
        // this cannot come to disagree with the one-document walk about which file decides.
        var pairs = await currentVersions
            .Select(v => new
            {
                v.DocumentId,
                v.UploadedBy,
                CurrentFileId = db.StoredFiles.AsNoTracking()
                    .Where(f => f.DocumentVersionId == v.Id)
                    .OrderBy(f => f.CreatedAt)
                    .ThenBy(f => f.Id)
                    .Select(f => (Guid?)f.Id)
                    .FirstOrDefault(),
            })
            .Where(x => x.CurrentFileId != null
                && (x.UploadedBy == ctx.UserId
                    || db.Attachments.AsNoTracking().Any(a => a.FileId == x.CurrentFileId)))
            .Select(x => new { x.DocumentId, FileId = x.CurrentFileId!.Value })
            .ToListAsync(ct);

        if (pairs.Count == 0)
        {
            return [];
        }

        var readable = await FileAccessRules.ReadableFileIdsAsync(
            db, ctx, [.. pairs.Select(p => p.FileId)], ct);
        return [.. pairs.Where(p => readable.Contains(p.FileId)).Select(p => p.DocumentId).Distinct()];
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
