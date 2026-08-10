// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// Documents, as a filterable world.
/// </summary>
/// <remarks>
/// <para>
/// The one world whose visible set is genuinely two questions. A document is readable on its own
/// account — owner, club, visibility band, and the cabinets it is filed in — and it is also readable
/// by anybody who may read the thing its file hangs on. A survey sheet attached to a cave belongs to
/// whoever may read that cave, whether or not they were ever named on the document.
/// </para>
/// <para>
/// Getting that second half wrong is what the owner asked to have closed: a document somebody can
/// open by following a link from a cave, but cannot find by searching, is a document they will
/// re-upload. So the reach is composed into the visible set here rather than applied to rows after
/// they are fetched — dropping rows afterwards is not a narrower listing, it is a listing whose
/// count and page boundaries were computed over rows the caller never sees.
/// </para>
/// </remarks>
public sealed class DocumentFilterWorld(SilexGisDbContext db) : FilterWorld<Document>
{
    public const string Key = "document";

    public override string World => Key;

    public override ValueTask<WorldVocabulary> VocabularyAsync(
        AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(DocumentFilterFields.Vocabulary);

    /// <summary>
    /// What this caller may see: what the rules admit, plus what they reach through an attachment.
    /// </summary>
    /// <remarks>
    /// Two passes, spelled exactly as the document listing spells them. The first asks the walk with
    /// no reach, to find what is already admitted; the reach is then asked only about the rest,
    /// because the walk is where a deny lives and a document denied by a rule stays denied however
    /// it is reached. Asking about everything would be a larger query for the same answer.
    /// </remarks>
    protected override async ValueTask<IQueryable<Document>> VisibleAsync(
        AccessContext caller, CancellationToken ct)
    {
        var all = db.Documents.AsNoTracking();

        var admitted = all
            .VisibleTo(caller, AccessDomain.Documents, null, (db.CabinetDocuments, db.Cabinets))
            .Select(d => d.Id);

        var reached = await DocumentAccessRules.ReachedByAttachmentAsync(
            db, caller, all.Where(d => !admitted.Contains(d.Id)).Select(d => d.Id), ct);

        return all.VisibleTo(
            caller, AccessDomain.Documents, reached, (db.CabinetDocuments, db.Cabinets));
    }

    protected override Expression<Func<Document, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        d => ids.Contains(d.Id);

    protected override ValueTask<Expression<Func<Document, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Compile(query.Where));

    private static Expression<Func<Document, bool>> Compile(FilterNode? node) => node switch
    {
        null => _ => true,
        AllOfNode all => all.Of.Count == 0
            ? _ => true
            : all.Of.Select(Compile).Aggregate((left, right) => left.And(right)),
        AnyOfNode any => any.Of.Count == 0
            ? _ => false
            : any.Of.Select(Compile).Aggregate((left, right) => left.Or(right)),
        NotNode not => Compile(not.Of).Not(),
        ConditionNode condition => Leaf(condition),
        _ => throw new InvalidOperationException($"No compiler arm for {node.GetType().Name}."),
    };

    private static Expression<Func<Document, bool>> Leaf(ConditionNode condition) => condition.Field switch
    {
        DocumentFilterFields.Title => FilterLeaves.Text<Document>(condition, d => d.Title),
        DocumentFilterFields.TypeId => FilterLeaves.Longs<Document>(condition, d => d.DocumentTypeId),
        DocumentFilterFields.Language => FilterLeaves.Text<Document>(condition, d => d.Language),
        DocumentFilterFields.OwnerId => FilterLeaves.Guids<Document>(condition, d => d.OwnerUserId),
        DocumentFilterFields.CavingGroupId =>
            FilterLeaves.Guids<Document>(condition, d => d.CavingGroupId),
        DocumentFilterFields.Visibility =>
            FilterLeaves.EnumField<Document, Visibility>(condition, d => d.Visibility),
        DocumentFilterFields.CreatedAt => FilterLeaves.Instant<Document>(condition, d => d.CreatedAt),
        DocumentFilterFields.UpdatedAt => FilterLeaves.Instant<Document>(condition, d => d.UpdatedAt),
        _ => throw new InvalidOperationException(
            $"No compiler arm for '{condition.Field}'. A field the vocabulary declares must have one "
            + "here, or a validated filter would silently match everything."),
    };

    protected override IQueryable<Document> Order(IQueryable<Document> rows, WorldQuery query)
    {
        var descending = query.Descending;
        return query.Sort switch
        {
            SortKey.Created => descending
                ? rows.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
                : rows.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id),
            SortKey.Title => descending
                ? rows.OrderByDescending(d => d.Title).ThenByDescending(d => d.Id)
                : rows.OrderBy(d => d.Title).ThenBy(d => d.Id),
            SortKey.Owner => descending
                ? rows.OrderByDescending(d => db.Users
                        .Where(u => u.Id == d.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenByDescending(d => d.Id)
                : rows.OrderBy(d => db.Users
                        .Where(u => u.Id == d.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault())
                    .ThenBy(d => d.Id),
            _ => descending
                ? rows.OrderByDescending(d => d.UpdatedAt).ThenByDescending(d => d.Id)
                : rows.OrderBy(d => d.UpdatedAt).ThenBy(d => d.Id),
        };
    }

    protected override async ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<Document> rows, AccessContext caller, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var typeIds = rows.Where(d => d.DocumentTypeId is not null)
            .Select(d => d.DocumentTypeId!.Value).Distinct().ToArray();
        var types = typeIds.Length == 0
            ? []
            : await db.DocumentTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return
        [
            .. rows.Select(d => new FilterHit
            {
                World = Key,
                Id = d.Id,
                Title = string.IsNullOrWhiteSpace(d.Title) ? Key : d.Title,
                Subtitle = d.DocumentTypeId is not null && types.TryGetValue(d.DocumentTypeId.Value, out var name)
                    ? name
                    : null,
                Symbol = Key,
                // A document has no position of its own. What it hangs on may have one, and that is
                // the other end's to disclose.
                Placeable = false,
            }),
        ];
    }
}
