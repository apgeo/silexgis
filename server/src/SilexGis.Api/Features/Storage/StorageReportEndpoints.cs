// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Storage;
using SilexGis.Infrastructure.Storage;

namespace SilexGis.Api.Features.Storage;

/// <summary>What fills the file store, how fast it grows, and what could be deleted, as of <see cref="TakenAt"/>.</summary>
/// <param name="Level">How full the installation is: <c>normal</c>, <c>notice</c>, <c>warning</c> or <c>full</c>.</param>
/// <param name="ByFileKind">The whole store by kind of file (<c>image</c>, <c>document</c>, <c>survey</c>, …).</param>
/// <param name="ScanByKind">Point clouds' files: <c>original</c> for uploaded recordings, else what processing made (<c>point-cloud-laz-display</c>, <c>point-cloud-tiles</c>, …).</param>
/// <param name="WorkDirectoryBytes">What the processing worker's job directories hold on its own volume.</param>
public sealed record StorageReportDto(
    DateTimeOffset TakenAt,
    long StoreUsedBytes,
    long? StoreCapBytes,
    long ScanUsedBytes,
    long? ScanCapBytes,
    long? StoreFreeBytes,
    long? WorkerFreeBytes,
    long MinFreeBytes,
    string Level,
    IReadOnlyList<StorageKindTotalDto> ByFileKind,
    IReadOnlyList<StorageKindTotalDto> ScanByKind,
    long WorkDirectoryBytes,
    IReadOnlyList<StorageHolderDto> ByCave,
    IReadOnlyList<StorageHolderDto> ByAccount,
    IReadOnlyList<StorageLargeItemDto> Largest,
    StorageGrowthDto Growth,
    IReadOnlyList<StorageReclaimableDto> Reclaimable);

public sealed record StorageKindTotalDto(string Kind, long Bytes, int Files);

public sealed record StorageHolderDto(Guid Id, string? Name, long Bytes, int Files);

public sealed record StorageLargeItemDto(
    Guid FileId,
    string Name,
    string Kind,
    long Bytes,
    Guid DocumentId,
    Guid OwnerUserId,
    string? OwnerName,
    Guid? CaveId,
    string? CaveName,
    DateTimeOffset CreatedAt);

/// <param name="FullOn">The day the installation's cap — or, without one, the store's disk — is reached at the current pace; null when not growing or unbounded.</param>
/// <param name="ScanBytesPerDay">How fast the point clouds alone grow, measured the same way.</param>
/// <param name="ScansFullOn">The day the point clouds' cap is reached at their pace; null without that cap, or when they are not growing.</param>
public sealed record StorageGrowthDto(
    long? SevenDayBytes, long? ThirtyDayBytes, long? BytesPerDay, DateOnly? FullOn, long? ScanBytesPerDay, DateOnly? ScansFullOn);

/// <param name="Group">
/// <c>superseded-scan-products</c>, <c>failed-run-work</c>, <c>older-survey-versions</c> or <c>expired-uploads</c>.
/// Uploaded originals are in none of them.
/// </param>
public sealed record StorageReclaimableDto(string Group, int Items, long Bytes);

/// <param name="Confirm">Must be true: deleting a group is one deliberate act, and a request without it deletes nothing.</param>
public sealed record StorageReclaimRequest(bool Confirm);

/// <summary>
/// Deliberately rule-free: an unconfirmed or missing body must reach the handler, which answers it
/// with the stable <c>storage.confirm_required</c> code the screen turns into a sentence, rather
/// than a generic validation failure.
/// </summary>
public sealed class StorageReclaimRequestValidator : AbstractValidator<StorageReclaimRequest>;

/// <param name="Items">How many things were deleted.</param>
/// <param name="FreedBytes">The room given back.</param>
public sealed record StorageReclaimResultDto(string Group, int Items, long FreedBytes);

/// <summary>
/// The storage report and its deletions. Every route is for full administrators alone: the report
/// lists cave names, file names and owners across the whole installation without asking what the
/// reader may see, and a deletion reaches every cave and account. Full administrators read
/// everything anyway, so nothing is shown to them that a visibility filter would have hidden.
/// </summary>
public static class StorageReportEndpoints
{
    public static RouteGroupBuilder MapStorageReportEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/storage").WithTags("Admin");

