// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>
/// Who was at a camp, and for which days.
/// </summary>
/// <remarks>
/// <para>
/// The camp's own roster, which is not the club's directory of people and not the list of who was
/// on its trips. It is kept rather than derived because the two would differ in exactly the people
/// it exists for: the cook, the driver and whoever kept the base camp were there for the fortnight
/// and went underground on none of it.
/// </para>
/// <para>
/// Every route here is a sub-resource of one camp, and the camp is what governs them: reading the
/// roster takes the right to read the camp, and every write takes the right to write it, through
/// the same ladder the camp's other writes use. A row has no separate governance of its own.
/// </para>
/// <para>
/// <strong>No route here accepts a person as a filter, and none ever should.</strong> A field
/// answering "which days was this named person somewhere" is a movement record assembled out of
/// rows the asker may not otherwise read. The trips refuse the same question for the same reason,
/// and the argument is stronger here: a trip is an afternoon and a stay is a fortnight.
/// </para>
/// </remarks>
public static class ExpeditionRosterEndpoints
{
    /// <summary>A stay this camp does not have.</summary>
    private const string EntryNotFoundCode = "expedition_roster.not_found";

    /// <summary>A role id no row of the camp-roster vocabulary carries.</summary>
    private const string RoleUnknownCode = "expedition_roster.role_unknown";

    /// <summary>
    /// An entry the club's directory does not hold. Only ever said of a person named by their
    /// entry: one named by a name is whoever that name turns out to mean, and cannot be unknown.
    /// </summary>
    private const string CaverUnknownCode = "expedition_roster.caver_unknown";

    /// <summary>
    /// The camp may be read, but the caller may not read people — so who was at it is withheld,
    /// names, dates and count together.
    /// </summary>
    private const string PeopleUnreadableCode = "expedition_roster.people_unreadable";

    public static RouteGroupBuilder MapExpeditionRosterEndpoints(this RouteGroupBuilder api)
    {
        // Mounted under the camp because the camp is what a stay belongs to and what decides who
        // may see it. The segment reads as this camp's roster and never as the club's directory,
        // which is what the word means everywhere else here.
        var roster = api.MapGroup("/expeditions/{expeditionId:guid}/roster").WithTags("Expeditions");

        roster.MapGet("/", ListAsync)
            .WithSummary(
                "Everybody recorded as having been at this camp, with the days of each stay and "
                + "how many people that comes to. Takes the right to read the camp and the right "
                + "to read people. Accepts no filter by person.");
        roster.MapPost("/", CreateAsync).WithValidation<ExpeditionRosterEntryWriteRequest>()
            .WithSummary(
                "Records that somebody was at this camp for a stretch of days (Write permission "
                + "on the camp). The person is named by their entry in the directory or by a "
                + "name, never both; a name already in the directory means that person and a "
                + "new one adds them. Stays may overlap and one person may have several.");
        roster.MapPut("/{entryId:long}", UpdateAsync).WithValidation<ExpeditionRosterEntryWriteRequest>()
            .WithSummary(
                "Rewrites one recorded stay whole (Write permission on the camp), naming the "
                + "person exactly as when recording one.");
        roster.MapDelete("/{entryId:long}", DeleteAsync)
            .WithSummary("Removes one recorded stay (Write permission on the camp).");

        return api;
    }

