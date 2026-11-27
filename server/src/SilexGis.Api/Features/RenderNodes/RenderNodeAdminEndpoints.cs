// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence.Configurations;
using SilexGis.Infrastructure.Rendering;

namespace SilexGis.Api.Features.RenderNodes;

/// <summary>Issue a render node: the name its administrator knows the machine by.</summary>
public sealed record RenderNodeIssueRequest(string Name);

public sealed class RenderNodeIssueRequestValidator : AbstractValidator<RenderNodeIssueRequest>
{
    public RenderNodeIssueRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n))
            .WithMessage("Give the machine a name, so you can tell it apart later.");
        RuleFor(x => x.Name).MaximumLength(RenderNodeConfiguration.NameMaxLength);
    }
}

/// <summary>A render node as the administrator's list shows it — never its token.</summary>
/// <param name="TokenPrefix">The token's first characters, so a machine's token file can be matched to its row.</param>
/// <param name="Online">Whether it called recently enough for a render to be taken soon.</param>
/// <param name="Worker">The worker's version, as its last claim said.</param>
/// <param name="Device">The device its last render used.</param>
/// <param name="BlenderVersions">The Blender versions it has, newest first.</param>
/// <param name="FreeDiskBytes">Free bytes on its scratch disk.</param>
public sealed record RenderNodeDto(
    Guid Id,
    string Name,
    string TokenPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastSeenAt,
    bool Online,
    string? Worker,
    string? Device,
    string? BlenderVersions,
    long? FreeDiskBytes);

/// <summary>A node just issued, and its token: the only time the token is shown anywhere.</summary>
public sealed record RenderNodeIssuedDto(RenderNodeDto Node, string Token);

/// <summary>
/// An administrator's list of render nodes, and issuing and withdrawing their tokens.
/// </summary>
/// <remarks>
/// Held over the installation's settings: a node takes renders of every account's flybys and
/// downloads the bundles made for them, so admitting one is the same kind of act as configuring
/// the mail server, and reading the list the same as reading the settings. A token is shown once,
/// in the answer that issues it; the list carries its first characters only. Issuing and
/// withdrawing are recorded in the change history like every other administrative write.
/// </remarks>
public static class RenderNodeAdminEndpoints
{
    public const string NameTakenCode = "render_node.name_taken";

    public static RouteGroupBuilder MapRenderNodeAdminEndpoints(this RouteGroupBuilder api)
    {
        var nodes = api.MapGroup("/admin/render-nodes").WithTags("Admin");
        nodes.MapGet("/", ListAsync)
            .WithSummary("The render nodes issued, the withdrawn ones last, with when each last called.");
        nodes.MapPost("/", IssueAsync).WithValidation<RenderNodeIssueRequest>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Issues a render node and its token; the token is returned once and never stored.");
        nodes.MapDelete("/{id:guid}", RevokeAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Withdraws a render node. Its next call is refused, and a render it held goes back to the queue.");
        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<RenderNodeDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        IAccessContextAccessor accessAccessor,
        FlybyRenders renders,
        IOptions<RenderNodeOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Read, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var nodes = await renders.NodesAsync(ct);
        return TypedResults.Ok<IReadOnlyList<RenderNodeDto>>([.. nodes.Select(n => Dto(n, options.Value, clock))]);
    }

    private static async Task<Results<Created<RenderNodeIssuedDto>, UnauthorizedHttpResult, ProblemHttpResult>> IssueAsync(
        RenderNodeIssueRequest request,
        IAccessContextAccessor accessAccessor,
        FlybyRenders renders,
        IOptions<RenderNodeOptions> options,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        var issued = await renders.IssueAsync(request.Name, ctx.UserId, ct);
        if (issued is not { } made)
        {
            return ApiProblems.Conflict(NameTakenCode, "A render node of that name is already issued.");
        }

        return TypedResults.Created(
            $"/api/v1/admin/render-nodes/{made.Node.Id}",
            new RenderNodeIssuedDto(Dto(made.Node, options.Value, clock), made.Token));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RevokeAsync(
        Guid id,
        IAccessContextAccessor accessAccessor,
        FlybyRenders renders,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!AccessEvaluator.Decide(ctx, AccessDomain.Settings, AccessAction.Write, null).Allowed)
        {
            return ApiProblems.Forbidden("access.forbidden");
        }

        return await renders.RevokeAsync(id, ct) ? TypedResults.NoContent() : ApiProblems.NotFound("render_node.not_found");
    }

    private static RenderNodeDto Dto(RenderNode node, RenderNodeOptions options, TimeProvider clock) => new(
        node.Id,
        node.Name,
        node.TokenPrefix,
        node.CreatedAt,
        node.RevokedAt,
        node.LastSeenAt,
        node.RevokedAt is null && node.LastSeenAt is { } seen
            && seen >= clock.GetUtcNow().AddSeconds(-Math.Max(1, options.OnlineSeconds)),
        node.Worker,
        node.Device,
        node.BlenderVersions,
        node.FreeDiskBytes);
}
