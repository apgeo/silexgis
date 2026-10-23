// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Filters;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
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
/// it hangs on. This world asks that rule, from its one home, before a position condition may match
/// a picture: a picture whose fix the reader is not shown matches no position condition, exactly as
/// a picture with no fix. No sort orders by position, and a hit is never placeable.
/// </para>
/// <para>
/// A cave condition passes the placement gate every spatial anchor passes, because a picture can
/// hang on a cave its reader may read but not place: "pictures of cave X" asked of such a cave is
/// refused in the words a cave that does not exist is refused in, so neither the answer nor the
/// refusal says which it was.
/// </para>
/// <para>
/// A distinct world from documents although the rows are documents: a surface narrows with the
/// world it names, and the gallery's filter must not be satisfiable by a document that is not a
/// picture, nor offer fields that only make sense for one.
/// </para>
/// </remarks>
public sealed class PhotographFilterWorld(
    SilexGisDbContext db,
    FeatureProtection protection,
    PhotoPositionDisclosure disclosure,
    FeatureFilterWorld anchors)
    : FilterWorld<Document>
{
    public const string Key = PhotographFilterFields.World;

    public override string World => Key;

    public override ValueTask<WorldVocabulary> VocabularyAsync(AccessContext caller, CancellationToken ct) =>
        ValueTask.FromResult(PhotographFilterFields.Vocabulary);

    protected override async ValueTask<IQueryable<Document>> VisibleAsync(AccessContext caller, CancellationToken ct) =>
        await PhotographReads.VisiblePhotographsAsync(db, caller, ct);

    protected override Expression<Func<Document, bool>> HasId(IReadOnlyCollection<Guid> ids) =>
        d => ids.Contains(d.Id);

    protected override async ValueTask<Expression<Func<Document, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct)
    {
        // The caves first: a refusal is the whole answer, and nothing about positions is asked of
        // a filter that is going to be refused.
        await GateCavesAsync(query.Caller, query.Where, ct);
        var positions = await PositionConditions.ResolveAsync(
            anchors, query.Caller, query.Where, ask => ShownFixesAsync(query.Caller, ask, ct), ct);
        return Compile(query.Where, positions);
    }

    /// <summary>
    /// The pictures whose fix answers the question and is one this caller is shown — the same
    /// disclosure rule, from the same home, the gallery's map mode and the photo layer ask.
    /// </summary>
    private async Task<IEnumerable<Guid>> ShownFixesAsync(AccessContext caller, SpatialAsk ask, CancellationToken ct)
    {
        var candidates = await PositionMembershipSql.PhotoFixesAsync(db, ask, ct);
        if (candidates.Count == 0)
        {
            return [];
        }

        var shown = await disclosure.DisclosableIdsAsync(caller, [.. candidates.Select(c => c.FileId)], ct);
        return candidates.Where(c => shown.Contains(c.FileId)).Select(c => c.DocumentId);
    }

    /// <summary>
    /// Every cave a cave condition names has to be one this caller may read and place exactly, by
    /// the placement gate the spatial anchors pass, or the filter is refused.
    /// </summary>
    /// <remarks>
    /// One refusal for a cave that does not exist, one the caller cannot read, one they cannot place
    /// exactly and anything that is not a cave at all — the same code and sentence a spatial anchor
    /// is refused with — and it is the whole answer, never a filter run with the condition left out.
    /// Answering instead would say, through the count, which pictures hang on a protected cave.
    /// </remarks>
    private async Task GateCavesAsync(AccessContext caller, FilterNode? where, CancellationToken ct)
    {
        var checkedIds = new HashSet<Guid>();
        foreach (var value in CaveValues(where))
        {
            if (!Guid.TryParse(value, out var id))
            {
                throw SpatialConditions.AnchorNotFound();
            }

            if (checkedIds.Add(id)
                && await protection.ScopeFeatureAsync(caller, id, ct) is not { Kind: FeatureKind.Cave })
            {
                throw SpatialConditions.AnchorNotFound();
            }
        }
    }

    private static IEnumerable<string> CaveValues(FilterNode? node) => node switch
    {
        ConditionNode { Field: PhotographFilterFields.Cave } condition =>
            condition.Values.OfType<IdValue>().Select(v => v.Value),
        null => [],
        _ => node.Children.SelectMany(CaveValues),
    };

    private Expression<Func<Document, bool>> Compile(
        FilterNode? node, IReadOnlyDictionary<ConditionNode, Guid[]> positions) => node switch
    {
        null => _ => true,
        AllOfNode all => all.Of.Count == 0
            ? _ => true
            : all.Of.Select(n => Compile(n, positions)).Aggregate((left, right) => left.And(right)),
        // An OR of nothing matches nothing, so clearing the last choice narrows rather than widens.
        AnyOfNode any => any.Of.Count == 0
            ? _ => false
            : any.Of.Select(n => Compile(n, positions)).Aggregate((left, right) => left.Or(right)),
        NotNode not => Compile(not.Of, positions).Not(),
        ConditionNode condition => Leaf(condition, positions),
        _ => throw new InvalidOperationException($"No compiler arm for {node.GetType().Name}."),
    };

    private Expression<Func<Document, bool>> Leaf(
        ConditionNode condition, IReadOnlyDictionary<ConditionNode, Guid[]> positions) => condition.Field switch
    {
        // Gated above, so every id here is a cave the caller may read and place exactly.
        PhotographFilterFields.Cave => PhotographReads.HangingUnder(
            db, [.. condition.Values.OfType<IdValue>().Select(v => Guid.Parse(v.Value))]),
        SpatialConditions.PositionField => DocumentIn(PositionConditions.Of(positions, condition)),
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

    private static Expression<Func<Document, bool>> DocumentIn(Guid[] documents) => d => documents.Contains(d.Id);

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
