// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Optimistic-concurrency plumbing over the xmin row version: single-resource GETs emit
/// an ETag; PUT/DELETE validate If-Match with 412 on mismatch. The header is enforced
/// when provided — clients that send what they last saw get lost-update protection; the
/// header becomes mandatory at the API freeze.
/// </summary>
public static class Concurrency
{
    /// <summary>
    /// Suffixes a reverse proxy appends to an entity tag when it compresses the response body.
    /// nginx and Apache both do this, and the browser stores what it was given, so the tag that
    /// comes back on the next write is the mangled one.
    /// </summary>
    private static readonly string[] EncodingSuffixes = ["-gzip", "-br", "-deflate", "-zstd"];

    /// <summary>
    /// Sets the ETag response header from the row's current version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read answers with the version it found; a write answers with the version it produced.
    /// The second matters as much as the first: a write is checked against the version the caller
    /// last held, and if only a read could hand one out, every write would leave its caller one
    /// version behind the row they had just written, and a second save a moment later would be
    /// refused as a conflict the caller cannot see. A client that is handed the version with the
    /// answer can carry it on the next write without a read in between.
    /// </para>
    /// <para>
    /// <paramref name="resourcePath"/> is for an answer to a request made on another path than
    /// the resource's own — an action posted beside it, such as a state move. The path goes out as
    /// <c>Content-Location</c>, which is how a response says whose representation it carries, so
    /// that a client can file the version where its next write on that resource will look for it.
    /// A read, a full update and a creation leave it out: the request path, or the
    /// <c>Location</c> header of a 201, already names the resource.
    /// </para>
    /// </remarks>
    public static async Task EmitETagAsync(
        HttpContext http, SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct,
        string? resourcePath = null)
    {
        var version = await ConcurrencySql.VersionAsync(db, table, id, ct);
        if (version is null)
        {
            return;
        }

        http.Response.Headers.ETag = $"\"{version}\"";
        if (resourcePath is not null)
        {
            http.Response.Headers.ContentLocation = resourcePath;
        }
    }

