// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using SilexGis.Api.Auth;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Rendering;

namespace SilexGis.Api.Features.RenderNodes;

/// <summary>
/// The routes a render node calls: a machine the operator runs the survey library's render worker
/// on (<c>therion-cli render-worker --server …/api/v1/render-nodes/ --token-file …</c>). It claims
/// a flyby's render job, downloads the job's stored bundle, says how far it got, uploads the film
/// and completes or fails the job. The node pulls: this application never starts a process for a
/// render and never reaches the node.
/// </summary>
/// <remarks>
/// <para>
/// <b>Open to no account, opened by a node's token.</b> The routes need no sign-in, and every one
/// of them answers 401 unless the request carries, as <c>Authorization: Bearer</c>, the token of a
/// node an administrator issued and has not withdrawn. The token opens these routes and nothing
/// else; it acts as nobody, and of the application's data it reads only the bundle of a job its
/// node holds — a bundle that was made for, and recorded against, the account that asked for the
/// render. Missing, unknown and withdrawn tokens are refused alike.
/// </para>
/// <para>
/// <b>Bodies, status codes and validation are the protocol's</b>, as the survey library writes it
/// (its render worker protocol, version 1): each message is checked against the library's own
/// schema and written by its own serializer.
/// </para>
/// <para>
/// Described in the served API description, as every deliberately open route is, so the
/// description says they need no sign-in; their bodies are the protocol's own, given by the
/// library's schema rather than here, and no page of this application calls them.
/// </para>
/// </remarks>
public static class RenderNodeEndpoints
{
    /// <summary>The code of a call without a node token the server knows.</summary>
    public const string UnauthorizedCode = "render_node.unauthorized";

    /// <summary>The longest JSON message a node sends; a heartbeat is a few hundred bytes.</summary>
    private const int MaxMessageBytes = 256 * 1024;

    public static RouteGroupBuilder MapRenderNodeEndpoints(this RouteGroupBuilder api)
    {
        var nodes = api.MapGroup("/render-nodes").WithTags("RenderNodes").AllowAnonymous();

        nodes.MapPost("/claim", ClaimAsync)
            .WithSummary("A render node asks for a render job (render worker protocol 1); 200 with the job, 204 with none. Node token only.");
        nodes.MapGet("/jobs/{jobId}/bundle", BundleAsync)
            .WithSummary("The stored bundle of a render job the calling node holds; 409 when it does not. Node token only.");
        nodes.MapPost("/jobs/{jobId}/heartbeat", HeartbeatAsync)
            .WithSummary("A render node's progress; renews its lease and answers continue or cancel. Node token only.");
        nodes.MapPut("/jobs/{jobId}/files/{name}", PutFileAsync)
            .WithSummary("One result file of a render job the calling node holds, checked against X-Content-SHA256. Node token only.");
        nodes.MapPost("/jobs/{jobId}/complete", CompleteAsync)
            .WithSummary("A render job succeeded with the files the node uploaded. Node token only.");
        nodes.MapPost("/jobs/{jobId}/fail", FailAsync)
            .WithSummary("A render job failed on the node; it is ended or queued again. Node token only.");
        return api;
    }

    private static async Task<IResult> ClaimAsync(HttpContext http, RenderNodeQueue queue, CancellationToken ct) =>
        await WithNodeAsync(http, queue, ct, async node =>
            await ReadAsync(http, ct) is { } body ? Answer(await queue.ClaimAsync(node, body, ct)) : TooLongMessage());

    private static async Task<IResult> HeartbeatAsync(string jobId, HttpContext http, RenderNodeQueue queue, CancellationToken ct) =>
        await WithNodeAsync(http, queue, ct, async node =>
            await ReadAsync(http, ct) is { } body ? Answer(await queue.HeartbeatAsync(node, jobId, body, ct)) : TooLongMessage());

    private static async Task<IResult> CompleteAsync(string jobId, HttpContext http, RenderNodeQueue queue, CancellationToken ct) =>
        await WithNodeAsync(http, queue, ct, async node =>
            await ReadAsync(http, ct) is { } body ? Answer(await queue.CompleteAsync(node, jobId, body, ct)) : TooLongMessage());

