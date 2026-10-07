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

/// <summary>One member trip a camp's sharing did not reach, which the caller may be told about.</summary>
/// <remarks>
/// Only ever built for a trip the caller may read — the title is on it, and a title is exactly
/// what a refusal must not hand to somebody who cannot open the trip.
/// </remarks>
public sealed record ExpeditionCascadeSkip(Guid TripId, string Title, CascadeSkipReason Reason);

/// <summary>
/// What one application of a camp's sharing came to. <see cref="Problem"/> set means nothing
/// was written at all.
/// </summary>
/// <param name="Problem">The refusal, or null when the cascade may be saved.</param>
/// <param name="Trips">Member trips the camp gathered when this ran.</param>
/// <param name="Shared">Member trips that took the sharing — every rule of it, or already had.</param>
/// <param name="Written">Rules staged as new rows.</param>
/// <param name="Updated">Rules this camp had already written here, restated with new actions.</param>
/// <param name="Unchanged">Rules this camp had already written here, exactly as asked.</param>
/// <param name="Skipped">The trips that did not take it and that the caller may read.</param>
/// <param name="SkippedNotNamed">
/// How many more did not take it. A number only: these are trips the caller may not read.
/// </param>
public sealed record ExpeditionCascadeOutcome(
    ProblemHttpResult? Problem,
    int Trips,
    int Shared,
    int Written,
    int Updated,
    int Unchanged,
    IReadOnlyList<ExpeditionCascadeSkip> Skipped,
    int SkippedNotNamed)
{
    /// <summary>How many member trips did not take the sharing, named or not.</summary>
    public int SkippedTrips => Skipped.Count + SkippedNotNamed;
}

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
/// <b>Trip by trip.</b> A member trip takes the whole of the sharing or none of it, and one
/// trip refusing does not refuse the others: a camp exists to gather other people's trips, and
/// an organiser who owns nine of ten should not have to wait on the tenth's owner to share the
/// nine. What refuses a trip has not moved — the granter must be able to administer that
/// trip's rules, and may hand out there no more than they hold there. The trips that refused
/// are reported: the ones the granter may read by name, the rest as a count, by the one rule
/// that decides what a skipped row may disclose. Applying again later picks a skipped trip up
/// once its owner has delegated.
/// </para>
/// <para>
/// <b>Nothing reached is still a refusal.</b> The rules of a camp's sharing are the rows on
/// its member trips and are stored nowhere else — nothing is written on the camp, whose own
/// readers are decided on the camp's own permissions tab. So a sharing every trip refused
/// would store nothing at all, and saying it had succeeded would leave a camp that claims a
/// grant it has no row of and cannot apply again. Rows are staged and added only once every
/// member trip has answered, so that refusal leaves the change tracker as it found it.
/// </para>
/// <para>
/// <b>Trips that join later are not covered.</b> This runs when somebody asks it to and reads
/// membership as it stands at that moment; a trip added afterwards carries no rule from it
/// until somebody applies the camp's sharing again — and is then taken or skipped by the same
/// per-trip rule as every other. Automatic coverage would mean a grant nobody performed, which
/// is precisely what an audit trail cannot account for.
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
        // counted among the skipped rather than left out of the answer, and of such a trip it
        // is the count alone that leaves this method. Ordered, so the trips an answer names
        // come in the order the camp's own page lists them rather than in whatever order the
        // table gave them up.
        var memberIds = await db.ExpeditionTrips.AsNoTracking()
            .Where(m => m.ExpeditionId == expeditionId)
            .Select(m => m.TripLogId)
            .ToListAsync(ct);
        var trips = await db.TripLogs.AsNoTracking()
            .Where(t => memberIds.Contains(t.Id))
            .OrderBy(t => t.TripDate).ThenBy(t => t.Id)
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
        var shared = 0;
        var skipped = new List<(ExpeditionCascadeSkip Row, bool CallerMayRead)>();

        foreach (var trip in trips)
        {
            // What this trip would have written onto it: the rules it does not carry yet, and
            // the ones it carries with other actions.
            var writes = new List<(ExpeditionShareEntryWrite Write, AccessEntry? Mine)>();
            foreach (var write in entries)
            {
                var mine = alreadyMine.Find(e => e.ScopeId == trip.Id
                    && e.SubjectKind == write.SubjectKind
                    && e.SubjectId == write.SubjectId
                    && e.Effect == write.Effect);
                if (mine is not null && mine.Actions == write.Actions)
                {
                    unchanged++;
                }
                else
                {
                    writes.Add((write, mine));
                }
            }

            // A trip that already carries all of it is covered, whoever is asking: nothing is
            // written, so there is no authority to ask for, and calling it skipped would have
            // the answer contradict the coverage the camp reports for the very same rows.
            if (writes.Count == 0)
            {
                shared++;
                continue;
            }

            var asked = writes.ConvertAll(x => x.Write);
            if (await RefusalOfAsync(db, access, ctx, expeditionId, trip, asked, ct) is { } reason)
            {
                // The rules it already carried exactly were counted above and stay as they
                // are; the trip as a whole did not take what was asked of it.
                //
                // Whether the granter may read the trip decides whether the answer may name
                // it, and is asked of the trip itself, the way opening it would be.
                var mayRead = (await access.DecideAsync(ctx, AccessAction.Read, trip, ct)).Allowed;
                skipped.Add((new ExpeditionCascadeSkip(trip.Id, trip.Title, reason), mayRead));
                continue;
            }

            shared++;
            foreach (var (write, mine) in writes)
            {
                if (mine is null)
                {
                    staged.Add(ToEntity(trip, write, ctx.UserId, expeditionId));
                }
                else
                {
                    restated.Add((mine, write.Actions));
                }
            }
        }

        // No trip took it, so there is nothing to write and nowhere for the sharing to live:
        // the camp holds no rule of its own. A refusal, built from the count and nothing else.
        if (shared == 0)
        {
            return Refused(Incomplete(skipped.Count));
        }

        db.AccessEntries.AddRange(staged);
        foreach (var (row, actions) in restated)
        {
            row.Actions = actions;
        }

        // The titles of the trips the granter may not read stop here: what leaves is the rows
        // the rule hands back to be named, and a number.
        var disclosed = AccessCascadeRules.Disclose(skipped);
        return new ExpeditionCascadeOutcome(
            null, trips.Count, shared, staged.Count, restated.Count, unchanged,
            disclosed.Named, disclosed.NotNamed);
    }

    /// <summary>
    /// Why this trip refuses the camp's sharing, or null when it takes it — because the granter
    /// may not write rules on it at all, or because some rule would hand out more than they
    /// hold there. The reason reaches the caller only for a trip they may read; for one they
    /// may not, neither the trip nor the reason leaves the writer.
    /// </summary>
    private static async Task<CascadeSkipReason?> RefusalOfAsync(
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
            return CascadeSkipReason.NotAdministered;
        }

        // The whole sharing or none of it on any one trip: a trip carrying two of a camp's
        // three rules would make "this camp is shared with them" mean something different on
        // every trip, and the coverage a rule reports could no longer be read as trips.
        foreach (var write in entries)
        {
            var candidate = ToEntity(trip, write, ctx.UserId, expeditionId);
            if (await AccessEntryMapping.RejectAsync(db, access, ctx, candidate, ct) is not null)
            {
                return CascadeSkipReason.BeyondHolding;
            }
        }

        return null;
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
    /// The refusal of a sharing no trip took, carrying the count as a field of its own so a
    /// client can say it in its own words rather than parsing the sentence. Nothing else about
    /// the refused trips is here, and nothing else may be added.
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
        new(problem, 0, 0, 0, 0, 0, [], 0);
}
