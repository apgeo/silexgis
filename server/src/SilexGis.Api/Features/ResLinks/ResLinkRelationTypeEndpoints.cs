// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.ResLinks;

/// <summary>
/// The relation vocabulary of resource links: the shipped rows plus whatever an
/// installation adds. Reading it is open to every signed-in caller (pickers and link
/// rendering need it); managing it is a full-administrator act. Shipped rows are the
/// exchange vocabulary — their codes and directedness are immutable and the rows
/// undeletable, because clients translate labels by code and directedness decides how
/// existing links validate their main member. Custom rows are installation-local:
/// freely edited, but their directedness is frozen while links use them (flipping it
/// would invalidate every such link's main marker in place), and deletion is refused
/// while any link references them.
/// </summary>
public static class ResLinkRelationTypeEndpoints
{
    public const string NotFoundCode = "reslink.relation.not_found";
    public const string CodeTakenCode = "reslink.relation.code_taken";
    public const string SeededImmutableCode = "reslink.relation.seeded_immutable";
    public const string InUseCode = "reslink.relation.in_use";

    public static RouteGroupBuilder MapResLinkRelationTypeEndpoints(this RouteGroupBuilder api)
    {
        var types = api.MapGroup("/reslinks/relation-types").WithTags("ResLinks");

        types.MapGet("/", ListAsync)
            .WithSummary("The relation vocabulary: seeded rows (translated by code) and custom rows (shown as written).");
        types.MapPost("/", CreateAsync).WithValidation<ResLinkRelationTypeWriteRequest>()
            .WithSummary("Adds a custom relation type (admin).");
        types.MapPatch("/{id:long}", UpdateAsync).WithValidation<ResLinkRelationTypeWriteRequest>()
            .WithSummary("Edits a relation type (admin); seeded code and directedness are immutable.");
        types.MapDelete("/{id:long}", DeleteAsync)
            .WithSummary("Deletes an unreferenced custom relation type (admin).");

        return api;
    }

    private static async Task<Results<Ok<List<ResLinkRelationTypeDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var rows = await db.ResLinkRelationTypes.AsNoTracking()
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<ResLinkRelationTypeDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        ResLinkRelationTypeWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!ctx.IsFullAdmin)
        {
            return ApiProblems.Forbidden();
        }

        var code = request.Code.Trim();
        if (await db.ResLinkRelationTypes.AnyAsync(r => r.Code == code, ct))
        {
            return ApiProblems.BadRequest(CodeTakenCode, "Another relation type already uses this code.");
        }

        var row = new ResLinkRelationType
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description,
            SortOrder = request.SortOrder,
            Directed = request.Directed,
            InverseName = request.InverseName,
        };
        db.ResLinkRelationTypes.Add(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/reslinks/relation-types/{row.Id}", ToDto(row));
    }

    private static async Task<Results<Ok<ResLinkRelationTypeDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        long id,
        ResLinkRelationTypeWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!ctx.IsFullAdmin)
        {
            return ApiProblems.Forbidden();
        }

        var row = await db.ResLinkRelationTypes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var code = request.Code.Trim();
        var seeded = ResLinkRelationTypeSeeds.IsSeeded(row.Code);
        if (seeded && (code != row.Code || request.Directed != row.Directed))
        {
            return ApiProblems.BadRequest(
                SeededImmutableCode, "A seeded relation type keeps its code and directedness.");
        }

        if (code != row.Code && await db.ResLinkRelationTypes.AnyAsync(r => r.Code == code && r.Id != id, ct))
        {
            return ApiProblems.BadRequest(CodeTakenCode, "Another relation type already uses this code.");
        }

        if (request.Directed != row.Directed
            && await db.ResLinks.AnyAsync(l => l.RelationTypeId == id, ct))
        {
            return ApiProblems.Conflict(
                InUseCode, "Directedness cannot change while links use this relation type.");
        }

        row.Code = code;
        row.Name = request.Name.Trim();
        row.Description = request.Description;
        row.SortOrder = request.SortOrder;
        row.Directed = request.Directed;
        row.InverseName = request.InverseName;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(row));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        long id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!ctx.IsFullAdmin)
        {
            return ApiProblems.Forbidden();
        }

        var row = await db.ResLinkRelationTypes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (ResLinkRelationTypeSeeds.IsSeeded(row.Code))
        {
            return ApiProblems.Conflict(SeededImmutableCode, "Seeded relation types cannot be deleted.");
        }

        // The database refuses this too (the FK restricts), but a request deserves a
        // stable code rather than a constraint violation.
        if (await db.ResLinks.AnyAsync(l => l.RelationTypeId == id, ct))
        {
            return ApiProblems.Conflict(InUseCode, "Links still use this relation type; retype them first.");
        }

        db.ResLinkRelationTypes.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    internal static ResLinkRelationTypeDto ToDto(ResLinkRelationType row) => new(
        row.Id,
        row.Code,
        row.Name,
        row.Description,
        row.SortOrder,
        row.Directed,
        row.InverseName,
        ResLinkRelationTypeSeeds.IsSeeded(row.Code));
}
