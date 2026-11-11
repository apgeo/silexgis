// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Persistence.Configurations;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Features.ScanProfiles;

/// <summary>A parameter file to import as a profile.</summary>
/// <param name="FileName">
/// The file's name. <c>.toml</c> is read as HDMapping's own parameter file (for its odometry),
/// <c>.json</c> as a profile exported from SilexGIS; with neither, a text starting with <c>{</c>
/// is JSON and anything else TOML. The name is also recorded as where the profile came from.
/// </param>
/// <param name="Content">The file's text.</param>
/// <param name="Name">The profile's name; when absent, the name a SilexGIS file carries, else the file name.</param>
/// <param name="DryRun">Only report what the import would make of the file; store nothing.</param>
public sealed record ProcessingProfileImportRequest(
    string FileName,
    string Content,
    ProcessingProfileScope Scope,
    Guid? CavingGroupId,
    string? Name,
    string? Description,
    bool DryRun);

/// <summary>What an import made of a file, key by key.</summary>
/// <param name="Format"><c>toml</c> or <c>json</c>, as the file was read.</param>
/// <param name="Taken">Keys the file set and the profile keeps.</param>
/// <param name="Unknown">Keys the step does not have, dropped.</param>
/// <param name="Defaulted">Keys the file left out, filled from the installed defaults.</param>
/// <param name="Fixed">Text keys the file changed; text settings are the program's own, so the default stands.</param>
/// <param name="DroppedSections">Sections never part of a profile (version, paths, display), dropped.</param>
/// <param name="Refused">Values of the wrong type or out of range; any one refuses the import.</param>
/// <param name="FileVersion">The version the file says wrote it.</param>
/// <param name="InstalledVersion">The version the values were checked against.</param>
/// <param name="VersionDiffers">The file names a version, and it is not the installed one.</param>
/// <param name="Accepted">Nothing was refused.</param>
public sealed record ProcessingProfileImportReportDto(
    string StepId,
    string Format,
    IReadOnlyList<string> Taken,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<string> Defaulted,
    IReadOnlyList<string> Fixed,
    IReadOnlyList<string> DroppedSections,
    IReadOnlyList<ProfileImportRefusal> Refused,
    string? FileVersion,
    string? InstalledVersion,
    bool VersionDiffers,
    bool Accepted);

/// <summary>The import's report, the values it resolved, and the profile it saved (absent on a dry run).</summary>
public sealed record ProcessingProfileImportResultDto(
    ProcessingProfileDto? Profile, ProcessingProfileImportReportDto Report, JsonElement Values);

/// <summary>A profile as SilexGIS writes it to a file, for any step.</summary>
/// <param name="Format">Always <c>silexgis.processing-profile</c>.</param>
/// <param name="FormatVersion">The version of this file's own shape.</param>
/// <param name="ToolVersion">The version of the tool the values were written for, when known.</param>
public sealed record ProcessingProfileFileDto(
    string Format,
    int FormatVersion,
    string StepId,
    string? ToolVersion,
    string Name,
    string? Description,
    JsonElement Values,
    DateTimeOffset ExportedAt);

public sealed class ProcessingProfileImportRequestValidator : AbstractValidator<ProcessingProfileImportRequest>
{
    /// <summary>The largest file text taken; HDMapping's whole parameter dump is a few kilobytes.</summary>
    public const int MaxContentLength = 256 * 1024;

    public ProcessingProfileImportRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(ProcessingProfileOrigins.MaxFileNameLength);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(MaxContentLength);
        RuleFor(x => x.Name).MaximumLength(ProcessingProfileConfiguration.NameLength);
        RuleFor(x => x.Description).MaximumLength(ProcessingProfileConfiguration.DescriptionLength);
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.CavingGroupId).NotNull().When(x => x.Scope == ProcessingProfileScope.CavingGroup)
            .WithMessage("A group profile names its caving group.");
        RuleFor(x => x.CavingGroupId).Null().When(x => x.Scope != ProcessingProfileScope.CavingGroup)
            .WithMessage("Only a group profile names a caving group.");
    }
}

