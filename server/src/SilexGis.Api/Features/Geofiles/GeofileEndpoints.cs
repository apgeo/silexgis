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
using SilexGis.Infrastructure.Filters;
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(
                "Paged geofile list; visibility-filtered. `filter` (a filter document as a link token) or "
                + "`savedFilterId` narrows it by the geofile world's conditions, applied after visibility "
                + "and counted over the narrowed set.");
        geofiles.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Single geofile.");
        geofiles.MapGet("/{id:guid}/status", GetStatusAsync)
            .WithSummary("Import status for polling.");
        geofiles.MapPut("/{id:guid}", UpdateAsync).WithValidation<GeofileUpdateRequest>()
            .WithSummary("Metadata update (Write permission); the uploaded file is immutable.");
        geofiles.MapGet("/{id:guid}/columns", GetColumnsAsync)
            .WithSummary("Header of a delimited upload, so a wrong coordinate-column guess can be corrected.");
        geofiles.MapPost("/{id:guid}/reimport", ReimportAsync).WithValidation<GeofileReimportRequest>()
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

    /// <summary>What the stored-file row holds of the uploaded file's own name.</summary>
    private const int MaxOriginalNameLength = 255;

    /// <summary>What the geofile row holds of its display name; the update rule allows the same.</summary>
    private const int MaxNameLength = 200;

    private static string Clipped(string value, int length) =>
        value.Length <= length ? value : value[..(char.IsHighSurrogate(value[length - 1]) ? length - 1 : length)];

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

        // Checked before the blob is written: a name the stored-file row cannot hold would
        // otherwise fail the save after the bytes are on disk, answering 500 and leaving a file
        // nothing names.
        if (Path.GetFileName(file.FileName).Length > MaxOriginalNameLength)
        {
            return ApiProblems.BadRequest(
                "geofile.file_name_too_long",
                $"The file name is longer than {MaxOriginalNameLength} characters; rename the file and upload it again.");
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

        // The display name is only a starting point the owner can edit, so a long one is cut to
        // what the column holds rather than refused.
        var name = Clipped(Path.GetFileNameWithoutExtension(file.FileName), MaxNameLength);
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

    private static async Task<Results<Ok<PagedResult<GeofileDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListAsync(
        SilexGisDbContext db,
        SurfaceFilters surfaceFilters,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        string? search,
        CancellationToken ct,
        string? filter = null,
        Guid? savedFilterId = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var narrowing = await surfaceFilters.ForAsync<Geofile>(ctx, GeofileFilterWorld.Key, filter, savedFilterId, ct);
        if (narrowing.Refusal is { } refusal)
        {
            return refusal.ToProblem();
        }

        // The filter only intersects what the walk admits, so the count and the page are taken over
        // rows this caller may see and nothing else.
        var query = narrowing.Narrowing!.Apply(db.Geofiles.AsNoTracking().VisibleTo(ctx, AccessDomain.Geofiles));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(g => EF.Functions.ILike(EF.Functions.Unaccent(g.Name), EF.Functions.Unaccent(pattern)));
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var result = await query.OrderByDescending(g => g.UpdatedAt).ThenByDescending(g => g.Id).ToPagedAsync(p, size, g => g.ToDto(), ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<GeofileDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var (geofile, problem) = await LoadReadableAsync(id, db, access, accessAccessor, ct);
        if (problem is not null)
        {
            return problem;
        }

        // The update answers the version it produced; the read hands out the same one, so that a
        // client re-reading after somebody else's change holds the version its next write needs.
        await Concurrency.EmitETagAsync(http, db, VersionedTable.Geofiles, geofile!.Id, ct);
        return TypedResults.Ok(geofile.ToDto());
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

        await using var hold = await Concurrency.HoldIfMatchAsync(http, db, VersionedTable.Geofiles, geofile.Id, ct);
        if (hold.Refusal is { } stale)
        {
            return stale;
        }

        // The hold read the row again; who may change it is decided on what it holds now.
        if (!(await access.DecideAsync(ctx, AccessAction.Write, geofile, ct)).Allowed)
        {
            return (await access.DecideAsync(ctx, AccessAction.Read, geofile, ct)).Allowed
                ? ApiProblems.Forbidden()
                : ApiProblems.NotFound("geofile.not_found");
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
        await hold.AnswerAsync(ct);
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
                fileStore.GetAbsolutePath(file.StoragePath), options?.Delimited?.Delimiter, options?.Delimited?.Encoding);
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

        // A file already queued or being read is not queued again: two imports of one geofile
        // would each replace the other's features, and the options of whichever ran last would
        // win without the caller learning which. The row is held while the queue is asked, so two
        // requests at the same moment cannot both hear "nothing queued".
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await GeodataSql.LockGeofileAsync(db, geofile.Id, ct);
        if (await GeodataSql.HasLiveImportAsync(db, geofile.Id, ProcessingJobKinds.GeofileImport, ct))
        {
            return ApiProblems.Conflict(
                "geofile.import_in_progress",
                "The file is still being read; re-import it once this import has finished.");
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
        await transaction.CommitAsync(ct);

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

        // The rows that point at the geofile by kind and id — files attached to it, tags on it,
        // and its place in a relation — have no foreign key, so nothing removes them unless this
        // does; a dead target must not linger in links as a permanent restricted-looking member.
        // They go in one transaction with the geofile, so a save that fails leaves it whole.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await PolymorphicRowCleanup.RemoveRowsPointingAtAsync(db, AttachedEntityType.Geofile, [geofile.Id], ct);

        // Imported features cascade with the geofile row; the upload goes explicitly. The
        // geofile owns its upload outright, so the whole document goes — an imported file has
        // no life of its own once the geofile it produced is gone.
        db.Geofiles.Remove(geofile);
        var removed = await documents.DeleteDocumentOfFileAsync(geofile.FileId, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

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
