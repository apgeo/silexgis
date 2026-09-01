// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Documents;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Typed cave facade over the feature aggregate: a cave is a feature row (identity, name,
/// description, access control, protection, main-entrance point cache) plus the cave
/// subtype row (speleological attributes). Aggregate invariants — hierarchy edges,
/// derived protection, subtree soft delete — go through the feature write service.
/// </summary>
public static class CaveEndpoints
{
    public static RouteGroupBuilder MapCaveEndpoints(this RouteGroupBuilder api)
    {
        var caves = api.MapGroup("/caves").WithTags("Caves");

        caves.MapGet("/", ListAsync)
            .WithSummary(
                "Paged cave list with filters, including `unplaced` for caves that have no position "
                + "at all; visibility-filtered, protected locations obfuscated.");
        caves.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single cave; protected location fields require ViewExactLocation.");
        caves.MapGet("/{id:guid}/summary", GetSummaryAsync)
            .WithSummary("Cave header data: related-record counts, main entrance, caller capabilities.");
        caves.MapPost("/", CreateAsync).WithValidation<CaveWriteRequest>()
            .WithSummary("Creates a cave (Create permission in the target context); the caller becomes owner.");
        caves.MapPut("/{id:guid}", UpdateAsync).WithValidation<CaveWriteRequest>()
            .WithSummary("Full update (Write permission).");
        caves.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Soft delete of the cave and its subtree (Delete permission).");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<CaveListItemDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> accessOptions,
        int? page,
        int? pageSize,
        string? sort,
        long? caveTypeId,
        string? region,
        string? search,
        decimal? minLength,
        string? bbox,
        string? tag,
        bool? unplaced,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.Features.AsNoTracking()
            .Include(f => f.Cave)
            .Where(f => f.Kind == FeatureKind.Cave)
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(f => db.Taggings.Any(tg =>
                tg.FeatureId == f.Id && db.Tags.Any(t => t.Id == tg.TagId && t.Slug == tag)));
        }

