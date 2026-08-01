// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
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
    Guid? TeamId,
    Visibility Visibility,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The anonymous shared-view payload: name + config only, nothing else leaks.</summary>
public sealed record SharedViewDto(string Name, JsonElement Config);

public sealed record MapViewWriteRequest(
    string Name,
    string? Description,
    JsonElement Config,
    bool IsHome,
    Guid? TeamId,
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

        // The share token IS the credential — this endpoint is on the anonymous
        // allow-list and returns only the view name and config document.
        api.MapGet("/shared/views/{token:guid}", SharedAsync)
            .WithTags("MapViews")
            .AllowAnonymous()
            .WithSummary("Read-only shared view by token (anonymous).");

        return api;
    }

    private static async Task<Results<Ok<List<MapViewDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var views = await db.MapViews.AsNoTracking()
            .VisibleTo(user, db.ObjectAcls, AttachedEntityType.MapView)
            .OrderByDescending(x => x.IsHome).ThenBy(x => x.Name)
            .ToListAsync(ct);
        return TypedResults.Ok(views.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<MapViewDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        MapViewWriteRequest request,
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (request.TeamId is not null && !user.IsAdmin && !user.IsMemberOf(request.TeamId.Value))
        {
            return ApiProblems.Forbidden("map_view.team_membership_required");
        }

        var view = new MapView { Name = request.Name, OwnerUserId = user.UserId };
        Apply(view, request);
        db.MapViews.Add(view);
        if (request.IsHome)
        {
            await ClearOtherHomesAsync(db, user.UserId, view.Id, ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/map-views/{view.Id}", ToDto(view));
    }

    private static async Task<Results<Ok<MapViewDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        MapViewWriteRequest request,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var view = await db.MapViews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, view, ObjectPermission.Write, ct))
        {
            return await permissions.CanAsync(user, view, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("map_view.not_found");
        }

        Apply(view, request);
        if (request.IsHome)
        {
            await ClearOtherHomesAsync(db, user.UserId, view.Id, ct);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(view));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var view = await db.MapViews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, view, ObjectPermission.Delete, ct))
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        db.MapViews.Remove(view);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MapViewDto>, UnauthorizedHttpResult, ProblemHttpResult>> ShareAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var view = await db.MapViews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, view, ObjectPermission.Share, ct))
        {
            return await permissions.CanAsync(user, view, ObjectPermission.Read, ct)
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
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var view = await db.MapViews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, view, ObjectPermission.Share, ct))
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
        CancellationToken ct)
    {
        var view = await db.MapViews.AsNoTracking().FirstOrDefaultAsync(x => x.ShareToken == token, ct);
        if (view is null)
        {
            return ApiProblems.NotFound("map_view.not_found");
        }

        // Name + config only. The config references layers by id; the map endpoints the
        // shared page calls still enforce visibility and location protection themselves —
        // a share token never widens data access beyond this document.
        return TypedResults.Ok(new SharedViewDto(view.Name, JsonSerializer.Deserialize<JsonElement>(view.Config)));
    }

    private static void Apply(MapView view, MapViewWriteRequest request)
    {
        view.Name = request.Name;
        view.Description = request.Description;
        view.Config = request.Config.GetRawText();
        view.IsHome = request.IsHome;
        view.TeamId = request.TeamId;
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
        x.ShareToken, x.IsHome, x.OwnerUserId, x.TeamId, x.Visibility, x.CreatedAt, x.UpdatedAt);
}
