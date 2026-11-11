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
/// The odometry profiles were measured on two published recordings with a terrestrial reference
/// scan each: a 227 m walk through an underground car park and a 78 m walk down a long corridor,
/// the case lidar odometry finds hardest because one stretch of corridor looks like the next. A
/// run of HDMapping on the same input and values gave the same result each time, so a difference
/// between two sets of values is the values' own.
/// </para>
/// <para>
/// "Fast preview" coarsens point decimation (5 cm against 1 cm) and the indoor grid (20 cm against
/// 10 cm), and runs 300 optimiser iterations over a 30 m lidar range (against 1000 and 70 m). It
/// took 13–14 % less time than the defaults, and its trajectory was off by 7.4 cm (car park) and
/// 20 cm (corridor) against the defaults' 7.2 cm and 18 cm. Only together do these values hold:
/// the grid and decimation alone, or the iterations and range alone, lost the corridor by 3–5 m,
/// and a coarser set that was twice as fast lost it by 2.3 m.
/// </para>
/// <para>
/// "Cave, narrow passages" keeps the points from 15 cm of the scanner on, where the defaults drop
/// everything nearer than 30 cm: in a squeeze the walls are that close, and they are what the
/// odometry has to hold on to. The corridor's trajectory error fell from 18 cm to 14 cm, the car
/// park's stayed at 7.2 cm, in the same time. A finer grid (5 or 7 cm) and HDMapping's "robust and
/// accurate" mode were tried for it too and lost the track on both recordings. It has not yet been
/// measured on a cave recording, for want of a published one with a reference.
/// </para>
/// <para>
/// The surface step's profiles hold its parameters as a plain object, like every step whose
/// schema is the registry's own; only the odometry's are wrapped under <c>values</c>, because
/// theirs are the program's sections. "Surface defaults" names nothing and means the step's
/// schema defaults (measured; the reasons are with the step's parameters); "Quick surface" drops the
/// octree one level, which on a cave passage took a third of the time and kept its surface within
/// 3 cm of the scan for 95 % of the points.
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
            "A quicker, rougher result to look at a recording before processing it properly: coarser decimation and grid, fewer iterations and a 30 m lidar range. Measured on two published recordings: about 14 % faster than the defaults, with a trajectory within 2 cm of theirs.",
            """{"values":{"filter_points":{"decimation":0.05},"in_out_params_indoor":{"resolution_X":0.2,"resolution_Y":0.2,"resolution_Z":0.2},"lidar_odometry_control":{"nr_iter":300,"max_distance_lidar":30.0}}}"""),
        new(
            CaveNarrowPassagesId,
            HdmappingOdometryStep.StepId,
            "Cave, narrow passages",
            "For tight cave passages: keeps the points from 15 cm of the scanner on, where the defaults drop those nearer than 30 cm. On a long corridor it held the track better than the defaults (14 cm against 18 cm off) in the same time; not yet measured on a cave recording.",
            """{"values":{"filter_points":{"filter_threshold_xy_inner":0.15}}}"""),
        new(
            SurfaceDefaultsId,
            MeshSurfaceStep.StepId,
            "Surface defaults",
            "The surface step's own values: 2 cm voxels, a Poisson depth of 10, 5 % of the least-supported faces trimmed, decimated to half a million triangles. Measured on three published scans, two of them in caves.",
            "{}"),
        new(
            SurfaceQuickLookId,
            MeshSurfaceStep.StepId,
            "Quick surface",
            "A rougher surface made faster, to see whether a scan is worth a full one: a Poisson depth of 9 instead of 10. On a cave passage it took a third of the time, with the surface within 3 cm of the scan for 95 % of its points.",
            """{"poissonDepth":9}"""),
    ];

    public static bool IsBuiltIn(Guid id) => All.Any(p => p.Id == id);
}
