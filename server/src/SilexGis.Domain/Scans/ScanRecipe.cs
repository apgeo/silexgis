// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Scans;

/// <summary>
/// One step of a recipe as it was resolved for one upload: the step, every parameter value it
/// will run with, and the hash of those values.
/// </summary>
/// <param name="StepId">The step's registry id.</param>
/// <param name="Parameters">The resolved parameter values, a JSON object; never a profile reference, so the run means the same thing whatever a profile says later.</param>
/// <param name="ParamsHash">The hash of <paramref name="Parameters"/> as <see cref="ScanRecipes.ParamsHash"/> computes it, kept beside the values so an artefact row can be matched to the step that would make it without re-hashing.</param>
public sealed record ScanRecipeStep(string StepId, JsonElement Parameters, string ParamsHash);

/// <summary>
/// The processing an upload is asked for: its steps in order, each with resolved parameters. A
/// snapshot — it is written into the upload's options when the upload is opened and read back
/// unchanged by every rerun.
/// </summary>
/// <param name="Steps">The steps in the order they run.</param>
/// <param name="RerunFrom">
/// The id of a step from which a rerun repeats the work even where an earlier run's artefacts
/// would do, or null to reuse whatever still matches. Set by a rerun that asks for a step to be
/// done again; a step before it is still reused. One-shot: the run that takes it up clears it
/// from the stored recipe, so the next plain rerun reuses again.
/// </param>
/// <param name="DisplayChosen">
/// Whether somebody chose how much of the cloud to show — the whole of it, or a thinned copy.
/// A recipe nobody chose for has no thinning step either, so the steps alone cannot tell "the
/// whole cloud, deliberately" from "nobody has decided yet"; and the difference matters, because
/// a run that has estimated a cloud over the installation's thresholds stops there to ask rather
/// than tile the whole thing unasked.
/// </param>
/// <param name="External">
/// The ids of the steps this upload has done on another computer instead of by the server's
/// worker, or null for none. A step whose definition always runs elsewhere need not be named.
/// Not part of any step's parameters: a step done elsewhere with the same values makes the same
/// product, so its artefact is matched exactly as a worker's would be.
/// </param>
/// <param name="Waiting">
/// Where the last run stopped for a step done elsewhere — the step and the artefacts it was to
/// read — or null when it is waiting for none. Written by the run when it stops, read by the
/// download of the step's inputs and by the upload of its outputs, cleared when the run is queued
/// again.
/// </param>
public sealed record ScanRecipe(
    IReadOnlyList<ScanRecipeStep> Steps,
    string? RerunFrom = null,
    bool DisplayChosen = false,
    IReadOnlyList<string>? External = null,
    ScanExternalWait? Waiting = null)
{
    /// <summary>
    /// Whether this upload has the step done on another computer: named so in the recipe, or a
    /// step that only ever runs elsewhere.
    /// </summary>
    public bool RunsElsewhere(string stepId) =>
        External?.Contains(stepId, StringComparer.Ordinal) == true
        || ScanSteps.Find(stepId)?.WhereItRuns == ScanStepLocation.External;

    /// <summary>Whether a run may reuse an earlier run's artefact of this step.</summary>
    public bool MayReuse(string stepId)
    {
        if (RerunFrom is null)
        {
            return true;
        }

        var from = Steps.ToList().FindIndex(s => s.StepId == RerunFrom);
        var at = Steps.ToList().FindIndex(s => s.StepId == stepId);
        return from >= 0 && at >= 0 && at < from;
    }
}

/// <summary>
/// The recipes a point cloud is given: the header read, the thinning estimated, the cloud thinned
/// when a person chose so, and the result tiled. Each step's values are checked against its
/// schema before they are written, so a default the schema itself would refuse — a defect in the
/// step, not in the request — is caught before it reaches a run, and a choice that does not fit is
/// refused with the schema's own words.
/// </summary>
public static class PointCloudRecipes
{
    /// <summary>
    /// The recipe an upload gets when it is opened: nobody has chosen anything yet. The full
    /// cloud is tiled when the estimate finds it small enough; over the thresholds the run stops
    /// at the estimate and waits for a choice.
    /// </summary>
    public static ScanRecipe Default(ITypedPropertiesValidator schemas) => For(schemas, choice: null, rerunFrom: null, out _)!;

