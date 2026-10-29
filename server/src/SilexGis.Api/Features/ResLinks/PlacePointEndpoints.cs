// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.RasterMaps;

namespace SilexGis.Api.Features.ResLinks;

/// <summary>
/// Pins a place on a phone's map, from the browser: a point measured on the picture as it is
/// drawn, in fractions of its width and height.
/// </summary>
/// <param name="DocumentId">The map's picture document.</param>
/// <param name="PlaceId">The place to pin, inside the map's cave.</param>
/// <param name="FileId">The file the point was measured on, which has to be the map's current
/// original — the file a phone measures on.</param>
/// <param name="X">Across the picture as drawn, 0 to 1.</param>
/// <param name="Y">Down the picture as drawn, 0 to 1.</param>
public sealed record PlacePointCreateRequest(Guid DocumentId, Guid PlaceId, Guid FileId, double X, double Y);

/// <summary>Moves a place pinned on a phone's map to a point of the map's current picture.</summary>
/// <param name="FileId">The file the point was measured on: the map's current original.</param>
/// <param name="X">Across the picture as drawn, 0 to 1.</param>
/// <param name="Y">Down the picture as drawn, 0 to 1.</param>
public sealed record PlacePointMoveRequest(Guid FileId, double X, double Y);

public sealed class PlacePointCreateRequestValidator : AbstractValidator<PlacePointCreateRequest>
{
    public PlacePointCreateRequestValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.PlaceId).NotEmpty();
        RuleFor(x => x.FileId).NotEmpty();
        RuleFor(x => x.X).InclusiveBetween(0, 1);
        RuleFor(x => x.Y).InclusiveBetween(0, 1);
    }
}

public sealed class PlacePointMoveRequestValidator : AbstractValidator<PlacePointMoveRequest>
{
    public PlacePointMoveRequestValidator()
    {
        RuleFor(x => x.FileId).NotEmpty();
        RuleFor(x => x.X).InclusiveBetween(0, 1);
        RuleFor(x => x.Y).InclusiveBetween(0, 1);
    }
}

/// <summary>
/// The browser's side of the places pinned on phones' maps: pinning a place, moving a pin, and —
/// through the ordinary link delete — removing one.
/// </summary>
/// <remarks>
/// <para>
/// A pin is part of its map, so writing one takes what a phone needs: the right to change the map's
/// cave. A pin the caller is not shown — the whole-link rule — answers as one that is not there,
/// before anything else is asked.
/// </para>
/// <para>
/// The browser measures in fractions of the picture as it is drawn; a phone measures in pixels of
/// the picture as stored. The point is converted once, here, by the one conversion there is, and
/// stored as the pixel a phone reads back — so it must be measured on the file a phone measures on,
/// and on a picture whose size is known. The fraction kept beside the pixel is the pixel's centre,
/// within half a pixel of the click.
/// </para>
/// <para>
/// A pin the browser makes is one the phones adopt as their own: it has an ordinary identifier, one
/// live pin per place on a map holds for it as for an upload, and the rasters stream hands it to a
/// phone exactly when it hands its place.
/// </para>
/// </remarks>
public static class PlacePointEndpoints
{
    public const string MapNotFoundCode = "reslink.place_point.map_not_found";
    public const string PlaceNotFoundCode = "reslink.place_point.place_not_found";
    public const string PlacePinnedCode = "reslink.place_point.place_pinned";
    public const string NotCurrentFileCode = "reslink.place_point.not_current_file";
    public const string FrameUnknownCode = "reslink.place_point.frame_unknown";

    public static RouteGroupBuilder MapPlacePointEndpoints(this RouteGroupBuilder links)
    {
        links.MapPost("/place-points", CreateAsync).WithValidation<PlacePointCreateRequest>()
            .WithSummary("Pins a place on a phone's map at a point of its current picture (whoever may change the map's cave).");
        links.MapPut("/{id:guid}/place-point", MoveAsync).WithValidation<PlacePointMoveRequest>()
            .WithSummary("Moves a place pinned on a phone's map to a point of its current picture; the phones receive it under the same identifier.");
        return links;
    }

