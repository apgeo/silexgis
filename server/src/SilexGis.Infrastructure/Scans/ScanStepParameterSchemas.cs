// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Scans;

/// <summary>Where a step's parameter schema was read from.</summary>
public enum ScanStepSchemaSource
{
    /// <summary>The schema written into the step's definition in this application.</summary>
    Registry = 0,

    /// <summary>Derived from the defaults a running worker dumped from the program it runs.</summary>
    Worker = 1,

    /// <summary>Derived from the last defaults a worker dumped, kept by the application while none is running.</summary>
    Stored = 2,
}

/// <summary>
/// The parameter schema a step's values are checked against, with the defaults it was derived from
/// when it follows the installed tool.
/// </summary>
/// <param name="ToolVersion">The version of the tool the schema describes; null when nothing has announced one.</param>
/// <param name="Defaults">The step's default parameters, in the shape a recipe stores them.</param>
/// <param name="DumpedDefaults">
/// For a step whose schema follows the installed tool, the defaults exactly as the tool dumped
/// them — with the sections a profile never holds and the non-finite values the editable defaults
/// leave out — which is what an import is compared against key by key; undefined otherwise.
/// </param>
public sealed record ScanStepParameterSchema(
    string StepId, string? ToolVersion, string SchemaJson, JsonElement Defaults, ScanStepSchemaSource Source,
    JsonElement DumpedDefaults = default);

/// <summary>
/// Answers "what may this step's parameters hold": the step's own schema for a step the
/// application defines completely, and for HDMapping's odometry a schema derived from the defaults
/// the installed program dumped, so it follows the version that will actually run.
/// </summary>
/// <remarks>
/// The worker's file is read on every ask and is the truth while it is there. Each time it says
/// something the application has not stored yet, a copy is kept in the database, and that copy is
/// what answers while no worker is running — so a profile can be edited and checked with the worker
/// stopped. Only before any worker has ever run does the odometry step fall back to its registered
/// shape, which checks no more than "sections of keys".
/// </remarks>
public sealed class ScanStepParameterSchemas(
    SilexGisDbContext db,
    ScanWorkerHandoff handoff,
    ITypedPropertiesValidator validator,
    TimeProvider clock,
    ILogger<ScanStepParameterSchemas> logger)
{
    public async Task<ScanStepParameterSchema> ForAsync(IScanStep step, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Id != HdmappingOdometryStep.StepId)
        {
            return new ScanStepParameterSchema(
                step.Id,
                handoff.ReadCapabilities()?.Find(step.Id)?.Version,
                step.ParameterSchemaJson,
                ScanSteps.DefaultParameters(step),
                ScanStepSchemaSource.Registry);
        }

        var dumped = handoff.ReadOdometryDefaultsDocument();
        if (dumped is not null)
        {
            await KeepAsync(dumped, ct);
            return Derived(step.Id, dumped.Version, dumped.Values, ScanStepSchemaSource.Worker);
        }

        var stored = await db.ScanStepDefaults.AsNoTracking().FirstOrDefaultAsync(d => d.StepId == step.Id, ct);
        if (stored is not null)
        {
            using var values = JsonDocument.Parse(stored.Values);
            return Derived(step.Id, stored.ToolVersion, values.RootElement, ScanStepSchemaSource.Stored);
        }

        return new ScanStepParameterSchema(
            step.Id, null, step.ParameterSchemaJson, ScanSteps.DefaultParameters(step), ScanStepSchemaSource.Registry);
    }

    /// <summary>What is wrong with these values for the schema, as "path: message" lines; empty when nothing is.</summary>
    public IReadOnlyList<string> Validate(ScanStepParameterSchema schema, JsonElement values)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return values.ValueKind != JsonValueKind.Object
            ? ["values must be an object"]
            : validator.Validate(schema.SchemaJson, values.GetRawText());
    }

    private static ScanStepParameterSchema Derived(string stepId, string version, JsonElement values, ScanStepSchemaSource source)
    {
        var editable = HdmappingParameterSchema.EditableDefaults(values);
        using var wrapped = JsonDocument.Parse($$"""{"values":{{editable.GetRawText()}}}""");
        return new ScanStepParameterSchema(
            stepId,
            string.IsNullOrEmpty(version) ? null : version,
            HdmappingParameterSchema.StepSchemaJson(values),
            wrapped.RootElement.Clone(),
            source,
            values.Clone());
    }

    /// <summary>
    /// Stores the dump unless the stored copy already says the same. Two requests storing the first
    /// copy at once is harmless: whichever loses finds the row the other wrote.
    /// </summary>
    private async Task KeepAsync(ScanWorkerDefaults dumped, CancellationToken ct)
    {
        var values = dumped.Values.GetRawText();
        var stored = await db.ScanStepDefaults.FirstOrDefaultAsync(d => d.StepId == dumped.StepId, ct);
        if (stored is not null
            && stored.ToolVersion == dumped.Version
            && stored.WrittenAt == dumped.WrittenAt
            && stored.NativeText == dumped.NativeText)
        {
            return;
        }

        if (stored is null)
        {
            stored = new ScanStepDefaults { StepId = dumped.StepId, ToolVersion = dumped.Version };
            db.ScanStepDefaults.Add(stored);
        }

        stored.ToolVersion = dumped.Version;
        stored.SoftwareVersion = dumped.SoftwareVersion;
        stored.Values = values;
        stored.NativeText = dumped.NativeText;
        stored.WrittenAt = dumped.WrittenAt;
        stored.StoredAt = clock.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            // Another request stored the same dump first. The copy is a convenience for when the
            // worker is gone; failing the read that noticed the file over it would be wrong.
            logger.LogDebug(e, "The default parameters of {Step} were stored by another request", dumped.StepId);
        }
        finally
        {
            db.Entry(stored).State = EntityState.Detached;
        }
    }
}
