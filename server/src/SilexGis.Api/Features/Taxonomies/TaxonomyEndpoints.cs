// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
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

/// <summary>
/// Read endpoints for the lookup taxonomies (editing arrives with the admin UI). Every
/// account holds the read through the All Users seed, so these stay as open as they were
/// — but as an entry an installation can tighten, not as an absence of any rule. The
/// security-bearing columns here (a kind's protected display, whether a link kind
/// locates) are why that distinction is worth making.
/// </summary>
public static class TaxonomyEndpoints
{
    /// <summary>Null when the caller may read the taxonomies, otherwise the refusal.</summary>
    private static async Task<ProblemHttpResult?> GuardAsync(
        IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        return ctx is not null
            && AccessEvaluator.Decide(ctx, AccessDomain.Taxonomies, AccessAction.Read, null).Allowed
                ? null
                : ApiProblems.Forbidden();
    }

    public static RouteGroupBuilder MapTaxonomyEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/cave-types", (SilexGisDbContext db, IAccessContextAccessor a, CancellationToken ct) =>
                ListAsync(db.CaveTypes, a, ct))
            .WithTags("Taxonomies");
        api.MapGet("/entrance-types", (SilexGisDbContext db, IAccessContextAccessor a, CancellationToken ct) =>
                ListAsync(db.EntranceTypes, a, ct))
            .WithTags("Taxonomies");
        api.MapGet("/rock-types", (SilexGisDbContext db, IAccessContextAccessor a, CancellationToken ct) =>
                ListAsync(db.RockTypes, a, ct))
            .WithTags("Taxonomies");
        api.MapGet("/feature-types", FeatureTypesAsync).WithTags("Taxonomies");
        api.MapGet("/link-kinds", LinkKindsAsync).WithTags("Taxonomies");

        return api;
    }

    private static async Task<Results<Ok<List<FeatureTypeDto>>, ProblemHttpResult>> FeatureTypesAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        if (await GuardAsync(accessAccessor, ct) is { } denied)
        {
            return denied;
        }

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
    }

    private static async Task<Results<Ok<List<LinkKindDto>>, ProblemHttpResult>> LinkKindsAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        if (await GuardAsync(accessAccessor, ct) is { } denied)
        {
            return denied;
        }

        return TypedResults.Ok(await db.LinkKinds.AsNoTracking()
            .OrderBy(k => k.SortOrder)
            .ThenBy(k => k.Id)
            .Select(k => new LinkKindDto(k.Id, k.Code, k.Name, k.Description, k.SortOrder, k.Locating))
            .ToListAsync(ct));
    }

    private static async Task<Results<Ok<List<TaxonomyDto>>, ProblemHttpResult>> ListAsync<T>(
        DbSet<T> set, IAccessContextAccessor accessAccessor, CancellationToken ct)
        where T : TaxonomyBase
    {
        if (await GuardAsync(accessAccessor, ct) is { } denied)
        {
            return denied;
        }

        return TypedResults.Ok(await set.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .Select(t => new TaxonomyDto(t.Id, t.Code, t.Name, t.Description, t.SortOrder))
            .ToListAsync(ct));
    }
}
