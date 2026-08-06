// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text.Json;
using Shouldly;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Surveys;

// One corner in survey axes — X east, Y north, Z up, in metres — and the three that make a face.
using Corner = (float X, float Y, float Z);
using Triangle = ((float X, float Y, float Z) A, (float X, float Y, float Z) B, (float X, float Y, float Z) C);

namespace SilexGis.Api.Tests;

/// <summary>
/// Reading survey meshes and writing them out for the 3D scene.
///
/// <para>
/// Every fixture here is built in code rather than checked in, so each test states the exact
/// geometry its expectation follows from — a checked-in export would make the numbers below
/// unexplainable without opening it in another tool.
/// </para>
/// </summary>
public class SurveyMeshTests
{
    [Fact]
    public void Weld_gives_one_vertex_per_distinct_corner()
    {
        // A closed cube: eight corners, shared by twelve triangles that name them 36 times.
        var stl = Stl(Cube(size: 10));

        var result = StlReader.Read(stl);

        result.TrianglesRead.ShouldBe(12);
        result.Mesh.VertexCount.ShouldBe(8);
        result.Mesh.Indices.Length.ShouldBe(36);
        result.DegenerateDropped.ShouldBe(0);
    }

    [Fact]
    public void Degenerate_triangles_are_dropped_and_counted()
    {
        // Two corners of the second triangle are the same point, so it has no surface. This is
        // ordinary in a survey export where a passage narrows to nothing, not a corrupt file.
        var stl = Stl([
            ((0, 0, 0), (1, 0, 0), (0, 1, 0)),
            ((2, 0, 0), (2, 0, 0), (3, 1, 0)),
        ]);

        var result = StlReader.Read(stl);

        result.TrianglesRead.ShouldBe(2);
        result.DegenerateDropped.ShouldBe(1);
        result.Mesh.TriangleCount.ShouldBe(1);
    }

    [Fact]
    public void Vertices_left_unused_by_dropped_triangles_are_not_written()
    {
        // (9,9,9) appears only in the degenerate triangle. Kept, it would be downloaded by every
        // viewer of this cave and drawn by nothing — and in a loch export, where more than half the
        // triangles can be degenerate, "it is only one vertex" stops being true.
        var stl = Stl([
            ((0, 0, 0), (1, 0, 0), (0, 1, 0)),
            ((9, 9, 9), (9, 9, 9), (8, 9, 9)),
        ]);

        var result = StlReader.Read(stl);

        result.Mesh.VertexCount.ShouldBe(3);
        result.Mesh.Positions.ShouldNotContain(9.0);
    }

    [Fact]
    public void A_file_whose_triangle_count_disagrees_with_its_length_is_refused()
    {
        var stl = Stl([((0, 0, 0), (1, 0, 0), (0, 1, 0))]);
        var bytes = stl.ToArray();
        // Claim two triangles while carrying one. Unchecked, this either reads past the end or
        // sizes an allocation from a number that is not a triangle count at all.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(80), 2);

        var thrown = Should.Throw<MeshIOException>(() => StlReader.Read(new MemoryStream(bytes)));

        thrown.Message.ShouldContain("not a binary STL");
    }

    [Fact]
    public void An_all_degenerate_file_is_refused_rather_than_written_empty()
    {
        var stl = Stl([((5, 5, 5), (5, 5, 5), (5, 5, 5))]);

        Should.Throw<MeshIOException>(() => StlReader.Read(stl))
            .Message.ShouldContain("no surface");
    }

    [Fact]
    public void Precision_lost_before_upload_is_detected_from_the_coordinates_themselves()
    {
        // A mesh exported at projected coordinates: a northing of five million metres, spanning the
        // few dozen metres a chamber actually measures. Nothing about the file says its precision
        // is gone; the magnitude of its own numbers does.
        var projected = Stl([
            ((360000, 5042100, 0), (360010, 5042100, 0), (360000, 5042110, 0)),
        ]);

        StlReader.Read(projected).CoarsestStepM.ShouldBeGreaterThanOrEqualTo(0.5);
    }

    [Fact]
    public void A_mesh_exported_about_its_own_origin_keeps_full_precision()
    {
        var local = Stl([((0, 0, 0), (10, 0, 0), (0, 10, 0))]);

        StlReader.Read(local).CoarsestStepM.ShouldBeLessThan(StlReader.PrecisionWarningStepM);
    }