    /// <summary>
    /// The camp's roster, with its people counted distinctly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two rights, not one: the right to read this camp <em>and</em> the right to read people. A
    /// trip's own list of people is served on the trip's read alone, but a camp is routinely
    /// readable by a wider audience than the trips gathered into it, and a stay is a fortnight
    /// where a trip is an afternoon — so following the trip exactly would disclose more of where
    /// somebody was, to more people, than the trip ever does. The narrower of the two rules is
    /// taken deliberately. On a default installation this withholds nothing, because every account
    /// holds the read over people; it is an installation that has taken that read away which is
    /// then also not shown who was at a camp, which is the answer such an installation asked for.
    /// </para>
    /// <para>
    /// A caller with no account is refused outright rather than served rows with blank names. The
    /// whole of this API already requires a sign-in, so today that changes nothing — it is here so
    /// that opening some route to a token later cannot quietly publish where people were.
    /// </para>
    /// <para>
    /// Unpaged and ordered by the day the stay began, with the key as the tie-break — two stays
    /// beginning on the same day are ordinary, and an order that cannot tell them apart leaves the
    /// database to choose.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<ExpeditionRosterDto>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        Guid expeditionId,
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

        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expeditionId, ct);
        if (expedition is null || !(await access.DecideAsync(ctx, AccessAction.Read, expedition, ct)).Allowed)
        {
            return ApiProblems.NotFound(ExpeditionEndpoints.NotFoundCode);
        }

        // Refused rather than answered blank: the camp is readable and the caller has been told so
        // by every other route on it, so hiding behind "no such camp" here would be a lie, and rows
        // with the names struck out would still say how many people were there and when.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Cavers, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden(PeopleUnreadableCode);
        }

        var rows = await db.ExpeditionRoster.AsNoTracking()
            .Where(x => x.ExpeditionId == expeditionId)
            .OrderBy(x => x.FromDate).ThenBy(x => x.Id)
            .ToListAsync(ct);

        // Resolved rather than joined: what a person may be shown as is a rule with one home, and
        // an account's own label wins there so nobody appears twice under two names.
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, rows.Select(x => x.CaverId), ct);

        // Distinct by person, not by row: the roster holds one row per person per role, so
        // somebody who cooked and drove is two rows and one person.
        var people = rows.Select(x => x.CaverId).Distinct().Count();

        return TypedResults.Ok(new ExpeditionRosterDto
        {
            ExpeditionId = expeditionId,
            Entries = [.. rows.Select(row => Map(row, labels))],
            People = people,
        });
    }

    private static async Task<Results<Created<ExpeditionRosterEntryDto>, ProblemHttpResult>> CreateAsync(
        Guid expeditionId,
        ExpeditionRosterEntryWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expeditionId, ct);
        if (expedition is null)
        {
            return ApiProblems.NotFound(ExpeditionEndpoints.NotFoundCode);
        }

        if (await ExpeditionEndpoints.RefuseUnlessWritableAsync(access, ctx, expedition, ct) is { } refusal)
        {
            return refusal;
        }

        if (await ValidateReferencesAsync(db, request, ct) is { } problem)
        {
            return problem;
        }

        var entry = new ExpeditionRosterEntry { ExpeditionId = expeditionId };
        Apply(entry, request, await PersonOfAsync(db, request, ct));
        db.ExpeditionRoster.Add(entry);

        // One save for the stay and for anybody its name added to the directory, so the two
        // land together or not at all.
        await db.SaveChangesAsync(ct);

        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, [entry.CaverId], ct);
        return TypedResults.Created(
            $"/api/v1/expeditions/{expeditionId}/roster/{entry.Id}", Map(entry, labels));
    }

    /// <summary>
    /// Rewrites one stay whole.
    /// </summary>
    /// <remarks>
    /// No precondition is demanded, exactly as none is on the camp's membership rows: a child row
    /// is not versioned against its parent, and demanding the camp's version to correct the day
    /// somebody arrived would refuse the ordinary edit made from a list nobody loaded a version of.
    /// </remarks>
    private static async Task<Results<Ok<ExpeditionRosterEntryDto>, ProblemHttpResult>> UpdateAsync(
        Guid expeditionId,
        long entryId,
        ExpeditionRosterEntryWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expeditionId, ct);
        if (expedition is null)
        {
            return ApiProblems.NotFound(ExpeditionEndpoints.NotFoundCode);
        }

        // The camp is asked before the row is looked for, so somebody with no right to the camp
        // never learns from a refusal whether a given stay is recorded against it.
        if (await ExpeditionEndpoints.RefuseUnlessWritableAsync(access, ctx, expedition, ct) is { } refusal)
        {
            return refusal;
        }

        var entry = await db.ExpeditionRoster
            .FirstOrDefaultAsync(x => x.Id == entryId && x.ExpeditionId == expeditionId, ct);
        if (entry is null)
        {
            return ApiProblems.NotFound(EntryNotFoundCode);
        }

        if (await ValidateReferencesAsync(db, request, ct) is { } problem)
        {
            return problem;
        }

        Apply(entry, request, await PersonOfAsync(db, request, ct));
        await db.SaveChangesAsync(ct);

        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, [entry.CaverId], ct);
        return TypedResults.Ok(Map(entry, labels));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid expeditionId,
        long entryId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var expedition = await db.Expeditions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expeditionId, ct);
        if (expedition is null)
        {
            return ApiProblems.NotFound(ExpeditionEndpoints.NotFoundCode);
        }

        if (await ExpeditionEndpoints.RefuseUnlessWritableAsync(access, ctx, expedition, ct) is { } refusal)
        {
            return refusal;
        }

        var entry = await db.ExpeditionRoster
            .FirstOrDefaultAsync(x => x.Id == entryId && x.ExpeditionId == expeditionId, ct);
        if (entry is null)
        {
            return ApiProblems.NotFound(EntryNotFoundCode);
        }

        // Removed rather than deleted in one statement: a stay disappearing is a change to the
        // camp's own record of who was there, and the camp's timeline only carries what the change
        // tracker sees.
        db.ExpeditionRoster.Remove(entry);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The person and the role — the two things a request's own shape cannot check.
    /// </summary>
    /// <remarks>
    /// Both are refused here with a code of their own rather than left to the foreign keys, so a
    /// client is told which of the two it got wrong instead of receiving a constraint violation.
    /// </remarks>
    private static async Task<ProblemHttpResult?> ValidateReferencesAsync(
        SilexGisDbContext db, ExpeditionRosterEntryWriteRequest request, CancellationToken ct)
    {
        // Asked only of a person named by their entry. A name cannot be wrong in this way: it
        // means whoever the directory already records under it, or somebody new.
        if (request.CaverId is { } caverId
            && !await db.Cavers.AsNoTracking().AnyAsync(c => c.Id == caverId, ct))
        {
            return ApiProblems.BadRequest(CaverUnknownCode, "That person is not in the club's directory.");
        }

        if (!await db.ExpeditionRosterRoles.AsNoTracking().AnyAsync(r => r.Id == request.RoleId, ct))
        {
            return ApiProblems.BadRequest(RoleUnknownCode, "That is not a camp-roster role.");
        }

        return null;
    }

    /// <summary>
    /// Who the stay is about: the entry the request names, or the person its name means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name is turned into a person by the rule every record that names somebody shares — the
    /// oldest entry already recorded under it, or a new one — and deliberately not by anything
    /// written here. A camp's roster and a trip's list of people name the same club's people,
    /// and two readings of one typed name would give that club two entries for one of them.
    /// </para>
    /// <para>
    /// Asked last, once nothing is left that could refuse the request. Resolving a name may add
    /// somebody to the directory — to the unit of work, saved with the stay — and a request that
    /// is turned away must not have left a person behind it.
    /// </para>
    /// <para>
    /// No right over the directory is asked for a name that adds somebody, because a trip asks
    /// none either: recording who was there is part of writing the record, and the right to
    /// write this camp has already been established by the time this runs.
    /// </para>
    /// </remarks>
    private static async Task<Guid> PersonOfAsync(
        SilexGisDbContext db, ExpeditionRosterEntryWriteRequest request, CancellationToken ct)
    {
        if (request.CaverId is { } caverId)
        {
            return caverId;
        }

        // Present, because the request's own shape requires a name wherever no entry is given.
        var name = request.NewCaverName!;
        return (await CaverNames.ResolveAsync(db, [name], ct)).IdOf(name);
    }

    private static void Apply(
        ExpeditionRosterEntry entry, ExpeditionRosterEntryWriteRequest request, Guid caverId)
    {
        entry.CaverId = caverId;
        entry.RoleId = request.RoleId;
        entry.FromDate = request.FromDate;

        // A stay ending the day it starts stores no end, the way the camp's own dates do, so one
        // day never reads as a range of itself. The database refuses an end that is not strictly
        // after the first day, so this normalisation is what lets the ordinary "same day twice"
        // request through.
        entry.ToDate = DayRange.EndForStorage(request.FromDate, request.ToDate);
        entry.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
    }

    private static ExpeditionRosterEntryDto Map(
        ExpeditionRosterEntry row, IReadOnlyDictionary<Guid, string> labels) => new()
        {
            Id = row.Id,
            ExpeditionId = row.ExpeditionId,
            CaverId = row.CaverId,
            CaverName = labels.GetValueOrDefault(row.CaverId) ?? string.Empty,
            RoleId = row.RoleId,
            FromDate = row.FromDate,
            ToDate = row.ToDate,
            Note = row.Note,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
        };
}
