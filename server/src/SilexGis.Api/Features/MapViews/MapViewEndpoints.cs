// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Map;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.MapViews;

public sealed record MapViewDto(
    Guid Id,
    string Name,
    string? Description,
    JsonElement Config,
    Guid? ShareToken,
    bool IsHome,
    Guid OwnerUserId,
    Guid? CavingGroupId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The anonymous shared-view payload: the view's name and document, the background to draw it
/// over, and which data overlays the link draws. Nothing about who saved it.
/// </summary>
/// <param name="Background">
/// The background the view was saved with, resolved into what a page with no account needs to draw
/// it — or the installation's default when that one needs an access key, is switched off, or is
/// not a background any more; null when the installation has no background a link may use.
/// </param>
/// <param name="Overlays">Which data overlays this link draws.</param>
public sealed record SharedViewDto(
    string Name, JsonElement Config, SharedViewBackgroundDto? Background, SharedViewOverlaysDto Overlays);

/// <summary>
/// A background a shared view's page draws. Only a source that needs no access key is ever one:
/// a key would be handed to whoever holds the link.
/// </summary>
public sealed record SharedViewBackgroundDto(
    string Name,
    MapLayerKind LayerKind,
    string UrlTemplate,
    MapLayerWmsOptions? Wms,
    MapLayerWmtsOptions? Wmts,
    string? Attribution,
    int MinZoom,
    int MaxZoom);

/// <param name="Entrances">The cave entrances overlay is drawn: the view had it on, and the installation allows it.</param>
/// <param name="Features">The surface features overlay is drawn, on the same two conditions.</param>
public sealed record SharedViewOverlaysDto(bool Entrances, bool Features);

public sealed record MapViewWriteRequest(
    string Name,
    string? Description,
    JsonElement Config,
    bool IsHome,
    Guid? CavingGroupId,
    Visibility Visibility);

public sealed class MapViewWriteRequestValidator : AbstractValidator<MapViewWriteRequest>
{
    public MapViewWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Config)
            .Must(c => c.ValueKind == JsonValueKind.Object)
            .WithMessage("Config must be a JSON object.");
    }
}

public static class MapViewEndpoints
{
    public static RouteGroupBuilder MapMapViewEndpoints(this RouteGroupBuilder api)
    {
        var views = api.MapGroup("/map-views").WithTags("MapViews");

        views.MapGet("/", ListAsync).WithSummary("The caller's visible saved views.");
        views.MapPost("/", CreateAsync).WithValidation<MapViewWriteRequest>()
            .WithSummary("Saves a view; the caller becomes owner.");
        views.MapPut("/{id:guid}", UpdateAsync).WithValidation<MapViewWriteRequest>()
            .WithSummary("Updates a saved view (Write permission).");
        views.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a saved view (Delete permission).");
        views.MapPost("/{id:guid}/share", ShareAsync)
            .WithSummary("Mints (or returns) the view's share token (Share permission).");
        views.MapDelete("/{id:guid}/share", UnshareAsync)
            .WithSummary("Revokes the share token.");

        // The share token IS the credential — this endpoint is on the anonymous allow-list and
        // returns the view's name and document, its background, and which overlays it draws.
        api.MapGet("/shared/views/{token:guid}", SharedAsync)
            .WithTags("MapViews")
            .AllowAnonymous()
            .RequireRateLimiting(SharedViewRateLimits.PolicyName)
            .WithSummary("Read-only shared view by token (anonymous): its document, a keyless background and which data overlays it draws.");

        return api;
    }

