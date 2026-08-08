// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Geodata;
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
        geofiles.MapGet("/{id:guid}/columns", GetColumnsAsync)
            .WithSummary("Header of a delimited upload, so a wrong coordinate-column guess can be corrected.");
        geofiles.MapPost("/{id:guid}/reimport", ReimportAsync)
            .WithSummary("Re-reads the upload, optionally with corrected source options (Write permission).");
        geofiles.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes a geofile with its imported features and stored file.");

        return api;
    }

    /// <summary>Upload format is inferred from the file extension.</summary>
    private static readonly Dictionary<string, GeofileFormat> FormatByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".gpx"] = GeofileFormat.Gpx,
        [".kml"] = GeofileFormat.Kml,
        [".kmz"] = GeofileFormat.Kmz,
        [".geojson"] = GeofileFormat.GeoJson,
        [".json"] = GeofileFormat.GeoJson,
        [".zip"] = GeofileFormat.Shapefile,
        [".csv"] = GeofileFormat.Csv,
        [".txt"] = GeofileFormat.Csv,
        [".tsv"] = GeofileFormat.Csv,
        [".wkt"] = GeofileFormat.Wkt,
        [".wkb"] = GeofileFormat.Wkb,
    };

    /// <summary>Extensions whose real format is decided by looking inside the archive.</summary>
    private static readonly string[] ArchiveExtensions = [".zip", ".kmz"];

    private static async Task<Results<Created<GeofileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        IUserContextAccessor userAccessor,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        var ctx = await accessAccessor.GetAsync(ct);
        if (user is null || ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Geofiles))
        {
            return ApiProblems.Forbidden(CreateRules.ForbiddenCode);
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

        // Both zipped formats can arrive under either extension, so the archive itself decides.
        // A name is a hint; what is inside is the fact.
        if (ArchiveExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            format = ArchiveFormatSniffer.Detect(fileStore.GetAbsolutePath(storagePath)) ?? format;
        }

        var name = Path.GetFileNameWithoutExtension(file.FileName);
        var storedFile = documents.Create(
            new StoredContent(
                storagePath,
                Path.GetFileName(file.FileName),
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length,
                sha256,
                FileKind.Vector),
            name,
            user.UserId,
            user.UserId).File;

        var geofile = new Geofile
        {
            Name = name,
            FileId = storedFile.Id,
            Format = format,
            OwnerUserId = user.UserId,
        };

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
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        string? search,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var query = db.Geofiles.AsNoTracking().VisibleTo(ctx, AccessDomain.Geofiles);

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
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var (geofile, problem) = await LoadReadableAsync(id, db, access, accessAccessor, ct);
        return problem is not null ? problem : TypedResults.Ok(geofile!.ToDto());
    }

    private static async Task<Results<Ok<GeofileStatusDto>, ProblemHttpResult>> GetStatusAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var (geofile, problem) = await LoadReadableAsync(id, db, access, accessAccessor, ct);
        return problem is not null ? problem : TypedResults.Ok(geofile!.ToStatusDto());
    }

    private static async Task<Results<Ok<GeofileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        GeofileUpdateRequest request,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var geofile = await db.Geofiles.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null)
        {
            return ApiProblems.NotFound("geofile.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Write, geofile, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, geofile, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("geofile.not_found");
        }

        if (await Concurrency.CheckIfMatchAsync(http, db, VersionedTable.Geofiles, geofile.Id, ct) is { } stale)
        {
            return stale;
        }

        if (request.CavingGroupId is not null
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Geofiles, request.CavingGroupId.Value))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        geofile.Name = request.Name;
        geofile.Description = request.Description;
        geofile.Style = request.Style is { ValueKind: JsonValueKind.Object } s ? s.GetRawText() : null;
        geofile.CavingGroupId = request.CavingGroupId;
        geofile.Visibility = request.Visibility;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(geofile.ToDto());
    }

    /// <summary>
    /// The header row of a delimited upload. Answering this from the stored file rather than
    /// from remembered parse state means it still works after a failed import, which is the
    /// only moment anybody asks it.
    /// </summary>
    private static async Task<Results<Ok<GeofileColumnsDto>, ProblemHttpResult>> GetColumnsAsync(
        Guid id,
        SilexGisDbContext db,
        IFileStore fileStore,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var (geofile, problem) = await LoadReadableAsync(id, db, access, accessAccessor, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (geofile!.Format != GeofileFormat.Csv)
        {
            return ApiProblems.BadRequest(
                "geofile.not_delimited", "Only a delimited upload has columns to choose between.");
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == geofile.FileId, ct);
        if (file is null)
        {
            return ApiProblems.NotFound("geofile.not_found");
        }

        var options = ReadSourceOptions(geofile);
        try
        {
            var columns = DelimitedVectorReader.ReadHeader(
                fileStore.GetAbsolutePath(file.StoragePath), options?.Delimited?.Delimiter);
            return TypedResults.Ok(new GeofileColumnsDto(columns));
        }
        catch (Exception e) when (e is VectorIOException or IOException)
        {
            return ApiProblems.BadRequest("geofile.unreadable", e.Message);
        }
    }

    /// <summary>
    /// Reads the upload again, optionally under corrected source options. The import handler
    /// already replaces the previous rows, so this is a re-queue rather than a second path —
    /// and it is what makes a bad coordinate-column guess recoverable without a fresh upload.
    /// </summary>
    private static async Task<Results<Ok<GeofileStatusDto>, ProblemHttpResult>> ReimportAsync(
        Guid id,
        GeofileReimportRequest? request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        var geofile = await db.Geofiles.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null || user is null)
        {
            return ApiProblems.NotFound("geofile.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Write, geofile, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, geofile, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("geofile.not_found");
        }

        if (request?.SourceOptions is { } sourceOptions)
        {
            geofile.SourceOptions = ImportJson.Serialize(sourceOptions);
        }

        geofile.ImportStatus = GeofileImportStatus.Uploaded;
        geofile.ImportError = null;
        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.GeofileImport,
            Payload = JsonSerializer.Serialize(new GeofileImportPayload(geofile.Id), JsonSerializerOptions.Web),
            RequestedBy = user.UserId,
        });
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(geofile.ToStatusDto());
    }

    private static GeofileSourceOptions? ReadSourceOptions(Geofile geofile)
    {
        if (string.IsNullOrWhiteSpace(geofile.SourceOptions))
        {
            return null;
        }

        try
        {
            return ImportJson.Deserialize<GeofileSourceOptions>(geofile.SourceOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IFileStore fileStore,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var geofile = await db.Geofiles.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null)
        {
            return ApiProblems.NotFound("geofile.not_found");
        }

        if (ctx is null || !(await access.DecideAsync(ctx, AccessAction.Delete, geofile, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, geofile, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("geofile.not_found");
        }

        // Polymorphic attachment rows have no FK to the geofile — clean them up in the
        // same transaction as the entity. Resource-link members follow the same
        // convention: a dead target must not linger in links as a permanent
        // restricted-looking member.
        await db.Attachments
            .Where(a => a.EntityType == AttachedEntityType.Geofile && a.EntityId == geofile.Id)
            .ExecuteDeleteAsync(ct);
        await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Geofile && m.EntityId == geofile.Id)
            .ExecuteDeleteAsync(ct);

        // Imported features cascade with the geofile row; the upload goes explicitly. The
        // geofile owns its upload outright, so the whole document goes — an imported file has
        // no life of its own once the geofile it produced is gone.
        db.Geofiles.Remove(geofile);
        var removed = await documents.DeleteDocumentOfFileAsync(geofile.FileId, ct);

        await db.SaveChangesAsync(ct);

        foreach (var storedFile in removed)
        {
            await fileStore.DeleteAsync(storedFile.StoragePath, CancellationToken.None);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Read-gated fetch; unreadable and missing geofiles are both 404.</summary>
    private static async Task<(Geofile? Geofile, ProblemHttpResult? Problem)> LoadReadableAsync(
        Guid id, SilexGisDbContext db, IAccessService access, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (geofile is null || !(await access.DecideAsync(ctx, AccessAction.Read, geofile, ct)).Allowed)
        {
            return (null, ApiProblems.NotFound("geofile.not_found"));
        }

        return (geofile, null);
    }
}
