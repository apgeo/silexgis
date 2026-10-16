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
/// Photographs — documents whose current file is a picture — as a filterable world.
/// </summary>
/// <remarks>
/// <para>
/// Which pictures a reader may see has one home, the photograph read rule the gallery, the albums
/// and the trip write-ups all draw from: the ordinary document rule with its reach through
/// attachments, narrowed to pictures. This world asks that rule and nothing else, so it lists the
/// rows the gallery lists.
/// </para>
/// <para>
/// What the gallery withholds beyond the rows is a picture's position, decided per picture by what
/// it hangs on. This world never answers a question about position at all — no field reads it, no
/// sort orders by it, and a hit is never placeable — so it has nothing of that rule to repeat.
/// </para>
/// <para>
/// A distinct world from documents although the rows are documents: a surface narrows with the
/// world it names, and the gallery's filter must not be satisfiable by a document that is not a
/// picture, nor offer fields that only make sense for one.
/// </para>
/// </remarks>
public sealed class PhotographFilterWorld(SilexGisDbContext db) : FilterWorld<Document>
{
    public const string Key = PhotographFilterFields.World;

    public override string World => Key;

    public override ValueTask<WorldVocabulary> VocabularyAsync(AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(PhotographFilterFields.Vocabulary);

    protected override async ValueTask<IQueryable<Document>> VisibleAsync(AccessContext caller, CancellationToken ct) =>
        await PhotographReads.VisiblePhotographsAsync(db, caller, ct);

    protected override Expression<Func<Document, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        d => ids.Contains(d.Id);

    protected override ValueTask<Expression<Func<Document, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct) =>
        ValueTask.FromResult(Compile(query.Where));

    private Expression<Func<Document, bool>> Compile(FilterNode? node) => node switch
    {
        null => _ => true,
        AllOfNode all => all.Of.Count == 0
            ? _ => true
            : all.Of.Select(Compile).Aggregate((left, right) => left.And(right)),
        // An OR of nothing matches nothing, so clearing the last choice narrows rather than widens.
        AnyOfNode any => any.Of.Count == 0
            ? _ => false
            : any.Of.Select(Compile).Aggregate((left, right) => left.Or(right)),
        NotNode not => Compile(not.Of).Not(),
        ConditionNode condition => Leaf(condition),
        _ => throw new InvalidOperationException($"No compiler arm for {node.GetType().Name}."),
    };

    private Expression<Func<Document, bool>> Leaf(ConditionNode condition) => condition.Field switch
    {
        PhotographFilterFields.Title => FilterLeaves.Text<Document>(condition, d => d.Title),
        PhotographFilterFields.Caption => FilterLeaves.Text<Document>(condition, d => db.PhotoDetails
            .Where(p => p.DocumentId == d.Id).Select(p => p.Caption).FirstOrDefault()),
        PhotographFilterFields.PlaceName => FilterLeaves.Text<Document>(condition, d => db.PhotoDetails
            .Where(p => p.DocumentId == d.Id).Select(p => p.PlaceName).FirstOrDefault()),
        PhotographFilterFields.Tag => TagLeaf(condition),
        // Taken, else uploaded: the date the gallery shows a picture under and its range control
        // has always asked, so the control written as a condition matches what it matched before.
        PhotographFilterFields.DatedAt => FilterLeaves.NullableInstant<Document>(condition, d => db.StoredFiles
            .Where(f => f.Kind == FileKind.Image
                && db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id))
            .Select(f => (DateTimeOffset?)(f.ContentCreatedAt ?? f.CreatedAt))
            .FirstOrDefault()),
        // The picture of the revision the document currently serves, as the gallery shows it.
        PhotographFilterFields.TakenAt => FilterLeaves.NullableInstant<Document>(condition, d => db.StoredFiles
            .Where(f => f.Kind == FileKind.Image
                && db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id))
            .Select(f => f.ContentCreatedAt)
            .FirstOrDefault()),
        CommonFilterFields.OwnerId => FilterLeaves.Guids<Document>(condition, d => d.OwnerUserId),
        CommonFilterFields.CavingGroupId => FilterLeaves.Guids<Document>(condition, d => d.CavingGroupId),
        CommonFilterFields.Visibility => FilterLeaves.EnumField<Document, Visibility>(condition, d => d.Visibility),
        CommonFilterFields.CreatedAt => FilterLeaves.Instant<Document>(condition, d => d.CreatedAt),
        CommonFilterFields.UpdatedAt => FilterLeaves.Instant<Document>(condition, d => d.UpdatedAt),
        _ => throw new InvalidOperationException(
            $"No compiler arm for '{condition.Field}'. A field the vocabulary declares must have one "
            + "here, or a validated filter would silently match everything."),
    };

    /// <summary>
    /// Tags on the picture the document currently serves, matched by the tag's slug. Tags sit on the
    /// file, where every other tag on an uploaded thing sits, so a picture re-uploaded as a new
    /// revision is asked about its new file's tags, as the gallery shows them.
    /// </summary>
    private Expression<Func<Document, bool>> TagLeaf(ConditionNode condition)
    {
        Expression<Func<Document, bool>> tagged = d => db.Taggings.Any(t =>
            t.EntityType == AttachedEntityType.StoredFile
            && db.StoredFiles.Any(f => f.Id == t.EntityId
                && db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id)));

        if (condition.Op is FilterOp.IsEmpty)
        {
            return tagged.Not();
        }

        if (condition.Op is FilterOp.IsNotEmpty)
        {
            return tagged;
        }

        var slugs = condition.Values.OfType<IdValue>().Select(v => v.Value).ToArray();
        return d => db.Taggings.Any(t =>
            t.EntityType == AttachedEntityType.StoredFile
            && db.Tags.Any(tag => tag.Id == t.TagId && slugs.Contains(tag.Slug))
            && db.StoredFiles.Any(f => f.Id == t.EntityId
                && db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.IsCurrent && v.DocumentId == d.Id)));
    }

    protected override IQueryable<Document> Order(IQueryable<Document> rows, WorldQuery query)
    {
        // Ties broken by id in every arm, or equal keys page unstably.
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

    protected override ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<Document> rows, AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<FilterHit>>(
        [
            .. rows.Select(d => new FilterHit
            {
                World = Key,
                Id = d.Id,
                Title = string.IsNullOrWhiteSpace(d.Title) ? Key : d.Title,
                Subtitle = null,
                Symbol = Key,
                // A picture's position is disclosed per reader by what the picture hangs on, and
                // only the gallery's own answer applies that rule. Placing a hit from here would be
                // a second, unchecked copy of it.
                Placeable = false,
            }),
        ]);
}