        admin.MapGet("/report", LatestAsync)
            .WithSummary("The latest storage report: totals by kind, cave and account, the largest files, growth, and what could be deleted.")
            .WithDescription(
                "Written once a day by a scheduled job; 404 storage.report_missing before the first. "
                + "Full administrators only: the report names caves, files and their owners across the whole "
                + "installation, unfiltered, so a grant over the settings domain alone is not enough. Usable by "
                + "an operator's monitoring, signed in as a full administrator, as it is.");
        admin.MapPost("/report", MeasureAsync)
            .WithSummary("Measures storage now and keeps the result as the latest report.")
            .WithDescription("Full administrators only, as for reading it. Counts as one of the daily reports for growth.");
        admin.MapPost("/reclaim/{group}", ReclaimAsync).WithValidation<StorageReclaimRequest>()
            .WithSummary("Deletes one group of what the report offers, as it stands now.")
            .WithDescription(
                "Full administrators only, and a body with confirm=true: a group reaches every cave and account. The group is computed again "
                + "when the request arrives, not taken from the report. Uploaded originals are never deleted: a group "
                + "that would reach one is refused with storage.reclaim_refused and nothing is deleted. Recorded in "
                + "the audit trail with what was given back.");
        return api;
    }

    private static async Task<Results<Ok<StorageReportDto>, UnauthorizedHttpResult, ProblemHttpResult>> LatestAsync(
        StorageReportService reports,
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
            return ApiProblems.Forbidden("access.forbidden");
        }

        var report = await reports.LatestAsync(ct);
        return report is null ? ApiProblems.NotFound("storage.report_missing") : TypedResults.Ok(ToDto(report));
    }

    private static async Task<Results<Ok<StorageReportDto>, UnauthorizedHttpResult, ProblemHttpResult>> MeasureAsync(
        StorageReportService reports,
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
            return ApiProblems.Forbidden("access.forbidden");
        }

        return TypedResults.Ok(ToDto(await reports.WriteAsync(ct)));
    }

    private static async Task<Results<Ok<StorageReclaimResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> ReclaimAsync(
        string group,
        StorageReclaimRequest? request,
        StorageReportService reports,
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
            return ApiProblems.Forbidden("access.forbidden");
        }

        if (!StorageReclaimGroups.IsKnown(group))
        {
            return ApiProblems.BadRequest(
                "storage.group_unknown",
                $"Only these groups may be deleted: {string.Join(", ", StorageReclaimGroups.All)}. Uploaded originals are never among them.");
        }

        if (request is not { Confirm: true })
        {
            return ApiProblems.BadRequest("storage.confirm_required", "Deleting a group needs confirm=true.");
        }

        try
        {
            var result = await reports.ReclaimAsync(group, ctx.UserId, ct);
            return TypedResults.Ok(new StorageReclaimResultDto(result.Group, result.Items, result.FreedBytes));
        }
        catch (StorageReclaimRefusedException e)
        {
            return ApiProblems.Conflict("storage.reclaim_refused", e.Message);
        }
    }

    private static StorageReportDto ToDto(StorageReport r) => new(
        r.TakenAt,
        r.StoreUsedBytes,
        r.StoreCapBytes,
        r.ScanUsedBytes,
        r.ScanCapBytes,
        r.StoreFreeBytes,
        r.WorkerFreeBytes,
        r.MinFreeBytes,
        StorageEndpoints.LevelName(r.Level),
        r.ByFileKind.Select(Kind).ToList(),
        r.ScanByKind.Select(Kind).ToList(),
        r.WorkDirectoryBytes,
        r.ByCave.Select(Holder).ToList(),
        r.ByAccount.Select(Holder).ToList(),
        r.Largest.Select(i => new StorageLargeItemDto(
            i.FileId, i.Name, i.Kind, i.Bytes, i.DocumentId, i.OwnerUserId, i.OwnerName, i.CaveId, i.CaveName, i.CreatedAt)).ToList(),
        new StorageGrowthDto(
            r.Growth.SevenDayBytes, r.Growth.ThirtyDayBytes, r.Growth.BytesPerDay, r.Growth.FullOn, r.Growth.ScanBytesPerDay, r.Growth.ScansFullOn),
        r.Reclaimable.Select(g => new StorageReclaimableDto(g.Group, g.Items, g.Bytes)).ToList());

    private static StorageKindTotalDto Kind(StorageKindTotal k) => new(k.Kind, k.Bytes, k.Files);

    private static StorageHolderDto Holder(StorageHolder h) => new(h.Id, h.Name, h.Bytes, h.Files);
}
