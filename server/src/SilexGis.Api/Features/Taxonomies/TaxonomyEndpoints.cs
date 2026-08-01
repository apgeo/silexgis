// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Taxonomies;

public sealed record TaxonomyDto(long Id, string Code, string Name, string? Description, int SortOrder);

/// <summary>
/// The data-level feature-kind registry. <c>Category</c>, <c>AcceptedGeometryClasses</c>,
/// <c>RequiresParent</c> and <c>PropertiesSchemaVersion</c> tell clients what a kind may
/// carry and how to validate it before a write; <c>ProtectedDisplay</c> is read-only
/// metadata (its edits are admin-only and audited) that lets a client explain why a
/// protected row arrives snapped or missing — it never substitutes for server-side
/// protection.
/// </summary>
public sealed record FeatureTypeDto(
    long Id, string Code, string Name, string? Description, int SortOrder,
    FeatureCategory Category, IReadOnlyList<GeometryClass> AcceptedGeometryClasses,
    bool RequiresParent, int PropertiesSchemaVersion, ProtectedDisplay ProtectedDisplay,
    string? SymbolFile, string? Style, string? PropertiesSchema);

/// <summary>
/// Link-kind taxonomy. <c>Locating</c> is fail-closed metadata (admin-only, audited edits):
/// links of a locating kind are the ones the server redacts when an endpoint is protected.
/// </summary>
public sealed record LinkKindDto(
    long Id, string Code, string Name, string? Description, int SortOrder, bool Locating);

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
            {
                // Materialize first: the accepted-classes column is a smallint[] behind a value
                // converter, so the mapping runs after the rows are read.
                var types = await db.FeatureTypes.AsNoTracking()
                    .OrderBy(t => t.SortOrder)
                    .ThenBy(t => t.Id)
                    .ToListAsync(ct);

                return TypedResults.Ok(types.Select(t => new FeatureTypeDto(
                    t.Id, t.Code, t.Name, t.Description, t.SortOrder,
                    t.Category, t.AcceptedGeometryClasses, t.RequiresParent, t.PropertiesSchemaVersion,
                    t.ProtectedDisplay, t.SymbolFile, t.Style, t.PropertiesSchema)).ToList());
            })
            .WithTags("Taxonomies");
        api.MapGet("/link-kinds", async (SilexGisDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.LinkKinds.AsNoTracking()
                    .OrderBy(k => k.SortOrder)
                    .ThenBy(k => k.Id)
                    .Select(k => new LinkKindDto(k.Id, k.Code, k.Name, k.Description, k.SortOrder, k.Locating))
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
