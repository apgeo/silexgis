// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Infrastructure.Rendering;

namespace SilexGis.Api.Features.Flybys;

/// <summary>Which of the bundle's plans the node plays: its draft (half size, fewer samples) or the whole.</summary>
public enum FlybyRenderPlan
{
    Draft = 0,
    Full = 1,
}

/// <summary>The film's container.</summary>
public enum FlybyRenderContainer
{
    Mp4 = 0,
    Webm = 1,
    Mkv = 2,
}

/// <summary>Where a render on the server stands.</summary>
public enum FlybyRenderStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>Render a flyby on the server: the bundle, as it is asked for to download, and how to render it.</summary>
/// <param name="Container">The film's container; left out, mp4.</param>
public sealed record FlybyBlenderRenderRequest(
    FlybyBlenderBundleRequest? Bundle,
    FlybyRenderPlan? Plan,
    FlybyRenderContainer? Container = null);

public sealed class FlybyBlenderRenderRequestValidator : AbstractValidator<FlybyBlenderRenderRequest>
{
    public FlybyBlenderRenderRequestValidator()
    {
        RuleFor(x => x.Bundle).NotNull().SetValidator(new FlybyBlenderBundleRequestValidator()!);
        RuleFor(x => x.Plan).NotNull();
    }
}

/// <summary>Whether rendering on the server is offered: a render node called lately.</summary>
public sealed record FlybyRenderAvailabilityDto(bool Available);

/// <summary>One file of a render's result.</summary>
/// <param name="Role">movie, still, subtitles, log or status.</param>
public sealed record FlybyRenderFileDto(string Name, string Role, long Size);

