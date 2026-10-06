// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Invented survey files, and the way back out of the mesh built from them.
///
/// <para>
/// One of the two compiled line-plot formats has a writer in the library that reads it; the other
/// has none, so the few items a test needs are written here by hand. Every number in a fixture is
/// then stated by the test that uses it, and an expectation about a ring's width can be read off
/// the test instead of off a file opened in another tool.
/// </para>
/// </summary>
internal static class SurveyFileSamples
{
    /// <summary>A station of an invented survey: its label and where it is, in metres.</summary>
    internal readonly record struct Station(string Name, double X, double Y, double Z);

    /// <summary>A leg of an invented survey, between two positions; <c>Splay</c> marks a shot at the wall.</summary>
    internal readonly record struct Leg(
        (double X, double Y, double Z) From, (double X, double Y, double Z) To, bool Splay = false);

    /// <summary>
    /// One cross-section of a passage, in metres, with a negative number where nothing was measured.
    /// </summary>
    internal readonly record struct CrossSection(string Station, double Left, double Right, double Up, double Down);

    /// <summary>
    /// A Survex image file of the current format version holding exactly what it is given: legs,
    /// then stations, then runs of cross-sections.
    ///
    /// <para>
    /// The legs are written first, while the label the format carries from item to item is still
    /// empty, so that no leg picks up a station's name as the survey it belongs to. The file names
    /// no coordinate system, which makes it one in plain metres about its own zero.
    /// </para>
    /// </summary>
    public static byte[] Survex3d(
        IReadOnlyList<Station> stations, IReadOnlyList<Leg> legs, IReadOnlyList<CrossSection[]> passages)
    {
        using var file = new MemoryStream();
        file.Write("Survex 3D Image File\nv8\nInvented survey\n@0\n"u8);
        file.WriteByte(0x00); // file-wide flags: a plan-view model
        file.WriteByte(0x00); // the first style item, which sets the ordinary style

        foreach (var leg in legs)
        {
            file.WriteByte(0x0f); // move
            Point(file, leg.From);

            // A leg that leaves the label alone (0x20), carrying its flags in the low bits.
            file.WriteByte((byte)(0x40 | 0x20 | (leg.Splay ? 0x04 : 0x00)));
            Point(file, leg.To);
        }

        var label = 0;
        foreach (var station in stations)
        {
            file.WriteByte(0x80 | 0x02); // a station, underground
            label = Relabel(file, label, station.Name);
            Point(file, (station.X, station.Y, station.Z));
        }

        foreach (var passage in passages)
        {
            for (var i = 0; i < passage.Length; i++)
            {
                // A cross-section in 16-bit centimetres; the low bit ends the passage.
                file.WriteByte((byte)(0x30 | (i == passage.Length - 1 ? 0x01 : 0x00)));
                label = Relabel(file, label, passage[i].Station);
                Centimetres16(file, passage[i].Left);
                Centimetres16(file, passage[i].Right);
                Centimetres16(file, passage[i].Up);
                Centimetres16(file, passage[i].Down);
            }
        }

        file.WriteByte(0x00); // stop
        return file.ToArray();
    }

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

    /// <summary>
    /// Replaces the whole carried label with <paramref name="name"/>, in the long form of the
    /// format's label patch: a zero byte, how many bytes to drop, how many to add, and the bytes.
    /// </summary>
    private static int Relabel(MemoryStream file, int carried, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        file.WriteByte(0x00);
        file.WriteByte((byte)carried);
        file.WriteByte((byte)bytes.Length);
        file.Write(bytes);
        return bytes.Length;
    }

    private static void Point(MemoryStream file, (double X, double Y, double Z) point)
    {
        Span<byte> bytes = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)Math.Round(point.X * 100));
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], (int)Math.Round(point.Y * 100));
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], (int)Math.Round(point.Z * 100));
        file.Write(bytes);
    }

    private static void Centimetres16(MemoryStream file, double metres)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, metres < 0 ? (short)-1 : (short)Math.Round(metres * 100));
        file.Write(bytes);
    }
}
