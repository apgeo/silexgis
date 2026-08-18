// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Permissions;

/// <summary>One rule a camp's sharing writes onto every trip the camp gathered.</summary>
/// <remarks>
/// No reach on the wire: a camp's sharing writes at object reach onto each member trip and
/// nowhere else, so offering the choice would only invite a caller to ask for one that is
/// refused. Rules on the camp itself are authored on the camp's own permissions tab.
/// </remarks>
public sealed record ExpeditionShareEntryWrite(
    AccessSubjectKind SubjectKind,
    Guid SubjectId,
    AccessEffect Effect,
    AccessAction Actions);

public sealed record ExpeditionShareRequest(IReadOnlyList<ExpeditionShareEntryWrite> Entries);

public sealed class ExpeditionShareRequestValidator : AbstractValidator<ExpeditionShareRequest>
{
    public ExpeditionShareRequestValidator()
    {
        RuleFor(x => x.Entries).NotEmpty()
            .WithMessage("Sharing a camp needs at least one rule.");
        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(x => x.SubjectId).NotEmpty();
            entry.RuleFor(x => x.SubjectKind).IsInEnum();
            entry.RuleFor(x => x.Effect).IsInEnum();
            entry.RuleFor(x => x.Actions)
                .Must(a => a != AccessAction.None)
                .WithMessage("A rule needs at least one action.");

            // Refused where the request is read, rather than dropped where the rows are
            // built: an action that may never travel with a cascade is a rule, and a rule
            // somebody has to remember to apply is one somebody eventually will not.
            entry.RuleFor(x => x.Actions)
                .Must(a => !AccessCascadeRules.CarriesNeverCascaded(a))
                .WithErrorCode(AccessCascadeRules.ExactLocationRefusedCode)
                .WithMessage(AccessCascadeRules.ExactLocationRefusedMessage);
        });
        RuleFor(x => x.Entries)
            .Must(e => e is null
                || e.Select(x => (x.SubjectKind, x.SubjectId, x.Effect)).Distinct().Count() == e.Count)
            .WithMessage("Duplicate rules for the same subject and effect.");
    }
}

/// <summary>
/// What one application of a camp's sharing came to. <see cref="Problem"/> set means nothing
/// was written at all.
/// </summary>
/// <param name="Problem">The refusal, or null when the cascade may be saved.</param>
/// <param name="Trips">Member trips the camp gathered when this ran.</param>
/// <param name="Written">Rules staged as new rows.</param>
/// <param name="Updated">Rules this camp had already written here, restated with new actions.</param>
/// <param name="Unchanged">Rules this camp had already written here, exactly as asked.</param>
public sealed record ExpeditionCascadeOutcome(
    ProblemHttpResult? Problem,
    int Trips,
    int Written,
    int Updated,
    int Unchanged);

/// <summary>
/// Sharing a camp, reaching the trips in it: one object-scoped rule per member trip, marked
/// with the camp that wrote it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Additive, and that is the whole reason this exists.</b> The other writer of per-object
/// rules is a full replace — it reads every direct rule on the object, removes them, and
/// writes the set it was handed. Driving that once per member trip would delete each trip's
/// own hand-authored rules as a side effect of somebody sharing a camp those trips happen to
/// belong to. Nothing here removes anything; the only rows it ever changes are ones this same
/// camp wrote earlier.
/// </para>
/// <para>
/// <b>Every row still passes the shared gate</b>, unchanged and one row at a time: the
/// scope-validity table, the existence of the trip it is anchored on, and the no-amplification
/// bound evaluated against <em>that trip's own facts</em>. Evaluating the bound at the camp
/// instead would be the amplification the whole model is built to refuse — the camp's owner
/// holds every action on the camp by owning it, and reading that as authority over a trip
/// somebody else owns is exactly how "may manage sharing" turns into full disclosure.
/// </para>
/// <para>
/// <b>All-or-nothing.</b> Rows are staged and added only once every member trip has answered,
/// so a refusal leaves the change tracker as it found it and there is no half-shared camp for
/// anybody to reason about. The refusal carries a count and never a name — see the rule it
/// calls for why.
/// </para>
/// <para>
/// <b>Trips that join later are not covered.</b> This runs when somebody asks it to and reads
/// membership as it stands at that moment; a trip added afterwards carries no rule from it
/// until somebody applies the camp's sharing again. Automatic coverage would mean a grant
/// nobody performed, which is precisely what an audit trail cannot account for.
/// </para>
/// </remarks>
public static class ExpeditionAccessCascade
{
    /// <summary>A camp asked to share what it gathers, gathering nothing.</summary>
    /// <remarks>
    /// The rules of a camp's sharing are the rows on its member trips and are stored nowhere else,
    /// so a camp with no trips has nowhere to put them. Answering such a request successfully
    /// would store nothing while saying it had — and the camp would then refuse to re-apply,
    /// having no rules to read back, so the natural order of work (make the camp, share it, gather
    /// the trips) would lose the grant with nothing anywhere to say it had gone.
    /// </remarks>
    public const string NoMemberTripsCode = "expedition_sharing.no_trips";