    /// <summary>
    /// The recipe for a choice of how much to show — null when nobody has chosen. Answers null
    /// and the schema's errors when the choice's values do not satisfy the downsample step's
    /// schema.
    /// </summary>
    public static ScanRecipe? For(
        ITypedPropertiesValidator schemas, PointCloudDisplayChoice? choice, string? rerunFrom, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        var steps = new List<ScanRecipeStep>
        {
            Resolved(schemas, PointCloudInspectStep.StepId, null, out _)!,
            Resolved(schemas, PointCloudEstimateStep.StepId, null, out _)!,
        };

        errors = [];
        if (choice is not null && choice.Method != PointCloudDisplayChoice.Full)
        {
            var chosen = new Dictionary<string, object?>(StringComparer.Ordinal) { ["method"] = choice.Method };
            if (choice.VoxelCm is { } voxel)
            {
                chosen["voxelCm"] = voxel;
            }

            if (choice.EveryNth is { } nth)
            {
                chosen["everyNth"] = nth;
            }

            using var document = JsonDocument.Parse(JsonSerializer.Serialize(chosen));
            var thinned = Resolved(schemas, PointCloudDownsampleStep.StepId, document.RootElement, out errors);
            if (thinned is null)
            {
                return null;
            }

            steps.Add(thinned);
        }

        steps.Add(Resolved(schemas, Py3dtilesTileStep.StepId, null, out _)!);
        return new ScanRecipe(steps, rerunFrom, DisplayChosen: choice is not null);
    }

    /// <summary>
    /// The recipe of a raw scanner recording: the recording inspected, its odometry run, the
    /// session exported into one cloud and the export checked, and from there exactly a point
    /// cloud's recipe for the same choice — the thinning estimated, the cloud thinned when chosen,
    /// the result tiled. Answers null and the schema's errors as <see cref="For"/> does.
    /// </summary>
    /// <param name="odometry">
    /// The odometry step to keep — a rerun keeps the parameters the recording was processed with —
    /// or null for the step at its defaults, whose values the run resolves before it runs.
    /// </param>
    public static ScanRecipe? ForRecording(
        ITypedPropertiesValidator schemas,
        PointCloudDisplayChoice? choice,
        string? rerunFrom,
        ScanRecipeStep? odometry,
        out IReadOnlyList<string> errors)
    {
        var cloud = For(schemas, choice, rerunFrom, out errors);
        if (cloud is null)
        {
            return null;
        }

        var steps = cloud.Steps.ToList();
        var after = steps.FindIndex(s => s.StepId == PointCloudInspectStep.StepId) + 1;
        steps.InsertRange(after,
        [
            odometry is { StepId: HdmappingOdometryStep.StepId } kept
                ? kept
                : Resolved(schemas, HdmappingOdometryStep.StepId, null, out _)!,
            Resolved(schemas, HdmappingExportStep.StepId, null, out _)!,
            Resolved(schemas, PointCloudCheckStep.StepId, null, out _)!,
        ]);
        return cloud with { Steps = steps };
    }

    /// <summary>
    /// The recipe of a refined HDMapping session sent as a scan's next version: a recording's
    /// recipe without the odometry, which was done — and refined — on the desktop the session
    /// comes from. The run starts at the export, and from there is a recording's.
    /// </summary>
    public static ScanRecipe? ForRefinedSession(
        ITypedPropertiesValidator schemas, PointCloudDisplayChoice? choice, string? rerunFrom, out IReadOnlyList<string> errors)
    {
        var recording = ForRecording(schemas, choice, rerunFrom, odometry: null, out errors);
        return recording is null
            ? null
            : recording with { Steps = [.. recording.Steps.Where(s => s.StepId != HdmappingOdometryStep.StepId)] };
    }

    /// <summary>
    /// Whether a stored recipe is a refined session's: it exports a session it never made. A
    /// rerun rebuilds such a recipe without the odometry, as the upload was opened.
    /// </summary>
    public static bool StartsFromSession(ScanRecipe? recipe) =>
        recipe is not null
        && recipe.Steps.Any(s => s.StepId == HdmappingExportStep.StepId)
        && !recipe.Steps.Any(s => s.StepId == HdmappingOdometryStep.StepId);

