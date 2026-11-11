// SPDX-License-Identifier: AGPL-3.0-or-later
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Storage;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Scans;
using SilexGis.Infrastructure.Storage;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>
/// A scan's step done on another computer: the download of what the step reads, and the upload of
/// what it made, which carries the run on.
/// </summary>
/// <remarks>
/// <para>
/// A run that reaches a step its recipe has done elsewhere stops there, succeeded at the stage
/// that says it waits, and records the step and the artefacts it was to read. The download is
/// those artefacts, the step's parameters in the tool's own format, and a README saying what to
/// run and what to send back. Each product is sent back under a fixed name as the raw body of a
/// request, checked (a cloud's header read, a session's archive opened) before it is kept, and
/// recorded exactly as a worker's product of the same step, inputs and parameters would be; when
/// the step's last product is in, the upload is queued again, and that run reuses the products and
/// carries on from the next step.
/// </para>
/// <para>
/// Both routes are on the cave's survey terms like every route here — the inputs are the
/// recording, whose satellite lines can carry the entrance's real position — and both need Write
/// on the cave: the download is for the person who will send the products back.
/// </para>
/// </remarks>
public static partial class SurveyUploadEndpoints
{
    /// <summary>The upload is not waiting for a step done on another computer.</summary>
    public const string NotAwaitingExternalCode = "survey_upload.not_awaiting_external";

    /// <summary>The name a product was sent under is not one the waiting step makes.</summary>
    public const string ExternalOutputUnknownCode = "survey_upload.external_output_unknown";

    /// <summary>The file sent is not the product its name says.</summary>
    public const string ExternalOutputInvalidCode = "survey_upload.external_output_invalid";

    /// <summary>The file sent is larger than this installation takes for one scan file.</summary>
    public const string ExternalOutputTooLargeCode = "survey_upload.external_output_too_large";

    // The folder of the download the step's inputs go in, and the files beside it.
    private const string BundleInputsFolder = "inputs";
    private const string BundleReadme = "README.txt";

