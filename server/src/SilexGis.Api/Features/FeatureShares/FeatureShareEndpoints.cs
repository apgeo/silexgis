// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.FeatureShares;

public static class FeatureShareEndpoints
{
    public static RouteGroupBuilder MapFeatureShareEndpoints(this RouteGroupBuilder api)
    {
        var shares = api.MapGroup("/features/{id:guid}/shares").WithTags("FeatureShares");

        shares.MapPost("/", MintAsync).WithValidation<FeatureShareCreateRequest>()
            .WithSummary("Mints a share link for the feature (Share permission); the token is returned once and never stored.");
        shares.MapGet("/", ListAsync)
            .WithSummary("The feature's share links — metadata only, never tokens (Share permission).");
        shares.MapDelete("/{shareId:guid}", RevokeAsync)
            .WithSummary("Revokes a share link (Share permission).");

        // The share token IS the credential, so this route sits on the anonymous
        // allow-list; whether a signed-in session is additionally required is the share's
        // own mode, decided per link. Location protection is applied server-side in every
        // mode — a share can never widen what coordinates a caller sees.
        api.MapGet("/shared/features/{token}", SharedAsync)
            .WithTags("FeatureShares")
            .AllowAnonymous()
            .WithSummary("Resolves a share link to the shared feature envelope.");

        return api;
    }

