// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

/// <summary>One rule a camp's sharing holds, and how much of the camp currently carries it.</summary>
/// <param name="Trips">
/// Member trips carrying this rule right now. Fewer than the camp has means trips joined after
/// the sharing was last applied — re-applying covers them.
/// </param>
public sealed record ExpeditionSharedRuleDto(
    AccessSubjectKind SubjectKind,
    Guid SubjectId,
    string? SubjectName,
    AccessEffect Effect,
    AccessAction Actions,
    int Trips);

/// <summary>What a camp's sharing comes to: its rules, and how many trips it has to reach.</summary>
public sealed record ExpeditionSharingDto(int MemberTrips, IReadOnlyList<ExpeditionSharedRuleDto> Rules);

/// <summary>
/// Sharing a camp, which reaches the trips the camp gathered: one rule written onto each of
/// them, marked with the camp that wrote it.
/// </summary>
/// <remarks>
/// <para>
/// Three deliberate acts rather than one settled state, because that is what an account of who
/// may reach what needs to be able to show. Applying adds and restates; re-applying carries the
/// same rules onto trips that joined since, which nothing does by itself; withdrawing removes
/// exactly the rows this camp wrote and touches nothing else. Each writes a row of its own on
/// the camp's trail, so the act is legible even when the rules it wrote are not new.
/// </para>
/// <para>
/// Every one of them takes the right to administer the camp's rules — the same right the camp's
/// own permissions tab takes — and each rule is then bounded again at the trip it lands on. The
/// camp is where a person asks; each trip is what answers.
/// </para>
/// </remarks>
public static class ExpeditionSharingEndpoints
{
    private const string NotFoundCode = "expedition.not_found";

    /// <summary>A camp asked to re-apply sharing it does not have.</summary>
    private const string NothingSharedCode = "expedition_sharing.nothing_shared";

    public static RouteGroupBuilder MapExpeditionSharingEndpoints(this RouteGroupBuilder api)
    {
        var sharing = api.MapGroup("/expeditions/{id:guid}/sharing").WithTags("Permissions");

        sharing.MapGet("/", ReadAsync)
            .WithSummary("What this camp's sharing grants, and how many of its trips carry it.");
        sharing.MapPost("/", ApplyAsync).WithValidation<ExpeditionShareRequest>()
            .WithSummary(
                "Shares this camp: one rule onto every trip it gathers, bounded at each trip by "
                + "what the caller holds there. Adds and restates; never removes.");
        sharing.MapPost("/re-apply", ReapplyAsync)
            .WithSummary(
                "Carries this camp's sharing onto the trips that joined since it was applied. "
                + "A trip joining is not covered by itself — this is the act that covers it.");
        sharing.MapDelete("/", WithdrawAsync)
            .WithSummary(
                "Withdraws every rule this camp's sharing wrote, and only those: a rule of the "
                + "same shape authored on a trip's own tab stays.");

        return api;
    }

    private static async Task<Results<Ok<ExpeditionSharingDto>, UnauthorizedHttpResult, ProblemHttpResult>> ReadAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var (expedition, problem) = await ResolveAsync(db, access, ctx, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        return TypedResults.Ok(await SharingAsync(db, user, expedition!.Id, ct));
    }

    private static async Task<Results<Ok<ExpeditionSharingDto>, UnauthorizedHttpResult, ProblemHttpResult>> ApplyAsync(
        Guid id,
        ExpeditionShareRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var (expedition, problem) = await ResolveAsync(db, access, ctx, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var outcome = await ExpeditionAccessCascade.StageAsync(db, access, ctx, id, request.Entries, ct);
        if (outcome.Problem is not null)
        {
            return outcome.Problem;
        }

        Record(db, currentUser, expedition!.Id, "applied", outcome);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await SharingAsync(db, user, expedition.Id, ct));
    }

    /// <summary>
    /// The camp's sharing as it now stands, applied again over the membership as it now stands.
    /// </summary>
    /// <remarks>
    /// Coverage is a deliberate act somebody performs. A trip joining a camp does not inherit
    /// the camp's sharing on its own: a grant nobody performed is a grant nobody can be asked
    /// about, and the trip's owner would find their trip shared by a membership change. So the
    /// rules stay where they were written and somebody says, in as many words, "and the ones
    /// that joined since" — which is a line in the trail with a person's name on it.
    /// </remarks>
    private static async Task<Results<Ok<ExpeditionSharingDto>, UnauthorizedHttpResult, ProblemHttpResult>> ReapplyAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var (expedition, problem) = await ResolveAsync(db, access, ctx, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var entries = await SharedRulesAsync(db, id, ct);
        if (entries.Count == 0)
        {
            return ApiProblems.Conflict(NothingSharedCode, "This camp shares nothing to re-apply.");
        }

        var outcome = await ExpeditionAccessCascade.StageAsync(db, access, ctx, id, entries, ct);
        if (outcome.Problem is not null)
        {
            return outcome.Problem;
        }

        Record(db, currentUser, expedition!.Id, "re-applied", outcome);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await SharingAsync(db, user, expedition.Id, ct));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> WithdrawAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (expedition, problem) = await ResolveAsync(db, access, ctx, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        // The marker, and nothing but the marker. Matching instead on subject, effect, actions
        // and reach would take with it a rule of the same shape somebody authored on a trip's
        // own tab, and they would never learn why the access they granted went away.
        //
        // No bound is asked for at each trip on the way out: withdrawing only ever takes access
        // away, so there is nothing here anybody could amplify. Loaded and removed rather than
        // deleted in one statement, so the trail carries the withdrawal.
        var written = await db.AccessEntries
            .Where(e => e.GrantedViaExpeditionId == expedition!.Id)
            .ToListAsync(ct);
        db.AccessEntries.RemoveRange(written);

        Record(db, currentUser, expedition!.Id, "withdrawn",
            new ExpeditionCascadeOutcome(null, 0, 0, 0, 0), withdrawn: written.Count);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The camp, and the right to administer its rules. A camp the caller cannot read answers
    /// the same way as one that is not there, which is how every other refusal in this
    /// application declines to say whether a thing exists.
    /// </summary>
    private static async Task<(Expedition? Expedition, ProblemHttpResult? Problem)> ResolveAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid id, CancellationToken ct)
    {
        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (expedition is null)
        {
            return (null, ApiProblems.NotFound(NotFoundCode));
        }

        if ((await access.DecideAsync(ctx, AccessAction.ManagePermissions, expedition, ct)).Allowed)
        {
            return (expedition, null);
        }

        return (null, (await access.DecideAsync(ctx, AccessAction.Read, expedition, ct)).Allowed
            ? ApiProblems.Forbidden()
            : ApiProblems.NotFound(NotFoundCode));
    }

