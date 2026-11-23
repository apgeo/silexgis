// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text.Json;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>One node of a wall mesh file: what it is called, its triangles and its colour.</summary>
/// <param name="Name">The node's name — <see cref="GlbWriter.WallsNodeName"/> or <see cref="GlbWriter.EstimatedNodeName"/>.</param>
/// <param name="Mesh">The triangles, in metres east, north and up about the file's zero point.</param>
/// <param name="Color">The colour the node's material states, linear RGB; null when it states none.</param>
public sealed record GlbPart(string Name, IndexedMesh Mesh, (double R, double G, double B)? Color);

/// <summary>
/// Reads back the wall mesh files <see cref="GlbWriter"/> writes.
///
/// <para>
/// A cave's walls are kept as the one file a scene draws, and that file is the walls: the mesh an
/// upload was converted to, or the one built from a survey's measured passage, already turned onto
/// true north and written about its anchor. Whatever else needs those triangles — another program's
/// scene, for one — reads them from here rather than building them again from the survey, so it
/// holds the same walls the viewer was shown, for both kinds of file.
/// </para>
///
/// <para>
/// It reads what this application wrote and is strict about it: one buffer inside the file,
/// 32-bit indices, 32-bit float positions, one primitive a mesh. Anything else is refused as not
/// one of ours rather than guessed at. Positions come back in survey axes — the writer turned them
/// into the file format's own, and this turns them back.
/// </para>
/// </summary>
public static class GlbReader
{
    private const uint Magic = 0x46546C67; // "glTF"
    private const uint JsonChunk = 0x4E4F534A; // "JSON"
    private const uint BinaryChunk = 0x004E4942; // "BIN\0"
    private const int HeaderBytes = 12;
    private const int ChunkHeaderBytes = 8;
    private const int UnsignedInt = 5125;
    private const int Float = 5126;

    /// <summary>The nodes of a wall mesh file that carry triangles, in the file's order.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a mesh file this application wrote.</exception>
    public static IReadOnlyList<GlbPart> Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderBytes + ChunkHeaderBytes
            || BinaryPrimitives.ReadUInt32LittleEndian(file) != Magic)
        {
            throw new InvalidDataException("Not a binary glTF file.");
        }

        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file[HeaderBytes..]));
        if (BinaryPrimitives.ReadUInt32LittleEndian(file[(HeaderBytes + 4)..]) != JsonChunk
            || jsonLength > file.Length - HeaderBytes - ChunkHeaderBytes)
        {
            throw new InvalidDataException("The mesh file does not begin with its description.");
        }

        var json = file.Slice(HeaderBytes + ChunkHeaderBytes, jsonLength);
        var binaryHeader = HeaderBytes + ChunkHeaderBytes + jsonLength;
        if (file.Length < binaryHeader + ChunkHeaderBytes
            || BinaryPrimitives.ReadUInt32LittleEndian(file[(binaryHeader + 4)..]) != BinaryChunk)
        {
            throw new InvalidDataException("The mesh file holds no geometry.");
        }

        var binaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file[binaryHeader..]));
        if (binaryLength > file.Length - binaryHeader - ChunkHeaderBytes)
        {
            throw new InvalidDataException("The mesh file is cut short.");
        }

        var binary = file.Slice(binaryHeader + ChunkHeaderBytes, binaryLength);

        JsonDocument document;
        try
        {
            var reader = new Utf8JsonReader(json);
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The mesh file's description cannot be read.", e);
        }

        using (document)
        {
            try
            {
                return ReadParts(document.RootElement, binary);
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException
                                          or ArgumentOutOfRangeException or FormatException or OverflowException)
            {
                throw new InvalidDataException("The mesh file is not laid out as this application writes one.", e);
            }
        }
    }

    private static List<GlbPart> ReadParts(JsonElement root, ReadOnlySpan<byte> binary)
    {
        var meshes = root.GetProperty("meshes");
        var accessors = root.GetProperty("accessors");
        var views = root.GetProperty("bufferViews");
        var hasMaterials = root.TryGetProperty("materials", out var materials);

        var parts = new List<GlbPart>();
        foreach (var node in root.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("mesh", out var meshIndex))
            {
                continue;
            }

            var primitives = meshes[meshIndex.GetInt32()].GetProperty("primitives");
            if (primitives.GetArrayLength() != 1)
            {
                throw new InvalidDataException("A mesh of the file has more than one part.");
            }

            var primitive = primitives[0];
            var indexBytes = Slice(accessors[primitive.GetProperty("indices").GetInt32()], views, binary, UnsignedInt, "SCALAR", out var indexCount);
            var positionBytes = Slice(
                accessors[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()], views, binary, Float, "VEC3", out var vertexCount);

            var indices = new int[indexCount];
            for (var i = 0; i < indexCount; i++)
            {
                var index = BinaryPrimitives.ReadUInt32LittleEndian(indexBytes[(i * 4)..]);
                if (index >= vertexCount)
                {
                    throw new InvalidDataException("A triangle of the file names a corner it does not have.");
                }

                indices[i] = (int)index;
            }

            // The file format has Y up and its third axis pointing south; a survey has Z up and Y
            // north. The writer turned one into the other, and this is that turn undone.
            var positions = new double[vertexCount * 3];
            for (var v = 0; v < vertexCount; v++)
            {
                var at = positionBytes[(v * 12)..];
                positions[(v * 3) + 0] = BinaryPrimitives.ReadSingleLittleEndian(at);
                positions[(v * 3) + 1] = -BinaryPrimitives.ReadSingleLittleEndian(at[8..]);
                positions[(v * 3) + 2] = BinaryPrimitives.ReadSingleLittleEndian(at[4..]);
            }

            (double R, double G, double B)? color = null;
            if (hasMaterials
                && primitive.TryGetProperty("material", out var materialIndex)
                && materials[materialIndex.GetInt32()].TryGetProperty("pbrMetallicRoughness", out var pbr)
                && pbr.TryGetProperty("baseColorFactor", out var factor)
                && factor.GetArrayLength() >= 3)
            {
                color = (factor[0].GetDouble(), factor[1].GetDouble(), factor[2].GetDouble());
            }

            var name = node.TryGetProperty("name", out var named) ? named.GetString() ?? string.Empty : string.Empty;
            parts.Add(new GlbPart(name, new IndexedMesh(positions, indices), color));
        }

        return parts;
    }

    /// <summary>The bytes one accessor describes, checked to be of the kind asked for and inside the file.</summary>
    private static ReadOnlySpan<byte> Slice(
        JsonElement accessor, JsonElement views, ReadOnlySpan<byte> binary, int componentType, string type, out int count)
    {
        if (accessor.GetProperty("componentType").GetInt32() != componentType
            || accessor.GetProperty("type").GetString() != type)
        {
            throw new InvalidDataException("An array of the file is not of the kind this application writes.");
        }

        count = accessor.GetProperty("count").GetInt32();
        var view = views[accessor.GetProperty("bufferView").GetInt32()];
        var offset = (view.TryGetProperty("byteOffset", out var viewOffset) ? viewOffset.GetInt32() : 0)
                     + (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
        var bytes = checked(count * 4 * (type == "VEC3" ? 3 : 1));
        if (count < 0 || offset < 0 || bytes > binary.Length - offset)
        {
            throw new InvalidDataException("An array of the file runs past its end.");
        }

        return binary.Slice(offset, bytes);
    }
}
