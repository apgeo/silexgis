// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Text.Json;

namespace SilexGis.Domain.Scans;

/// <summary>Where a step's work is done.</summary>
public enum ScanStepLocation
{
    /// <summary>In the API process itself: cheap, dependency-free work such as reading a header.</summary>
    Api = 0,

    /// <summary>In the isolated worker container, which announces the steps it can run; absent worker, absent step.</summary>
    Worker = 1,

    /// <summary>On another machine by hand: the run stops at the step, offers its inputs, and waits for its outputs to be uploaded.</summary>
    External = 2,
}

/// <summary>
/// One unit of a scan's processing: what it takes, what it makes, where it runs, and what its
/// parameters may be. A step is a definition, not a run — a run is a processing job plus the
/// artefact rows it writes.
/// </summary>
/// <remarks>
/// Defined in code and listed in <see cref="ScanSteps"/>, never discovered or loaded: a new tool
/// is one class here and one capability in the worker, and nothing else learns about it. The
/// parameter schema is JSON Schema text because the installation's validator already speaks it
/// and because a form can be generated from it; it is a string in this layer so that the domain
/// depends on no schema library.
/// </remarks>
public interface IScanStep
{
    /// <summary>The step's id, <c>tool.verb</c> in lower case, such as <c>pointcloud.inspect</c>; the key a recipe and an artefact name it by.</summary>
    string Id { get; }

    /// <summary>What the step is called where a person sees it.</summary>
    string DisplayName { get; }

    /// <summary>The tool the step runs, in the spelling a worker announces it under; <c>silexgis</c> for work the API does itself.</summary>
    string Tool { get; }

    /// <summary>The kinds of artefact the step reads.</summary>
    IReadOnlyList<ScanArtifactKind> Inputs { get; }

    /// <summary>The kinds of artefact the step writes.</summary>
    IReadOnlyList<ScanArtifactKind> Outputs { get; }

    ScanStepLocation WhereItRuns { get; }

    /// <summary>
    /// A JSON Schema (draft 2020-12) object schema for the step's parameters. Every property has
    /// a default, and additional properties are refused, so a recipe is checked against it before
    /// it is written and a parameter nobody defined cannot ride along into the hash.
    /// </summary>
    string ParameterSchemaJson { get; }
}

/// <summary>Reads a point cloud's public header and records in the API process, and writes what they say on the model.</summary>
public sealed class PointCloudInspectStep : IScanStep
{
    public const string StepId = "pointcloud.inspect";

    public string Id => StepId;

    public string DisplayName => "Read the point cloud's header";

    public string Tool => ScanSteps.ApiTool;

    public IReadOnlyList<ScanArtifactKind> Inputs => [ScanArtifactKind.PointCloudLaz];

    public IReadOnlyList<ScanArtifactKind> Outputs => [];

    public ScanStepLocation WhereItRuns => ScanStepLocation.Api;

    public string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;
}

/// <summary>
/// One pass over the cloud in the worker: how many points would remain at each of a few voxel
/// sizes, with the tile size and tiling time each would take, so a person can choose how much of
/// a large cloud to show before anything is tiled.
/// </summary>
public sealed class PointCloudEstimateStep : IScanStep
{
    public const string StepId = "pointcloud.estimate";

    public string Id => StepId;

    public string DisplayName => "Estimate what a thinned copy would keep";

    public string Tool => "laspy";

    public IReadOnlyList<ScanArtifactKind> Inputs => [ScanArtifactKind.PointCloudLaz];

    public IReadOnlyList<ScanArtifactKind> Outputs => [ScanArtifactKind.Report];

    public ScanStepLocation WhereItRuns => ScanStepLocation.Worker;

