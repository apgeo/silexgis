// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The mobile sync surface. Every route here is inside the authenticated group and none of
/// them is on the anonymous allow-list: a device is an account holder or it is nothing, and
/// what it may carry is decided against that account exactly as a browser's reads are.
/// </summary>
public static class SyncEndpoints
{
    /// <summary>
    /// The protocol generation this server speaks. A constant rather than a setting: it is a
    /// statement about the code, and an installation that could lower it would be claiming to
    /// speak a contract it does not implement. Bumped only by a change that breaks a client
    /// pinned to the previous value; anything additive is announced through the feature list.
    /// </summary>
    public const int ContractVersion = 1;

    /// <summary>
    /// The optional parts of the contract this build serves, announced by name. A device that
    /// meets a server serving only some of them takes the parts that are there instead of
    /// failing at the first transfer — which is why reading rows is announced separately from
    /// writing them, and why a build that can only be read from says so.
    /// </summary>
    public static readonly IReadOnlyList<string> ServedFeatures = ["download"];

    public static RouteGroupBuilder MapSyncEndpoints(this RouteGroupBuilder api)
    {
        var sync = api.MapGroup("/sync").WithTags("Sync");

        // These routes are named, and almost nothing else on this server is. A name becomes the
        // operation's identifier in the served description, which is what a generated client on
        // another platform turns into a method name: unnamed, the generator invents one from the
        // path and it changes whenever the path is tidied. This surface is the one a separate
        // application is written against from the description alone, so its names are part of the
        // contract and are chosen here rather than left to a tool. Naming the rest of the server
        // is a larger change than this, and is not made in passing.
        //
        // Problem responses are declared for the same reason: a code branched on by a device has
        // to be visible to whoever writes the device. The declared statuses are the ones this
        // slice can answer; the body is the standard problem document carrying a stable `code`.
        sync.MapGet("/capabilities", CapabilitiesAsync)
            .WithName("syncCapabilities")
            .WithSummary("The contract version and limits a device sizes itself to (authenticated).");

        var sets = sync.MapGroup("/sets");
        sets.MapGet("/", ListAsync).WithName("syncListSets")
            .WithSummary("The caller's own sync sets.");
        sets.MapPost("/", CreateAsync).WithValidation<SyncSetWriteRequest>()
            .WithName("syncCreateSet")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Creates a sync set owned by the caller.");
        sets.MapGet("/{id:guid}", GetAsync).WithName("syncGetSet")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One of the caller's own sync sets.");
        sets.MapPut("/{id:guid}", UpdateAsync).WithValidation<SyncSetWriteRequest>()
            .WithName("syncReplaceSet")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Replaces a sync set the caller owns; bumps its revision when anything changed.");
        sets.MapDelete("/{id:guid}", DeleteAsync).WithName("syncDeleteSet")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Deletes a sync set the caller owns.");
        sets.MapSyncDownloadEndpoints();

        return api;
    }