        if (caveTypeId is not null)
        {
            query = query.Where(f => f.Cave!.CaveTypeId == caveTypeId);
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            query = query.Where(f => f.Cave!.Region != null && EF.Functions.ILike(f.Cave!.Region, $"%{region}%"));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(f =>
                (f.Name != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(f.Name), EF.Functions.Unaccent(pattern)))
                || (f.Cave!.OtherToponyms != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(f.Cave!.OtherToponyms), EF.Functions.Unaccent(pattern))));
        }

        if (minLength is not null)
        {
            query = query.Where(f => f.Cave!.SurveyedLength >= minLength);
        }

        // Caves with no position at all, or only the ones that have one. A cave's point is the
        // cache of its main entrance, so a cave with neither is on no map and inside no bounding
        // box — findable here and nowhere else. That used to be a rare accident; a cave imported
        // from a register that publishes no coordinates arrives that way by nature, so there has
        // to be a way to list them and work through them.
        if (unplaced is { } wantUnplaced)
        {
            query = wantUnplaced ? query.Where(f => f.Geom == null) : query.Where(f => f.Geom != null);
        }

        if (Bbox.TryParse(bbox, out var box))
        {
            var polygon = box.ToPolygon();
            query = query.Where(f => f.Geom != null && f.Geom.Intersects(polygon));
        }

        query = ApplySort(query, sort);

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);

        // Exact view is decided per row against every protected root above it.
        var exactIds = await protection.ExactViewIdsAsync(ctx, [.. rows.Select(f => f.Id)], ct);
        var grid = accessOptions.Value.LocationGridMeters;
        var items = rows.Select(f => f.ToListItem(exactIds.Contains(f.Id), grid)).ToList();
        return TypedResults.Ok(new PagedResult<CaveListItemDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<CaveDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await CaveFeatureAsync(db.Features.AsNoTracking(), id, ct);
        if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
        {
            // Existence of a cave the caller cannot read is not disclosed.
            return ApiProblems.NotFound("cave.not_found");
        }

        var exact = (await protection.ExactViewIdsAsync(ctx, [feature.Id], ct)).Contains(feature.Id);
        var parents = await ParentsAsync(db, ctx!, feature.Id, ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.Features, feature.Id, ct);
        return TypedResults.Ok(feature.ToDto(exact, accessOptions.Value.LocationGridMeters, parents));
    }

    private static async Task<Results<Ok<CaveSummaryDto>, ProblemHttpResult>> GetSummaryAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AssociationDisclosure associations,
        IFileAccessTokenService tokens,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await CaveFeatureAsync(db.Features.AsNoTracking(), id, ct);
        if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        var centerlineCount = await db.Centerlines.CountAsync(c => c.CaveFeatureId == id, ct);
        var surveyModelCount = await db.SurveyModels.CountAsync(s => s.CaveFeatureId == id, ct);

        // Counted the same way the list of them is filtered, through both of the rules that
        // filter it, and for the same reason: a number that disagreed with the list would say
        // exactly what the list declined to. That is two separate declinings — a rule written
        // against a document keeps it out for this caller, and a guarded position keeps the
        // pairing back — and a count honouring one of them would still be an announcement of
        // the other.
        var attachmentRows = await (from a in db.Attachments.AsNoTracking()
                                    where a.FeatureId == id
                                    join f in db.StoredFiles.AsNoTracking() on a.FileId equals f.Id
                                    join v in db.DocumentVersions.AsNoTracking() on f.DocumentVersionId equals v.Id
                                    join d in db.Documents.AsNoTracking() on v.DocumentId equals d.Id
                                    select new
                                    {
                                        a.Id,
                                        a.IsPrimary,
                                        a.Caption,
                                        FileId = f.Id,
                                        f.Kind,
                                        Document = d,
                                        HasOwnPosition = f.Geom != null,
                                    })
            .ToListAsync(ct);

        // Every row here names this cave, which the caller was authorised for a few lines
        // above — so reach through the attachment is established and the walk needs no query.
        var cabinetReach = await DocumentAccessRules.CabinetReachAsync(
            db, ctx!, AccessAction.Read, [.. attachmentRows.Select(r => r.Document.Id)], ct);
        var readableRows = attachmentRows
            .Where(r => DocumentAccessRules.AllowedByOwnRulesOrAttachment(
                ctx!, r.Document, AccessAction.Read, cabinetReach))
            .ToList();
        var withheldAttachments = await associations.WithheldIdsAsync(
            ctx,
            [.. readableRows.Select(r => new AssociationCandidate(r.Id, new FeatureAssociation(id, r.HasOwnPosition)))],
            ct);
        var attachmentCount = readableRows.Count - withheldAttachments.Count;

        // Drawn from the rows that survived both filters rather than from the attachment table:
        // a headline picture is the most visible thing a cave has, so one that outlived the
        // caller's read rule would be the loudest possible way to leak it.
        var headlineRow = readableRows.FirstOrDefault(
            r => r.IsPrimary && r.Kind == FileKind.Image && !withheldAttachments.Contains(r.Id));
        var headline = headlineRow is null
            ? null
            : new CaveHeadlinePictureDto(
                headlineRow.Id,
                headlineRow.Document.Id,
                headlineRow.FileId,
                // A derivatives-only token by construction, not by decision: this URL is handed
                // to everybody who may see the cave, and the picture's own bytes are a separate
                // question answered on the file's own route.
                $"/api/v1/files/{headlineRow.FileId}/thumbnail?size=480&token="
                    + Uri.EscapeDataString(tokens.CreateToken(headlineRow.FileId, FileDelivery.DerivativesOnly)),
                headlineRow.Caption);

        // Counted the same way the list of them is filtered, through both of the rules that
        // filter it. A caller who may read this cave but not place it is answered with an empty
        // page when they ask for its trips, because the trips carry their own geometries and
        // listing them would place the cave; a count taken past that rule would tell the same
        // caller exactly how many trips there are above a list showing none of them.
        //
        // Every role counts and each trip counts once: two roles naming this cave on one trip
        // are two ways of saying the trip went there, not two trips.
        var tripLogCount = 0;
        if (!await protection.ShouldRedactLinkAsync(ctx, id, ct))
        {
            var namingTrips = TripRoleLinks.TripIdsNaming(db, id);
            tripLogCount = await db.TripLogs.AsNoTracking()
                .VisibleTo(ctx!, AccessDomain.TripLogs)
                .CountAsync(t => namingTrips.Contains(t.Id), ct);
        }

        var main = await db.CaveEntrances.AsNoTracking()
            .Include(e => e.Feature)
            .FirstOrDefaultAsync(e => e.CaveFeatureId == id && e.IsMain, ct);

        var exactIds = await protection.ExactViewIdsAsync(ctx, main is null ? [id] : [id, main.Id], ct);
        var mainDto = main is null
            ? null
            : new CaveMainEntranceDto(
                main.Id,
                main.Feature.Name,
                CaveMapping.MapGeom(main.Feature.Geom, exactIds.Contains(main.Id), accessOptions.Value.LocationGridMeters),
                ApproximateLocation: !exactIds.Contains(main.Id));

        var effective = await access.EffectiveAsync(ctx, feature, ct);
        var caps = new CavePermissionsDto(
            CanWrite: effective.HasFlag(AccessAction.Write),
            CanDelete: effective.HasFlag(AccessAction.Delete),
            CanShare: effective.HasFlag(AccessAction.Share),
            CanManagePermissions: effective.HasFlag(AccessAction.ManagePermissions),
            // The real multi-root decision, not the row-local ACL flag: an ancestor's
            // protection vetoes exact view even when the row itself grants it.
            CanViewExactLocation: exactIds.Contains(id));

        return TypedResults.Ok(new CaveSummaryDto(
            feature.Id, feature.Name ?? string.Empty, feature.Cave!.EntranceCount,
            centerlineCount, surveyModelCount, attachmentCount, tripLogCount, mainDto, caps,
            headline));
    }

    private static async Task<Results<Created<CaveDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        CaveWriteRequest request,
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await CavingGroupBindingAllowedAsync(db, ctx, request.CavingGroupId, ct))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        IReadOnlyList<ParentSpec> parents = [];
        var parentFacts = await CreateContext.ParentCreateFactsAsync(
            db, ctx, request.ParentId, request.CavingGroupId, FeatureKind.Cave, ct);
        if (request.ParentId is { } parentId)
        {
            // The parent must exist and be readable by the caller — a body reference must
            // never confirm the existence of rows the caller cannot see.
            if (parentFacts is null)
            {
                return ApiProblems.BadRequest("cave.parent_not_found");
            }

            parents = [new ParentSpec(parentId, IsPrimary: true)];
        }

        // Creation is decided by the walk against that target context, so a Create deny
        // bites and a subtree- or club-scoped Create reaches exactly where it should.
        if (!CreateRules.MayCreate(ctx, AccessDomain.Features, parentFacts
            ?? AccessTargetFacts.ForCreate(null, [], request.CavingGroupId, FeatureKind.Cave)))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        var feature = new Feature
        {
            OwnerUserId = user.UserId,
            // Safe to set directly on create: the write service recomputes the subtree's
            // effective protection while establishing the hierarchy edges.
            LocationProtected = request.LocationProtected,
        };
        var cave = new Cave();
        feature.Cave = cave;
        request.Apply(feature, cave);

        try
        {
            await writer.CreateCaveAsync(feature, cave, parents, ct);
            if (request.Properties is { ValueKind: JsonValueKind.Object })
            {
                await writer.ValidatePropertiesAsync(feature, ct);
            }
        }
        catch (FeatureWriteException ex)
        {
            return ApiProblems.BadRequest(ex.Code, string.Join("; ", ex.Errors));
        }

        await db.SaveChangesAsync(ct);
        var parentDtos = await ParentsAsync(db, ctx, feature.Id, ct);
        // The creator owns the row, so the mapping is exact by construction.
        return TypedResults.Created(
            $"/api/v1/caves/{feature.Id}",
            feature.ToDto(exact: true, accessOptions.Value.LocationGridMeters, parentDtos));
    }

    private static async Task<Results<Ok<CaveDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CaveWriteRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        FeatureWriteService writer,
        IAccessContextAccessor accessAccessor,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await CaveFeatureAsync(db.Features, id, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Write, feature, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("cave.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct, required: true) is { } stale)
        {
            return stale;
        }

        if (request.CavingGroupId != feature.CavingGroupId && !await CavingGroupBindingAllowedAsync(db, ctx, request.CavingGroupId, ct))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        var cave = feature.Cave!;

        // Write-path protection guard: a caller without exact view only ever saw
        // obfuscated values (null address/registry/notes), so ignore any change they
        // submit to those fields — a full-replace PUT would otherwise write the
        // obfuscated echo back over the real data.
        var exact = (await protection.ExactViewIdsAsync(ctx, [feature.Id], ct)).Contains(feature.Id);
        var preserved = (cave.ClosestAddress, cave.LandRegistryNumber, cave.LocationNotes);

        request.Apply(feature, cave);

        if (!exact)
        {
            (cave.ClosestAddress, cave.LandRegistryNumber, cave.LocationNotes) = preserved;
        }

        try
        {
            // The protection flag itself is preserved for non-exact callers: they must not
            // be able to clear the very protection that redacts what they see. Flips go
            // through the write service, which restamps the whole subtree.
            if (exact && request.LocationProtected != feature.LocationProtected)
            {
                await writer.SetLocationProtectedAsync(feature.Id, request.LocationProtected, ct);
            }

            if (request.Properties is { ValueKind: JsonValueKind.Object })
            {
                await writer.ValidatePropertiesAsync(feature, ct);
            }
        }
        catch (FeatureWriteException ex)
        {
            return ApiProblems.BadRequest(ex.Code, string.Join("; ", ex.Errors));
        }

        await db.SaveChangesAsync(ct);
        await Concurrency.EmitETagAsync(http, db, VersionedTable.Features, feature.Id, ct);
        var parents = await ParentsAsync(db, ctx, feature.Id, ct);
        return TypedResults.Ok(feature.ToDto(exact, accessOptions.Value.LocationGridMeters, parents));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        FeatureWriteService writer,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var feature = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);
        if (feature is null)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, feature, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("cave.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Features, feature.Id, ct) is { } stale)
        {
            return stale;
        }

        // One stamp over the cave and its containment subtree (entrances, centerlines),
        // so a later restore undoes exactly this deletion.
        await writer.SoftDeleteAsync(feature.Id, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static Task<Feature?> CaveFeatureAsync(IQueryable<Feature> features, Guid id, CancellationToken ct) =>
        features.Include(f => f.Cave).FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);

    /// <summary>Breadcrumb data: the containment parents the caller may see, primary edge first.</summary>
    private static async Task<IReadOnlyList<CaveParentDto>> ParentsAsync(
        SilexGisDbContext db, AccessContext ctx, Guid featureId, CancellationToken ct)
    {
        var rows = await db.FeatureHierarchyEdges.AsNoTracking()
            .Where(e => e.ChildId == featureId)
            .Join(
                db.Features.VisibleTo(ctx, db.Features, db.FeatureSetMembers),
                e => e.ParentId,
                f => f.Id,
                (e, f) => new { f.Id, f.Name, e.IsPrimary })
            .ToListAsync(ct);
        return [.. rows
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => new CaveParentDto(x.Id, x.Name, x.IsPrimary))];
    }

    private static async Task<bool> CavingGroupBindingAllowedAsync(
        SilexGisDbContext db, AccessContext ctx, Guid? cavingGroupId, CancellationToken ct)
    {
        if (cavingGroupId is null || ctx.IsFullAdmin)
        {
            return cavingGroupId is null || await db.CavingGroups.AnyAsync(t => t.Id == cavingGroupId, ct);
        }

        return CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, cavingGroupId.Value);
    }

    private static IQueryable<Feature> ApplySort(IQueryable<Feature> query, string? sort) =>
        sort switch
        {
            "name" => query.OrderBy(f => f.Name),
            "-name" => query.OrderByDescending(f => f.Name),
            "surveyedLength" => query.OrderBy(f => f.Cave!.SurveyedLength),
            "-surveyedLength" => query.OrderByDescending(f => f.Cave!.SurveyedLength),
            "depth" => query.OrderBy(f => f.Cave!.Depth),
            "-depth" => query.OrderByDescending(f => f.Cave!.Depth),
            "region" => query.OrderBy(f => f.Cave!.Region),
            "-region" => query.OrderByDescending(f => f.Cave!.Region),
            "updatedAt" => query.OrderBy(f => f.UpdatedAt),
            "-updatedAt" or null or "" => query.OrderByDescending(f => f.UpdatedAt),
            _ => query.OrderByDescending(f => f.UpdatedAt),
        };
}
