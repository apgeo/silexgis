// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>One archived survey source of a cave, as its current revision stands.</summary>
public sealed record SurveySourceDto(
    Guid Id,
    Guid CaveId,
    SurveySourceKind Kind,
    string Name,
    string FileName,
    string? Description,
    Guid DocumentId,
    Guid FileId,
    string MediaType,
    long SizeBytes,
    int VersionNumber,
    /// <summary>Signed URL for the current revision's bytes.</summary>
    string ContentUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The raw survey material a cave's compiled models were produced from — Therion and Survex
/// sources, a project configuration, a survey app's export bundle, and the log a compilation
/// wrote.
///
/// <para>
/// Storage is the document machinery's, not this slice's: an upload is sniffed, hashed and
/// recorded exactly as any other upload is, and a corrected source is a new revision of the same
/// document rather than a second record. What this slice adds is which formats may be archived,
/// that the bytes have to be the format the name claims, and that the whole thing hangs off a
/// cave.
/// </para>
///
/// <para>
/// Access is the cave's, on the same terms as its compiled models: a survey source can name fixed
/// coordinates, so for a location-protected cave every route here withholds the records entirely
/// from callers without exact-location access.
/// </para>
///
/// <para>
/// Archiving comes first and reading second, because keeping the file is the half that is lost for
/// ever if it is not done at upload. Of the formats here only the compiler's log is read, and only
/// for what it says about the compilation that wrote it — how well the survey closes. That reading
/// is queued, not done in the request, and it never changes the bytes that were archived.
/// </para>
/// </summary>
public static class SurveySourceEndpoints
{
    /// <summary>Sources are small next to the models they compile to; matches the model cap.</summary>
    private const long MaxUploadBytes = 100L * 1024 * 1024;

    /// <summary>
    /// What the two text columns behind an archive entry hold. Checked here rather than left to the
    /// database, because the database's answer to an over-long value is a failed save with nothing a
    /// caller can act on — and by then the bytes are in the file store with no row to reference them.
    /// The file name is stored twice, as the display name and as the name it arrived under, so one
    /// limit governs both: a value that passed one cap and failed the other would be the same string
    /// accepted and refused at once.
    /// </summary>
    private const int MaxNameLength = 255;

    private const int MaxDescriptionLength = 4000;

    /// <summary>The file name will not fit what an archive entry can hold.</summary>
    private const string NameInvalidCode = "survey_source.name_invalid";

    /// <summary>The note written against the source is longer than one can be.</summary>
    private const string DescriptionInvalidCode = "survey_source.description_invalid";

    public static RouteGroupBuilder MapSurveySourceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/caves/{caveId:guid}/survey-sources", ListAsync)
            .WithTags("SurveySources")
            .WithSummary("Archived survey sources of a cave; withheld without the exact-location permission.");
        api.MapPost("/caves/{caveId:guid}/survey-sources", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes))
            .WithTags("SurveySources")
            .WithSummary("Archives a survey source file against the cave (Write on the cave).");
        api.MapDelete("/survey-sources/{id:guid}", DeleteAsync)
            .WithTags("SurveySources")
            .WithSummary("Removes the archive entry (Write on the cave); the stored document is kept.");

        return api;
    }

    private static async Task<Results<Ok<List<SurveySourceDto>>, ProblemHttpResult>> ListAsync(
        Guid caveId,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var cave = await CaveFeatureAsync(db, caveId, ct);
        if (cave is null || !(await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed)
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        // The cave stays readable; its survey material does not follow it out.
        if (!await SurveyModelAccess.LocationOpenAsync(protection, ctx, caveId, ct))
        {
            return TypedResults.Ok(new List<SurveySourceDto>());
        }

        var rows = await db.SurveySources.AsNoTracking()
            .Where(s => s.CaveFeatureId == caveId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        var current = await CurrentRevisionsAsync(db, [.. rows.Select(r => r.DocumentId)], ct);
        return TypedResults.Ok(
            rows.Where(r => current.ContainsKey(r.DocumentId))
                .Select(r => ToDto(r, current[r.DocumentId], tokens))
                .ToList());
    }

    private static async Task<Results<Created<SurveySourceDto>, UnauthorizedHttpResult, ProblemHttpResult>> UploadAsync(
        Guid caveId,
        IFormFile file,
        [FromForm] string? description,
        SilexGisDbContext db,
        ContentIntake intake,
        DocumentWriteService documents,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cave = await CaveFeatureAsync(db, caveId, ct);
        if (cave is null
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound("cave.not_found");
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        if (SurveySourceFormats.ClaimedKind(file.FileName) is not { } kind)
        {
            return ApiProblems.BadRequest(
                SurveySourceFormats.UnsupportedCode,
                $"Archive a survey source: {string.Join(", ", SurveySourceFormats.AcceptedExtensions)}.");
        }

        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return ApiProblems.BadRequest("survey_source.size_invalid", "The file is empty or exceeds 100 MB.");
        }

        // Both checked before anything is written. A file name is chosen by whoever is uploading and
        // a deep export path produces a long one without anybody trying, so this is an ordinary
        // request to answer rather than an attack to survive — and answering it after the bytes are
        // stored would leave a file in the store that no row will ever point at.
        var name = Path.GetFileName(file.FileName);
        if (name.Length == 0 || name.Length > MaxNameLength)
        {
            return ApiProblems.BadRequest(
                NameInvalidCode,
                $"The file name is longer than {MaxNameLength} characters; rename the file and archive it again.");
        }

        var note = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (note is { Length: > MaxDescriptionLength })
        {
            return ApiProblems.BadRequest(
                DescriptionInvalidCode,
                $"The description is longer than {MaxDescriptionLength} characters.");
        }

        // Stored the way every other upload is stored — one describer, so an archived source is
        // hashed, sized and sniffed exactly as a photograph or a report is.
        var content = await intake.FromStreamAsync(file.OpenReadStream(), file.FileName, file.ContentType, ct);

        // The name says which format this claims to be; only the bytes can say whether it is one.
        // Checked after storing because that is where the whole file is readable, which is also
        // why the refusal has to take the orphaned blob with it.
        if (!SurveySourceFormats.ContentMatches(kind, await HeaderAsync(fileStore, content.StoragePath, ct)))
        {
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            return ApiProblems.BadRequest(
                SurveySourceFormats.MismatchCode,
                $"This file is not what its name says: {Path.GetExtension(file.FileName).ToLowerInvariant()} "
                + $"was expected to be {SurveySourceFormats.ExpectedContent(kind)}.");
        }

        var archived = documents.Create(
            content with
            {
                // Refining the class the bytes established, never overruling it: the sniffer can
                // tell text from an archive but not one survey language from another, and the
                // extension has just been checked against the bytes rather than trusted on its own.
                // Recorded as survey material rather than as a document, because that is what it is
                // and because reading it is precisely what this archive does not do.
                MimeType = SurveySourceFormats.MediaTypeFor(kind),
                Kind = FileKind.Survey,
            },
            Path.GetFileNameWithoutExtension(file.FileName),
            ctx.UserId,
            ctx.UserId);

        var source = new SurveySource
        {
            CaveFeatureId = caveId,
            DocumentId = archived.Version.DocumentId,
            Kind = kind,
            Name = name,
            OriginalFileName = name,
            Description = note,
        };

        db.SurveySources.Add(source);

        // A compilation log is the one archived format that says something about the survey rather
        // than being the survey: how the run ended and how far each of its loops missed closing.
        // The record and the job that fills it in are written in the same save as the source, so a
        // reading is never queued against an archive entry that did not land — and the record names
        // the exact revision it will be read from, because a corrected log is a later revision of
        // this same source and its figures must not be mistaken for these.
        if (kind == SurveySourceKind.TherionLog)
        {
            var compilation = new SurveyCompilation
            {
                CaveFeatureId = caveId,
                SurveySourceId = source.Id,
                LogFileId = archived.File.Id,
                LogVersionNumber = archived.Version.VersionNumber,
            };

            db.SurveyCompilations.Add(compilation);
            db.ProcessingJobs.Add(new ProcessingJob
            {
                Kind = ProcessingJobKinds.SurveyCompilation,
                Payload = JsonSerializer.Serialize(
                    new SurveyCompilationPayload(compilation.Id), JsonSerializerOptions.Web),
                RequestedBy = ctx.UserId,
            });
        }

        await db.SaveChangesAsync(ct);

        var revision = new Revision(
            archived.File.Id,
            archived.File.MimeType,
            archived.File.SizeBytes,
            archived.Version.VersionNumber);
        return TypedResults.Created(
            $"/api/v1/caves/{caveId}/survey-sources", ToDto(source, revision, tokens));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var source = await db.SurveySources.FirstOrDefaultAsync(s => s.Id == id, ct);
        var cave = source is null ? null : await CaveFeatureAsync(db, source.CaveFeatureId, ct);
        if (source is null || cave is null
            || !await SurveyModelAccess.VisibleAsync(access, protection, ctx, cave, ct))
        {
            return ApiProblems.NotFound("survey_source.not_found");
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        // Only the archive entry goes. The document behind it is a document like any other — it may
        // be filed on a shelf, attached elsewhere or shared — and deleting it from here would take
        // those with it.
        db.SurveySources.Remove(source);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>The file of a document's current revision, for each document asked about.</summary>
    private sealed record Revision(Guid FileId, string MediaType, long SizeBytes, int VersionNumber);

    private static async Task<Dictionary<Guid, Revision>> CurrentRevisionsAsync(
        SilexGisDbContext db, List<Guid> documentIds, CancellationToken ct)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        // The uploaded file, not a rendition made from it: a converted copy hangs off the same
        // revision, and what an archive hands back has to be the bytes that were archived.
        var rows = await (
            from version in db.DocumentVersions.AsNoTracking()
            join stored in db.StoredFiles.AsNoTracking()
                on version.Id equals stored.DocumentVersionId
            where documentIds.Contains(version.DocumentId)
                && version.IsCurrent
                && stored.ConvertedFromFileId == null
            select new { version.DocumentId, version.VersionNumber, stored.Id, stored.MimeType, stored.SizeBytes })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.DocumentId)
            .ToDictionary(
                g => g.Key,
                g => new Revision(g.First().Id, g.First().MimeType, g.First().SizeBytes, g.First().VersionNumber));
    }

    private static async Task<byte[]> HeaderAsync(IFileStore fileStore, string storagePath, CancellationToken ct)
    {
        var buffer = new byte[FileFormats.HeaderBytes];
        await using var saved = await fileStore.OpenReadAsync(storagePath, ct);
        var read = await saved.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        return buffer[..read];
    }

    private static Task<Feature?> CaveFeatureAsync(SilexGisDbContext db, Guid caveFeatureId, CancellationToken ct) =>
        db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == caveFeatureId && f.Kind == FeatureKind.Cave, ct);

    private static SurveySourceDto ToDto(SurveySource s, Revision revision, IFileAccessTokenService tokens) => new(
        s.Id,
        s.CaveFeatureId,
        s.Kind,
        s.Name,
        s.OriginalFileName,
        s.Description,
        s.DocumentId,
        revision.FileId,
        revision.MediaType,
        revision.SizeBytes,
        revision.VersionNumber,
        $"/api/v1/files/{revision.FileId}/content?token={Uri.EscapeDataString(tokens.CreateToken(revision.FileId, FileDelivery.Full))}",
        s.CreatedAt,
        s.UpdatedAt);
}