/// <summary>
/// Profiles in and out of files: HDMapping's own TOML for its odometry, and a SilexGIS profile
/// file for any step.
/// </summary>
/// <remarks>
/// <para>
/// An import never stores what HDMapping would quietly misread. Every key is compared with the
/// installed program's defaults: unknown keys are dropped, keys the file leaves out are filled
/// from the defaults, and a value of the wrong kind or out of range refuses the whole import
/// naming the key, because the program reads such a value as zero and runs on. The sections that
/// describe the machine that wrote the file (its version, its directories, the desktop program's
/// display) are dropped; a version other than the installed one is named in the report, not
/// refused. A dry run returns the same report without storing anything, so a screen can show it
/// before the person decides.
/// </para>
/// <para>
/// Both routes need the installed program's defaults for HDMapping, which the application keeps a
/// copy of; until a worker has run once there is nothing to check a file against, and the import
/// says so rather than storing unchecked values.
/// </para>
/// </remarks>
public static class ProcessingProfileTransferEndpoints
{
    /// <summary>The file does not read as TOML or as JSON.</summary>
    public const string UnreadableCode = "processing_profile.import_unreadable";

    /// <summary>The file holds values the step's parameters cannot take; the detail names each key.</summary>
    public const string RefusedCode = "processing_profile.import_refused";

    /// <summary>The JSON file is not a SilexGIS profile file.</summary>
    public const string NotAProfileCode = "processing_profile.import_not_a_profile";

    /// <summary>No HDMapping worker has run yet, so its defaults, and what a file may hold, are not known.</summary>
    public const string DefaultsUnknownCode = "processing_profile.defaults_unknown";

    /// <summary>The export format asked for is not one the profile's step has.</summary>
    public const string FormatUnsupportedCode = "processing_profile.export_format_unsupported";

