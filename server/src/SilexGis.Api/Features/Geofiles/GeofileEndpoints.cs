// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Geofiles;

public static class GeofileEndpoints
{
    public static RouteGroupBuilder MapGeofileEndpoints(this RouteGroupBuilder api)
    {
        var geofiles = api.MapGroup("/geofiles").WithTags("Geofiles");

        geofiles.MapPost("/", UploadAsync)
            .DisableAntiforgery() // bearer-token API; no cookie-form surface to forge
            .WithSummary("Uploads a vector file (multipart) and queues the server-side import.");
        geofiles.MapGet("/", ListAsync)
            .WithSummary("Paged geofile list; visibility-filtered.");
        geofiles.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single geofile.");
        geofiles.MapGet("/{id:guid}/status", GetStatusAsync)
            .WithSummary("Import status for polling.");
        geofiles.MapPut("/{id:guid}", UpdateAsync).WithValidation<GeofileUpdateRequest>()
            .WithSummary("Metadata update (Write permission); the uploaded file is immutable.");
        geofiles.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a geofile with its imported features and stored file.");

        return api;
    }

    /// <summary>Upload format is inferred from the file extension.</summary>
    private static readonly Dictionary<string, GeofileFormat> FormatByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".gpx"] = GeofileFormat.Gpx,
        [".kml"] = GeofileFormat.Kml,
        [".geojson"] = GeofileFormat.GeoJson,
        [".json"] = GeofileFormat.GeoJson,
        [".zip"] = GeofileFormat.Shapefile,
        [".wkt"] = GeofileFormat.Wkt,
        [".wkb"] = GeofileFormat.Wkb,
    };

    private static async Task<Results<Created<GeofileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        SilexGisDbContext db,
        IFileStore fileStore,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!user.CanCreateContent)
        {
            return ApiProblems.Forbidden("geofile.create_requires_editor");
        }

        if (file.Length == 0)
        {
            return ApiProblems.BadRequest("geofile.file_empty", "The uploaded file is empty.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!FormatByExtension.TryGetValue(extension, out var format))
        {
            return ApiProblems.BadRequest(
                "geofile.format_unsupported",
                $"Unsupported file extension '{extension}'. Supported: {string.Join(", ", FormatByExtension.Keys)}.");
        }

        // Persist the blob, then hash it from disk (uploads can be large; no buffering).
        string storagePath;
        await using (var content = file.OpenReadStream())
        {
            storagePath = await fileStore.SaveAsync(content, extension, ct);
        }

        string sha256;
        await using (var saved = await fileStore.OpenReadAsync(storagePath, ct))
        {
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(saved, ct));
        }

        var storedFile = new StoredFile
        {
            StoragePath = storagePath,
            OriginalName = Path.GetFileName(file.FileName),
            MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            Sha256 = sha256,
            UploadedBy = user.UserId,
            Kind = FileKind.Vector,
        };
        storedFile.VersionGroupId = storedFile.Id; // head of its own version chain

        var geofile = new Geofile
        {
            Name = Path.GetFileNameWithoutExtension(file.FileName),
            FileId = storedFile.Id,
            Format = format,
            OwnerUserId = user.UserId,
        };

        db.StoredFiles.Add(storedFile);
        db.Geofiles.Add(geofile);
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.GeofileImport,
            Payload = JsonSerializer.Serialize(new GeofileImportPayload(geofile.Id), JsonSerializerOptions.Web),
            RequestedBy = user.UserId,
        });
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/geofiles/{geofile.Id}", geofile.ToDto());
    }

    private static async Task<Results<Ok<PagedResult<GeofileDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IUserContextAccessor userAccessor,
        int? page,
        int? pageSize,
        string? search,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.Geofiles.AsNoTracking().VisibleTo(user, db.ObjectAcls, AttachedEntityType.Geofile);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(g => EF.Functions.ILike(EF.Functions.Unaccent(g.Name), EF.Functions.Unaccent(pattern)));
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var result = await query.OrderByDescending(g => g.UpdatedAt).ToPagedAsync(p, size, g => g.ToDto(), ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<GeofileDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var (geofile, problem) = await LoadReadableAsync(id, db, permissions, userAccessor, ct);
        return problem is not null ? problem : TypedResults.Ok(geofile!.ToDto());
    }

    private static async Task<Results<Ok<GeofileStatusDto>, ProblemHttpResult>> GetStatusAsync(
        Guid id,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var (geofile, problem) = await LoadReadableAsync(id, db, permissions, userAccessor, ct);
        return problem is not null ? problem : TypedResults.Ok(geofile!.ToStatusDto());
    }

    private static async Task<Results<Ok<GeofileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        GeofileUpdateRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var geofile = await db.Geofiles.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null)
        {
            return ApiProblems.NotFound("geofile.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, geofile, ObjectPermission.Write, ct))
        {
            return await permissions.CanAsync(user, geofile, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("geofile.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Geofiles, geofile.Id, ct) is { } stale)
        {
            return stale;
        }

        if (request.TeamId is not null && !user.IsAdmin && !user.IsMemberOf(request.TeamId.Value))
        {
            return ApiProblems.Forbidden("geofile.team_membership_required");
        }

        geofile.Name = request.Name;
        geofile.Description = request.Description;
        geofile.Style = request.Style is { ValueKind: JsonValueKind.Object } s ? s.GetRawText() : null;
        geofile.TeamId = request.TeamId;
        geofile.Visibility = request.Visibility;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(geofile.ToDto());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IFileStore fileStore,
        IPermissionService permissions,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var geofile = await db.Geofiles.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null)
        {
            return ApiProblems.NotFound("geofile.not_found");
        }

        if (user is null || !await permissions.CanAsync(user, geofile, ObjectPermission.Delete, ct))
        {
            return await permissions.CanAsync(user, geofile, ObjectPermission.Read, ct)
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("geofile.not_found");
        }

        var storedFile = await db.StoredFiles.FirstOrDefaultAsync(f => f.Id == geofile.FileId, ct);

        // Polymorphic attachment rows have no FK to the geofile — clean them up in the
        // same transaction as the entity.
        await db.Attachments
            .Where(a => a.EntityType == AttachedEntityType.Geofile && a.EntityId == geofile.Id)
            .ExecuteDeleteAsync(ct);

        // Imported features cascade with the geofile row; the file row goes explicitly
        // (uploads are 1:1 with geofiles until the Phase 3 media work).
        db.Geofiles.Remove(geofile);
        if (storedFile is not null)
        {
            db.StoredFiles.Remove(storedFile);
        }

        await db.SaveChangesAsync(ct);

        if (storedFile is not null)
        {
            await fileStore.DeleteAsync(storedFile.StoragePath, CancellationToken.None);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Read-gated fetch; unreadable and missing geofiles are both 404.</summary>
    private static async Task<(Geofile? Geofile, ProblemHttpResult? Problem)> LoadReadableAsync(
        Guid id, SilexGisDbContext db, IPermissionService permissions, IUserContextAccessor userAccessor, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null || !await permissions.CanAsync(user, geofile, ObjectPermission.Read, ct))
        {
            return (null, ApiProblems.NotFound("geofile.not_found"));
        }

        return (geofile, null);
    }
}