    /// <summary>The recipe with its odometry step's parameter set replaced, hashed afresh; unchanged when it has no odometry step.</summary>
    public static ScanRecipe WithOdometryValues(ScanRecipe recipe, JsonElement values)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        using var parameters = JsonDocument.Parse(
            JsonSerializer.Serialize(new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["values"] = values }));
        return recipe with
        {
            Steps = [.. recipe.Steps.Select(s => s.StepId == HdmappingOdometryStep.StepId ? ScanRecipes.Step(s.StepId, parameters.RootElement) : s)],
        };
    }

    /// <summary>
    /// The recipe with a step switched to be done on another computer, or null when the step is
    /// not one that may be: a step the recipe does not hold, one the API does itself (reading
    /// a header is not offered as a download), or one whose products have no name to be sent back under. Switching a step already switched changes nothing.
    /// </summary>
    public static ScanRecipe? WithExternal(ScanRecipe recipe, string stepId)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (!recipe.Steps.Any(s => s.StepId == stepId)
            || ScanSteps.Find(stepId) is not { WhereItRuns: not ScanStepLocation.Api } step
            || step.Outputs.Count == 0
            || step.Outputs.Any(k => ScanExternalOutputs.NamesFor(k).Count == 0))
        {
            return null;
        }

        return recipe.RunsElsewhere(stepId)
            ? recipe
            : recipe with { External = [.. recipe.External ?? [], stepId] };
    }

    /// <summary>The recipe with one step's parameters replaced, hashed afresh; unchanged when the recipe has no such step.</summary>
    public static ScanRecipe WithStepParameters(ScanRecipe recipe, string stepId, JsonElement parameters)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe with
        {
            Steps = [.. recipe.Steps.Select(s => s.StepId == stepId ? ScanRecipes.Step(s.StepId, parameters) : s)],
        };
    }

    /// <summary>
    /// The odometry parameters the recipe carries, completed with the program's defaults for every
    /// key they do not name; null when the recipe has no odometry step or nothing would change.
    /// </summary>
    /// <remarks>
    /// A set chosen from a profile names only the keys the profile changes, and while no worker has
    /// dumped its defaults there is nothing to complete it with. The run completes it once the
    /// defaults are known, so the upload still records the whole set the step ran with. An empty
    /// set becomes the defaults themselves.
    /// </remarks>
    public static JsonElement? CompletedOdometryValues(ScanRecipe recipe, JsonElement defaults)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var step = recipe.Steps.FirstOrDefault(s => s.StepId == HdmappingOdometryStep.StepId);
        if (step is null || defaults.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var current = step.Parameters.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object
            ? values
            : default;
        var completed = ProcessingProfileValues.LayOver(defaults, current);
        return current.ValueKind == JsonValueKind.Object && JsonElement.DeepEquals(completed, current) ? null : completed;
    }

    /// <summary>Whether the recipe's odometry step still waits for its parameters to be resolved: it carries an empty set.</summary>
    public static bool OdometryUnresolved(ScanRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var step = recipe.Steps.FirstOrDefault(s => s.StepId == HdmappingOdometryStep.StepId);
        return step is not null
            && (!step.Parameters.TryGetProperty("values", out var values)
                || values.ValueKind != JsonValueKind.Object
                || !values.EnumerateObject().Any());
    }

    /// <summary>
    /// A step with the given values laid over its defaults, every value written out, checked
    /// against the step's schema. The defaults alone must always pass: failing on them is a
    /// defect in the step, and is thrown rather than answered.
    /// </summary>
    private static ScanRecipeStep? Resolved(
        ITypedPropertiesValidator schemas, string stepId, JsonElement? chosen, out IReadOnlyList<string> errors)
    {
        var step = ScanSteps.Find(stepId)
            ?? throw new InvalidOperationException($"The scan step {stepId} is not registered.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in ScanSteps.DefaultParameters(step).EnumerateObject())
        {
            values[property.Name] = property.Value;
        }

        if (chosen is { ValueKind: JsonValueKind.Object } given)
        {
            foreach (var property in given.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }
        }

        using var merged = JsonDocument.Parse(JsonSerializer.Serialize(values));
        var found = schemas.Validate(step.ParameterSchemaJson, merged.RootElement.GetRawText());
        if (found.Count > 0)
        {
            if (chosen is null)
            {
                throw new InvalidOperationException(
                    $"The defaults of scan step {step.Id} do not satisfy its own schema: {string.Join("; ", found)}.");
            }

            errors = found;
            return null;
        }

        errors = [];
        return ScanRecipes.Step(step.Id, merged.RootElement);
    }
}

