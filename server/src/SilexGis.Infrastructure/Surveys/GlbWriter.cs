// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text.Json;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Writes a welded mesh as a single self-contained glTF 2.0 binary file.
///
/// <para>
/// Written by hand rather than through a library, because the format is a twelve-byte header, two
/// length-prefixed chunks and a small JSON document describing where in the second chunk each array
/// starts. The libraries that do this bring a native mesh toolkit with them — a second set of
/// platform binaries to ship, pin and keep working on both operating systems this runs on — to
/// perform an operation that is fully specified in the paragraph above.
/// </para>
/// </summary>
public static class GlbWriter
{
    private const uint Magic = 0x46546C67; // "glTF"
    private const uint Version = 2;
    private const uint JsonChunk = 0x4E4F534A; // "JSON"
    private const uint BinaryChunk = 0x004E4942; // "BIN\0"

    private const int HeaderBytes = 12;
    private const int ChunkHeaderBytes = 8;

    // glTF component and target constants, from the specification's own numbering.
    private const int UnsignedInt = 5125;
    private const int Float = 5126;
    private const int ArrayBuffer = 34962;
    private const int ElementArrayBuffer = 34963;

    /// <summary>The colour walls are drawn in, linear RGB: a pale limestone grey.</summary>
    public static readonly (double R, double G, double B) WallColor = (0.72, 0.68, 0.62);

    /// <summary>The name of the node estimated passages are written under, which a viewer finds them by.</summary>
    public const string EstimatedNodeName = "estimated";

    /// <summary>The name of the node a survey's walls are written under.</summary>
    public const string WallsNodeName = "walls";