    [Fact]
    public void Glb_is_a_valid_container_a_viewer_can_read()
    {
        var mesh = StlReader.Read(Stl(Cube(size: 10))).Mesh;
        using var output = new MemoryStream();

        GlbWriter.Write(mesh, (0, 0, 0), output);

        var bytes = output.ToArray();
        BinaryPrimitives.ReadUInt32LittleEndian(bytes).ShouldBe(0x46546C67u); // "glTF"
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)).ShouldBe(2u);
        // A viewer trusts the declared total; anything else and it reads a chunk that is not there.
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)).ShouldBe((uint)bytes.Length);

        var json = ReadJsonChunk(bytes);
        var primitive = json.RootElement
            .GetProperty("meshes")[0].GetProperty("primitives")[0];
        primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32().ShouldBe(1);
        primitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32().ShouldBe(2);

        var accessors = json.RootElement.GetProperty("accessors");
        accessors[0].GetProperty("count").GetInt32().ShouldBe(36); // indices
        accessors[1].GetProperty("count").GetInt32().ShouldBe(8);  // positions
        accessors[2].GetProperty("count").GetInt32().ShouldBe(8);  // normals

        // Looked at from inside a passage, every wall shows its back face.
        json.RootElement.GetProperty("materials")[0].GetProperty("doubleSided").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void The_declared_buffer_length_matches_the_bytes_that_follow_it()
    {
        var mesh = StlReader.Read(Stl(Cube(size: 10))).Mesh;
        using var output = new MemoryStream();

        GlbWriter.Write(mesh, (0, 0, 0), output);

        var bytes = output.ToArray();
        var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        var binaryStart = 12 + 8 + jsonLength;
        var binaryLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(binaryStart));

        (binaryStart + 8 + binaryLength).ShouldBe(bytes.Length);
        var json = ReadJsonChunk(bytes);
        json.RootElement.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32()
            .ShouldBe(binaryLength);
    }

    [Fact]
    public void The_mesh_is_moved_onto_the_origin_it_is_anchored_by()
    {
        // One triangle a kilometre east and two north of the point the model will be placed at.
        var mesh = StlReader.Read(Stl([((1000, 2000, 5), (1010, 2000, 5), (1000, 2010, 5))])).Mesh;
        using var output = new MemoryStream();

        GlbWriter.Write(mesh, (1000, 2000, 5), output);

        var json = ReadJsonChunk(output.ToArray());
        var min = json.RootElement.GetProperty("accessors")[1].GetProperty("min");
        var max = json.RootElement.GetProperty("accessors")[1].GetProperty("max");

        // Survey axes are X east, Y north, Z up; glTF's are X east, Y up, Z south. So a corner ten
        // metres north of the origin has to come out ten metres along negative Z, and nothing may
        // be left carrying the thousands the file was written with.
        min[0].GetDouble().ShouldBe(0, tolerance: 1e-6);
        max[0].GetDouble().ShouldBe(10, tolerance: 1e-6);
        min[1].GetDouble().ShouldBe(0, tolerance: 1e-6);
        min[2].GetDouble().ShouldBe(-10, tolerance: 1e-6);
        max[2].GetDouble().ShouldBe(0, tolerance: 1e-6);
    }

    // ---- placing the mesh in the world -------------------------------------

    [Fact]
    public void A_local_file_is_placed_at_the_position_given_for_its_zero_point()
    {
        var converter = new SurveyMeshConverter(new ProjCoordinateProjector());
        using var output = new MemoryStream();

        var result = converter.Convert(
            Stl([((0, 0, 0), (10, 0, 0), (0, 10, 0))]),
            new MeshSourceDeclaration(SourceEpsg: null, 25.4472, 45.5312, OriginHeightM: 1100),
            output);

        result.AnchorLongitude.ShouldBe(25.4472);
        result.AnchorLatitude.ShouldBe(45.5312);
        result.AnchorHeightM.ShouldBe(1100);
        // Local metres are already aligned to north; there is no grid to correct for.
        result.AppliedRotationDeg.ShouldBe(0);
    }

    [Fact]
    public void A_local_file_with_no_position_is_refused_rather_than_placed_somewhere()
    {
        var converter = new SurveyMeshConverter(new ProjCoordinateProjector());
        using var output = new MemoryStream();

        Should.Throw<MeshIOException>(() => converter.Convert(
                Stl([((0, 0, 0), (10, 0, 0), (0, 10, 0))]),
                new MeshSourceDeclaration(SourceEpsg: null, null, null, OriginHeightM: 1100),
                output))
            .Message.ShouldContain("zero point");
    }

    [Fact]
    public void A_projected_file_is_placed_from_its_own_coordinates()
    {
        var converter = new SurveyMeshConverter(new ProjCoordinateProjector());
        using var output = new MemoryStream();

        // Eastings and northings from a real Therion export, in UTM zone 35N.
        var result = converter.Convert(
            Stl([
                ((359994, 5042094, 0), (360292, 5042094, 0), (359994, 5042171, 0)),
                ((360292, 5042094, 0), (360292, 5042171, 0), (359994, 5042171, 0)),
            ]),
            new MeshSourceDeclaration(SourceEpsg: 32635, null, null, OriginHeightM: 1100),
            output);

        // Piatra Craiului. Asserted to four decimals — about ten metres — because the point of the
        // test is that the pair is not silently swapped and the zone is honoured, and a swapped
        // pair lands in the Indian Ocean rather than a few metres off.
        result.AnchorLongitude.ShouldBe(25.2093, tolerance: 0.001);
        result.AnchorLatitude.ShouldBe(45.5187, tolerance: 0.001);

        // This far east of the zone's central meridian the grid is turned by more than a degree,
        // and the mesh is turned back by the same amount so its ends land where they belong.
        result.AppliedRotationDeg.ShouldBe(1.274, tolerance: 0.02);
    }

    [Fact]
    public void A_coordinate_system_this_installation_cannot_resolve_is_refused()
    {
        var converter = new SurveyMeshConverter(new ProjCoordinateProjector());
        using var output = new MemoryStream();

        Should.Throw<MeshIOException>(() => converter.Convert(
            Stl([((359994, 5042094, 0), (360292, 5042094, 0), (359994, 5042171, 0))]),
            new MeshSourceDeclaration(SourceEpsg: 999999, null, null, OriginHeightM: 1100),
            output));
    }

    [Fact]
    public void Projected_coordinates_come_back_as_longitude_and_latitude_in_that_order()
    {
        // Guards the one mistake in this file that produces no error and no visible defect until
        // someone looks at a map: the register defines WGS 84 as latitude first, and a transform
        // left on that default returns the pair the other way round. Zone 35N covers 24°–30°E, so
        // a longitude that came back below 30 and a latitude above 40 can only be the right way up.
        var point = new ProjCoordinateProjector().ToWgs84(32635, 360143, 5042133);

        point.ShouldNotBeNull();
        point.Value.Longitude.ShouldBeInRange(24, 30);
        point.Value.Latitude.ShouldBeInRange(40, 50);
    }

    // ---- fixtures ----------------------------------------------------------

    private static Triangle[] Cube(float size)
    {
        // Eight corners, each shared by three of the six faces.
        Corner[] c =
        [
            (0, 0, 0), (size, 0, 0), (size, size, 0), (0, size, 0),
            (0, 0, size), (size, 0, size), (size, size, size), (0, size, size),
        ];

        (int A, int B, int C)[] faces =
        [
            (0, 2, 1), (0, 3, 2), // bottom
            (4, 5, 6), (4, 6, 7), // top
            (0, 1, 5), (0, 5, 4), // south
            (2, 3, 7), (2, 7, 6), // north
            (1, 2, 6), (1, 6, 5), // east
            (3, 0, 4), (3, 4, 7), // west
        ];

        return [.. faces.Select(f => (c[f.A], c[f.B], c[f.C]))];
    }

    private static MemoryStream Stl(Triangle[] triangles)
    {
        var bytes = new byte[84 + (triangles.Length * 50)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(80), triangles.Length);

        for (var t = 0; t < triangles.Length; t++)
        {
            var at = 84 + (t * 50);
            // Bytes 0..11 are the exporter's face normal, left zero on purpose: exporters disagree
            // about it and the reader recomputes from the geometry, so a wrong one must not matter.
            var (a, b, c) = triangles[t];
            WriteCorner(bytes.AsSpan(at + 12), a);
            WriteCorner(bytes.AsSpan(at + 24), b);
            WriteCorner(bytes.AsSpan(at + 36), c);
        }

        return new MemoryStream(bytes);
    }

    private static void WriteCorner(Span<byte> target, Corner corner)
    {
        BinaryPrimitives.WriteSingleLittleEndian(target, corner.X);
        BinaryPrimitives.WriteSingleLittleEndian(target[4..], corner.Y);
        BinaryPrimitives.WriteSingleLittleEndian(target[8..], corner.Z);
    }

    private static JsonDocument ReadJsonChunk(byte[] glb)
    {
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        return JsonDocument.Parse(glb.AsMemory(20, length));
    }

}
