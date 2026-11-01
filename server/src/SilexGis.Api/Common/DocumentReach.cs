// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Which documents of a candidate set a caller may read, decided for the whole set at once:
/// the rules written about each document and its filings first, then — for the ones those
/// leave out — reach through something the document's current file hangs on. This is the
/// listing form of the document walk, the one the cabinet and document listings answer with.
/// </summary>
public static class DocumentReach
{
    /// <summary>
    /// Reach through an attachment, asked only about the candidates the rules alone did not
    /// already admit — everything else would be a walk over every world a file can hang in for
    /// an answer that changes nothing.
    /// </summary>
    public static Task<IReadOnlyCollection<Guid>> ReachOverAsync(
        SilexGisDbContext db, AccessContext ctx, IQueryable<Document> candidates, CancellationToken ct)
    {
        var admitted = candidates
            .VisibleTo(ctx, AccessDomain.Documents, null, (db.CabinetDocuments, db.Cabinets))
            .Select(d => d.Id);
        return DocumentAccessRules.ReachedByAttachmentAsync(
            db, ctx, candidates.Where(d => !admitted.Contains(d.Id)).Select(d => d.Id), ct);
    }

    /// <summary>The readable part of <paramref name="candidates"/>, as a query still to be ordered and shaped.</summary>
    public static async Task<IQueryable<Document>> ReadableAsync(
        SilexGisDbContext db, AccessContext ctx, IQueryable<Document> candidates, CancellationToken ct)
    {
        var reached = await ReachOverAsync(db, ctx, candidates, ct);
        return candidates.VisibleTo(ctx, AccessDomain.Documents, reached, (db.CabinetDocuments, db.Cabinets));
    }
}