/// <summary>
/// Builds, writes and reads recipes, and computes the one hash every artefact's parameters are
/// compared by.
/// </summary>
public static class ScanRecipes
{
    /// <summary>The name the recipe is stored under in the upload's options document.</summary>
    public const string OptionsKey = "recipe";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A step with its hash computed from the parameters given.</summary>
    public static ScanRecipeStep Step(string stepId, JsonElement parameters) =>
        new(stepId, parameters.Clone(), ParamsHash(parameters));

    /// <summary>A step with no parameters: an empty object, hashed like any other.</summary>
    public static ScanRecipeStep Step(string stepId)
    {
        using var empty = JsonDocument.Parse("{}");
        return Step(stepId, empty.RootElement);
    }

    /// <summary>
    /// A step at its schema's defaults, every value written out and hashed: what an upload is
    /// given when nobody chooses parameters for it.
    /// </summary>
    public static ScanRecipeStep StepWithDefaults(IScanStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return Step(step.Id, ScanSteps.DefaultParameters(step));
    }

    /// <summary>
    /// The hash of a parameter object that depends on its values alone: the same values written
    /// in another key order, with other whitespace, or with a number spelt another way (<c>1</c>
    /// and <c>1.0</c>) hash the same. SHA-256 of the canonical text, lower-case hex.
    /// </summary>
    /// <remarks>
    /// Canonical text is JSON with object keys sorted by ordinal comparison at every depth, no
    /// whitespace, strings escaped by one writer, and numbers written as an integer when they are
    /// one and as a shortest round-trip double otherwise. Arrays keep their order: a list of
    /// values is a different request when reordered, where a set of named settings is not.
    /// </remarks>
    public static string ParamsHash(JsonElement parameters)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            WriteCanonical(writer, parameters);
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    /// <summary>The hash of parameters given as JSON text; a convenience over <see cref="ParamsHash(JsonElement)"/>.</summary>
    public static string ParamsHash(string parametersJson)
    {
        using var document = JsonDocument.Parse(parametersJson);
        return ParamsHash(document.RootElement);
    }

    /// <summary>
    /// The recipe as the upload's options document: an object with the recipe under
    /// <see cref="OptionsKey"/>, so it can sit in the same column the survey-archive run options
    /// use without either reading the other's members.
    /// </summary>
    public static string ToOptionsJson(ScanRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var envelope = new Dictionary<string, ScanRecipe>(StringComparer.Ordinal) { [OptionsKey] = recipe };
        return JsonSerializer.Serialize(envelope, Json);
    }

    /// <summary>
    /// The recipe an upload's options document carries, or null when it carries none or cannot be
    /// read — an upload of a survey archive has options of another shape and no recipe at all.
    /// </summary>
    public static ScanRecipe? FromOptionsJson(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(optionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(OptionsKey, out var recipeElement)
                || recipeElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var recipe = recipeElement.Deserialize<ScanRecipe>(Json);
            if (recipe?.Steps is null || recipe.Steps.Any(s =>
                    string.IsNullOrEmpty(s.StepId)
                    || string.IsNullOrEmpty(s.ParamsHash)
                    || s.Parameters.ValueKind != JsonValueKind.Object))
            {
                return null;
            }

            // The elements come out of a document that is disposed on return; cloned, they outlive it.
            // The step to run again from and the display choice come back with the steps: a
            // rerun's request is carried to the run by this document and nothing else.
            return recipe with { Steps = [.. recipe.Steps.Select(s => s with { Parameters = s.Parameters.Clone() })] };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    writer.WriteNumberValue(integer);
                }
                else
                {
                    // "1.0" does not parse as an integer; it is the same value as 1, and is written as 1.
                    var value = element.GetDouble();
                    if (double.IsInteger(value) && Math.Abs(value) < 9_007_199_254_740_992d)
                    {
                        writer.WriteNumberValue((long)value);
                    }
                    else
                    {
                        writer.WriteNumberValue(value);
                    }
                }

                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }
}
