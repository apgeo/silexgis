// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Taxonomies;

/// <summary>
/// One job somebody may be recorded as having done on a trip. <c>IsSeeded</c> travels with the
/// row so a client can say which rows it may offer to edit without keeping its own copy of the
/// shipped list.
/// </summary>
public sealed record TripParticipantRoleDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsSeeded);

/// <summary>A participant role as an administrator asks for it to be.</summary>
public sealed record TripParticipantRoleRequest(
    string Code,
    string Name,
    string? Description,
    int SortOrder);

public sealed class TripParticipantRoleRequestValidator : AbstractValidator<TripParticipantRoleRequest>
{
    public TripParticipantRoleRequestValidator()
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
/// The vocabulary of what somebody did on a trip: the rows that ship plus whatever a club adds.
/// Reading it is open to every account through the taxonomy read the All Users seed carries —
/// every roster renders a role from it. Editing it needs taxonomy rights, which by default only a
/// full administrator holds.
/// <para>
/// Shipped rows are the exchange vocabulary: their codes are immutable and the rows undeletable,
/// because clients translate labels by code and installations exchange records under them, so a
/// renamed or deleted code would silently break both. Two of them carry more than that: a roster
/// with no "participant" row could not record attendance, and "proposer" is what the right to
/// edit a proposed trip is decided by — an answer no administrator should be able to delete.
/// Their wording and ordering stay editable, because that is presentation and an installation is
/// entitled to its own. Rows a club adds are installation-local and freely edited, and deletion
/// is refused while anybody is still recorded under one.
/// </para>
/// </summary>
public static class TripParticipantRoleEndpoints
{
    public const string NotFoundCode = "trip_participant_role.not_found";
    public const string CodeTakenCode = "trip_participant_role.code_taken";
    public const string SeededImmutableCode = "trip_participant_role.seeded_immutable";
    public const string InUseCode = "trip_participant_role.in_use";

    public static RouteGroupBuilder MapTripParticipantRoleEndpoints(this RouteGroupBuilder api)
    {
        var roles = api.MapGroup("/trip-participant-roles").WithTags("Taxonomies");

        roles.MapGet("/", ListAsync)
            .WithSummary("The participant roles: shipped rows (translated by code) and club rows (shown as written).");
        roles.MapPost("/", CreateAsync)
            .WithValidation<TripParticipantRoleRequest>()
            .WithSummary("Adds a participant role.");
        roles.MapPut("/{id:long}", UpdateAsync)
            .WithValidation<TripParticipantRoleRequest>()
            .WithSummary("Updates a participant role; a shipped row keeps its code.");
        roles.MapDelete("/{id:long}", DeleteAsync)
            .WithSummary("Deletes an unused club participant role; shipped rows cannot be deleted.");

        return api;
    }

    private static async Task<Results<Ok<List<TripParticipantRoleDto>>, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var rows = await db.TripParticipantRoles.AsNoTracking()
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<TripParticipantRoleDto>, ProblemHttpResult>> CreateAsync(
        TripParticipantRoleRequest request,
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
        if (await db.TripParticipantRoles.AnyAsync(r => r.Code == code, ct))
        {
            return ApiProblems.BadRequest(CodeTakenCode, "Another participant role already uses this code.");
        }

        var row = new TripParticipantRole
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description,
            SortOrder = request.SortOrder,
        };
        db.TripParticipantRoles.Add(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/trip-participant-roles/{row.Id}", ToDto(row));
    }

    private static async Task<Results<Ok<TripParticipantRoleDto>, ProblemHttpResult>> UpdateAsync(
        long id,
        TripParticipantRoleRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null || !AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden();
        }

        var row = await db.TripParticipantRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var code = request.Code.Trim();
        // A shipped code is what clients translate labels by and what installations exchange
        // records under, so renaming one would silently break both. The wording and the ordering
        // are presentation and stay editable.
        if (TripParticipantRoleSeeds.IsSeeded(row.Code) && code != row.Code)
        {
            return ApiProblems.BadRequest(SeededImmutableCode, "A shipped participant role keeps its code.");
        }

        if (code != row.Code && await db.TripParticipantRoles.AnyAsync(r => r.Code == code && r.Id != id, ct))
        {
            return ApiProblems.BadRequest(CodeTakenCode, "Another participant role already uses this code.");
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

        var row = await db.TripParticipantRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Refused as a conflict rather than a bad request: deleting a row that ships with the
        // software conflicts with what the row is, where an edit that only wanted a different
        // code is a request that could have been written another way.
        if (TripParticipantRoleSeeds.IsSeeded(row.Code))
        {
            return ApiProblems.Conflict(SeededImmutableCode, "Shipped participant roles cannot be deleted.");
        }

        // The database refuses this too (the foreign key restricts), but a request deserves a
        // stable code and a remedy rather than a constraint violation.
        if (await db.TripLogParticipants.AnyAsync(p => p.RoleId == id, ct))
        {
            return ApiProblems.Conflict(InUseCode, "People are still recorded in this role; re-role them first.");
        }

        db.TripParticipantRoles.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static TripParticipantRoleDto ToDto(TripParticipantRole row) => new(
        row.Id,
        row.Code,
        row.Name,
        row.Description,
        row.SortOrder,
        TripParticipantRoleSeeds.IsSeeded(row.Code));
}
