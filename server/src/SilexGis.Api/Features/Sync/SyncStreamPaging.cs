// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The download streams a sync set is read through. Each orders its own rows on its own key and
/// issues positions only it will honour.
/// </summary>
public enum SyncStream
{
    /// <summary>The set's features and the record of the ones that have gone.</summary>
    Features,

    /// <summary>The documents hanging on the set's features, as links with the document embedded.</summary>
    Documents,
}

/// <summary>
/// How one stream orders its rows: the moment a row last changed, and the identifier that breaks
/// ties between rows that changed together. Both are expressions so the database does the
/// ordering and the comparison against a resume position; the in-memory merge reads the same
/// expressions, compiled, so the two can never disagree about where a row sorts.
/// </summary>
/// <remarks>
/// The change key is the one expression a stream owns. Replacing it — with a commit-ordered
/// sequence, say — is a change to the stream's own order and to nothing else.
/// </remarks>
public sealed class SyncStreamOrder<T>
{
    private readonly Func<T, DateTimeOffset> changedAtOf;
    private readonly Func<T, Guid> idOf;

    public SyncStreamOrder(Expression<Func<T, DateTimeOffset>> changedAt, Expression<Func<T, Guid>> id)
    {
        ChangedAt = changedAt;
        Id = id;
        changedAtOf = changedAt.Compile();
        idOf = id.Compile();
    }

    /// <summary>When a row last changed, as the database computes it.</summary>
    public Expression<Func<T, DateTimeOffset>> ChangedAt { get; }

    /// <summary>The tie-breaker between rows sharing a change time.</summary>
    public Expression<Func<T, Guid>> Id { get; }

    /// <summary>A row read off this stream's query, placed where the stream orders it.</summary>
    public SyncStreamEntry Entry(T row) => new(changedAtOf(row), idOf(row), row!);
}

/// <summary>One row of a page before it is cut: where it sorts, and what it is.</summary>
public readonly record struct SyncStreamEntry(DateTimeOffset ChangedAt, Guid Id, object Row);

/// <summary>
/// A stream request that passed the checks every stream makes: the set this caller owns, the
/// position to resume after, the page size and the set's roots.
/// </summary>
/// <param name="Cursor">The position exactly as the device sent it, echoed back on a page that
/// emits nothing so that a caught-up device keeps the position it already holds.</param>
public sealed record SyncStreamOpening(
    SyncStream Stream,
    SyncSet Set,
    JsonElement Settings,
    IReadOnlyList<Guid> Roots,
    SyncCursor? From,
    int Size,
    string? Cursor);

/// <summary>One cut page: its rows in stream order, where to resume, and whether more is waiting.</summary>
public sealed record SyncStreamCut(IReadOnlyList<object> Rows, string? NextCursor, bool HasMore);

/// <summary>
/// The paging every download stream shares: resolving the set, reading and checking the resume
/// position, sizing the page, and cutting a page out of a stream's live rows and its tombstones.
/// What a stream carries, who may have it and how it is shaped stay with the stream; how it is
/// walked lives here, once, so that no stream walks differently from another.
/// </summary>
/// <remarks>
/// <para>
/// A stream is read in halves — its live rows and the record of rows that have gone — because the
/// two are different questions to the database. Each half is advanced past the position and read
/// one row beyond the page, then the halves are merged on one key and cut. Reading one extra row
/// from each half is what lets the cut tell whether anything was waiting behind it without asking
/// the database a second question.
/// </para>
/// <para>
/// The halves of a page are merged on the pair (change time, identifier) in memory, and each half
/// was ordered on the same pair by the database. The two orders agree for identifiers because both
/// order them as they are written out, digit by digit; a key whose in-memory and database orders
/// differed would hand a device rows twice or never, which is why a stream's key is restricted to a
/// time and a <see cref="Guid"/>.
/// </para>
/// </remarks>
public static class SyncStreamPaging
{
    /// <summary>
    /// The checks every stream makes before it reads a row. Answers a problem the stream returns
    /// as it stands, or the opening it reads against.
    /// </summary>
    public static async Task<(SyncStreamOpening? Opening, ProblemHttpResult? Problem)> OpenAsync(
        SilexGisDbContext db,
        AccessContext ctx,
        SyncStream stream,
        Guid setId,
        string? cursor,
        int? pageSize,
        SyncOptions options,
        CancellationToken ct)
    {
        // Resolved by owner, deliberately and unconditionally. These are the routes a phone pulls
        // rows through, and a phone authenticates as exactly one account: a set somebody else owns
        // is answered like one that is not there, whatever that account is otherwise allowed. An
        // installation may let a full administrator read another caver's selection through the
        // set's own address; it may not let one pull that selection down onto a device.
        var set = await db.SyncSets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == setId && x.OwnerUserId == ctx.UserId, ct);
        if (set is null)
        {
            return (null, ApiProblems.NotFound("sync.set_not_found"));
        }