    public string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "voxelsCm": {
              "description": "The voxel sizes, in centimetres, the remaining points are counted at.",
              "type": "array",
              "items": { "type": "number", "exclusiveMinimum": 0, "maximum": 1000 },
              "minItems": 1,
              "maxItems": 10,
              "default": [1, 2, 3, 5, 10]
            }
          },
          "additionalProperties": false
        }
        """;
}

/// <summary>
/// A thinned copy of the cloud for display, made in the worker: one point per voxel of N cm (the
/// point nearest the cell's centre), or every n-th point. The original is never altered; the copy
/// exists only to be tiled.
/// </summary>
public sealed class PointCloudDownsampleStep : IScanStep
{
    public const string StepId = "pointcloud.downsample";

    public string Id => StepId;

    public string DisplayName => "Thin the point cloud for display";

    public string Tool => "laspy";

    public IReadOnlyList<ScanArtifactKind> Inputs => [ScanArtifactKind.PointCloudLaz];

    public IReadOnlyList<ScanArtifactKind> Outputs => [ScanArtifactKind.PointCloudLazDisplay];

    public ScanStepLocation WhereItRuns => ScanStepLocation.Worker;

    public string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "method": {
              "description": "voxel keeps one point per cell of voxelCm; everyNth keeps every n-th point.",
              "type": "string",
              "enum": ["voxel", "everyNth"],
              "default": "voxel"
            },
            "voxelCm": {
              "description": "The cell size in centimetres, when the method is voxel.",
              "type": "number",
              "exclusiveMinimum": 0,
              "maximum": 1000,
              "default": 2
            },
            "everyNth": {
              "description": "Keep every n-th point, when the method is everyNth.",
              "type": "integer",
              "minimum": 1,
              "maximum": 10000,
              "default": 2
            }
          },
          "additionalProperties": false
        }
        """;
}

/// <summary>Converts a point cloud into 3D Tiles with py3dtiles, in the worker, in the cloud's own frame.</summary>
/// <remarks>
/// Only the schema is fixed here; the handler that writes the worker's request and collects its
/// answer arrives with the worker itself. The parameters are the few py3dtiles
/// decisions a person could want to change; the tiler always writes tiles in the cloud's local
/// frame, which is not a parameter because tiles must never carry coordinates.
/// </remarks>
public sealed class Py3dtilesTileStep : IScanStep
{
    public const string StepId = "py3dtiles.tile";

    public string Id => StepId;

    public string DisplayName => "Tile the point cloud for viewing";

    public string Tool => "py3dtiles";

    public IReadOnlyList<ScanArtifactKind> Inputs => [ScanArtifactKind.PointCloudLaz, ScanArtifactKind.PointCloudLazDisplay];

    public IReadOnlyList<ScanArtifactKind> Outputs => [ScanArtifactKind.PointCloudTiles];

    public ScanStepLocation WhereItRuns => ScanStepLocation.Worker;

    public string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "specVersion": {
              "description": "The 3D Tiles specification version the tileset is written to.",
              "type": "string",
              "enum": ["1.0"],
              "default": "1.0"
            },
            "rgb": {
              "description": "Keep each point's colour when the cloud carries one.",
              "type": "boolean",
              "default": true
            },
            "intensity": {
              "description": "Keep each point's return intensity as an extra attribute.",
              "type": "boolean",
              "default": true
            }
          },
          "additionalProperties": false
        }
        """;
}

/// <summary>The steps this server knows, by id. A list in code, never a scan of assemblies.</summary>
public static class ScanSteps
{
    /// <summary>The tool name of work the API process does itself.</summary>
    public const string ApiTool = "silexgis";

    /// <summary>
    /// The step id an uploaded original is attributed to. Not a step in the registry — nothing
    /// ran — but every artefact names the step that made it, and the one a person sent is made by
    /// the upload.
    /// </summary>
    public const string UploadStepId = "pointcloud.upload";

    public static IReadOnlyList<IScanStep> All { get; } =
    [
        new PointCloudInspectStep(),
        new PointCloudEstimateStep(),
        new PointCloudDownsampleStep(),
        new Py3dtilesTileStep(),
    ];

    private static readonly Dictionary<string, IScanStep> ById =
        All.ToDictionary(step => step.Id, StringComparer.Ordinal);

    /// <summary>The step with this id, or null for an id no step has: a recipe naming one is refused, not guessed at.</summary>
    public static IScanStep? Find(string id) =>
        id is not null && ById.TryGetValue(id, out var step) ? step : null;

    /// <summary>
    /// The parameter values a step runs with when nobody chose otherwise: every property of its
    /// schema at the schema's own default, as one JSON object. A recipe snapshots these values,
    /// never the fact that the defaults were taken, so a run made before a default changed still
    /// says what it ran with and is repeated exactly, and an artefact made with the old value is
    /// not mistaken for one made with the new.
    /// </summary>
    public static JsonElement DefaultParameters(IScanStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        using var schema = JsonDocument.Parse(step.ParameterSchemaJson);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (schema.RootElement.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in properties.EnumerateObject())
                {
                    if (property.Value.TryGetProperty("default", out var fallback))
                    {
                        writer.WritePropertyName(property.Name);
                        fallback.WriteTo(writer);
                    }
                }
            }

            writer.WriteEndObject();
        }

        using var resolved = JsonDocument.Parse(buffer.WrittenMemory);
        return resolved.RootElement.Clone();
    }
}