/// <summary>A flyby rendered on the server, as its requester follows it.</summary>
/// <param name="Phase">The node's last word on where it is: downloading, rendering, uploading…</param>
/// <param name="Message">The node's last message, "Rendering frame 37/240".</param>
/// <param name="Done">Frames rendered of <paramref name="Total"/>, over the whole film.</param>
/// <param name="Error">Why it failed or stopped, as "reason: message".</param>
/// <param name="CancelRequested">Whether its requester asked it to stop and the node has yet to hear.</param>
/// <param name="Files">The result: the film, and its subtitles and log; empty until it succeeded.</param>
/// <param name="ExpiresAt">When the film is deleted.</param>
public sealed record FlybyRenderDto(
    Guid Id,
    string Name,
    FlybyRenderPlan Plan,
    FlybyRenderContainer Container,
    FlybyRenderStatus Status,
    int Attempts,
    string? Phase,
    string? Message,
    int? Done,
    int? Total,
    double? SecondsLeft,
    string? Error,
    bool CancelRequested,
    string? Device,
    double? RenderSeconds,
    IReadOnlyList<FlybyRenderFileDto> Files,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>A render queued, with what went into its bundle as the download counts it.</summary>
public sealed record FlybyRenderStartedDto(
    FlybyRenderDto Render,
    int Walls,
    int Lines,
    int NotDrawn,
    int WallsLeftOut,
    int Markers,
    int MarkersNotDrawn,
    int Photos,
    int UndatedLegs,
    int CavesOnSurveyDate,
    int UndatedCaves);

/// <summary>
/// A flyby rendered on the server: its Blender bundle is made exactly as for the download, stored,
/// and rendered by a render node — a machine the operator runs a render worker on, which takes the
/// job over HTTP. This application starts no process for it; it holds the bundle and the film.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bundle is decided as the download's is, for the account asking</b> — the same code
/// makes it, and records it (<c>format: blender-render</c>), so what a node renders is what that
/// account could have downloaded and rendered itself. The node is the operator's machine, admitted
/// by an administrator's token.
/// </para>
/// <para>
/// <b>A render and its film are its requester's alone</b>: every route here answers about the
/// caller's own renders, and another account's is a 404 exactly as one that does not exist is. A
/// film is kept for a while after it is made, then deleted.
/// </para>
/// </remarks>
public static class FlybyRenderEndpoints
{
    public const string AlreadyActiveCode = "flyby_render.already_active";
    public const string StorageFullCode = "flyby_render.storage_full";
    public const string NotFoundCode = "flyby_render.not_found";

    public static RouteGroupBuilder MapFlybyRenderEndpoints(this RouteGroupBuilder api)
    {
        var renders = api.MapGroup("/flybys/blender-renders").WithTags("Flybys");
        renders.MapGet("/availability", AvailabilityAsync)
            .WithSummary("Whether a flyby can be rendered on the server now: a render node called lately.");
        renders.MapPost("/", StartAsync).WithValidation<FlybyBlenderRenderRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary(
                "Makes the flyby's Blender bundle as the download does, stores it and queues its render "
                + "for a render node. One render of the caller's may be queued or running at a time.");
        renders.MapGet("/", MineAsync)
            .WithSummary("The caller's renders still kept, newest first.");
        renders.MapGet("/{id:guid}", OneAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One of the caller's renders: where it stands, how far it got, and its result.");
        renders.MapPost("/{id:guid}/cancel", CancelAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Stops one of the caller's renders: a queued one at once, a running one at its node's next heartbeat.");
        renders.MapGet("/{id:guid}/files/{name}", FileAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("A file of the result of one of the caller's renders: the film, its subtitles, its log.");
        return api;
    }

    private static async Task<Ok<FlybyRenderAvailabilityDto>> AvailabilityAsync(FlybyRenders renders, CancellationToken ct) =>
        TypedResults.Ok(new FlybyRenderAvailabilityDto(await renders.AnyNodeOnlineAsync(ct)));

    private static async Task<Results<Created<FlybyRenderStartedDto>, UnauthorizedHttpResult, ProblemHttpResult>> StartAsync(
        FlybyBlenderRenderRequest request,
        FlybyBundleServices services,
        FlybyRenders renders,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        if (currentUser.UserId is not { } requester)
        {
            return TypedResults.Unauthorized();
        }

        var made = await FlybyBlenderBundleEndpoints.MakeAsync(request.Bundle!, services, "blender-render", ct);
        if (made.Unauthorized)
        {
            return TypedResults.Unauthorized();
        }

        if (made.Problem is { } problem)
        {
            return problem;
        }

        var bundle = made.Bundle!;
        var (render, refusal) = await renders.EnqueueAsync(
            requester,
            request.Bundle!.Name is { Length: > 0 } name ? name : bundle.FileName,
            bundle.Zip,
            request.Plan == FlybyRenderPlan.Full ? "full" : "draft",
            (request.Container ?? FlybyRenderContainer.Mp4).ToString().ToLowerInvariant(),
            ct);
        return refusal switch
        {
            FlybyRenderRefusal.AlreadyActive => ApiProblems.Conflict(
                AlreadyActiveCode, "A render of yours is queued or running already; stop it or wait for it first."),
            FlybyRenderRefusal.StorageFull => ApiProblems.Conflict(
                StorageFullCode, "The space this server keeps for renders is full."),
            _ => TypedResults.Created(
                $"/api/v1/flybys/blender-renders/{render!.Render.Id}",
                new FlybyRenderStartedDto(
                    Dto(render),
                    bundle.Walls,
                    bundle.Lines,
                    bundle.NotDrawn,
                    bundle.WallsLeftOut,
                    bundle.Markers,
                    bundle.MarkersNotDrawn,
                    bundle.Photos,
                    bundle.UndatedLegs,
                    bundle.CavesOnSurveyDate,
                    bundle.UndatedCaves)),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<FlybyRenderDto>>, UnauthorizedHttpResult>> MineAsync(
        FlybyRenders renders, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } requester)
        {
            return TypedResults.Unauthorized();
        }

        var mine = await renders.MineAsync(requester, ct);
        return TypedResults.Ok<IReadOnlyList<FlybyRenderDto>>([.. mine.Select(Dto)]);
    }

    private static async Task<Results<Ok<FlybyRenderDto>, UnauthorizedHttpResult, ProblemHttpResult>> OneAsync(
        Guid id, FlybyRenders renders, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } requester)
        {
            return TypedResults.Unauthorized();
        }

        return await renders.OneAsync(requester, id, ct) is { } render
            ? TypedResults.Ok(Dto(render))
            : ApiProblems.NotFound(NotFoundCode);
    }

    private static async Task<Results<Ok<FlybyRenderDto>, UnauthorizedHttpResult, ProblemHttpResult>> CancelAsync(
        Guid id, FlybyRenders renders, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } requester)
        {
            return TypedResults.Unauthorized();
        }

        return await renders.CancelAsync(requester, id, ct) is { } render
            ? TypedResults.Ok(Dto(render))
            : ApiProblems.NotFound(NotFoundCode);
    }

    private static async Task<Results<FileStreamHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> FileAsync(
        Guid id, string name, FlybyRenders renders, IFileStore fileStore, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } requester)
        {
            return TypedResults.Unauthorized();
        }

        if (await renders.FileAsync(requester, id, name, ct) is not { } file)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var stream = await fileStore.OpenReadAsync(file.StoragePath, ct);
        return TypedResults.File(stream, ContentType(file.Name), file.Name);
    }

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        ".png" => "image/png",
        ".srt" => "application/x-subrip",
        ".vtt" => "text/vtt",
        ".json" => "application/json",
        _ => "text/plain",
    };

    private static FlybyRenderDto Dto(FlybyRenderView view)
    {
        var render = view.Render;
        var job = view.Job;
        return new FlybyRenderDto(
            render.Id,
            render.Name,
            render.Plan == "full" ? FlybyRenderPlan.Full : FlybyRenderPlan.Draft,
            render.Container switch
            {
                "webm" => FlybyRenderContainer.Webm,
                "mkv" => FlybyRenderContainer.Mkv,
                _ => FlybyRenderContainer.Mp4,
            },
            (FlybyRenderStatus)(int)view.State,
            job?.Attempts ?? 0,
            view.State == FlybyRenderState.Running ? render.Phase : null,
            view.State == FlybyRenderState.Running ? render.Message : null,
            job?.ProgressDone,
            job?.ProgressTotal,
            view.State == FlybyRenderState.Running ? render.SecondsLeft : null,
            view.State is FlybyRenderState.Failed or FlybyRenderState.Cancelled || view.State == FlybyRenderState.Queued
                ? job?.Error
                : null,
            render.CancelRequestedAt is not null && view.State is FlybyRenderState.Queued or FlybyRenderState.Running,
            render.Device,
            render.RenderSeconds,
            [.. render.Files.Where(f => f.Role is not null).OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => new FlybyRenderFileDto(f.Name, f.Role!, f.Size))],
            render.CreatedAt,
            render.CompletedAt,
            render.ExpiresAt);
    }
}
