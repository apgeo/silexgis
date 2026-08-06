// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// What the person uploading a mesh had to tell us, because the file could not.
///
/// <para>
/// An STL carries eighty bytes of free text, a triangle count and triangles. Nothing in it says
/// what its numbers mean — and measurement across real exports from the same toolchain found some
/// written in metres about a fixed station and others in a projected national grid, with no way to
/// tell from the artifact which. Two of them were in *different* grids. So the convention is
/// declared, never guessed: a wrong guess puts a cave hundreds of kilometres from where it is, and
/// puts it there silently.
/// </para>
/// </summary>
/// <param name="SourceEpsg">
/// The projected system the file's X and Y are in, or null when they are plain metres about a point
/// the uploader names.
/// </param>
/// <param name="OriginLongitude">Where the file's own zero point is, for a local file.</param>
/// <param name="OriginLatitude">Where the file's own zero point is, for a local file.</param>
/// <param name="OriginHeightM">
/// The altitude the file's Z = 0 plane sits at.
///
/// <para>
/// Asked for rather than read, because Z in these files is not an altitude: across every real
/// sample measured, the highest Z anywhere was 38 m, in a country whose caves are between 200 and
/// 2000. The exports are written about a station fixed at zero height, so the plane they are
/// measured from is knowledge that only the surveyor has.
/// </para>
/// </param>
public sealed record MeshSourceDeclaration(
    int? SourceEpsg,
    double? OriginLongitude,
    double? OriginLatitude,
    double OriginHeightM);

/// <summary>Where a converted mesh belongs and what converting it found.</summary>
public sealed record MeshConversionResult(
    double AnchorLongitude,
    double AnchorLatitude,
    double AnchorHeightM,
    double AppliedRotationDeg,
    int TrianglesRead,
    int DegenerateDropped,
    int VertexCount,
    bool SourcePrecisionLost);

/// <summary>
/// Turns an uploaded survey mesh into one file the 3D scene can draw, positioned in the world.
/// </summary>
public sealed class SurveyMeshConverter(ICoordinateProjector projector)
{
    /// <summary>
    /// Converts <paramref name="input"/> into <paramref name="output"/> and says where it goes.
    /// </summary>
    /// <exception cref="MeshIOException">The file cannot be read, or the declaration cannot be used.</exception>
    public MeshConversionResult Convert(Stream input, MeshSourceDeclaration declaration, Stream output)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var read = StlReader.Read(input);
        var mesh = read.Mesh;

        var (origin, anchor) = Anchor(mesh, declaration);

        // Grid north is not true north anywhere but on the projection's central meridian, and the
        // difference grows with distance from the anchor — right in the middle, wrong at the ends.
        // Turning the vertices here rather than declaring the angle alongside them means there is
        // no second place it can be applied, forgotten, or applied twice.
        if (anchor.ConvergenceDeg != 0)
        {
            Rotate(mesh.Positions, origin, -anchor.ConvergenceDeg);
        }

        GlbWriter.Write(mesh, origin, output);

        return new MeshConversionResult(
            anchor.Longitude,
            anchor.Latitude,
            declaration.OriginHeightM,
            -anchor.ConvergenceDeg,
            read.TrianglesRead,
            read.DegenerateDropped,
            mesh.VertexCount,
            read.CoarsestStepM >= StlReader.PrecisionWarningStepM);
    }

    /// <summary>
    /// The point in file coordinates the mesh is written about, and where in the world that is.
    /// </summary>
    private (
        (double X, double Y, double Z) Origin,
        ProjectedPoint Anchor) Anchor(IndexedMesh mesh, MeshSourceDeclaration declaration)
    {
        if (declaration.SourceEpsg is not { } epsg)
        {
            // Local metres. The file's own zero is the fixed station the survey was tied to, so it
            // is the point the uploader gave a position for — not the middle of the mesh, which is
            // wherever the passages happen to average out to.
            if (declaration.OriginLongitude is not { } lon || declaration.OriginLatitude is not { } lat)
            {
                throw new MeshIOException(
                    "This file's coordinates are local, so it needs the position its zero point sits at.");
            }

            return ((0, 0, 0), new ProjectedPoint(lon, lat, 0));
        }

        // Projected. The file already knows where it is; what it needs is a point close enough to
        // the geometry that writing the rest as 32-bit offsets from it keeps full precision. The
        // centre of its own footprint is the closest such point to every part of it.
        var (min, max) = Bounds(mesh.Positions);
        var centre = ((min[0] + max[0]) / 2, (min[1] + max[1]) / 2, 0.0);

        var anchor = projector.ToWgs84(epsg, centre.Item1, centre.Item2)
            ?? throw new MeshIOException(
                $"EPSG:{epsg} is not a coordinate system this installation can resolve, so the "
                + "file's coordinates cannot be placed.");

        if (Math.Abs(anchor.Latitude) > 90 || Math.Abs(anchor.Longitude) > 180)
        {
            throw new MeshIOException(
                $"Read as EPSG:{epsg}, this file's coordinates fall outside the world. Check the "
                + "coordinate system the survey was exported in.");
        }

        return (centre, anchor);
    }

    /// <summary>Turns the mesh about its origin, in the horizontal plane.</summary>
    private static void Rotate(double[] positions, (double X, double Y, double Z) origin, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        for (var i = 0; i < positions.Length; i += 3)
        {
            var x = positions[i] - origin.X;
            var y = positions[i + 1] - origin.Y;
            positions[i] = origin.X + ((x * cos) - (y * sin));
            positions[i + 1] = origin.Y + ((x * sin) + (y * cos));
        }
    }

    private static (double[] Min, double[] Max) Bounds(double[] positions)
    {
        var min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var max = new[] { double.MinValue, double.MinValue, double.MinValue };
        for (var i = 0; i < positions.Length; i++)
        {
            var axis = i % 3;
            min[axis] = Math.Min(min[axis], positions[i]);
            max[axis] = Math.Max(max[axis], positions[i]);
        }

        return (min, max);
    }
}
