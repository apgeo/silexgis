// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Scans;

/// <summary>A profile the installation ships with: offered to every account and changed by nobody.</summary>
/// <param name="Id">Fixed for ever, so the same built-in is the same row on every installation and a remembered choice of it survives an upgrade.</param>
/// <param name="ValuesJson">The step's parameters as a profile holds them; only the keys that differ from the program's defaults.</param>
public sealed record BuiltInProcessingProfile(Guid Id, string StepId, string Name, string Description, string ValuesJson);

/// <summary>
/// The built-in processing profiles. The single home of their values: the start-up seeder writes
/// them, and rewrites a stored built-in whenever its definition here changes, since nobody can
/// have edited one.
/// </summary>
/// <remarks>
/// <para>
/// "HDMapping defaults" names no value at all. A profile only lays its keys over the defaults of
/// the program that is installed, so the empty one means exactly "what this version of HDMapping
/// does unasked" and stays right across upgrades.
/// </para>
/// <para>
/// "Fast preview" trades accuracy for time on the levers that cost most: coarser point decimation
/// (5 cm against 1 cm), a coarser indoor grid (20 cm against 10 cm), fewer optimiser iterations
/// (300 against 1000) and a 30 m lidar range against 70 m — cave passages rarely need more. These
/// are first values, not measured ones.
/// </para>
/// <para>
/// "Cave, narrow passages" ships with the defaults' values. Its own values are still to be tuned
/// on a cave recording, and its description says so, so nobody mistakes it for a tuned set.
/// </para>
/// <para>
/// The surface step's profiles hold its parameters as a plain object, like every step whose
/// schema is the registry's own; only the odometry's are wrapped under <c>values</c>, because
/// theirs are the program's sections. "Surface defaults" names nothing and means the step's
/// schema defaults; "Quick surface" coarsens the voxel, the normal radius with it, the octree
/// depth and the triangle count.
/// </para>
/// </remarks>
public static class BuiltInProcessingProfiles
{
    public static readonly Guid HdmappingDefaultsId = new("019a6000-0000-7000-8000-000000000001");

    public static readonly Guid FastPreviewId = new("019a6000-0000-7000-8000-000000000002");

    public static readonly Guid CaveNarrowPassagesId = new("019a6000-0000-7000-8000-000000000003");

    public static readonly Guid SurfaceDefaultsId = new("019a6000-0000-7000-8000-000000000004");

    public static readonly Guid SurfaceQuickLookId = new("019a6000-0000-7000-8000-000000000005");

    public static IReadOnlyList<BuiltInProcessingProfile> All { get; } =
    [
        new(
            HdmappingDefaultsId,
            HdmappingOdometryStep.StepId,
            "HDMapping defaults",
            "Exactly what the installed version of HDMapping does when nothing is changed.",
            """{"values":{}}"""),
        new(
            FastPreviewId,
            HdmappingOdometryStep.StepId,
            "Fast preview",
            "A quicker, rougher result to look at a recording before processing it properly: coarser decimation and grid, fewer iterations and a 30 m lidar range. First values, not yet measured against a reference.",
            """{"values":{"filter_points":{"decimation":0.05},"in_out_params_indoor":{"resolution_X":0.2,"resolution_Y":0.2,"resolution_Z":0.2},"lidar_odometry_control":{"nr_iter":300,"max_distance_lidar":30.0}}}"""),
        new(
            CaveNarrowPassagesId,
            HdmappingOdometryStep.StepId,
            "Cave, narrow passages",
            "Meant for tight cave passages. Its values are still to be tuned on a cave recording; until then it runs with HDMapping's defaults.",
            """{"values":{}}"""),
        new(
            SurfaceDefaultsId,
            MeshSurfaceStep.StepId,
            "Surface defaults",
            "The surface step's own values: 2 cm voxels, a Poisson depth of 9, 2 % of the least-supported faces trimmed, decimated to half a million triangles. First values, still to be tuned on cave scans.",
            "{}"),
        new(
            SurfaceQuickLookId,
            MeshSurfaceStep.StepId,
            "Quick surface",
            "A rougher surface made faster, to see whether a scan is worth a full one: 5 cm voxels, a Poisson depth of 8 and at most 200,000 triangles. First values, not yet measured against a reference.",
            """{"voxelCm":5,"normalRadiusCm":15,"poissonDepth":8,"targetTriangles":200000}"""),
    ];

    public static bool IsBuiltIn(Guid id) => All.Any(p => p.Id == id);
}