    /// <summary>
    /// The distinct rules this camp's sharing holds, read back from the rows it wrote. Where
    /// one subject and effect appear with more than one action set — which the routes here
    /// cannot produce, but a repaired database could — the rule written last is the camp's.
    /// </summary>
    private static async Task<List<ExpeditionShareEntryWrite>> SharedRulesAsync(
        SilexGisDbContext db, Guid expeditionId, CancellationToken ct)
    {
        var rows = await db.AccessEntries.AsNoTracking()
            .Where(e => e.GrantedViaExpeditionId == expeditionId && e.SubjectKind != null)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        return
        [
            .. rows
                .GroupBy(e => (e.SubjectKind!.Value, e.SubjectId!.Value, e.Effect))
                .Select(g => new ExpeditionShareEntryWrite(
                    g.Key.Item1, g.Key.Item2, g.Key.Item3, g.Last().Actions)),
        ];
    }

    private static async Task<ExpeditionSharingDto> SharingAsync(
        SilexGisDbContext db, UserContext user, Guid expeditionId, CancellationToken ct)
    {
        var rows = await db.AccessEntries.AsNoTracking()
            .Where(e => e.GrantedViaExpeditionId == expeditionId && e.SubjectKind != null)
            .ToListAsync(ct);
        var memberTrips = await db.ExpeditionTrips.AsNoTracking()
            .CountAsync(m => m.ExpeditionId == expeditionId, ct);

        var userIds = rows.Where(x => x.SubjectKind == AccessSubjectKind.User)
            .Select(x => x.SubjectId!.Value).Distinct().ToList();
        var cavingGroupIds = rows.Where(x => x.SubjectKind == AccessSubjectKind.CavingGroup)
            .Select(x => x.SubjectId!.Value).Distinct().ToList();
        // Resolved rather than projected: the label a subject may be shown under is a rule
        // with one home, and it is never their address.
        var userNames = await ProfileDirectory.ResolveLabelsAsync(db, user, userIds, ct);
        var cavingGroupNames = await db.CavingGroups.AsNoTracking()
            .Where(g => cavingGroupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        var rules = rows
            .GroupBy(e => (e.SubjectKind!.Value, e.SubjectId!.Value, e.Effect, e.Actions))
            .Select(g => new ExpeditionSharedRuleDto(
                g.Key.Item1,
                g.Key.Item2,
                g.Key.Item1 == AccessSubjectKind.User
                    ? userNames.GetValueOrDefault(g.Key.Item2)
                    : cavingGroupNames.GetValueOrDefault(g.Key.Item2),
                g.Key.Item3,
                g.Key.Item4,
                g.Select(e => e.ScopeId).Distinct().Count()))
            .OrderBy(r => r.SubjectKind).ThenBy(r => r.SubjectName ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        return new ExpeditionSharingDto(memberTrips, rules);
    }

    /// <summary>
    /// The act itself, on the camp's trail. The rules the act wrote carry their own rows and
    /// point back here, but an act that restated what was already in place writes none of them
    /// — and "somebody re-applied this camp's sharing and it changed nothing" is exactly the
    /// line a person reconstructing who granted what needs to be able to read.
    /// </summary>
    private static void Record(
        SilexGisDbContext db,
        ICurrentUser currentUser,
        Guid expeditionId,
        string act,
        ExpeditionCascadeOutcome outcome,
        int withdrawn = 0)
    {
        db.Set<AuditEntry>().Add(new AuditEntry
        {
            UserId = currentUser.UserId,
            Action = AuditActions.PermissionChanged,
            EntityType = nameof(Expedition),
            EntityId = expeditionId.ToString(),
            Changes = JsonSerializer.Serialize(
                new Dictionary<string, Dictionary<string, object?>>
                {
                    ["Sharing"] = new() { ["old"] = null, ["new"] = act },
                    ["Trips"] = new() { ["old"] = null, ["new"] = outcome.Trips },
                    ["RulesWritten"] = new() { ["old"] = null, ["new"] = outcome.Written },
                    ["RulesRestated"] = new() { ["old"] = null, ["new"] = outcome.Updated },
                    ["RulesWithdrawn"] = new() { ["old"] = null, ["new"] = withdrawn },
                }),
        });
    }
}
