// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// A wall mesh file read back: what the writer wrote comes out again, node by node, in survey
/// axes — and anything that is not one of this application's files is refused.
/// </summary>
public class GlbReaderTests
{
    /// <summary>A tetrahedron: four corners no two of which share a coordinate, so a swapped axis shows.</summary>
    private static readonly IndexedMesh Walls = new(
        [0, 0, 0, 12.5, 1.25, -3, 4, 9.75, -7.5, 6, 2, 5.5],
        [0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0]);

    private static readonly IndexedMesh Estimated = new(
        [20, 0, -1, 21, 0, -1, 20, 1, -2],
        [0, 1, 2]);

    [Fact]
    public void A_mesh_comes_back_as_it_went_in_east_north_and_up_in_their_places()
    {
        using var file = new MemoryStream();
        GlbWriter.Write(Walls, (0, 0, 0), file);

        var parts = GlbReader.Read(file.ToArray());

        var part = parts.ShouldHaveSingleItem();
        part.Name.ShouldBe(GlbWriter.WallsNodeName);
        part.Mesh.Indices.ShouldBe(Walls.Indices);
        // Stored as 32-bit floats; these values are exact in them.
        part.Mesh.Positions.ShouldBe(Walls.Positions);
        part.Color.ShouldNotBeNull();
        part.Color.Value.R.ShouldBe(GlbWriter.WallColor.R, 1e-6);
    }

    [Fact]
    public void A_mesh_written_about_an_origin_comes_back_about_that_origin()
    {
        using var file = new MemoryStream();
        GlbWriter.Write(Walls, (2, 1, -3), file);

        var read = GlbReader.Read(file.ToArray()).ShouldHaveSingleItem().Mesh.Positions;

        for (var v = 0; v < Walls.VertexCount; v++)
        {
            read[v * 3].ShouldBe(Walls.Positions[v * 3] - 2);
            read[(v * 3) + 1].ShouldBe(Walls.Positions[(v * 3) + 1] - 1);
            read[(v * 3) + 2].ShouldBe(Walls.Positions[(v * 3) + 2] + 3);
        }
    }

    [Fact]
    public void Estimated_passages_come_back_as_a_part_of_their_own_with_their_colour()
    {
        using var file = new MemoryStream();
        GlbWriter.Write(Walls, Estimated, (0.9, 0.25, 0.05), (0, 0, 0), file);

        var parts = GlbReader.Read(file.ToArray());

        parts.Select(p => p.Name).ShouldBe([GlbWriter.WallsNodeName, GlbWriter.EstimatedNodeName]);
        parts[0].Mesh.Positions.ShouldBe(Walls.Positions);
        parts[1].Mesh.Positions.ShouldBe(Estimated.Positions);
        parts[1].Mesh.Indices.ShouldBe(Estimated.Indices);
        parts[1].Color!.Value.R.ShouldBe(0.9, 1e-6);
        parts[1].Color!.Value.G.ShouldBe(0.25, 1e-6);
        parts[1].Color!.Value.B.ShouldBe(0.05, 1e-6);
    }

    [Fact]
    public void A_file_of_estimated_passages_alone_has_no_walls_part()
    {
        using var file = new MemoryStream();
        GlbWriter.Write(null, Estimated, (0.9, 0.25, 0.05), (0, 0, 0), file);

        GlbReader.Read(file.ToArray()).ShouldHaveSingleItem().Name.ShouldBe(GlbWriter.EstimatedNodeName);
    }

    [Fact]
    public void Bytes_that_are_not_a_mesh_file_are_refused()
    {
        Should.Throw<InvalidDataException>(() => GlbReader.Read([]));
        Should.Throw<InvalidDataException>(() => GlbReader.Read(Encoding.ASCII.GetBytes("solid not a mesh at all, just some text")));

        using var file = new MemoryStream();
        GlbWriter.Write(Walls, (0, 0, 0), file);
        var whole = file.ToArray();

        // Cut short in its geometry, and cut short in its description.
        Should.Throw<InvalidDataException>(() => GlbReader.Read(whole.AsSpan(0, whole.Length - 20)));
        Should.Throw<InvalidDataException>(() => GlbReader.Read(whole.AsSpan(0, 40)));
    }

    [Fact]
    public void A_file_whose_triangles_name_corners_it_does_not_have_is_refused()
    {
        using var file = new MemoryStream();
        GlbWriter.Write(Walls, (0, 0, 0), file);
        var bytes = file.ToArray();

        // The first index of the geometry: the binary chunk begins after the header, the
        // description and the two chunk headers.
        var jsonLength = BitConverter.ToInt32(bytes, 12);
        var firstIndex = 12 + 8 + jsonLength + 8;
        BitConverter.TryWriteBytes(bytes.AsSpan(firstIndex), 999u);

        Should.Throw<InvalidDataException>(() => GlbReader.Read(bytes));
    }
}
