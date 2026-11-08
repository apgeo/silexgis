// SPDX-License-Identifier: AGPL-3.0-or-later
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

/// <summary>A named set of parameters for one scan processing step.</summary>
/// <param name="ToolVersion">The version of the tool the values were written for, when one was known.</param>
/// <param name="OwnerUserId">The author; absent for a built-in profile.</param>
/// <param name="CavingGroupId">The group a group profile is kept for.</param>
/// <param name="Values">The step's parameters, in the shape a recipe stores them.</param>
/// <param name="Origin"><c>builtIn</c>, <c>edited</c>, or <c>imported:</c> and the file name.</param>
/// <param name="BuiltIn">Shipped with the installation, and never changed in place.</param>
/// <param name="MayEdit">Whether the caller may change this profile.</param>
/// <param name="MayDelete">Whether the caller may delete this profile.</param>
public sealed record ProcessingProfileDto(
    Guid Id,
    string StepId,
    string? ToolVersion,
    string Name,
    string? Description,
    ProcessingProfileScope Scope,
    Guid? OwnerUserId,
    Guid? CavingGroupId,
    JsonElement Values,
    string Origin,
    bool BuiltIn,
    bool MayEdit,
    bool MayDelete,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A new profile.</summary>
/// <param name="StepId">The registered step whose parameters these are.</param>
/// <param name="CavingGroupId">The group, for a group profile; absent for every other scope.</param>
/// <param name="Values">The step's parameters, in the shape a recipe stores them.</param>
public sealed record ProcessingProfileCreateRequest(
    string StepId,
    string Name,
    string? Description,
    ProcessingProfileScope Scope,
    Guid? CavingGroupId,
    JsonElement Values);

/// <summary>A profile's name, description and values, rewritten as a whole. Its step and scope are fixed.</summary>
public sealed record ProcessingProfileUpdateRequest(string Name, string? Description, JsonElement Values);

/// <summary>What a step's parameters may hold, and the values it runs with when nobody chose otherwise.</summary>
/// <param name="ToolVersion">The version of the tool the schema describes, when one is known.</param>
/// <param name="Schema">A JSON Schema (2020-12) of the step's whole parameter object.</param>
/// <param name="Defaults">The default parameter object.</param>
/// <param name="Source">
/// <c>registry</c> (written into this application), <c>worker</c> (derived from what the running
/// worker's program dumped) or <c>stored</c> (derived from the last dump kept while no worker runs).
/// </param>
public sealed record ScanStepParameterSchemaDto(
    string StepId, string? ToolVersion, JsonElement Schema, JsonElement Defaults, ScanStepSchemaSource Source);

public sealed class ProcessingProfileCreateRequestValidator : AbstractValidator<ProcessingProfileCreateRequest>
{
    public ProcessingProfileCreateRequestValidator()
    {
        RuleFor(x => x.StepId).NotEmpty().MaximumLength(ScanArtifactConfiguration.StepIdLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProcessingProfileConfiguration.NameLength);
        RuleFor(x => x.Description).MaximumLength(ProcessingProfileConfiguration.DescriptionLength);
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.CavingGroupId).NotNull().When(x => x.Scope == ProcessingProfileScope.CavingGroup)
            .WithMessage("A group profile names its caving group.");
        RuleFor(x => x.CavingGroupId).Null().When(x => x.Scope != ProcessingProfileScope.CavingGroup)
            .WithMessage("Only a group profile names a caving group.");
        RuleFor(x => x.Values).Must(v => v.ValueKind == JsonValueKind.Object)
            .WithMessage("Values must be a JSON object.");
    }
}

public sealed class ProcessingProfileUpdateRequestValidator : AbstractValidator<ProcessingProfileUpdateRequest>
{
    public ProcessingProfileUpdateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProcessingProfileConfiguration.NameLength);
        RuleFor(x => x.Description).MaximumLength(ProcessingProfileConfiguration.DescriptionLength);
        RuleFor(x => x.Values).Must(v => v.ValueKind == JsonValueKind.Object)
            .WithMessage("Values must be a JSON object.");
    }
}

/// <summary>
/// Processing profiles: the named parameter sets a scan processing step can run with, kept for
/// oneself, for a caving group, or for the whole installation.
/// </summary>
/// <remarks>
/// <para>
/// Who sees and who changes a profile is decided in one place, <see cref="ProcessingProfileRules"/>:
/// a personal profile is its author's alone; a group profile is read by the group's members and an
/// installation profile by everyone; changing either of those is the scan-profiles right. A profile
/// the caller may not read is answered as absent, never as forbidden, so the routes do not tell
/// anyone which profiles exist in groups they are not in.
/// </para>
/// <para>
/// Values are checked against the step's schema before they are stored — for HDMapping's odometry
/// the schema derived from what the installed program dumped — so a profile never holds a key the
/// program does not have, or a value of the wrong type that the program would silently read as
/// zero. Not open to a personal access key: profiles are managed on the web.
/// </para>
/// </remarks>
public static class ProcessingProfileEndpoints
{
    public const string NotFoundCode = "processing_profile.not_found";