    private static async Task<Results<Ok<SyncCapabilitiesDto>, UnauthorizedHttpResult>> CapabilitiesAsync(
        IOptions<SyncOptions> options,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        // Asked before anything else, so it is also the first place a device finds out its
        // credential has lapsed — which is why it resolves a context rather than trusting the
        // route group alone to have done it.
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new SyncCapabilitiesDto(
            ContractVersion,
            options.Value.ResolvedPageSizeMax,
            options.Value.ResolvedUploadRowsMax,
            ServedFeatures));
    }

    private static async Task<Results<Ok<List<SyncSetDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var sets = await OwnedBy(db, ctx.UserId).AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var ids = sets.Select(s => s.Id).ToList();
        var members = await db.SyncSetMembers.AsNoTracking()
            .Where(m => ids.Contains(m.SyncSetId))
            .ToListAsync(ct);

        return TypedResults.Ok(sets
            .Select(s => ToDto(s, [.. members.Where(m => m.SyncSetId == s.Id).Select(m => m.RootFeatureId)]))
            .ToList());
    }

    private static async Task<Results<Ok<SyncSetDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await OwnedBy(db, ctx.UserId).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (set is null)
        {
            return NotFound();
        }

        return TypedResults.Ok(ToDto(set, await RootIdsAsync(db, set.Id, ct)));
    }

    private static async Task<Results<Created<SyncSetDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        SyncSetWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var refused = await RefuseBindingAsync(db, ctx, request, ct);
        if (refused is not null)
        {
            return refused;
        }

        var set = new SyncSet { Name = request.Name, OwnerUserId = ctx.UserId };
        Apply(set, request);
        db.SyncSets.Add(set);
        AddMembers(db, set.Id, request.RootFeatureIds);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/sync/sets/{set.Id}", ToDto(set, request.RootFeatureIds));
    }

    private static async Task<Results<Ok<SyncSetDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SyncSetWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await OwnedBy(db, ctx.UserId).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (set is null)
        {
            return NotFound();
        }

        var refused = await RefuseBindingAsync(db, ctx, request, ct);
        if (refused is not null)
        {
            return refused;
        }

        Apply(set, request);

        var existing = await db.SyncSetMembers.Where(m => m.SyncSetId == set.Id).ToListAsync(ct);
        var wanted = request.RootFeatureIds.ToHashSet();
        db.SyncSetMembers.RemoveRange(existing.Where(m => !wanted.Contains(m.RootFeatureId)));
        AddMembers(db, set.Id, [.. wanted.Except(existing.Select(m => m.RootFeatureId))]);

        // Only a write that actually moved something is a new revision. A device that reposts
        // the selection it already holds must not be told its own copy has gone stale, or every
        // idle resend would cost a full re-read.
        if (db.ChangeTracker.HasChanges())
        {
            set.Revision++;
        }

        // Two writers are ordinary here rather than exotic: the settings page and the caver's
        // own phone hold the same set, and both post the whole of it. Left alone they read the
        // same membership rows before either writes, compute the same additions and collide on
        // the one-root-once index — an unhandled database error, and a revision bumped once for
        // two different stored states, which is the single value the device compares against.
        // The loser is told so instead, in the same words for either collision, and retries.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is DbUpdateConcurrencyException || IsMembershipCollision(e))
        {
            return Conflict();
        }

        return TypedResults.Ok(ToDto(set, request.RootFeatureIds));
    }

    /// <summary>
    /// Whether a failed save is two writers adding the same root to the same set, rather than
    /// any other constraint. Matched on the index by name so a different violation is not
    /// reported as a lost race.
    /// </summary>
    private static bool IsMembershipCollision(Exception e) =>
        e is DbUpdateException { InnerException: PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_sync_set_members_set_root",
        } };

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var set = await OwnedBy(db, ctx.UserId).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (set is null)
        {
            return NotFound();
        }

        // Membership rows go with the set through the cascade; nothing that was synced is
        // touched, because a sync set never owned any of it.
        db.SyncSets.Remove(set);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody wrote the set between this request reading it and deleting it. Revoking is
            // the one act here that must not happen against a state its caller has not seen, so
            // it is answered rather than applied blind.
            return Conflict();
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// The whole visibility rule for this table, in one place: a sync set is per-account
    /// configuration and its owner is its only reader — administrators included. There is no
    /// domain to evaluate and no ruleset that reaches it, and a set somebody else owns is
    /// reported exactly like one that does not exist, so listing cannot be used to count them.
    /// </summary>
    private static IQueryable<SyncSet> OwnedBy(SilexGisDbContext db, Guid userId) =>
        db.SyncSets.Where(x => x.OwnerUserId == userId);

    private static ProblemHttpResult NotFound() => ApiProblems.NotFound("sync.set_not_found");

    private static ProblemHttpResult Conflict() => ApiProblems.Conflict(
        "sync.set_conflict", "This sync set was changed by another write. Read it again and re-send.");

    /// <summary>
    /// Refuses a write whose caving-group binding or named roots the caller has no business
    /// with. Returns null when the request may proceed.
    /// </summary>
    private static async Task<ProblemHttpResult?> RefuseBindingAsync(
        SilexGisDbContext db, AccessContext ctx, SyncSetWriteRequest request, CancellationToken ct)
    {
        if (request.CavingGroupId is { } groupId)
        {
            if (!await db.CavingGroups.AsNoTracking().AnyAsync(g => g.Id == groupId, ct))
            {
                return ApiProblems.BadRequest("sync.caving_group_not_found", "That caving group does not exist.");
            }

            // Narrower than the general binding rule on purpose. Binding a sync set to a group
            // hands nobody anything — nobody but the owner can read it — so the only question
            // worth asking is whether the caver is actually in the group whose rows they are
            // about to create, which is a question about their own memberships.
            if (!ctx.IsFullAdmin && !ctx.CavingGroupIds.Contains(groupId))
            {
                return ApiProblems.Forbidden(
                    "sync.caving_group_forbidden", "You are not a member of that caving group.");
            }
        }

        if (request.RootFeatureIds.Count == 0)
        {
            return null;
        }

        var ids = request.RootFeatureIds.ToArray();
        var readable = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => ids.Contains(f.Id))
            .Select(f => f.Id)
            .ToListAsync(ct);
        if (readable.Count != ids.Length)
        {
            // A root the caller cannot read is reported exactly like a missing one: telling
            // them apart would turn this endpoint into a way of asking whether a cave exists.
            return ApiProblems.BadRequest("sync.root_not_found", "A named root feature does not exist.");
        }

        return null;
    }

    private static void AddMembers(SilexGisDbContext db, Guid syncSetId, IReadOnlyList<Guid> rootIds)
    {
        db.SyncSetMembers.AddRange(rootIds.Select(rootId => new SyncSetMember
        {
            SyncSetId = syncSetId,
            RootFeatureId = rootId,
        }));
    }

    private static async Task<List<Guid>> RootIdsAsync(SilexGisDbContext db, Guid syncSetId, CancellationToken ct) =>
        await db.SyncSetMembers.AsNoTracking()
            .Where(m => m.SyncSetId == syncSetId)
            .Select(m => m.RootFeatureId)
            .ToListAsync(ct);

    private static void Apply(SyncSet set, SyncSetWriteRequest request)
    {
        set.Name = request.Name;
        set.CavingGroupId = request.CavingGroupId;
        set.UploadVisibility = request.UploadVisibility;

        // Always an object by the time it gets here — validation refuses anything else,
        // an absent property included, so there is no "absent means empty" case to mistake
        // for an edit that cleared the document.
        var text = request.Settings.GetRawText();

        // Compared as documents, never as text. The database normalises a JSON document on the
        // way in — key order and whitespace both move — so the stored string is essentially
        // never byte-identical to what a device sent, and a text comparison would make every
        // resend of an unchanged document look like an edit and bump the revision that tells
        // the device whether its copy is stale.
        using var incoming = JsonDocument.Parse(text);
        using var stored = JsonDocument.Parse(set.Settings);
        if (!JsonElement.DeepEquals(stored.RootElement, incoming.RootElement))
        {
            set.Settings = text;
        }
    }

    private static SyncSetDto ToDto(SyncSet set, IReadOnlyList<Guid> rootFeatureIds) => new(
        set.Id,
        set.Name,
        set.CavingGroupId,
        set.UploadVisibility,
        rootFeatureIds,
        JsonSerializer.Deserialize<JsonElement>(set.Settings),
        set.Revision,
        set.CreatedAt,
        set.UpdatedAt);
}