        SyncCursor? from = null;
        if (cursor is not null)
        {
            if (!SyncCursor.TryDecode(cursor, stream, out var decoded))
            {
                return (null, ApiProblems.BadRequest(
                    "sync.cursor_invalid", "That resume position was not issued by this server."));
            }

            // A resume position means "everything in this selection, after here". Change the
            // selection and the second half stops describing the first: a cave added to the set
            // moves no row, so nothing under it is ever after a caught-up device's position and
            // the whole cave would stay invisible until an unrelated edit happened to touch it.
            // The device is told to drop the position rather than handed a page that silently
            // omits what it just asked to carry.
            if (decoded.SetRevision != set.Revision)
            {
                return (null, ApiProblems.Conflict(
                    "sync.cursor_stale",
                    "The selection changed after that resume position was issued. "
                    + "Drop the cursor and read the set from the beginning."));
            }

            from = decoded;
        }

        // A device that names no size gets the installation's default, and any size at all is held
        // inside the ceiling that same installation announces.
        var size = Math.Clamp(pageSize ?? options.ResolvedDefaultPageSize, 1, options.ResolvedPageSizeMax);
        var settings = JsonSerializer.Deserialize<JsonElement>(set.Settings);

        var roots = await db.SyncSetMembers.AsNoTracking()
            .Where(m => m.SyncSetId == set.Id)
            .Select(m => m.RootFeatureId)
            .ToListAsync(ct);

        return (new SyncStreamOpening(stream, set, settings, roots, from, size, cursor), null);
    }

    /// <summary>
    /// Restricts a half of a stream to what lies strictly after the device's last position.
    /// Compared as a pair, so two rows sharing a change time are still ordered against each other
    /// and neither is repeated nor skipped.
    /// </summary>
    public static IQueryable<T> After<T>(IQueryable<T> rows, SyncStreamOrder<T> order, SyncCursor? from)
    {
        if (from is not { } c)
        {
            return rows;
        }

        // The position enters the query the way a captured variable does, so the database is
        // sent it as a parameter rather than as text spliced into the statement.
        Expression<Func<DateTimeOffset>> at = () => c.ChangedAt;
        Expression<Func<Guid>> past = () => c.Id;

        var row = order.ChangedAt.Parameters[0];
        var changedAt = order.ChangedAt.Body;
        var id = new Rebind(order.Id.Parameters[0], row).Visit(order.Id.Body);
        var after = Expression.OrElse(
            Expression.GreaterThan(changedAt, at.Body),
            Expression.AndAlso(
                Expression.Equal(changedAt, at.Body),
                Expression.GreaterThan(id, past.Body)));
        return rows.Where(Expression.Lambda<Func<T, bool>>(after, row));
    }

    /// <summary>
    /// The first rows of a half in the order the cursor is defined against, one more than the page
    /// holds. Ties are broken by identifier — without it the database is free to return equal rows
    /// in any order and does, so a row could arrive on two consecutive pages and another on
    /// neither.
    /// </summary>
    public static IQueryable<T> Probe<T>(IQueryable<T> rows, SyncStreamOrder<T> order, int size) =>
        rows.OrderBy(order.ChangedAt).ThenBy(order.Id).Take(size + 1);

    /// <summary>
    /// Merges the halves a stream read into one page and cuts it. The halves are read separately
    /// because they are asked different questions of the database, and merged here because they
    /// are one stream to the device: interleaving them by the same key is what keeps a single
    /// cursor able to resume any of them.
    /// </summary>
    /// <remarks>
    /// A page that emits nothing hands back the position it was asked from, so a device that is
    /// caught up keeps the position it holds rather than being sent back to the beginning.
    /// </remarks>
    public static SyncStreamCut Cut(SyncStreamOpening opening, params IEnumerable<SyncStreamEntry>[] halves)
    {
        var page = halves.SelectMany(half => half)
            .OrderBy(x => x.ChangedAt).ThenBy(x => x.Id)
            .ToList();
        var hasMore = page.Count > opening.Size;
        if (hasMore)
        {
            page = page[..opening.Size];
        }

        var next = page.Count > 0
            ? new SyncCursor(opening.Set.Revision, page[^1].ChangedAt, page[^1].Id).Encode(opening.Stream)
            : opening.Cursor;
        return new SyncStreamCut([.. page.Select(x => x.Row)], next, hasMore);
    }

    /// <summary>Points the identifier expression at the change-key expression's row.</summary>
    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