    private static async Task<Results<Ok<List<MapViewDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var views = await db.MapViews.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.MapViews)
            .OrderByDescending(x => x.IsHome).ThenBy(x => x.Name)
            .ToListAsync(ct);
        return TypedResults.Ok(views.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<MapViewDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        MapViewWriteRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.MapViews, request.CavingGroupId))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
        }

        if (request.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.MapViews, request.CavingGroupId.Value))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        var view = new MapView { Name = request.Name, OwnerUserId = ctx.UserId };
        Apply(view, request);
        db.MapViews.Add(view);
        if (request.IsHome)
        {
            await ClearOtherHomesAsync(db, ctx.UserId, view.Id, ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/map-views/{view.Id}", ToDto(view));
    }

    private static async Task<Results<Ok<MapViewDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        MapViewWriteRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var view = await db.MapViews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Write, view, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, view, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("map_view.not_found");
        }

        // Re-binding on update moves rights exactly as binding on create does; Write on
        // the row is not consent to hand it to a club.
        if (request.CavingGroupId is { } requested
            && requested != view.CavingGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.MapViews, requested))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        Apply(view, request);
        if (request.IsHome)
        {
            await ClearOtherHomesAsync(db, ctx.UserId, view.Id, ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(view));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var view = await db.MapViews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, view, ct)).Allowed)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        // Resource-link members naming the view have no FK; they go with it.
        await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.MapView && m.EntityId == view.Id)
            .ExecuteDeleteAsync(ct);
        db.MapViews.Remove(view);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MapViewDto>, UnauthorizedHttpResult, ProblemHttpResult>> ShareAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var view = await db.MapViews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Share, view, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, view, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("map_view.not_found");
        }

        // Idempotent: an existing token is reused so shared links stay stable.
        view.ShareToken ??= Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(view));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnshareAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var view = await db.MapViews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Share, view, ct)).Allowed)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        view.ShareToken = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SharedViewDto>, ProblemHttpResult>> SharedAsync(
        Guid token,
        SilexGisDbContext db,
        IOptions<MapOptions> mapOptions,
        CancellationToken ct)
    {
        var view = await db.MapViews.AsNoTracking().FirstOrDefaultAsync(x => x.ShareToken == token, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        // The data the link draws comes from its own token-scoped routes, which answer for the
        // least-privileged reader there is; this answer only says which of them to ask.
        var config = SharedViewConfig.Read(view.Config);
        var dataOn = mapOptions.Value.SharedViewData;
        var anyAccount = await AccessContextResolver.ResolveForAnyAccountAsync(db, ct);
        return TypedResults.Ok(new SharedViewDto(
            view.Name,
            JsonSerializer.Deserialize<JsonElement>(view.Config),
            await SharedBackgroundAsync(db, anyAccount, config.BaseLayerId, ct),
            new SharedViewOverlaysDto(dataOn && config.EntrancesVisible, dataOn && config.SurfaceFeaturesVisible)));
    }

    /// <summary>
    /// The background a link draws: the view's own when a page with no account may use it, the
    /// installation's default otherwise, and the first one that may be used after that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A source that needs an access key is never one, because the key is in its address and the
    /// address goes to whoever holds the link. The view's own choice falls back rather than failing:
    /// a view saved over a keyed basemap still opens, over a background anybody may be sent.
    /// </para>
    /// <para>
    /// Read as the least-privileged reader: an installation that takes the catalogue away from its
    /// accounts has taken it from the link as well.
    /// </para>
    /// </remarks>
    private static async Task<SharedViewBackgroundDto?> SharedBackgroundAsync(
        SilexGisDbContext db, AccessContext anyAccount, long? chosenId, CancellationToken ct)
    {
        if (!AccessEvaluator.Decide(anyAccount, AccessDomain.MapLayers, AccessAction.Read, null).Allowed)
        {
            return null;
        }

        var usable = (await db.MapLayers.AsNoTracking()
                .Where(l => l.Enabled && l.IsBase && l.ApiKeyName == null)
                .OrderBy(l => l.SortOrder)
                .ToListAsync(ct))
            .Where(l => MapLayerOptionsJson.IsDrawable(l.LayerKind, l.Options))
            .ToList();
        var layer = usable.FirstOrDefault(l => l.Id == chosenId)
            ?? usable.FirstOrDefault(l => l.IsDefault)
            ?? usable.FirstOrDefault();
        return layer is null
            ? null
            : new SharedViewBackgroundDto(
                layer.Name,
                layer.LayerKind,
                layer.UrlTemplate,
                MapLayerOptionsJson.Wms(layer.LayerKind, layer.Options),
                MapLayerOptionsJson.Wmts(layer.LayerKind, layer.Options),
                layer.Attribution,
                layer.MinZoom,
                layer.MaxZoom);
    }

    private static void Apply(MapView view, MapViewWriteRequest request)
    {
        view.Name = request.Name;
        view.Description = request.Description;
        view.Config = request.Config.GetRawText();
        view.IsHome = request.IsHome;
        view.CavingGroupId = request.CavingGroupId;
        view.Visibility = request.Visibility;
    }

    /// <summary>One home view per user.</summary>
    private static async Task ClearOtherHomesAsync(
        SilexGisDbContext db, Guid userId, Guid exceptViewId, CancellationToken ct)
    {
        await db.MapViews
            .Where(x => x.OwnerUserId == userId && x.IsHome && x.Id != exceptViewId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsHome, false), ct);
    }

    private static MapViewDto ToDto(MapView x) => new(
        x.Id, x.Name, x.Description,
        JsonSerializer.Deserialize<JsonElement>(x.Config),
        x.ShareToken, x.IsHome, x.OwnerUserId, x.CavingGroupId, x.Visibility, x.CreatedAt, x.UpdatedAt);
}