    /// <summary>
    /// Takes the row a versioned write is about to change, and compares the If-Match precondition
    /// with the version it holds. The answer carries a 412 Problem on a mismatch, a 428 when
    /// <paramref name="required"/> is set (the contract for entities edited through a loaded
    /// detail view) and the header is missing, and otherwise a hold the caller writes under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row stays locked from the comparison until the write commits, which is what makes the
    /// precondition mean anything. A comparison made by a read of its own and a save made later
    /// leave a window in which two writers holding the same version both pass and both commit,
    /// each keeping the columns it changed: a silent merge, with neither told, which is the lost
    /// update the precondition exists to refuse. Holding the row closes it. A second writer waits
    /// behind the first, then reads the version the first one left and is refused.
    /// </para>
    /// <para>
    /// The caller loads, decides access, takes the hold, changes what it changes, saves, and ends
    /// with <see cref="VersionHold.AnswerAsync"/> (or <see cref="VersionHold.CommitAsync"/> for an
    /// answer that carries no representation, such as a delete). A hold that is disposed without
    /// either rolls back, so an early refusal after the hold writes nothing. The transaction is
    /// the context's own; a write that already opened one takes the lock inside it and leaves the
    /// commit to its owner. Anything the write calls that opens a transaction of its own cannot
    /// run under a hold: the context has one already.
    /// </para>
    /// <para>
    /// A missing header with <paramref name="required"/> unset is the documented last-write-wins
    /// case: no comparison is made, but the row is held all the same, so that the version the
    /// write answers is the one it produced and not a later writer's.
    /// </para>
    /// <para>
    /// The caller loaded the row before it was held, so what the context tracks of it may be older
    /// than what the hold found: another writer can commit in between. The tracked copies of the
    /// held row (the entity keyed by <paramref name="id"/>, and a subtype row sharing its key) are
    /// therefore read again under the lock. Without that a last-write-wins save is applied to the
    /// stale copy: a column the request sets to the value it had before the other writer changed
    /// it compares equal to the stale original, is not written at all, and the answer then shows
    /// the requested value beside the version of a row that holds the other writer's. A caller
    /// whose access depends on the held row's own columns decides it again after the hold.
    /// </para>
    /// </remarks>
    /// <param name="containerId">
    /// A row of the same table that the write also changes and that other writes hold first — a
    /// cave, for a write on one of its entrances. It is locked before the held row, without a
    /// comparison, so that every write reaching both takes them in the same order. Two writes that
    /// each hold one and then reach for the other wait on each other until the database aborts one
    /// of them; taken in one order, the second simply waits for the first.
    /// </param>
    public static async Task<VersionHold> HoldIfMatchAsync(
        HttpContext http, SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct,
        bool required = false, Guid? containerId = null)
    {
        var header = http.Request.Headers.IfMatch;
        if (header.Count == 0 && required)
        {
            return VersionHold.Refused(ApiProblems.PreconditionRequired(
                "concurrency.if_match_required",
                "This resource requires an If-Match header carrying the version you last loaded."));
        }

        var owned = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        var hold = new VersionHold(http, db, table, id, owned);
        try
        {
            if (containerId is { } container && container != id)
            {
                await ConcurrencySql.LockVersionAsync(db, table, container, ct);
            }

            var held = await ConcurrencySql.LockVersionAsync(db, table, id, ct);
            if (header.Count > 0 && !Names(header, $"\"{held}\""))
            {
                await hold.DisposeAsync();
                return VersionHold.Refused(VersionMismatch());
            }

            await ReloadHeldRowAsync(db, id, ct);
            return hold;
        }
        catch
        {
            await hold.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Reads again every unchanged tracked entity whose single key is the held row's id: the row
    /// itself and, for the feature aggregate, the subtype row that shares its key. Rows the
    /// caller loaded beside it are not held and are left as they are.
    /// </summary>
    private static async Task ReloadHeldRowAsync(SilexGisDbContext db, Guid id, CancellationToken ct)
    {
        var heldRows = db.ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Unchanged
                && entry.Metadata.FindPrimaryKey() is { Properties.Count: 1 } key
                && entry.Property(key.Properties[0].Name).CurrentValue is Guid value
                && value == id)
            .ToList();
        foreach (var entry in heldRows)
        {
            await entry.ReloadAsync(ct);
        }
    }

    private static ProblemHttpResult VersionMismatch() =>
        ApiProblems.PreconditionFailed(
            "concurrency.version_mismatch",
            "The resource changed since it was loaded. Reload and reapply your edits.");

    /// <summary>
    /// Validates the If-Match precondition against a version this application computed from
    /// more than one row, for a write that reaches more rows than the one it names. Same answers
    /// as <see cref="HoldIfMatchAsync"/>: 428 without the header when it is required, 412 when it
    /// does not name <paramref name="currentVersion"/>. The caller computes the version under the
    /// lock of the rows it covers, inside the transaction it writes in.
    /// </summary>
    public static ProblemHttpResult? CheckIfMatchVersion(
        HttpContext http, string? currentVersion, bool required = false)
    {
        var header = http.Request.Headers.IfMatch;
        if (header.Count == 0)
        {
            return required
                ? ApiProblems.PreconditionRequired(
                    "concurrency.if_match_required",
                    "This resource requires an If-Match header carrying the version you last loaded.")
                : null;
        }

        return currentVersion is not null && Names(header, $"\"{currentVersion}\"")
            ? null
            : VersionMismatch();
    }

    /// <summary>
    /// A weak entity tag over a value this application computed: a validator for an answer that
    /// means the same as the last one without being the same bytes.
    /// </summary>
    /// <remarks>
    /// Weak on purpose where it is used: a compressing proxy may pass a weak tag through as it is
    /// and must alter or drop a strong one, and an answer carrying a freshly signed address
    /// differs from the previous answer byte for byte while saying the same thing. The value must
    /// hold no comma and end in none of the coding suffixes, since a request's list of tags is
    /// split on the one and a proxy's suffix is cut off by the other; digits and hexadecimal are
    /// both safe.
    /// </remarks>
    public static string WeakETag(string value) => $"W/\"{value}\"";

    /// <summary>
    /// Whether the request's <c>If-None-Match</c> names <paramref name="currentTag"/> — that is,
    /// whether the caller already holds the answer about to be sent.
    /// </summary>
    /// <remarks>
    /// The same comparison a write's <c>If-Match</c> gets, for the same reason: the tag comes back
    /// as a proxy left it, weakened or with the coding it applied appended, and neither says the
    /// answer changed. A request without the header holds nothing and matches nothing.
    /// </remarks>
    public static bool MatchesIfNoneMatch(HttpContext http, string currentTag) =>
        Names(http.Request.Headers.IfNoneMatch, currentTag);

    /// <summary>
    /// Whether a conditional header — one tag, a list of them, or <c>*</c> — names the current tag.
    /// </summary>
    private static bool Names(StringValues header, string currentTag)
    {
        var current = Canonical(currentTag);
        foreach (var value in header)
        {
            // One header line may carry a list of tags. Ours are digits or hexadecimal, so
            // splitting on the comma cannot cut one in half.
            foreach (var candidate in (value ?? string.Empty).Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*" || Canonical(trimmed) == current)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// An entity tag reduced to the part this application issued, so that what a proxy did to it
    /// on the way out does not read as a change to the row.
    /// </summary>
    /// <remarks>
    /// Two things happen to a tag between here and the browser. A compressing reverse proxy
    /// appends the coding it applied — nginx turns <c>"1031"</c> into <c>"1031-gzip"</c> — because
    /// the compressed body really is a different sequence of bytes; and a cache may downgrade a
    /// strong tag to a weak one. Neither says anything about the row, but both come back on the
    /// next write, and comparing them literally refuses every edit made through the deployment
    /// this project ships: the response that carried the version was JSON, which that nginx
    /// compresses, while the write that replays it is not. The comparison is therefore made on
    /// the version itself, which is what it was ever about.
    /// </remarks>
    private static string Canonical(string tag)
    {
        var value = tag.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        foreach (var suffix in EncodingSuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return value[..^suffix.Length];
            }
        }

        return value;
    }
}

/// <summary>
/// A versioned row held for a write: the lock taken by <see cref="Concurrency.HoldIfMatchAsync"/>,
/// kept until the write commits, and the version the write produced handed back with its answer.
/// </summary>
public sealed class VersionHold : IAsyncDisposable
{
    private readonly HttpContext? http;
    private readonly SilexGisDbContext? db;
    private readonly VersionedTable table;
    private readonly Guid id;
    private readonly IDbContextTransaction? owned;
    private bool saved;
    private bool failed;
    private bool committed;

    internal VersionHold(
        HttpContext http, SilexGisDbContext db, VersionedTable table, Guid id, IDbContextTransaction? owned)
    {
        this.http = http;
        this.db = db;
        this.table = table;
        this.id = id;
        this.owned = owned;
        if (owned is not null)
        {
            db.SavedChanges += OnSavedChanges;
            db.SaveChangesFailed += OnSaveChangesFailed;
        }
    }

    private VersionHold(ProblemHttpResult refusal) => Refusal = refusal;

    /// <summary>Why the write may not go ahead; null when the row is held for it.</summary>
    public ProblemHttpResult? Refusal { get; }

    internal static VersionHold Refused(ProblemHttpResult refusal) => new(refusal);

    /// <summary>
    /// Commits the write and answers the version it produced as the ETag, read inside the write's
    /// own transaction so that it cannot be a later writer's. <paramref name="resourcePath"/> goes
    /// out as Content-Location for an action posted beside the resource, as with
    /// <see cref="Concurrency.EmitETagAsync"/>. A row the write removed answers no version.
    /// </summary>
    public async Task AnswerAsync(CancellationToken ct, string? resourcePath = null)
    {
        EnsureHeld();
        var version = await ConcurrencySql.VersionAsync(db!, table, id, ct);
        await CommitAsync(ct);
        if (version is null)
        {
            return;
        }

        http!.Response.Headers.ETag = $"\"{version}\"";
        if (resourcePath is not null)
        {
            http.Response.Headers.ContentLocation = resourcePath;
        }
    }

    /// <summary>Commits the write, for an answer that carries no version (a delete).</summary>
    public async Task CommitAsync(CancellationToken ct)
    {
        EnsureHeld();
        if (owned is not null)
        {
            await owned.CommitAsync(ct);
        }

        committed = true;
    }

    /// <summary>Rolls back a transaction this hold opened and that was not committed.</summary>
    /// <remarks>
    /// <para>
    /// Disposal is where every path out of the write ends: the answer, an early refusal, and an
    /// exception on its way up. Only the first commits, so the others all roll back here, which is
    /// what a failed or refused write needs.
    /// </para>
    /// <para>
    /// One case is a mistake rather than a failure: changes were saved, nothing failed, and the
    /// handler simply never ended with <see cref="AnswerAsync"/> or <see cref="CommitAsync"/>, so
    /// its caller is told the write landed while it is rolled back. That is logged as an error.
    /// It is not thrown: disposal also runs while another exception is unwinding — a later
    /// statement that failed, a client that went away — and an exception thrown from here would
    /// replace that one, and the real cause with it. A source guard keeps handlers ending their
    /// holds, which is where the mistake is cheapest to catch.
    /// </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (owned is null)
        {
            return;
        }

        db!.SavedChanges -= OnSavedChanges;
        db.SaveChangesFailed -= OnSaveChangesFailed;
        await owned.DisposeAsync();
        if (saved && !committed && !failed && !http!.RequestAborted.IsCancellationRequested)
        {
            http.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger(typeof(VersionHold))
                .LogError(
                    "A write saved changes under the version hold of {Table} {Id} and was never committed; "
                    + "it has been rolled back. End the write with AnswerAsync or CommitAsync.",
                    table, id);
        }
    }

    private void OnSavedChanges(object? sender, SavedChangesEventArgs e) => saved = true;

    private void OnSaveChangesFailed(object? sender, SaveChangesFailedEventArgs e) => failed = true;

    private void EnsureHeld()
    {
        if (Refusal is not null)
        {
            throw new InvalidOperationException("A refused version hold cannot answer or commit a write.");
        }
    }
}
