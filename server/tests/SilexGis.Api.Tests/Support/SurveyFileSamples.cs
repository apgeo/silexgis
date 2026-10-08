// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text.Json;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Invented survey files, and the way back out of the mesh built from them.
///
/// <para>
/// The file is written by the one writer the demonstration data also uses, so a test's survey and
/// the demonstration's are made the same way; what is here beside it is the reading of a mesh,
/// which only a test needs.
/// </para>
/// </summary>
internal static class SurveyFileSamples
{
    /// <summary>
    /// A Survex image file holding exactly what it is given: legs, then stations, then runs of
    /// cross-sections, in plain metres about its own zero.
    /// </summary>
    public static byte[] Survex3d(
        IReadOnlyList<InventedSurveyFile.Station> stations,
        IReadOnlyList<InventedSurveyFile.Leg> legs,
        IReadOnlyList<InventedSurveyFile.CrossSection[]> passages) =>
        InventedSurveyFile.Survex3d(stations, legs, passages);

    /// <summary>
    /// Every vertex of a glTF binary mesh, turned back from that format's axes into the ones a
    /// survey is measured in: metres east, north and up of the mesh's own zero.
    /// </summary>
    public static List<(double East, double North, double Up)> GlbVertices(byte[] glb)
    {
        var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var binaryStart = 20 + jsonLength + 8;

        var position = json.RootElement.GetProperty("meshes")[0].GetProperty("primitives")[0]
            .GetProperty("attributes").GetProperty("POSITION").GetInt32();
        var accessor = json.RootElement.GetProperty("accessors")[position];
        var view = json.RootElement.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var at = binaryStart + view.GetProperty("byteOffset").GetInt32();
        var count = accessor.GetProperty("count").GetInt32();

        var vertices = new List<(double, double, double)>(count);
        for (var i = 0; i < count; i++)
        {
            var x = BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(at + (i * 12)));
            var y = BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(at + (i * 12) + 4));
            var z = BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(at + (i * 12) + 8));

            // glTF is Y-up with its third axis pointing south.
            vertices.Add((x, -z, y));
        }

        return vertices;
    }
}