    private static async Task<Results<Created<ResLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        PlacePointCreateRequest request,
        SilexGisDbContext db,
        PlacePointStore store,
        ResLinkTargetDirectory targets,
        AssociationDisclosure associations,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // A map the caller may not read is answered as one that is not there.
        var map = await db.RasterMaps.AsNoTracking().FirstOrDefaultAsync(m => m.DocumentId == request.DocumentId, ct);
        var cave = map is null ? null : await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == map.CaveFeatureId, ct);
        if (map is null || cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound(MapNotFoundCode);
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed)
        {
            return ApiProblems.Forbidden(detail: "Pinning a place on a map takes the right to change its cave.");
        }

        // A place inside the map's cave that the caller may read and see exactly: pinning a place
        // on a map of the cave states where it is, which is not for somebody who may not know.
        var place = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .FirstOrDefaultAsync(f => f.Id == request.PlaceId, ct);
        if (place is null
            || !place.AncestorIds.Contains(cave.Id)
            || !(await protection.ExactViewIdsAsync(ctx, [place.Id], ct)).Contains(place.Id))
        {
            return ApiProblems.NotFound(PlaceNotFoundCode);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await store.LockAsync(map.Id, ct);
        if (await store.LivePinOfAsync(map.Id, place.Id, Guid.Empty, ct))
        {
            return ApiProblems.Conflict(PlacePinnedCode, "That place is already pinned on this map.");
        }

        var (picture, problem) = await MeasuredOnAsync(store, map, request.FileId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (px, py) = PlacePointFrame.FromFraction(PictureFrames.Of(picture!)!.Value, request.X, request.Y);
        var link = await store.AddAsync(map, picture!, Guid.CreateVersion7(), place.Id, px, py, ctx.UserId, ct);
        await ResLinkEndpoints.SaveWithFreshShortCodeAsync(db, link, ct);
        await tx.CommitAsync(ct);

        var dto = (await ResLinkEndpoints.ProjectAsync(
            db, targets, associations, protection, photoPositions, access, ctx, [link], ct)).Single();
        return TypedResults.Created($"/api/v1/reslinks/{link.Id}", dto);
    }

    private static async Task<Results<Ok<ResLinkDto>, UnauthorizedHttpResult, ProblemHttpResult>> MoveAsync(
        Guid id,
        PlacePointMoveRequest request,
        SilexGisDbContext db,
        PlacePointStore store,
        PlacePointDisclosure placePoints,
        ResLinkTargetDirectory targets,
        AssociationDisclosure associations,
        FeatureProtection protection,
        PhotoPositionDisclosure photoPositions,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var standing = await ResLinkStanding.Of(db).AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (standing is null
            || !await ResLinkEndpoints.IsPlacePointAsync(db, standing, ct)
            || await placePoints.IsHiddenAsync(ctx, id, ct))
        {
            return ApiProblems.NotFound(ResLinkEndpoints.NotFoundCode);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pin = await store.FindAsync(id, ct);
        if (pin is null || pin.Map.DeletedAt is not null)
        {
            return ApiProblems.NotFound(ResLinkEndpoints.NotFoundCode);
        }

        await store.LockAsync(pin.Map.Id, ct);
        if (!await MayWriteCaveAsync(db, access, ctx, pin.Map.CaveFeatureId, ct))
        {
            return ApiProblems.Forbidden(detail: "Moving a place pinned on a map takes the right to change its cave.");
        }

        var (picture, problem) = await MeasuredOnAsync(store, pin.Map, request.FileId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (px, py) = PlacePointFrame.FromFraction(PictureFrames.Of(picture!)!.Value, request.X, request.Y);
        await store.MoveAsync(pin, px, py, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return TypedResults.Ok((await ResLinkEndpoints.ProjectAsync(
            db, targets, associations, protection, photoPositions, access, ctx, [pin.Link], ct)).Single());
    }

    /// <summary>
    /// A place pin removed from the browser: stamped, so the phones are told it went, by whoever
    /// may change the map's cave. The caller has already been shown the pin.
    /// </summary>
    internal static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RemoveAsync(
        SilexGisDbContext db, PlacePointStore store, IAccessService access, AccessContext ctx, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pin = await store.FindAsync(id, ct);
        if (pin is null)
        {
            return ApiProblems.NotFound(ResLinkEndpoints.NotFoundCode);
        }

        await store.LockAsync(pin.Map.Id, ct);
        if (!await MayWriteCaveAsync(db, access, ctx, pin.Map.CaveFeatureId, ct))
        {
            return ApiProblems.Forbidden(detail: "Removing a place pinned on a map takes the right to change its cave.");
        }

        PlacePointStore.Remove(pin, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Of these place pins, the ones this caller may move or remove: those on maps of caves the
    /// caller may change. Asked once per cave, never once per pin.
    /// </summary>
    internal static async Task<HashSet<Guid>> WritableAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, IReadOnlyCollection<Guid> pinIds, CancellationToken ct)
    {
        if (pinIds.Count == 0)
        {
            return [];
        }

        var caves = await (
                from pin in PlacePointQueries.Rows(db).AsNoTracking()
                where pinIds.Contains(pin.Id)
                join map in db.RasterMaps.AsNoTracking() on pin.MapId equals map.Id
                select new { pin.Id, map.CaveFeatureId })
            .ToListAsync(ct);
        var writable = new HashSet<Guid>();
        foreach (var cave in caves.Select(c => c.CaveFeatureId).Distinct())
        {
            if (await MayWriteCaveAsync(db, access, ctx, cave, ct))
            {
                writable.Add(cave);
            }
        }

        return [.. caves.Where(c => writable.Contains(c.CaveFeatureId)).Select(c => c.Id)];
    }

    private static async Task<bool> MayWriteCaveAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid caveId, CancellationToken ct)
    {
        var cave = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == caveId, ct);
        return cave is not null && (await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed;
    }

    /// <summary>
    /// The map's current original, when it is the file the browser measured on and its size is
    /// known; otherwise why a point measured there cannot be stored as a phone's pixel.
    /// </summary>
    private static async Task<(StoredFile? Picture, ProblemHttpResult? Problem)> MeasuredOnAsync(
        PlacePointStore store, RasterMap map, Guid fileId, CancellationToken ct)
    {
        var picture = await store.PictureOfAsync(map, ct);
        if (picture is null || picture.Id != fileId)
        {
            return (null, ApiProblems.Conflict(NotCurrentFileCode,
                "The point was measured on a picture that is no longer the map's current one."));
        }

        return PictureFrames.Of(picture) is null
            ? (null, ApiProblems.Conflict(FrameUnknownCode,
                "The size of this picture is not known, so a point on it cannot be given to the phones."))
            : (picture, null);
    }
}