    private static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static RouteGroupBuilder MapProcessingProfileTransferEndpoints(this RouteGroupBuilder profiles)
    {
        profiles.MapPost("/import", ImportAsync).WithValidation<ProcessingProfileImportRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Imports a profile from HDMapping's TOML (odometry) or a SilexGIS profile file (any step), with a report of what was taken, dropped, filled and refused. dryRun reports without saving. 400 processing_profile.import_refused names each refused key.");
        profiles.MapGet("/{id:guid}/export", ExportAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("A profile as a file: format=toml writes HDMapping's own parameter file (odometry only), format=json (the default) a SilexGIS profile file for any step.");
        return profiles;
    }

    private static async Task<Results<Ok<ProcessingProfileImportResultDto>, Created<ProcessingProfileImportResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> ImportAsync(
        ProcessingProfileImportRequest request,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        ScanStepParameterSchemas schemas,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!request.DryRun && !ProcessingProfileRules.MayManage(ctx, request.Scope, ctx.UserId, AccessAction.Create))
        {
            return ApiProblems.Forbidden(ProcessingProfileEndpoints.ForbiddenCode);
        }

        if (!request.DryRun && request.CavingGroupId is { } groupId && !await db.CavingGroups.AnyAsync(g => g.Id == groupId, ct))
        {
            return ApiProblems.BadRequest(ProcessingProfileEndpoints.GroupUnknownCode);
        }

        var asToml = IsToml(request.FileName, request.Content);
        string stepId;
        JsonElement fileValues;
        string? fileVersion;
        string? fileName = null;
        string? fileDescription = null;
        if (asToml)
        {
            stepId = HdmappingOdometryStep.StepId;
            try
            {
                fileValues = HdmappingToml.Read(request.Content);
            }
            catch (FormatException e)
            {
                return ApiProblems.BadRequest(UnreadableCode, $"The file does not read as TOML: {e.Message}");
            }

            fileVersion = null;
        }
        else
        {
            ProcessingProfileFileDto? file;
            try
            {
                file = JsonSerializer.Deserialize<ProcessingProfileFileDto>(request.Content, FileJson);
            }
            catch (JsonException e)
            {
                return ApiProblems.BadRequest(UnreadableCode, $"The file does not read as JSON: {e.Message}");
            }

            if (file is null || file.Format != ProcessingProfileImport.FileFormat || string.IsNullOrEmpty(file.StepId))
            {
                return ApiProblems.BadRequest(NotAProfileCode,
                    $"The file is not a SilexGIS processing profile (its format must be '{ProcessingProfileImport.FileFormat}' and it must name a step).");
            }

            if (file.FormatVersion > ProcessingProfileImport.FileFormatVersion)
            {
                return ApiProblems.BadRequest(NotAProfileCode,
                    $"The file was written in a newer profile format ({file.FormatVersion}) than this server reads.");
            }

            stepId = file.StepId;
            fileValues = file.Values;
            fileVersion = file.ToolVersion;
            fileName = file.Name;
            fileDescription = file.Description;
        }

        if (ScanSteps.Find(stepId) is not { } step)
        {
            return ApiProblems.BadRequest(ProcessingProfileEndpoints.StepUnknownCode, $"No processing step is called '{stepId}'.");
        }

        if (!ProcessingProfileEndpoints.HasParameters(step))
        {
            return ApiProblems.BadRequest(ProcessingProfileEndpoints.StepWithoutParametersCode, $"The step '{stepId}' takes no parameters.");
        }

        var schema = await schemas.ForAsync(step, ct);
        ProfileImportResult result;
        if (step.Id == HdmappingOdometryStep.StepId)
        {
            if (schema.DumpedDefaults.ValueKind != JsonValueKind.Object)
            {
                return ApiProblems.Conflict(DefaultsUnknownCode,
                    "HDMapping's default parameters are not known yet: no HDMapping worker has started on this installation, so there is nothing to check the file against.");
            }

            // A TOML file is HDMapping's sections themselves; a SilexGIS file holds them under "values".
            var sections = asToml ? fileValues
                : fileValues.ValueKind == JsonValueKind.Object && fileValues.TryGetProperty("values", out var inner) ? inner
                : default;
            result = ProcessingProfileImport.Hdmapping(
                sections,
                schema.DumpedDefaults,
                fileVersion,
                asToml ? ProcessingProfileImport.SoftwareVersionOf(schema.ToolVersion) : schema.ToolVersion);
        }
        else
        {
            result = ProcessingProfileImport.Flat(
                fileValues, ScanSteps.DefaultParameters(step), DeclaredKeys(schema.SchemaJson), fileVersion, schema.ToolVersion);
        }

        var report = result.Report;
        if (report.Accepted)
        {
            report = report.WithSchemaErrors(schemas.Validate(schema, result.Values));
        }

        var reportDto = ToDto(step.Id, asToml ? "toml" : "json", report);
        if (request.DryRun)
        {
            return TypedResults.Ok(new ProcessingProfileImportResultDto(null, reportDto, result.Values));
        }

        if (!report.Accepted)
        {
            return ApiProblems.BadRequest(RefusedCode,
                "The file holds values the step cannot take: " + string.Join("; ", report.Refused.Select(r => $"{r.Key}: {r.Reason}")));
        }

        var name = FirstNonEmpty(request.Name, fileName, Path.GetFileNameWithoutExtension(request.FileName), "Imported profile")!;
        if (name.Length > ProcessingProfileConfiguration.NameLength)
        {
            name = name[..ProcessingProfileConfiguration.NameLength];
        }

        var description = FirstNonEmpty(request.Description, fileDescription);
        if (description is { Length: > ProcessingProfileConfiguration.DescriptionLength })
        {
            description = description[..ProcessingProfileConfiguration.DescriptionLength];
        }

        var profile = new ProcessingProfile
        {
            StepId = step.Id,
            ToolVersion = schema.ToolVersion,
            Name = name,
            Description = description,
            Scope = request.Scope,
            OwnerUserId = ctx.UserId,
            CavingGroupId = request.Scope == ProcessingProfileScope.CavingGroup ? request.CavingGroupId : null,
            Values = result.Values.GetRawText(),
            Origin = ProcessingProfileOrigins.Imported(request.FileName),
        };
        db.ProcessingProfiles.Add(profile);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/scan-profiles/{profile.Id}",
            new ProcessingProfileImportResultDto(ProcessingProfileEndpoints.ToDto(ctx, profile), reportDto, result.Values));
    }

    private static async Task<Results<FileContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> ExportAsync(
        Guid id,
        string? format,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        ScanStepParameterSchemas schemas,
        TimeProvider clock,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var profile = await db.ProcessingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null || !ProcessingProfileRules.MayRead(ctx, profile))
        {
            return ApiProblems.NotFound(ProcessingProfileEndpoints.NotFoundCode);
        }

        var slug = Tag.Slugify(profile.Name);
        if (string.IsNullOrEmpty(slug))
        {
            slug = "profile";
        }

        using var values = JsonDocument.Parse(profile.Values);
        switch ((format ?? "json").Trim().ToLowerInvariant())
        {
            case "json":
                var file = new ProcessingProfileFileDto(
                    ProcessingProfileImport.FileFormat, ProcessingProfileImport.FileFormatVersion, profile.StepId,
                    profile.ToolVersion, profile.Name, profile.Description, values.RootElement, clock.GetUtcNow());
                return TypedResults.File(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(file, FileJson) + "\n"), "application/json", $"{slug}.profile.json");
            case "toml" when profile.StepId == HdmappingOdometryStep.StepId:
                var schema = ScanSteps.Find(profile.StepId) is { } step ? await schemas.ForAsync(step, ct) : null;
                var sections = values.RootElement.TryGetProperty("values", out var inner) ? inner : default;
                var text = HdmappingToml.Write(
                    sections,
                    schema?.DumpedDefaults ?? default,
                    VersionInfo(schema?.DumpedDefaults ?? default, profile.ToolVersion),
                    [
                        $"HDMapping odometry parameters: \"{OneLine(profile.Name)}\", exported from SilexGIS.",
                        $"Written for HDMapping {profile.ToolVersion ?? "(version not recorded)"}.",
                    ]);
                return TypedResults.File(Encoding.UTF8.GetBytes(text), HdmappingToml.ContentType, $"{slug}.toml");
            default:
                return ApiProblems.BadRequest(FormatUnsupportedCode,
                    profile.StepId == HdmappingOdometryStep.StepId
                        ? "A profile is exported as json or toml."
                        : $"A profile of '{profile.StepId}' is exported as json; toml is HDMapping's own format.");
        }
    }

    /// <summary>
    /// The installed program's <c>[version_info]</c>, naming the version the profile was written
    /// for: HDMapping's desktop program reads the table, and the file should say what the values
    /// were tuned on, not what happens to be installed today.
    /// </summary>
    private static Dictionary<string, string>? VersionInfo(JsonElement dumpedDefaults, string? toolVersion)
    {
        var info = new Dictionary<string, string>(StringComparer.Ordinal);
        if (dumpedDefaults.ValueKind == JsonValueKind.Object
            && dumpedDefaults.TryGetProperty("version_info", out var table)
            && table.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in table.EnumerateObject().Where(k => k.Value.ValueKind == JsonValueKind.String))
            {
                info[key.Name] = key.Value.GetString()!;
            }
        }

        if (ProcessingProfileImport.SoftwareVersionOf(toolVersion) is { } version)
        {
            info["software_version"] = version;
        }

        return info.Count == 0 ? null : info;
    }

    private static bool IsToml(string fileName, string content)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".toml" => true,
            ".json" => false,
            _ => !content.TrimStart().StartsWith('{'),
        };
    }

    private static List<string> DeclaredKeys(string schemaJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        return schema.RootElement.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().Select(p => p.Name).ToList()
            : [];
    }

    private static ProcessingProfileImportReportDto ToDto(string stepId, string format, ProfileImportReport r) =>
        new(stepId, format, r.Taken, r.Unknown, r.Defaulted, r.Fixed, r.DroppedSections, r.Refused,
            r.FileVersion, r.InstalledVersion, r.VersionDiffers, r.Accepted);

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Replace("\"", "'", StringComparison.Ordinal);
}
