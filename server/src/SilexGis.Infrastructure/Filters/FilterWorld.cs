// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Filters;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// One kind of thing that can be filtered and listed: features, documents, trips, and the rest.
/// </summary>
public interface IFilterWorld
{
    /// <summary>The key a scope names this world by. Stored in saved filters, so it never changes.</summary>
    string World { get; }

    /// <summary>
    /// What can be asked of this world by this caller. Per-caller because the answer is not the
    /// same for everyone — an installation's feature types, the groups somebody can see — and
    /// because a vocabulary listing a field whose values the caller may not enumerate would be a
    /// way to enumerate them.
    /// </summary>
    ValueTask<WorldVocabulary> VocabularyAsync(AccessContext caller, CancellationToken ct);

    ValueTask<WorldPage> QueryAsync(WorldQuery query, CancellationToken ct);
}

/// <summary>
/// The base every world is built on, and the reason the filter cannot widen what anybody sees.
/// </summary>
/// <remarks>
/// <para>
/// There is one rule this whole feature rests on: a caller's filter narrows the rows they may
/// already see, and can never reach outside them. Stated as a rule it is easy to agree with and
/// easy to break — all it takes is one world, written a year from now by somebody in a hurry,
/// applying its <c>Where</c> to the table instead of to the visible set, and no test that world
/// happens to have will notice.
/// </para>
/// <para>
/// So the order is not written down as a rule here. It is written once, in
/// <see cref="QueryAsync"/>, which is not virtual and therefore cannot be overridden. A world
/// supplies the pieces — what this caller may see, what their filter means, how to order, how to
/// describe a row — and has no say in what happens to them. Getting the order wrong is not
/// something a new world can do, because it is not something a new world does at all.
/// </para>
/// <para>
/// What the base cannot check is whether <see cref="Visible"/> is honest: a world could return the
/// whole table from it and every structural guarantee here would hold while leaking everything.
/// That is what the conformance suite is for — every registered world is made to answer, as an
/// outsider, about a row it must not return.
/// </para>
/// </remarks>
public abstract class FilterWorld<TEntity> : IFilterWorld
    where TEntity : class
{
    public abstract string World { get; }

    public abstract ValueTask<WorldVocabulary> VocabularyAsync(AccessContext caller, CancellationToken ct);

    /// <summary>
    /// The rows this caller may see, before anything they asked for is applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the world's whole security contribution. It must be the same set the world's own
    /// list endpoint would return to this caller with no filter at all — if the two ever disagree,
    /// the filter has become a second way to ask, with a second answer.
    /// </para>
    /// <para>
    /// Asynchronous because for real worlds it has to be. A feature's visible set excludes
    /// protected centrelines, which takes a lookup; a document's includes the ones reached through
    /// an attachment, which takes another. Making this synchronous would push both into the
    /// caller's filter, where they are not visibility any more and where the next world author has
    /// no reason to look for them.
    /// </para>
    /// </remarks>
    protected abstract ValueTask<IQueryable<TEntity>> VisibleAsync(
        AccessContext caller, CancellationToken ct);

    /// <summary>
    /// What the caller's conditions mean as one predicate over this world's rows.
    /// </summary>
    /// <remarks>
    /// Asynchronous because some conditions need a lookup before they can be compiled — a spatial
    /// anchor resolved to the ids within a radius, a tag slug resolved to an id. Those lookups are
    /// themselves subject to what the caller may see, which is the world's business, not the base's.
    /// </remarks>
    protected abstract ValueTask<Expression<Func<TEntity, bool>>> CompileAsync(
        WorldQuery query, CancellationToken ct);

    /// <summary>
    /// The rows in the asked-for order. Only sorts this world's vocabulary declares ever arrive.
    /// </summary>
    protected abstract IQueryable<TEntity> Order(IQueryable<TEntity> rows, WorldQuery query);

    /// <summary>These rows, by id.</summary>
    protected abstract Expression<Func<TEntity, bool>> HasId(IReadOnlyCollection<Guid> ids);

    /// <summary>
    /// One page of rows as hits.
    /// </summary>
    /// <remarks>
    /// Given the materialised page rather than a queryable, because describing a row usually needs
    /// something a projection cannot reach — which of these the caller may place exactly, what the
    /// installation calls this type. The page is bounded, so the second round trip is bounded too.
    /// </remarks>
    protected abstract ValueTask<IReadOnlyList<FilterHit>> ProjectAsync(
        IReadOnlyList<TEntity> rows, AccessContext caller, CancellationToken ct);

    /// <summary>
    /// Visibility first, then the caller's filter, then order, then a page.
    /// </summary>
    /// <remarks>
    /// Not virtual, on purpose: this is the order, and it is the same order for every world there
    /// will ever be. A derived class can still hide it or re-declare the interface, which C# gives
    /// nobody a way to forbid — but neither is something anybody does without meaning to, and the
    /// conformance suite asks the registered instance rather than a type it trusts.
    /// </remarks>
    public async ValueTask<WorldPage> QueryAsync(WorldQuery query, CancellationToken ct)
    {
        var visible = await VisibleAsync(query.Caller, ct);

        // Named rows are narrowed here, inside the walk, rather than being a condition somebody
        // could write. Both orders give the same rows; only this one makes it impossible to add a
        // world later whose id condition is evaluated before its visibility.
        if (query.Ids is { Count: > 0 })
        {
            visible = visible.Where(HasId(query.Ids));
        }

        var matching = visible.Where(await CompileAsync(query, ct));

        // Counted over the same composed set the page is taken from, never over the visible set
        // alone. A total that ignored the filter would tell the caller how many rows exist that
        // they were not shown, which is the sort of arithmetic this whole design exists to prevent.
        var total = query.WantTotal ? await matching.CountAsync(ct) : (int?)null;

        var page = await Order(matching, query)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(ct);

        return new WorldPage(await ProjectAsync(page, query.Caller, ct), total);
    }
}