    private static async Task<IResult> FailAsync(string jobId, HttpContext http, RenderNodeQueue queue, CancellationToken ct) =>
        await WithNodeAsync(http, queue, ct, async node =>
            await ReadAsync(http, ct) is { } body ? Answer(await queue.FailAsync(node, jobId, body, ct)) : TooLongMessage());

    /// <summary>The bundle, streamed as stored; never a redirect.</summary>
    private static async Task<IResult> BundleAsync(
        string jobId, HttpContext http, RenderNodeQueue queue, CancellationToken ct) =>
        await WithNodeAsync(http, queue, ct, async node =>
        {
            var (answer, bundle) = await queue.BundleAsync(node, jobId, ct);
            return bundle is null ? Answer(answer) : TypedResults.File(bundle.Content, "application/zip", bundle.FileName);
        });

    /// <summary>
    /// One result file, the body as it is. The request may be as large as the largest file this
    /// server takes, which is larger than the application's ordinary request limit: a film is sent
    /// in one request.
    /// </summary>
    private static async Task<IResult> PutFileAsync(
        string jobId,
        string name,
        HttpContext http,
        RenderNodeQueue queue,
        IOptions<RenderNodeOptions> options,
        CancellationToken ct) =>
        await WithNodeAsync(http, queue, ct, async node =>
        {
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = options.Value.MaxResultBytes + 1;
            }

            int? attempt = int.TryParse(http.Request.Query["attempt"], NumberStyles.None, CultureInfo.InvariantCulture, out var a)
                ? a
                : null;
            string? sha256 = http.Request.Headers["X-Content-SHA256"].ToString() is { Length: > 0 } header ? header : null;
            return Answer(await queue.PutFileAsync(
                node, jobId, name, attempt, sha256, http.Request.ContentLength, http.Request.Body, ct));
        });

    private static async Task<IResult> WithNodeAsync(
        HttpContext http, RenderNodeQueue queue, CancellationToken ct, Func<RenderNode, Task<IResult>> then)
    {
        var node = await queue.AuthenticateAsync(AccessKeySchemes.BearerValue(http.Request), ct);
        return node is null
            ? TypedResults.Problem(
                detail: "This route needs the token of a render node, as 'Authorization: Bearer <token>'.",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = UnauthorizedCode })
            : await then(node);
    }

    /// <summary>The body as text, or null when it is longer than any message of the protocol.</summary>
    private static async Task<string?> ReadAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength > MaxMessageBytes)
        {
            return null;
        }

        using var held = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await http.Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (held.Length + n > MaxMessageBytes)
            {
                return null;
            }

            held.Write(buffer, 0, n);
        }

        return Encoding.UTF8.GetString(held.GetBuffer(), 0, (int)held.Length);
    }

    private static IResult TooLongMessage() => TypedResults.Problem(
        detail: "A render node's message is at most 256 KB.",
        statusCode: StatusCodes.Status413PayloadTooLarge,
        extensions: new Dictionary<string, object?> { ["code"] = RenderNodeQueue.InvalidMessageCode });

    private static IResult Answer(RenderNodeAnswer answer) => answer.Status switch
    {
        RenderNodeStatus.Ok when answer.Json is { } json => TypedResults.Text(json, "application/json", Encoding.UTF8),
        RenderNodeStatus.Ok => TypedResults.Ok(),
        RenderNodeStatus.NoContent => TypedResults.NoContent(),
        _ => TypedResults.Problem(
            detail: answer.Detail,
            statusCode: answer.Status switch
            {
                RenderNodeStatus.BadRequest => StatusCodes.Status400BadRequest,
                RenderNodeStatus.Unauthorized => StatusCodes.Status401Unauthorized,
                RenderNodeStatus.NotFound => StatusCodes.Status404NotFound,
                RenderNodeStatus.Conflict => StatusCodes.Status409Conflict,
                RenderNodeStatus.TooLarge => StatusCodes.Status413PayloadTooLarge,
                _ => StatusCodes.Status500InternalServerError,
            },
            extensions: new Dictionary<string, object?> { ["code"] = answer.Code }),
    };
}