    public const string ForbiddenCode = "access.forbidden";

    /// <summary>The step named is not one this server knows.</summary>
    public const string StepUnknownCode = "processing_profile.step_unknown";

    /// <summary>The step takes no parameters, so a profile for it would hold nothing.</summary>
    public const string StepWithoutParametersCode = "processing_profile.step_without_parameters";

    /// <summary>The values do not fit the step's parameter schema; the detail names each key.</summary>
    public const string ValuesInvalidCode = "processing_profile.values_invalid";

    /// <summary>The caving group named does not exist.</summary>
    public const string GroupUnknownCode = "processing_profile.group_unknown";

    /// <summary>A built-in profile is never changed or deleted; save a copy instead.</summary>
    public const string BuiltInCode = "processing_profile.built_in";

    public static RouteGroupBuilder MapProcessingProfileEndpoints(this RouteGroupBuilder api)
    {
        var profiles = api.MapGroup("/scan-profiles").WithTags("ScanProfiles");

        profiles.MapGet("/", ListAsync)
            .WithSummary("The processing profiles the caller may use, optionally for one step: installation ones, their groups' ones and their own.");
        profiles.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One processing profile. Answered as absent where the caller may not read it.");
        profiles.MapPost("/", CreateAsync).WithValidation<ProcessingProfileCreateRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Saves a processing profile. A personal one is anybody's to save; an installation or group one needs the scan-profiles Create right. 400 processing_profile.values_invalid names each key the step's schema refuses.");
        profiles.MapPut("/{id:guid}", UpdateAsync).WithValidation<ProcessingProfileUpdateRequest>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Rewrites a profile's name, description and values. 409 processing_profile.built_in for a built-in one.");
        profiles.MapDelete("/{id:guid}", DeleteAsync)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Deletes a profile. Past runs keep the values they resolved; 409 processing_profile.built_in for a built-in one.");
        profiles.MapGet("/schemas/{stepId}", SchemaAsync)
            .WithSummary("What a step's parameters may hold and its defaults; for HDMapping's odometry, as the installed program dumped them.");
        profiles.MapProcessingProfileTransferEndpoints();
        profiles.MapProcessingProfileUseEndpoints();

        return api;
    }

    private static async Task<Results<Ok<List<ProcessingProfileDto>>, UnauthorizedHttpResult>> ListAsync(
        string? stepId,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var userId = ctx.UserId;
        var groups = ctx.CavingGroupIds.ToList();
        var everyGroup = ProcessingProfileRules.MayReadEveryGroup(ctx);
        var query = db.ProcessingProfiles.AsNoTracking()
            .Where(p => p.Scope == ProcessingProfileScope.Installation
                || (p.Scope == ProcessingProfileScope.Personal && p.OwnerUserId == userId)
                || (p.Scope == ProcessingProfileScope.CavingGroup
                    && (everyGroup || (p.CavingGroupId != null && groups.Contains(p.CavingGroupId.Value)))));
        if (!string.IsNullOrEmpty(stepId))
        {
            query = query.Where(p => p.StepId == stepId);
        }

        var rows = await query.OrderBy(p => p.StepId).ThenBy(p => p.Scope).ThenBy(p => p.Name).ThenBy(p => p.Id)
            .ToListAsync(ct);

        // The query and the rule are two statements of one decision; the rule has the last word,
        // so a row the query let through by mistake is still not shown.
        return TypedResults.Ok(rows.Where(p => ProcessingProfileRules.MayRead(ctx, p)).Select(p => ToDto(ctx, p)).ToList());
    }