    /// <summary>
    /// Writes the mesh, moved so that <paramref name="origin"/> becomes its zero point.
    ///
    /// <para>
    /// The move is the reason this takes an origin at all. Vertices are stored as 32-bit floats,
    /// whose spacing at a projected easting of half a million metres is centimetres and at a
    /// northing of five million is half a metre — so a mesh written at its world coordinates would
    /// be visibly quantised, in a way no viewer could undo. Written about its own origin, the same
    /// floats carry micron spacing over the size of a cave, and the position it belongs at is
    /// carried separately as a pair of full-precision degrees.
    /// </para>
    /// </summary>
    /// <param name="mesh">Vertices in the source file's coordinates, Z upwards.</param>
    /// <param name="origin">The point in those coordinates that becomes the model's zero.</param>
    /// <param name="output">Where the file is written; not closed here.</param>
    public static void Write(IndexedMesh mesh, (double X, double Y, double Z) origin, Stream output)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Write([new Part(WallsNodeName, "cave", mesh, WallColor)], flagged: false, origin, output);
    }

    /// <summary>
    /// Writes a survey's walls and, apart from them, the tubes of an estimated size around its
    /// passages that have no size in the file — either may be missing, not both.
    ///
    /// <para>
    /// Two nodes rather than one mesh, so that a viewer can turn the estimates off without turning
    /// off the walls. Each vertex of both also carries an application attribute,
    /// <c>_ESTIMATED</c> — one on the estimates, zero on the walls — so that a viewer can recolour
    /// the estimates alone with a shader; it is written on both because a shader that reads an
    /// attribute some primitive lacks is switched off for that primitive. Any other glTF viewer
    /// ignores the attribute and draws the estimates in the colour they were built in.
    /// </para>
    /// </summary>
    /// <param name="estimatedColor">Linear RGB of the estimated tubes.</param>
    public static void Write(
        IndexedMesh? walls,
        IndexedMesh? estimated,
        (double R, double G, double B) estimatedColor,
        (double X, double Y, double Z) origin,
        Stream output)
    {
        if (estimated is null)
        {
            ArgumentNullException.ThrowIfNull(walls);
            Write(walls, origin, output);
            return;
        }

        var parts = new List<Part>();
        if (walls is not null)
        {
            parts.Add(new Part(WallsNodeName, "cave", walls, WallColor));
        }

        parts.Add(new Part(EstimatedNodeName, "estimated", estimated, estimatedColor, Flag: 1));
        Write(parts, flagged: true, origin, output);
    }

    /// <summary>
    /// Turns <c>#rrggbb</c> into the linear RGB a glTF material states its colour in, so that the
    /// colour on screen is the colour that was chosen. Null for anything else.
    /// </summary>
    public static (double R, double G, double B)? LinearFromHex(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            return null;
        }

        static double Linear(int channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (Linear((rgb >> 16) & 0xFF), Linear((rgb >> 8) & 0xFF), Linear(rgb & 0xFF));
    }

    /// <summary>One node of the file: a mesh, its material, and its value of the estimate flag.</summary>
    private sealed record Part(string Node, string Material, IndexedMesh Mesh, (double R, double G, double B) Color, float Flag = 0);

    /// <summary>A part laid out for writing: its arrays, and how many bytes each takes.</summary>
    private sealed record Laid(Part Part, float[] Positions, float[] Normals, float[]? Flags)
    {
        public int IndexBytes => Part.Mesh.Indices.Length * sizeof(uint);

        public int PositionBytes => Positions.Length * sizeof(float);

        public int NormalBytes => Normals.Length * sizeof(float);

        public int FlagBytes => (Flags?.Length ?? 0) * sizeof(float);

        public int Bytes => IndexBytes + PositionBytes + NormalBytes + FlagBytes;
    }

    private static void Write(IReadOnlyList<Part> parts, bool flagged, (double X, double Y, double Z) origin, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var laid = new List<Laid>(parts.Count);
        foreach (var part in parts)
        {
            var positions = ToGltfAxes(part.Mesh.Positions, origin);
            var normals = SmoothNormals(positions, part.Mesh.Indices);
            float[]? flags = null;
            if (flagged)
            {
                flags = new float[part.Mesh.VertexCount];
                Array.Fill(flags, part.Flag);
            }

            laid.Add(new Laid(part, positions, normals, flags));
        }

        var json = BuildJson(laid);
        var jsonPadding = Padding(json.Length);
        var binaryLength = laid.Sum(l => l.Bytes);

        // Every offset in the JSON above is already fixed, so the total is known before a byte of
        // it is written and the file can be produced in one pass rather than buffered whole.
        var total = HeaderBytes
            + ChunkHeaderBytes + json.Length + jsonPadding
            + ChunkHeaderBytes + binaryLength;

        WriteWord(output, Magic);
        WriteWord(output, Version);
        WriteWord(output, (uint)total);

        WriteWord(output, (uint)(json.Length + jsonPadding));
        WriteWord(output, JsonChunk);
        output.Write(json);
        for (var i = 0; i < jsonPadding; i++)
        {
            output.WriteByte(0x20); // the JSON chunk pads with spaces, so it stays parseable
        }

        WriteWord(output, (uint)binaryLength);
        WriteWord(output, BinaryChunk);
        foreach (var part in laid)
        {
            WriteIndices(output, part.Part.Mesh.Indices);
            WriteFloats(output, part.Positions);
            WriteFloats(output, part.Normals);
            if (part.Flags is { } flags)
            {
                WriteFloats(output, flags);
            }
        }
    }

    /// <summary>
    /// Moves the mesh onto its origin and into glTF's axes.
    ///
    /// <para>
    /// Survey data is Z-up: X east, Y north, Z up. glTF is Y-up, and stays right-handed by having
    /// its third axis point south. Doing this here rather than declaring a rotation on the node
    /// keeps the file correct in any viewer, including the ones that ignore such a declaration.
    /// </para>
    /// </summary>
    private static float[] ToGltfAxes(double[] source, (double X, double Y, double Z) origin)
    {
        var result = new float[source.Length];
        for (var i = 0; i < source.Length; i += 3)
        {
            result[i + 0] = (float)(source[i + 0] - origin.X);
            result[i + 1] = (float)(source[i + 2] - origin.Z);
            result[i + 2] = (float)-(source[i + 1] - origin.Y);
        }

        return result;
    }

    /// <summary>
    /// One normal per vertex, from the faces meeting there, weighted by their area.
    ///
    /// <para>
    /// Normals are written rather than left out although the specification says a mesh without them
    /// is flat-shaded: renderers commonly treat a primitive with no normals as unlit instead, and an
    /// unlit cave is a silhouette — a single flat colour in which no passage, chamber or slope can
    /// be made out, which is the entire content of the model.
    /// </para>
    /// <para>
    /// The weighting falls out of the arithmetic for free: a triangle's cross product is already
    /// twice its area in length, so adding it unnormalised gives each face an influence proportional
    /// to how much surface it actually contributes, and the many slivers a survey export is full of
    /// stop outvoting the faces a viewer can see.
    /// </para>
    /// </summary>
    private static float[] SmoothNormals(float[] positions, int[] indices)
    {
        var normals = new float[positions.Length];

        for (var t = 0; t < indices.Length; t += 3)
        {
            var a = indices[t + 0] * 3;
            var b = indices[t + 1] * 3;
            var c = indices[t + 2] * 3;

            double abX = positions[b + 0] - positions[a + 0];
            double abY = positions[b + 1] - positions[a + 1];
            double abZ = positions[b + 2] - positions[a + 2];
            double acX = positions[c + 0] - positions[a + 0];
            double acY = positions[c + 1] - positions[a + 1];
            double acZ = positions[c + 2] - positions[a + 2];

            var nX = (float)((abY * acZ) - (abZ * acY));
            var nY = (float)((abZ * acX) - (abX * acZ));
            var nZ = (float)((abX * acY) - (abY * acX));

            foreach (var vertex in (ReadOnlySpan<int>)[a, b, c])
            {
                normals[vertex + 0] += nX;
                normals[vertex + 1] += nY;
                normals[vertex + 2] += nZ;
            }
        }

        for (var i = 0; i < normals.Length; i += 3)
        {
            var length = MathF.Sqrt(
                (normals[i] * normals[i])
                + (normals[i + 1] * normals[i + 1])
                + (normals[i + 2] * normals[i + 2]));
            if (length > 0)
            {
                normals[i] /= length;
                normals[i + 1] /= length;
                normals[i + 2] /= length;
            }
            else
            {
                // Only reachable where every face touching a vertex cancelled out, which needs a
                // surface folded exactly back on itself. Upwards is as good as any other answer and
                // keeps the attribute valid, which a zero vector would not be.
                normals[i + 1] = 1;
            }
        }

        return normals;
    }

    private static byte[] BuildJson(IReadOnlyList<Laid> parts)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();

            w.WriteStartObject("asset");
            w.WriteString("version", "2.0");
            w.WriteString("generator", "SilexGIS survey mesh converter");
            w.WriteEndObject();

            w.WriteNumber("scene", 0);
            w.WriteStartArray("scenes");
            w.WriteStartObject();
            w.WriteStartArray("nodes");
            for (var p = 0; p < parts.Count; p++)
            {
                w.WriteNumberValue(p);
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();

            w.WriteStartArray("nodes");
            for (var p = 0; p < parts.Count; p++)
            {
                w.WriteStartObject();
                w.WriteString("name", parts[p].Part.Node);
                w.WriteNumber("mesh", p);
                w.WriteEndObject();
            }

            w.WriteEndArray();

            // Accessors and buffer views run part by part, each part's in the same order: indices,
            // positions, normals, and the estimate flag where there is one.
            var perPart = parts[0].Flags is null ? 3 : 4;

            w.WriteStartArray("meshes");
            for (var p = 0; p < parts.Count; p++)
            {
                var first = p * perPart;
                w.WriteStartObject();
                w.WriteString("name", parts[p].Part.Node);
                w.WriteStartArray("primitives");
                w.WriteStartObject();
                w.WriteStartObject("attributes");
                w.WriteNumber("POSITION", first + 1);
                w.WriteNumber("NORMAL", first + 2);
                if (perPart == 4)
                {
                    w.WriteNumber("_ESTIMATED", first + 3);
                }

                w.WriteEndObject();
                w.WriteNumber("indices", first);
                w.WriteNumber("material", p);
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("materials");
            foreach (var part in parts)
            {
                w.WriteStartObject();
                w.WriteString("name", part.Part.Material);
                w.WriteStartObject("pbrMetallicRoughness");
                w.WriteStartArray("baseColorFactor");
                var (r, g, b) = part.Part.Color;
                foreach (var channel in (ReadOnlySpan<double>)[r, g, b, 1.0])
                {
                    w.WriteNumberValue(Math.Round(channel, 6));
                }

                w.WriteEndArray();
                w.WriteNumber("metallicFactor", 0);
                w.WriteNumber("roughnessFactor", 1);
                w.WriteEndObject();

                // A cave is looked at from the inside, where every wall is presenting its back face.
                // Left single-sided the model would be an empty hole with a few far walls floating in
                // it, which reads as a broken upload rather than as a cave.
                w.WriteBoolean("doubleSided", true);
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("accessors");
            var bufferView = 0;
            foreach (var part in parts)
            {
                var (min, max) = Bounds(part.Positions);
                WriteAccessor(w, bufferView: bufferView++, componentType: UnsignedInt, count: part.Part.Mesh.Indices.Length, type: "SCALAR");

                w.WriteStartObject();
                w.WriteNumber("bufferView", bufferView++);
                w.WriteNumber("componentType", Float);
                w.WriteNumber("count", part.Part.Mesh.VertexCount);
                w.WriteString("type", "VEC3");
                // Required on POSITION, and not decoration: a viewer sizes its view and decides whether
                // the model is on screen at all from these before it reads a single vertex.
                WriteVector(w, "min", min);
                WriteVector(w, "max", max);
                w.WriteEndObject();

                WriteAccessor(w, bufferView: bufferView++, componentType: Float, count: part.Part.Mesh.VertexCount, type: "VEC3");
                if (part.Flags is not null)
                {
                    WriteAccessor(w, bufferView: bufferView++, componentType: Float, count: part.Part.Mesh.VertexCount, type: "SCALAR");
                }
            }

            w.WriteEndArray();

            w.WriteStartArray("bufferViews");
            var offset = 0;
            foreach (var part in parts)
            {
                WriteBufferView(w, offset: offset, length: part.IndexBytes, target: ElementArrayBuffer);
                offset += part.IndexBytes;
                WriteBufferView(w, offset: offset, length: part.PositionBytes, target: ArrayBuffer);
                offset += part.PositionBytes;
                WriteBufferView(w, offset: offset, length: part.NormalBytes, target: ArrayBuffer);
                offset += part.NormalBytes;
                if (part.Flags is not null)
                {
                    WriteBufferView(w, offset: offset, length: part.FlagBytes, target: ArrayBuffer);
                    offset += part.FlagBytes;
                }
            }

            w.WriteEndArray();

            w.WriteStartArray("buffers");
            w.WriteStartObject();
            w.WriteNumber("byteLength", offset);
            w.WriteEndObject();
            w.WriteEndArray();

            w.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteAccessor(Utf8JsonWriter w, int bufferView, int componentType, int count, string type)
    {
        w.WriteStartObject();
        w.WriteNumber("bufferView", bufferView);
        w.WriteNumber("componentType", componentType);
        w.WriteNumber("count", count);
        w.WriteString("type", type);
        w.WriteEndObject();
    }

    private static void WriteBufferView(Utf8JsonWriter w, int offset, int length, int target)
    {
        w.WriteStartObject();
        w.WriteNumber("buffer", 0);
        w.WriteNumber("byteOffset", offset);
        w.WriteNumber("byteLength", length);
        w.WriteNumber("target", target);
        w.WriteEndObject();
    }

    private static void WriteVector(Utf8JsonWriter w, string name, float[] value)
    {
        w.WriteStartArray(name);
        foreach (var component in value)
        {
            w.WriteNumberValue(component);
        }

        w.WriteEndArray();
    }

    private static (float[] Min, float[] Max) Bounds(float[] positions)
    {
        var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
        var max = new[] { float.MinValue, float.MinValue, float.MinValue };
        for (var i = 0; i < positions.Length; i++)
        {
            var axis = i % 3;
            min[axis] = MathF.Min(min[axis], positions[i]);
            max[axis] = MathF.Max(max[axis], positions[i]);
        }

        return (min, max);
    }

    private static void WriteIndices(Stream output, int[] indices)
    {
        var buffer = new byte[Math.Min(indices.Length, 8192) * sizeof(uint)];
        var written = 0;
        while (written < indices.Length)
        {
            var take = Math.Min(buffer.Length / sizeof(uint), indices.Length - written);
            for (var i = 0; i < take; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    buffer.AsSpan(i * sizeof(uint)), (uint)indices[written + i]);
            }

            output.Write(buffer.AsSpan(0, take * sizeof(uint)));
            written += take;
        }
    }

    private static void WriteFloats(Stream output, float[] values)
    {
        var buffer = new byte[Math.Min(values.Length, 8192) * sizeof(float)];
        var written = 0;
        while (written < values.Length)
        {
            var take = Math.Min(buffer.Length / sizeof(float), values.Length - written);
            for (var i = 0; i < take; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    buffer.AsSpan(i * sizeof(float)), values[written + i]);
            }

            output.Write(buffer.AsSpan(0, take * sizeof(float)));
            written += take;
        }
    }

    /// <summary>Bytes needed to bring a chunk up to the four-byte boundary every chunk starts on.</summary>
    private static int Padding(int length) => (4 - (length % 4)) % 4;

    /// <summary>Every length, offset and tag in the container is one little-endian 32-bit word.</summary>
    private static void WriteWord(Stream output, uint value)
    {
        Span<byte> word = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(word, value);
        output.Write(word);
    }
}
