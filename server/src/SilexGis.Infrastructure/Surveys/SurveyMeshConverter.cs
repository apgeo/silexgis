// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Surveys;

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
    /// <exception cref="SurveySourceException">The file cannot be read, or the declaration cannot be used.</exception>
    public MeshConversionResult Convert(Stream input, SurveySourceDeclaration declaration, Stream output)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var read = StlReader.Read(input);
        var mesh = read.Mesh;

        var placement = SurveyPlacement.Resolve(projector, declaration, () => FootprintCentre(mesh.Positions));

        // The vertices are turned here rather than the angle being declared alongside them, so that
        // nothing downstream has to know to apply it — and so that nothing downstream can apply it
        // a second time.
        if (placement.AppliedRotationDeg != 0)
        {
            Turn(mesh.Positions, placement);
        }

        GlbWriter.Write(mesh, placement.Origin, output);

        return new MeshConversionResult(
            placement.Anchor.Longitude,
            placement.Anchor.Latitude,
            placement.OriginHeightM,
            placement.AppliedRotationDeg,
            read.TrianglesRead,
            read.DegenerateDropped,
            mesh.VertexCount,
            read.CoarsestStepM >= StlReader.PrecisionWarningStepM);
    }

    /// <summary>Turns the mesh about its origin, in the horizontal plane.</summary>
    private static void Turn(double[] positions, SurveyPlacement placement)
    {
        for (var i = 0; i < positions.Length; i += 3)
        {
            (positions[i], positions[i + 1]) = placement.Turn(positions[i], positions[i + 1]);
        }
    }

    /// <summary>
    /// The middle of the mesh's own footprint: the point closest to every part of it, and so the
    /// one that keeps the most precision when the rest is written as 32-bit offsets from it.
    /// </summary>
    private static (double X, double Y) FootprintCentre(double[] positions)
    {
        var (min, max) = Bounds(positions);
        return ((min[0] + max[0]) / 2, (min[1] + max[1]) / 2);
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