    private static void MapExternalStepEndpoints(RouteGroupBuilder uploads, long fileCap)
    {
        uploads.MapGet("/{id:guid}/external/bundle", ExternalBundleAsync)
            .WithName("getSurveyUploadExternalBundle")
            .AddEndpointFilter<ContractFilter>()
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("The inputs, parameters and instructions of the step this upload waits for, to be done on another computer, as one zip.");
        uploads.MapPut("/{id:guid}/external/{name}", PutExternalOutputAsync)
            .WithName("putSurveyUploadExternalOutput")
            .AddEndpointFilter<ContractFilter>()
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(fileCap))
            .Accepts<Stream>("application/octet-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Sends one product of the step this upload waits for, under the name the download's README gives it; the last one queues the run again.");
    }

    /// <summary>What a waiting upload waits for: the step as its recipe holds it, its definition, and the wait.</summary>
    private sealed record ExternalWait(ScanRecipe Recipe, ScanRecipeStep Step, IScanStep Definition, ScanExternalWait Wait);

    /// <summary>The wait, or null when the upload is not stopped before a step done elsewhere.</summary>
    private static ExternalWait? WaitOf(SurveyUpload upload)
    {
        if (upload.Status != SurveyUploadStatus.Succeeded
            || upload.Stage != SurveyUploadStage.AwaitingExternal
            || ScanRecipes.FromOptionsJson(upload.Options) is not { Waiting: { } wait } recipe
            || recipe.Steps.FirstOrDefault(s => s.StepId == wait.StepId) is not { } step
            || ScanSteps.Find(wait.StepId) is not { } definition)
        {
            return null;
        }

        return new ExternalWait(recipe, step, definition, wait);
    }

    private static ProblemHttpResult NotAwaiting() =>
        ApiProblems.Conflict(NotAwaitingExternalCode, "This upload is not waiting for a step done on another computer.");

    private static async Task<Results<PushStreamHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExternalBundleAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        IFileStore fileStore,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        ScanStepParameterSchemas schemas,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        if (WaitOf(found.Upload) is not { } waiting)
        {
            return NotAwaiting();
        }

        // Every input is found before the first byte is sent: once the zip has started, a missing
        // file can only cut the download short, which looks like a network fault.
        var ids = waiting.Wait.InputArtifactIds.ToList();
        var inputs = await db.ScanArtifacts.AsNoTracking()
            .Where(a => a.SurveyUploadId == found.Upload.Id && ids.Contains(a.Id))
            .Join(db.StoredFiles.AsNoTracking(), a => a.FileId, f => f.Id, (a, f) => new { a.Id, a.Kind, a.Format, f.StoragePath, f.OriginalName })
            .ToListAsync(ct);
        if (inputs.Count != ids.Count || inputs.Any(i => !File.Exists(fileStore.GetAbsolutePath(i.StoragePath))))
        {
            return ApiProblems.Conflict(FilesMissingCode, "A file the step reads can no longer be found; run the upload again.");
        }

        var entries = new List<(string Name, string Path)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in ids.Select(i => inputs.First(x => x.Id == i)))
        {
            // A recording's files keep their names, which is how HDMapping finds its chunks; any
            // other input is named after its kind. A name that is not a plain file name, or one
            // that came twice, is given the artefact's id instead, so nothing lands outside the
            // folder or over another input when the zip is unpacked.
            var plain = Path.GetFileName(input.OriginalName);
            var name = input.Kind == ScanArtifactKind.RawRecording && plain == input.OriginalName && plain.Length > 0
                ? plain
                : $"{KindName(input.Kind)}.{input.Format}";
            if (!taken.Add(name))
            {
                name = $"{input.Id:N}.{input.Format}";
                taken.Add(name);
            }

            entries.Add(($"{BundleInputsFolder}/{name}", fileStore.GetAbsolutePath(input.StoragePath)));
        }

        var schema = await schemas.ForAsync(waiting.Definition, ct);
        var (parametersName, parameters) = ParametersFile(waiting, schema);
        var readme = Readme(found.Upload, waiting, schema, parametersName);
        var download = $"{BundleStem(found.Upload)}.{waiting.Step.StepId.Replace('.', '-')}.zip";

        // The zip writer finishes each entry, and the archive's directory, with synchronous writes
        // even through its asynchronous calls, which the web server refuses by default. Allowed
        // for this one response: the alternative is to assemble gigabytes on disk first.
        if (http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>() is { } control)
        {
            control.AllowSynchronousIO = true;
        }

        return TypedResults.Stream(
            async body =>
            {
                // Written straight to the response, never assembled first: a recording is many
                // gigabytes. Point files are compressed already and are stored as they are.
                await using var zip = await ZipArchive.CreateAsync(body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, ct);
                await WriteTextAsync(zip, BundleReadme, readme, ct);
                await WriteTextAsync(zip, parametersName, parameters, ct);
                foreach (var (name, path) in entries)
                {
                    var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                    await using var target = await entry.OpenAsync(ct);
                    await using var source = File.OpenRead(path);
                    await source.CopyToAsync(target, ct);
                }
            },
            "application/zip",
            download);
    }

    private static async Task WriteTextAsync(ZipArchive zip, string name, string text, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var target = await entry.OpenAsync(ct);
        await target.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
    }

    /// <summary>
    /// The step's parameters as the download carries them: HDMapping's own parameter file for its
    /// odometry, so the desktop program reads it as it is, and the values as JSON for any other step.
    /// </summary>
    private static (string Name, string Text) ParametersFile(ExternalWait waiting, ScanStepParameterSchema schema)
    {
        if (waiting.Step.StepId == HdmappingOdometryStep.StepId)
        {
            var sections = waiting.Step.Parameters.TryGetProperty("values", out var values) ? values : default;
            var text = HdmappingToml.Write(
                sections,
                schema.DumpedDefaults,
                VersionInfoOf(schema.DumpedDefaults),
                [
                    "HDMapping odometry parameters for one recording, written by SilexGIS.",
                    $"Written for HDMapping {schema.ToolVersion ?? "(version not recorded)"}.",
                ]);
            return ("parameters.toml", text);
        }

        return ("parameters.json", JsonSerializer.Serialize(waiting.Step.Parameters, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    // The installed program's [version_info] table, which HDMapping's desktop program reads.
    private static Dictionary<string, string>? VersionInfoOf(JsonElement dumpedDefaults)
    {
        if (dumpedDefaults.ValueKind != JsonValueKind.Object
            || !dumpedDefaults.TryGetProperty("version_info", out var table)
            || table.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var info = table.EnumerateObject()
            .Where(k => k.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(k => k.Name, k => k.Value.GetString()!, StringComparer.Ordinal);
        return info.Count == 0 ? null : info;
    }

    /// <summary>
    /// What the person doing the step needs to know, in plain text: the tool and its version, what
    /// to run on which files, and every name the products are sent back under. English, like the
    /// run's other notes; it names no position and nothing out of the recording.
    /// </summary>
    private static string Readme(SurveyUpload upload, ExternalWait waiting, ScanStepParameterSchema schema, string parametersName)
    {
        var text = new StringBuilder();
        text.Append("SilexGIS: a processing step to do on another computer\n");
        text.Append("=====================================================\n\n");
        text.Append("Scan: ").Append(OneLineOf(upload.SurveyTitle)).Append('\n');
        text.Append("Step: ").Append(waiting.Definition.DisplayName).Append(" (").Append(waiting.Step.StepId).Append(")\n");
        text.Append("Tool: ").Append(waiting.Definition.Tool);
        if (!string.IsNullOrEmpty(schema.ToolVersion))
        {
            text.Append(", version ").Append(schema.ToolVersion).Append(" on the server");
        }

        text.Append("\n\n");
        text.Append("What is in this archive\n-----------------------\n");
        text.Append($"{BundleInputsFolder}/  the files the step reads.\n");
        text.Append(parametersName).Append("  the parameters the server would have run the step with.\n\n");
        if (waiting.Step.StepId == HdmappingOdometryStep.StepId)
        {
            text.Append("What to run\n-----------\n");
            text.Append("HDMapping's LiDAR odometry (step 1) over the recording, with the parameter file:\n\n");
            text.Append($"    lidar_odometry_step_1 {BundleInputsFolder} {parametersName} <session folder>\n\n");
            text.Append("or open the recording and the parameter file in HDMapping's odometry program.\n\n");
        }

        text.Append("What to send back\n-----------------\n");
        text.Append("Upload each file below on the scan's page, under exactly this name:\n\n");
        foreach (var kind in waiting.Definition.Outputs)
        {
            text.Append("    ").Append(string.Join(" or ", ScanExternalOutputs.NamesFor(kind))).Append("  ").Append(OutputWords(kind)).Append('\n');
        }

        text.Append("\nWhen the last one has arrived, the server carries on with the next step.\n");
        return text.ToString();

        static string OutputWords(ScanArtifactKind kind) => kind switch
        {
            ScanArtifactKind.HdmSession =>
                "the session folder zipped, with session.mjs at the root of the archive (zip the folder's contents, not the folder); the cache folder may be left out.",
            ScanArtifactKind.PointCloudLaz => "the exported point cloud, LAZ or LAS.",
            ScanArtifactKind.Trajectory => "the trajectory, as the tool exports it.",
            ScanArtifactKind.Report => "the step's report, JSON.",
            _ => "",
        };
    }

    private static string OneLineOf(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "(untitled)" : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string BundleStem(SurveyUpload upload)
    {
        var stem = FileFormats.FitFileName(upload.SurveyTitle);
        return string.IsNullOrWhiteSpace(stem) ? "scan" : stem;
    }

    private static string KindName(ScanArtifactKind kind) => kind switch
    {
        ScanArtifactKind.HdmSession => "session",
        ScanArtifactKind.PointCloudLaz => "cloud",
        ScanArtifactKind.Trajectory => "trajectory",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static async Task<Results<Ok<SurveyUploadDto>, Accepted<SurveyUploadDto>, UnauthorizedHttpResult, ProblemHttpResult>> PutExternalOutputAsync(
        Guid id,
        string name,
        HttpRequest http,
        SilexGisDbContext db,
        ContentIntake intake,
        IFileStore fileStore,
        IAccessService access,
        FeatureProtection protection,
        IAccessContextAccessor accessAccessor,
        StorageFiguresService storage,
        PointCloudProcessing processing,
        IFileAccessTokenService tokens,
        SurveyUploadPublicLinkService links,
        IOptions<SurveyUploadOptions> options,
        IOptions<ScanOptions> scans,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var found = await VisibleUploadAsync(db, access, protection, ctx, id, ct);
        if (found is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!await SurveyModelAccess.WritableAsync(access, ctx, found.Cave, ct))
        {
            return ApiProblems.Forbidden();
        }

        var upload = found.Upload;
        if (WaitOf(upload) is not { } waiting)
        {
            return NotAwaiting();
        }

        if (ScanExternalOutputs.KindOf(waiting.Definition, name) is not { } kind)
        {
            var names = waiting.Definition.Outputs.SelectMany(ScanExternalOutputs.NamesFor);
            return ApiProblems.BadRequest(
                ExternalOutputUnknownCode,
                $"The step this upload waits for makes no file by that name; it takes {string.Join(", ", names)}.",
                "name",
                name);
        }

        var cap = scans.Value.UploadCap;
        if (http.ContentLength is { } announced)
        {
            if (announced > cap)
            {
                return TooLarge(cap);
            }

            var room = await storage.ForAsync(ctx.UserId, ct: ct);
            if (StorageRules.Shortfall(room.Limits(scan: true), announced) is { } shortfall)
            {
                return StorageProblems.Insufficient(shortfall);
            }
        }

        var content = await intake.FromStreamAsync(new CappedStream(http.Body, cap + 1), name, http.ContentType, ct);
        if (content.SizeBytes > cap)
        {
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            return TooLarge(cap);
        }

        if (await PointCloudProcessing.ExternalOutputProblemAsync(kind, fileStore.GetAbsolutePath(content.StoragePath), ct) is { } problem)
        {
            await fileStore.DeleteAsync(content.StoragePath, CancellationToken.None);
            return ApiProblems.BadRequest(ExternalOutputInvalidCode, problem, "name", name);
        }

        await processing.StoreExternalOutputAsync(upload, waiting.Step, waiting.Wait, kind, name, content, ct);
        if (!await processing.ExternalOutputsCompleteAsync(upload.Id, waiting.Step, waiting.Wait, ct))
        {
            await db.Entry(upload).ReloadAsync(ct);
            return TypedResults.Ok(await ToDtoAsync(db, tokens, links, options.Value, upload, found.Cave, writable: true, ct));
        }

        // The step's products are all in: the run is queued again, and reuses them. The wait is
        // taken off the recipe in the same save, so a second request for the last product cannot
        // queue it twice — it finds the upload no longer waiting.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await SurveyUploadWrites.ClearRunAsync(db, upload.Id, ct))
            {
                return ApiProblems.Conflict(RunInProgressCode, "This upload's processing is already queued or running.");
            }

            await db.Entry(upload).ReloadAsync(ct);
            upload.Options = ScanRecipes.ToOptionsJson(waiting.Recipe with { Waiting = null });
            upload.Status = SurveyUploadStatus.Queued;
            Queue(db, upload);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await db.Entry(upload).ReloadAsync(ct);
        return Accepted(await ToDtoAsync(db, tokens, links, options.Value, upload, found.Cave, writable: true, ct));

        static ProblemHttpResult TooLarge(long cap) =>
            ApiProblems.BadRequest(
                ExternalOutputTooLargeCode,
                $"The file is larger than the {cap} bytes this installation takes for one scan file.",
                "maxBytes",
                cap);
    }
}
