// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Storage;
using SilexGis.Infrastructure.Storage;

namespace SilexGis.Api.Features.Storage;

/// <summary>
/// How much room there is, asked before anything is sent: the installation's total, the scans'
/// total, the account's own figure, and whether what is about to be added fits under all of them.
/// </summary>
/// <param name="AddingBytes">What the request said it is about to add (zero when it said nothing).</param>
/// <param name="Fits">Whether that fits under every ceiling that applies.</param>
/// <param name="Shortfall">When it does not fit: the ceiling missing most, and by how much.</param>
/// <param name="Level">How full the installation is: <c>normal</c>, <c>notice</c>, <c>warning</c> or <c>full</c>.</param>
public sealed record StorageRoomDto(
    long AddingBytes,
    bool Fits,
    StorageShortfallDto? Shortfall,
    long StoreUsedBytes,
    long? StoreCapBytes,
    long ScanUsedBytes,
    long? ScanCapBytes,
    long AccountUsedBytes,
    long? AccountCapBytes,
    string Level);

/// <param name="Limit">The ceiling: <c>account</c>, <c>installation</c>, <c>scans</c>, <c>storeDisk</c> or <c>workerDisk</c>.</param>
public sealed record StorageShortfallDto(string Limit, long NeededBytes, long RoomBytes, long MissingBytes);

public static class StorageEndpoints
{
    public static RouteGroupBuilder MapStorageEndpoints(this RouteGroupBuilder api)
    {
        var storage = api.MapGroup("/storage").WithTags("Storage");
        storage.MapGet("/room", RoomAsync)
            .WithSummary("The storage totals and caps, and whether an upload or a processing run of a given size fits.")
            .WithDescription(
                "addingBytes is what is about to be added (an upload's file sizes, or a run's estimated products). "
                + "scan=true checks it as a point cloud's: under the scans' own cap as well, and on the processing "
                + "worker's disk when processing=true. The same check refuses the upload or the run itself, with "
                + "the code storage.insufficient and the missing amount, so this is advice given early, not a reservation.");
        return api;
    }

    private static async Task<Results<Ok<StorageRoomDto>, UnauthorizedHttpResult, ProblemHttpResult>> RoomAsync(
        StorageFiguresService figures,
        IAccessContextAccessor accessAccessor,
        long? addingBytes,
        bool? scan,
        bool? processing,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (addingBytes is < 0)
        {
            return ApiProblems.BadRequest("storage.adding_invalid", "addingBytes cannot be negative.");
        }

        var now = await figures.ForAsync(ctx.UserId, ct: ct);
        var adding = addingBytes ?? 0;
        var shortfall = StorageRules.Shortfall(now.Limits(scan ?? false, onWorkerDisk: (scan ?? false) && (processing ?? false)), adding);
        return TypedResults.Ok(new StorageRoomDto(
            adding,
            shortfall is null,
            shortfall is null
                ? null
                : new StorageShortfallDto(StorageProblems.LimitName(shortfall.Kind), shortfall.NeededBytes, shortfall.RoomBytes, shortfall.MissingBytes),
            now.StoreUsedBytes,
            now.StoreCapBytes,
            now.ScanUsedBytes,
            now.ScanCapBytes,
            now.AccountUsedBytes,
            now.AccountCapBytes,
            LevelName(now.Level)));
    }

    /// <summary>A level as answers spell it.</summary>
    public static string LevelName(StorageLevel level) => level.ToString().ToLowerInvariant();
}
