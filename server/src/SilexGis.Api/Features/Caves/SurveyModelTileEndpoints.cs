// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// Delivery of a scan's tiles: one entry of the stored tileset archive per request, named by the
/// address a viewer derives from the root tileset it was handed.
/// </summary>
/// <remarks>
/// <para>
/// A tile reader fetches these addresses ambiently and can attach no header, so the token in
/// the address is the whole credential — minted over the model's converted file when the scan
/// was listed to a caller who may see it, for a session, exactly as a georeferenced map's
/// raster is. Nothing here re-decides anything: the route checks that the token was minted for
/// this very archive and hands over the named entry.
/// </para>
/// <para>
/// Every refusal is the one not-found the model's own routes give: a token that does not
/// verify, one minted for another file, a model that is not a tiled scan, a name that is not a
/// tile's and an entry the archive does not hold are told apart by nothing. Whether a scan
/// exists, and what its archive holds, is part of what an unverified caller is not told.
/// </para>
/// <para>
/// The tiles are written in the cloud's own frame and carry no coordinates, so they follow the
/// scan's default rules and not the exact-location one — the shift that would place them is
/// withheld on its own, with the placement. That is also why a tile is not recorded as a
/// delivery of the file: the archive whole is, at the file route; a tile is a piece drawn from
/// it, fetched by the hundred while a scene is on screen.
/// </para>
/// </remarks>
public static class SurveyModelTileEndpoints
{
    private const string NotFoundCode = "survey_model.not_found";

    /// <summary>
    /// A tile's bytes never change once the archive is stored — a scan tiled again is a new
    /// converted file, and every address of the old one stops opening with it — so a browser may
    /// keep what it fetched for as long as it likes. Private, because the address carries a
    /// token minted for one caller.
    /// </summary>
    private const string Immutable = "private, max-age=31536000, immutable";

    public static RouteGroupBuilder MapSurveyModelTileEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/survey-models/{id:guid}/tiles/{**path}", TileAsync)
            .AllowAnonymous() // the token in the address is the credential; a tile reader attaches no header
            .WithTags("SurveyModels")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One entry of a scan's tileset archive — tileset.json or a .pnts tile — by the signed address the scan's listing handed out.");
        return api;
    }

    private static async Task<IResult> TileAsync(
        Guid id,
        string path,
        string? token,
        HttpContext http,
        SilexGisDbContext db,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        // The name is judged before anything is looked up or opened, so a name built to walk out
        // of the archive or to fish for something that is not a tile costs nothing and learns
        // nothing.
        var contentType = Domain.Scans.TilesetEntryNames.ContentTypeOf(path);
        if (contentType is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var formats = SurveyModelKinds.FormatsOf(SurveyModelKind.PointCloud);
        var model = await db.SurveyModels.AsNoTracking()
            .Where(m => m.Id == id && m.ConvertedFileId != null && formats.Contains(m.Format))
            .Select(m => new { ConvertedFileId = m.ConvertedFileId!.Value })
            .FirstOrDefaultAsync(ct);
        if (model is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // The token must have been minted over this model's current archive and for its whole
        // bytes; a token for a thumbnail-only reach, for another file, or for the archive this
        // scan had before it was tiled again, opens nothing.
        var grant = tokens.Validate(token, model.ConvertedFileId);
        if (grant is null || grant.Delivery != FileDelivery.Full)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var storagePath = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == model.ConvertedFileId)
            .Select(f => f.StoragePath)
            .FirstOrDefaultAsync(ct);
        if (storagePath is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var entry = TilesetArchive.Open(fileStore.GetAbsolutePath(storagePath), path);
        if (entry is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // The validator names the stored archive and the entry's own checksum: the same entry of
        // the same archive is the same bytes whichever token the address carried.
        var etag = new EntityTagHeaderValue($"\"{model.ConvertedFileId:N}-{entry.Crc32:x8}\"");
        if (http.Request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } held
            && held.Any(h => h.Equals(EntityTagHeaderValue.Any) || h.Compare(etag, useStrongComparison: true)))
        {
            entry.Dispose();
            http.Response.Headers.CacheControl = Immutable;
            http.Response.Headers.ETag = etag.ToString();
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        http.Response.Headers.CacheControl = Immutable;
        return new TileEntryResult(entry, etag);
    }

    /// <summary>
    /// Writes one archive entry out with its length stated and the archive closed afterwards.
    /// Written by hand because the framework's stream results take their length from a seekable
    /// stream, and an entry inflated out of a zip is not one — yet its length is known from the
    /// archive's directory, and a reader drawing a progress bar from it deserves to be told.
    /// </summary>
    private sealed class TileEntryResult(TilesetEntry entry, EntityTagHeaderValue etag) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            using (entry)
            {
                var response = httpContext.Response;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = entry.ContentType;
                response.ContentLength = entry.Length;
                response.Headers.ETag = etag.ToString();
                await entry.Content.CopyToAsync(response.Body, httpContext.RequestAborted);
            }
        }
    }
}