    private static async Task<Results<Created<FeatureShareCreatedDto>, UnauthorizedHttpResult, ProblemHttpResult>> MintAsync(
        Guid id,
        FeatureShareCreateRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Share, feature, ct)).Allowed)
        {
            // Existence of a feature the caller cannot read is not disclosed.
            return (await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("feature.not_found");
        }

        // 32 random bytes, base64url-encoded, become the URL token; only its SHA-256 is
        // stored, so a database leak cannot resurrect live links and the token cannot be
        // shown a second time.
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var share = new FeatureShare
        {
            FeatureId = feature.Id,
            TokenHash = HashToken(token),
            Mode = request.Mode,
            IncludeSubtree = request.IncludeSubtree,
            CreatedBy = ctx.UserId,
        };
        db.FeatureShares.Add(share);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/features/{feature.Id}/shares/{share.Id}",
            new FeatureShareCreatedDto(share.Id, token, share.Mode, share.IncludeSubtree, share.CreatedAt));
    }

    private static async Task<Results<Ok<List<FeatureShareDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Share, feature, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("feature.not_found");
        }

        var items = await db.FeatureShares.AsNoTracking()
            .Where(s => s.FeatureId == id)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new FeatureShareDto(
                s.Id, s.Mode, s.IncludeSubtree, s.CreatedBy, s.CreatedAt, s.RevokedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(items);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RevokeAsync(
        Guid id,
        Guid shareId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Share, feature, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("feature.not_found");
        }

        var share = await db.FeatureShares.FirstOrDefaultAsync(
            s => s.Id == shareId && s.FeatureId == id, ct);
        if (share is null)
        {
            return ApiProblems.NotFound("share.not_found");
        }

        // Idempotent: a second revoke keeps the original revocation timestamp.
        if (share.RevokedAt is null)
        {
            share.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SharedFeatureEnvelopeDto>, ProblemHttpResult>> SharedAsync(
        string token,
        SilexGisDbContext db,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> access,
        CancellationToken ct)
    {
        // Malformed, unknown and revoked tokens answer identically: the token is the only
        // credential, so nothing about its validity may be distinguishable.
        if (string.IsNullOrEmpty(token) || token.Length > 100)
        {
            return ApiProblems.NotFound("share.not_found");
        }

        var hash = HashToken(token);
        var share = await db.FeatureShares.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == hash && s.RevokedAt == null, ct);
        if (share is null)
        {
            return ApiProblems.NotFound("share.not_found");
        }

        // The soft-delete query filter makes a deleted feature unreachable — its share
        // links die with it.
        var feature = await db.Features.AsNoTracking()
            .Include(f => f.Cave).Include(f => f.Entrance).Include(f => f.Centerline)
            .FirstOrDefaultAsync(f => f.Id == share.FeatureId, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("share.not_found");
        }

        AccessContext? viewer = null;
        if (share.Mode == FeatureShareMode.RequiresLogin)
        {
            viewer = await accessAccessor.GetAsync(ct);
            if (viewer is null)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    extensions: new Dictionary<string, object?> { ["code"] = "share.login_required" });
            }

            // A requires-login share is only a resolvable address: the caller's own
            // permissions gate the read, exactly as on the regular feature routes.
            if (!await db.Features.VisibleTo(viewer, db.Features, db.FeatureSetMembers).AnyAsync(f => f.Id == feature.Id, ct))
            {
                return ApiProblems.NotFound("share.not_found");
            }
        }

        // Public mode: the share itself grants Read of this envelope, but for location
        // protection the viewer stays anonymous (no grants), so a protected chain is
        // always obfuscated — a link can never widen what coordinates it shows.
        var children = share.IncludeSubtree
            ? await LoadPrimarySubtreeAsync(db, viewer, feature.Id, ct)
            : null;

        var allIds = new List<Guid> { feature.Id };
        if (children is not null)
        {
            allIds.AddRange(children.Select(c => c.Id));
        }

        var exactIds = await protection.ExactViewIdsAsync(viewer, allIds, ct);
        var withholdTypeIds = await WithholdTypeIdsAsync(db, feature, children, exactIds, ct);

        // Rows whose protected display is total withholding never surface at all:
        // centerlines trace the cave's exact underground position, and Withhold-typed
        // kinds are declared unsafe to show even snapped.
        if (!exactIds.Contains(feature.Id) && IsWithheld(feature.Kind, feature.FeatureTypeId, withholdTypeIds))
        {
            return ApiProblems.NotFound("share.not_found");
        }

        var childDtos = children?
            .Where(c => exactIds.Contains(c.Id) || !IsWithheld(c.Kind, c.FeatureTypeId, withholdTypeIds))
            .Select(c => new SharedFeatureChildDto(c.Id, c.Kind, c.Name))
            .ToList();

        return TypedResults.Ok(ToEnvelope(
            feature, exactIds.Contains(feature.Id), access.Value.LocationGridMeters, childDtos));
    }

    /// <summary>SHA-256 of the URL token, base64url — the stored/looked-up form; the plaintext never persists.</summary>
    private static string HashToken(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed record SubtreeRow(Guid Id, FeatureKind Kind, string? Name, long? FeatureTypeId);

    /// <summary>
    /// The share's subtree: descendants reachable from the shared feature by following
    /// PRIMARY hierarchy edges only (secondary DAG memberships do not extend a share).
    /// Candidates come from the flat ancestor arrays; the primary-chain membership is
    /// decided in memory over their primary-parent edges. For a requires-login share the
    /// caller's read filter applies first, so an invisible intermediate feature also
    /// hides everything hanging below it.
    /// </summary>
    private static async Task<List<SubtreeRow>> LoadPrimarySubtreeAsync(
        SilexGisDbContext db, AccessContext? viewer, Guid rootId, CancellationToken ct)
    {
        var candidates = db.Features.AsNoTracking()
            .Where(f => f.Id != rootId && f.AncestorIds.Contains(rootId));
        if (viewer is not null)
        {
            candidates = candidates.VisibleTo(viewer, db.Features, db.FeatureSetMembers);
        }

        var rows = await candidates
            .Select(f => new SubtreeRow(f.Id, f.Kind, f.Name, f.FeatureTypeId))
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return rows;
        }

        var candidateIds = rows.Select(r => r.Id).ToHashSet();
        var primaryParent = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.IsPrimary && candidateIds.Contains(e.ChildId))
            .ToDictionaryAsync(e => e.ChildId, e => e.ParentId, ct);

        var memo = new Dictionary<Guid, bool>();
        return [.. rows.Where(r => InSubtree(r.Id)).OrderBy(r => r.Name).ThenBy(r => r.Id)];

        bool InSubtree(Guid featureId)
        {
            if (memo.TryGetValue(featureId, out var known))
            {
                return known;
            }

            memo[featureId] = false; // guard: an (impossible) edge cycle terminates as "outside"
            var inside = primaryParent.TryGetValue(featureId, out var parent)
                && (parent == rootId || (candidateIds.Contains(parent) && InSubtree(parent)));
            memo[featureId] = inside;
            return inside;
        }
    }

    /// <summary>
    /// Feature-type ids whose protected display is total withholding, among the types of
    /// the generic rows the caller may NOT view exactly (the only rows the setting can
    /// affect). Subtyped kinds carry their policy in code, not in feature_types.
    /// </summary>
    private static async Task<HashSet<long>> WithholdTypeIdsAsync(
        SilexGisDbContext db,
        Feature feature,
        List<SubtreeRow>? children,
        HashSet<Guid> exactIds,
        CancellationToken ct)
    {
        var typeIds = new HashSet<long>();
        if (!exactIds.Contains(feature.Id) && feature.FeatureTypeId is not null)
        {
            typeIds.Add(feature.FeatureTypeId.Value);
        }

        foreach (var child in children ?? [])
        {
            if (!exactIds.Contains(child.Id) && child.FeatureTypeId is not null)
            {
                typeIds.Add(child.FeatureTypeId.Value);
            }
        }

        if (typeIds.Count == 0)
        {
            return [];
        }

        var withheld = await db.FeatureTypes.AsNoTracking()
            .Where(t => typeIds.Contains(t.Id) && t.ProtectedDisplay == ProtectedDisplay.Withhold)
            .Select(t => t.Id)
            .ToListAsync(ct);
        return [.. withheld];
    }

    /// <summary>Whole-row withholding for non-exact viewers, by kind/type.</summary>
    private static bool IsWithheld(FeatureKind kind, long? featureTypeId, HashSet<long> withholdTypeIds) =>
        kind == FeatureKind.Centerline
        || (featureTypeId is not null && withholdTypeIds.Contains(featureTypeId.Value));

    private static SharedFeatureEnvelopeDto ToEnvelope(
        Feature f, bool exact, double gridMeters, IReadOnlyList<SharedFeatureChildDto>? children)
    {
        // Obfuscation by geometry class: a point is snapped to the grid; any other class
        // (lines, polygons, Multi*) traces real shape and cannot be safely snapped, so it
        // is omitted entirely. Snap drops Z, so a protected entrance's altitude never
        // rides along in the coordinates.
        GeoJsonGeometry? geometry = null;
        if (f.Geom is not null)
        {
            if (exact)
            {
                geometry = GeoJsonGeometry.From(f.Geom);
            }
            else if (f.Geom is Point point)
            {
                geometry = GeoJsonGeometry.From(LocationProtection.Snap(point, gridMeters));
            }
        }

        var featureDto = new SharedFeatureDto(
            f.Id, f.Kind, f.FeatureTypeId, f.Category, f.Name,
            geometry,
            ApproximateLocation: !exact,
            f.Description,
            JsonSerializer.Deserialize<JsonElement>(f.Properties),
            f.CreatedAt, f.UpdatedAt);

        return new SharedFeatureEnvelopeDto(
            f.Kind,
            featureDto,
            f.Cave is null ? null : ToCaveDto(f.Cave, exact),
            f.Entrance is null ? null : ToEntranceDto(f.Entrance, exact),
            f.Centerline is null ? null : ToCenterlineDto(f.Centerline),
            children);
    }

    private static SharedCaveDto ToCaveDto(Cave c, bool exact) => new(
        c.OtherToponyms,
        c.IdentificationCode,
        c.CaveTypeId,
        c.Website,
        c.Region,
        c.HydrographicBasin,
        c.Valley,
        c.TributaryRiver,
        // The textual precise-location trio is protected alongside the coordinates.
        exact ? c.ClosestAddress : null,
        exact ? c.LandRegistryNumber : null,
        exact ? c.LocationNotes : null,
        c.RockTypeId,
        c.RockAge,
        c.SurveyedLength,
        c.EstimatedLength,
        c.RealExtension,
        c.ProjectedExtension,
        c.Depth,
        c.PositiveDepth,
        c.NegativeDepth,
        c.PotentialDepth,
        c.Altitude,
        c.Volume,
        c.Area,
        c.RamificationIndex,
        c.CaveAge,
        c.ExplorationStatus,
        c.ProtectionClass,
        c.IsShowCave,
        c.ShowCaveLength,
        c.DiscoveryDate,
        c.Discoverer,
        c.EntranceCount);

    private static SharedEntranceDto ToEntranceDto(CaveEntrance e, bool exact) => new(
        e.EntranceTypeId,
        e.IsMain,
        exact ? e.Altitude : null,
        exact ? e.PositionQuality : null,
        e.SurveyedAt);

    private static SharedCenterlineDto ToCenterlineDto(Centerline c) => new(
        c.IsDefault,
        c.LengthM,
        c.PathCount,
        c.Source);
}