    /// <summary>
    /// Stages the camp's sharing across its member trips. Saves nothing: the caller owns the
    /// unit of work, so the rules and whatever else the same act writes land together or not
    /// at all.
    /// </summary>
    public static async Task<ExpeditionCascadeOutcome> StageAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        Guid expeditionId,
        IReadOnlyList<ExpeditionShareEntryWrite> entries,
        CancellationToken ct)
    {
        // Restated here and not only at the validator. The validator guards the route; this
        // guards the rule, and a second caller written later would otherwise reach the rows
        // without passing the first one.
        if (entries.Any(e => AccessCascadeRules.CarriesNeverCascaded(e.Actions)))
        {
            return Refused(ApiProblems.BadRequest(
                AccessCascadeRules.ExactLocationRefusedCode,
                AccessCascadeRules.ExactLocationRefusedMessage));
        }

        foreach (var entry in entries)
        {
            var exists = entry.SubjectKind == AccessSubjectKind.User
                ? (await ProfileDirectory.ExistingIdsAsync(db, [entry.SubjectId], ct)).Count > 0
                : await db.CavingGroups.AnyAsync(g => g.Id == entry.SubjectId, ct);
            if (!exists)
            {
                return Refused(ApiProblems.BadRequest(
                    "access.subject_unknown", "A rule's subject does not exist."));
            }
        }

        // Every member trip, with no visibility filter: one the granter cannot read has to be
        // counted among the refusals rather than quietly skipped, and it is the count alone
        // that leaves this method.
        var memberIds = await db.ExpeditionTrips.AsNoTracking()
            .Where(m => m.ExpeditionId == expeditionId)
            .Select(m => m.TripLogId)
            .ToListAsync(ct);
        var trips = await db.TripLogs.AsNoTracking()
            .Where(t => memberIds.Contains(t.Id))
            .ToListAsync(ct);
        if (trips.Count == 0)
        {
            return Refused(ApiProblems.Conflict(
                NoMemberTripsCode,
                "This camp gathers no trips yet, so there is nothing for its sharing to be "
                + "written onto. Add the trips first, then share the camp."));
        }

        // Tracked: a restatement of a rule this same camp wrote earlier is an edit of that
        // row, and an edit that never reaches the change tracker is one the trail cannot show.
        var alreadyMine = await db.AccessEntries
            .Where(e => e.GrantedViaExpeditionId == expeditionId)
            .ToListAsync(ct);

        var staged = new List<AccessEntry>();
        var restated = new List<(AccessEntry Row, AccessAction Actions)>();
        var unchanged = 0;
        var refusedTrips = 0;

        foreach (var trip in trips)
        {
            if (await RefusesAsync(db, access, ctx, expeditionId, trip, entries, ct))
            {
                refusedTrips++;
                continue;
            }

            foreach (var write in entries)
            {
                var mine = alreadyMine.Find(e => e.ScopeId == trip.Id
                    && e.SubjectKind == write.SubjectKind
                    && e.SubjectId == write.SubjectId
                    && e.Effect == write.Effect);
                if (mine is null)
                {
                    staged.Add(ToEntity(trip, write, ctx.UserId, expeditionId));
                }
                else if (mine.Actions != write.Actions)
                {
                    restated.Add((mine, write.Actions));
                }
                else
                {
                    unchanged++;
                }
            }
        }

        if (refusedTrips > 0)
        {
            return Refused(Incomplete(refusedTrips));
        }

        db.AccessEntries.AddRange(staged);
        foreach (var (row, actions) in restated)
        {
            row.Actions = actions;
        }

        return new ExpeditionCascadeOutcome(null, trips.Count, staged.Count, restated.Count, unchanged);
    }

    /// <summary>
    /// Whether this trip refuses the camp's sharing — because the granter may not write rules
    /// on it at all, or because some rule would hand out more than they hold there. True is
    /// the whole answer: which trip it was, and which of the two reasons applied, are both
    /// things the caller must not learn.
    /// </summary>
    private static async Task<bool> RefusesAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        Guid expeditionId,
        TripLog trip,
        IReadOnlyList<ExpeditionShareEntryWrite> entries,
        CancellationToken ct)
    {
        // Administering a trip's rules is the same right here as on the trip's own permissions
        // tab. Without it, "I may read your trip" would silently become "I may hand your trip
        // to whoever I like", which it is nowhere else in this application.
        if (!(await access.DecideAsync(ctx, AccessAction.ManagePermissions, trip, ct)).Allowed)
        {
            return true;
        }

        foreach (var write in entries)
        {
            var candidate = ToEntity(trip, write, ctx.UserId, expeditionId);
            if (await AccessEntryMapping.RejectAsync(db, access, ctx, candidate, ct) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static AccessEntry ToEntity(
        TripLog trip, ExpeditionShareEntryWrite write, Guid grantedBy, Guid expeditionId) => new()
        {
            SubjectKind = write.SubjectKind,
            SubjectId = write.SubjectId,
            Effect = write.Effect,
            Domain = AccessDomains.Of(trip),
            Actions = write.Actions,
            ScopeKind = AccessScopeKind.Object,
            ScopeId = trip.Id,
            GrantedBy = grantedBy,
            GrantedViaExpeditionId = expeditionId,
        };

    /// <summary>
    /// The refusal, carrying the count as a field of its own so a client can say it in its own
    /// words rather than parsing the sentence. Nothing else about the refused trips is here,
    /// and nothing else may be added.
    /// </summary>
    private static ProblemHttpResult Incomplete(int refusedTrips) =>
        TypedResults.Problem(
            detail: AccessCascadeRules.IncompleteDetail(refusedTrips),
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = AccessCascadeRules.IncompleteCode,
                ["refusedTripCount"] = refusedTrips,
            });

    private static ExpeditionCascadeOutcome Refused(ProblemHttpResult problem) =>
        new(problem, 0, 0, 0, 0);
}
