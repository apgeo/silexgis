// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Expeditions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Taxonomies;

/// <summary>
/// One thing somebody may be recorded as having been at a camp as. <c>IsSeeded</c> travels with
/// the row so a client can say which rows it may offer to edit without keeping its own copy of the
/// shipped list.
/// </summary>
public sealed record ExpeditionRosterRoleDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsSeeded);

/// <summary>A camp-roster role as an administrator asks for it to be.</summary>
public sealed record ExpeditionRosterRoleRequest(
    string Code,
    string Name,
    string? Description,
    int SortOrder);

public sealed class ExpeditionRosterRoleRequestValidator : AbstractValidator<ExpeditionRosterRoleRequest>
{
    public ExpeditionRosterRoleRequestValidator()
    {
        // Codes are the natural key installations exchange rows by, so they stay to the
        // characters that survive a URL, a file name and a spreadsheet unharmed.
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50)
            .Matches("^[a-z0-9_]+$")
            .WithMessage("The code may contain lower-case letters, digits and underscores.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

/// <summary>
/// The vocabulary of what somebody was at a camp as: the rows that ship plus whatever a club adds.
/// Reading it is open to every account through the taxonomy read the All Users seed carries — a
/// camp's roster renders a role from it. Editing it needs taxonomy rights, which by default only a
/// full administrator holds.
/// <para>
/// Separate from the roles people are recorded under on a trip, and deliberately: cooking for
/// thirty people and keeping the base camp are what a camp's roster exists to record, and neither
/// is a job underground — adding them to the trip vocabulary would offer them on the trip form,
/// where they mean nothing.
/// </para>
/// <para>
/// Shipped rows are the exchange vocabulary: their codes are immutable and the rows undeletable,
/// because clients translate labels by code and installations exchange records under them, so a
/// renamed or deleted code would silently break both. One of them carries more than that: a camp
/// with no "member" row could not record that somebody was simply there. Their wording and
/// ordering stay editable, because that is presentation and an installation is entitled to its
/// own. Rows a club adds are installation-local and freely edited, and deletion is refused while
/// anybody is still recorded under one.
/// </para>
/// </summary>
public static class ExpeditionRosterRoleEndpoints
{
    public const string NotFoundCode = "expedition_roster_role.not_found";
    public const string CodeTakenCode = "expedition_roster_role.code_taken";
    public const string SeededImmutableCode = "expedition_roster_role.seeded_immutable";
    public const string InUseCode = "expedition_roster_role.in_use";

    public static RouteGroupBuilder MapExpeditionRosterRoleEndpoints(this RouteGroupBuilder api)
    {
        var roles = api.MapGroup("/expedition-roster-roles").WithTags("Taxonomies");

        roles.MapGet("/", ListAsync)
            .WithSummary("The camp-roster roles: shipped rows (translated by code) and club rows (shown as written).");
        roles.MapPost("/", CreateAsync)
            .WithValidation<ExpeditionRosterRoleRequest>()
            .WithSummary("Adds a camp-roster role.");
        roles.MapPut("/{id:long}", UpdateAsync)
            .WithValidation<ExpeditionRosterRoleRequest>()
            .WithSummary("Updates a camp-roster role; a shipped row keeps its code.");
        roles.MapDelete("/{id:long}", DeleteAsync)
            .WithSummary("Deletes an unused club camp-roster role; shipped rows cannot be deleted.");

        return api;
    }

    private static async Task<Results<Ok<List<ExpeditionRosterRoleDto>>, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var rows = await db.ExpeditionRosterRoles.AsNoTracking()
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<ExpeditionRosterRoleDto>, ProblemHttpResult>> CreateAsync(
        ExpeditionRosterRoleRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (!CreateRules.MayCreate(ctx, AccessDomain.Taxonomies))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var code = request.Code.Trim();
        if (await db.ExpeditionRosterRoles.AnyAsync(r => r.Code == code, ct))
        {
            return ApiProblems.BadRequest(CodeTakenCode, "Another camp-roster role already uses this code.");
        }

        var row = new ExpeditionRosterRole
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description,
            SortOrder = request.SortOrder,
        };
        db.ExpeditionRosterRoles.Add(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/expedition-roster-roles/{row.Id}", ToDto(row));
    }

    private static async Task<Results<Ok<ExpeditionRosterRoleDto>, ProblemHttpResult>> UpdateAsync(
        long id,
        ExpeditionRosterRoleRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var row = await db.ExpeditionRosterRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var code = request.Code.Trim();
        // A shipped code is what clients translate labels by and what installations exchange
        // records under, so renaming one would silently break both. The wording and the ordering
        // are presentation and stay editable.
        if (ExpeditionRosterRoleSeeds.IsSeeded(row.Code) && code != row.Code)
        {
            return ApiProblems.BadRequest(SeededImmutableCode, "A shipped camp-roster role keeps its code.");
        }

        if (code != row.Code && await db.ExpeditionRosterRoles.AnyAsync(r => r.Code == code && r.Id != id, ct))
        {
            return ApiProblems.BadRequest(CodeTakenCode, "Another camp-roster role already uses this code.");
        }

        row.Code = code;
        row.Name = request.Name.Trim();
        row.Description = request.Description;
        row.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(row));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        long id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Delete, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var row = await db.ExpeditionRosterRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Refused as a conflict rather than a bad request: deleting a row that ships with the
        // software conflicts with what the row is, where an edit that only wanted a different
        // code is a request that could have been written another way.
        if (ExpeditionRosterRoleSeeds.IsSeeded(row.Code))
        {
            return ApiProblems.Conflict(SeededImmutableCode, "Shipped camp-roster roles cannot be deleted.");
        }

        // The database refuses this too (the foreign key restricts), but a request deserves a
        // stable code and a remedy rather than a constraint violation.
        if (await db.ExpeditionRoster.AnyAsync(p => p.RoleId == id, ct))
        {
            return ApiProblems.Conflict(InUseCode, "People are still recorded in this role; re-role them first.");
        }

        db.ExpeditionRosterRoles.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static ExpeditionRosterRoleDto ToDto(ExpeditionRosterRole row) => new(
        row.Id,
        row.Code,
        row.Name,
        row.Description,
        row.SortOrder,
        ExpeditionRosterRoleSeeds.IsSeeded(row.Code));
}
