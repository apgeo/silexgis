// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Taxonomies;

public sealed record TaxonomyDto(long Id, string Code, string Name, string? Description, int SortOrder);

public sealed record FeatureTypeDto(
    long Id, string Code, string Name, string? Description, int SortOrder,
    GeometryKind GeometryKind, string? SymbolFile, string? Style, string? PropertiesSchema);

/// <summary>Read endpoints for the lookup taxonomies (admin CRUD arrives with the admin UI).</summary>
public static class TaxonomyEndpoints
{
    public static RouteGroupBuilder MapTaxonomyEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/cave-types", (SilexGisDbContext db, CancellationToken ct) => ListAsync(db.CaveTypes, ct))
            .WithTags("Taxonomies");
        api.MapGet("/entrance-types", (SilexGisDbContext db, CancellationToken ct) => ListAsync(db.EntranceTypes, ct))
            .WithTags("Taxonomies");
        api.MapGet("/rock-types", (SilexGisDbContext db, CancellationToken ct) => ListAsync(db.RockTypes, ct))
            .WithTags("Taxonomies");
        api.MapGet("/feature-types", async (SilexGisDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.FeatureTypes.AsNoTracking()
                    .OrderBy(t => t.SortOrder)
                    .Select(t => new FeatureTypeDto(
                        t.Id, t.Code, t.Name, t.Description, t.SortOrder,
                        t.GeometryKind, t.SymbolFile, t.Style, t.PropertiesSchema))
                    .ToListAsync(ct)))
            .WithTags("Taxonomies");

        return api;
    }

    private static async Task<Ok<List<TaxonomyDto>>> ListAsync<T>(DbSet<T> set, CancellationToken ct)
        where T : TaxonomyBase =>
        TypedResults.Ok(await set.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .Select(t => new TaxonomyDto(t.Id, t.Code, t.Name, t.Description, t.SortOrder))
            .ToListAsync(ct));
}