    private static async Task<Results<Ok<ProcessingProfileDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var profile = await db.ProcessingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return profile is null || !ProcessingProfileRules.MayRead(ctx, profile)
            ? ApiProblems.NotFound(NotFoundCode)
            : TypedResults.Ok(ToDto(ctx, profile));
    }

    private static async Task<Results<Created<ProcessingProfileDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        ProcessingProfileCreateRequest request,
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

        if (!ProcessingProfileRules.MayManage(ctx, request.Scope, ctx.UserId, AccessAction.Create))
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        if (request.CavingGroupId is { } groupId && !await db.CavingGroups.AnyAsync(g => g.Id == groupId, ct))
        {
            return ApiProblems.BadRequest(GroupUnknownCode);
        }

        var (schema, refusal) = await SchemaForAsync(request.StepId, schemas, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        if (Refuse(schemas, schema!, request.Values) is { } invalid)
        {
            return invalid;
        }

        var profile = new ProcessingProfile
        {
            StepId = request.StepId,
            ToolVersion = schema!.ToolVersion,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Scope = request.Scope,
            OwnerUserId = ctx.UserId,
            CavingGroupId = request.Scope == ProcessingProfileScope.CavingGroup ? request.CavingGroupId : null,
            Values = request.Values.GetRawText(),
            Origin = ProcessingProfileOrigins.Edited,
        };
        db.ProcessingProfiles.Add(profile);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/scan-profiles/{profile.Id}", ToDto(ctx, profile));
    }

    private static async Task<Results<Ok<ProcessingProfileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        ProcessingProfileUpdateRequest request,
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

        var profile = await db.ProcessingProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null || !ProcessingProfileRules.MayRead(ctx, profile))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (ProcessingProfileOrigins.IsBuiltIn(profile.Origin))
        {
            return ApiProblems.Conflict(BuiltInCode, "A built-in profile is not changed in place; save a copy of it instead.");
        }

        if (!ProcessingProfileRules.MayChange(ctx, profile, AccessAction.Write))
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        var (schema, refusal) = await SchemaForAsync(profile.StepId, schemas, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        if (Refuse(schemas, schema!, request.Values) is { } invalid)
        {
            return invalid;
        }

        profile.Name = request.Name.Trim();
        profile.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var values = request.Values.GetRawText();
        if (!JsonEquals(profile.Values, values))
        {
            // New values are written for the version that checked them, and they are now somebody's
            // edit, whatever file they first came from.
            profile.Values = values;
            profile.ToolVersion = schema!.ToolVersion;
            profile.Origin = ProcessingProfileOrigins.Edited;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToDto(ctx, profile));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var profile = await db.ProcessingProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null || !ProcessingProfileRules.MayRead(ctx, profile))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (ProcessingProfileOrigins.IsBuiltIn(profile.Origin))
        {
            return ApiProblems.Conflict(BuiltInCode, "A built-in profile cannot be deleted.");
        }

        if (!ProcessingProfileRules.MayChange(ctx, profile, AccessAction.Delete))
        {
            return ApiProblems.Forbidden(ForbiddenCode);
        }

        db.ProcessingProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ScanStepParameterSchemaDto>, UnauthorizedHttpResult, ProblemHttpResult>> SchemaAsync(
        string stepId,
        IAccessContextAccessor accessAccessor,
        ScanStepParameterSchemas schemas,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (ScanSteps.Find(stepId) is not { } step)
        {
            return ApiProblems.NotFound(StepUnknownCode);
        }

        var schema = await schemas.ForAsync(step, ct);
        using var schemaDocument = JsonDocument.Parse(schema.SchemaJson);
        return TypedResults.Ok(new ScanStepParameterSchemaDto(
            schema.StepId, schema.ToolVersion, schemaDocument.RootElement.Clone(), schema.Defaults, schema.Source));
    }

    internal static async Task<(ScanStepParameterSchema? Schema, ProblemHttpResult? Refusal)> SchemaForAsync(
        string stepId, ScanStepParameterSchemas schemas, CancellationToken ct)
    {
        if (ScanSteps.Find(stepId) is not { } step)
        {
            return (null, ApiProblems.BadRequest(StepUnknownCode, $"No processing step is called '{stepId}'."));
        }

        if (!HasParameters(step))
        {
            return (null, ApiProblems.BadRequest(StepWithoutParametersCode, $"The step '{stepId}' takes no parameters."));
        }

        return (await schemas.ForAsync(step, ct), null);
    }

    internal static ProblemHttpResult? Refuse(ScanStepParameterSchemas schemas, ScanStepParameterSchema schema, JsonElement values)
    {
        var errors = schemas.Validate(schema, values);
        return errors.Count == 0
            ? null
            : ApiProblems.BadRequest(ValuesInvalidCode, string.Join("; ", errors));
    }

    /// <summary>Whether a step's schema declares any parameter at all.</summary>
    internal static bool HasParameters(IScanStep step)
    {
        using var schema = JsonDocument.Parse(step.ParameterSchemaJson);
        return schema.RootElement.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.EnumerateObject().Any();
    }

    private static bool JsonEquals(string a, string b)
    {
        using var left = JsonDocument.Parse(a);
        using var right = JsonDocument.Parse(b);
        return JsonElement.DeepEquals(left.RootElement, right.RootElement);
    }

    internal static ProcessingProfileDto ToDto(AccessContext ctx, ProcessingProfile p)
    {
        using var values = JsonDocument.Parse(p.Values);
        return new ProcessingProfileDto(
            p.Id, p.StepId, p.ToolVersion, p.Name, p.Description, p.Scope, p.OwnerUserId, p.CavingGroupId,
            values.RootElement.Clone(), p.Origin, ProcessingProfileOrigins.IsBuiltIn(p.Origin),
            ProcessingProfileRules.MayChange(ctx, p, AccessAction.Write),
            ProcessingProfileRules.MayChange(ctx, p, AccessAction.Delete),
            p.CreatedAt, p.UpdatedAt);
    }
}
