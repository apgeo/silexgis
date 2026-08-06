// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>A mesh file could not be read, with a reason worth showing the person who uploaded it.</summary>
public sealed class MeshIOException(string message) : Exception(message);

/// <summary>
/// A triangle mesh with shared vertices, in the coordinates its file used.
///
/// <para>
/// Positions are laid out x,y,z per vertex and indices three per triangle, which is the layout both
/// the writer and the accessors it declares consume directly — an intermediate representation of
/// structs would be copied twice for nothing at these sizes.
/// </para>
/// </summary>
public sealed record IndexedMesh(double[] Positions, int[] Indices)
{
    public int VertexCount => Positions.Length / 3;

    public int TriangleCount => Indices.Length / 3;
}

/// <summary>What reading a survey mesh found, beyond the geometry itself.</summary>
/// <param name="Mesh">The welded mesh, still in file coordinates.</param>
/// <param name="TrianglesRead">Triangles the file declared.</param>
/// <param name="DegenerateDropped">
/// Triangles discarded because their three corners are not three distinct points. Up to 55% of a
/// Therion loch export is these; they carry no surface and would only cost bytes downstream.
/// </param>
/// <param name="CoarsestStepM">
/// The finest distance the file could still express at its own magnitude, on its worst axis.
/// Coordinates are stored as 32-bit floats, whose spacing grows with magnitude: near zero it is
/// microns, but at a projected northing of five million metres it is half a metre. A file that
/// large has already lost the precision a survey was measured at, before it ever reached us, and
/// re-centring cannot bring it back.
/// </param>
public sealed record MeshReadResult(
    IndexedMesh Mesh,
    int TrianglesRead,
    int DegenerateDropped,
    double CoarsestStepM);

/// <summary>
/// Reads binary STL — the format every cave-survey toolchain in use exports — into a mesh whose
/// vertices are shared.
///
/// <para>
/// STL is a triangle soup: every triangle carries its own three corners, so a vertex shared by
/// twelve triangles is stored twelve times, and nothing in the file says they are the same point.
/// Welding identical corners is therefore not an optimisation applied to a mesh, it is the step
/// that turns a soup into one, and it is what makes the file small enough to send to a phone.
/// </para>
/// </summary>
public static class StlReader
{
    /// <summary>Fixed part of a binary STL: an 80-byte header and a triangle count.</summary>
    private const int HeaderBytes = 84;

    /// <summary>Per triangle: a face normal and three corners, three floats each, then two spare bytes.</summary>
    private const int TriangleBytes = 50;

    /// <summary>
    /// Above this spacing the source coordinates are too coarse for the survey they came from, and
    /// the uploader is told so. Half a decimetre is already worse than any cave survey's own
    /// closure error, and well inside what a projected coordinate in 32-bit float destroys.
    /// </summary>
    public const double PrecisionWarningStepM = 0.05;

    public static MeshReadResult Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[HeaderBytes];
        stream.ReadExactly(header);

        // ASCII STL opens with "solid". Binary files are not supposed to, but some writers put a
        // name there anyway, so the word alone does not decide it — the declared triangle count
        // matching the file length does, and that is checked below either way.
        var triangleCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(80));
        if (triangleCount <= 0)
        {
            throw new MeshIOException(
                "This file declares no triangles. Binary STL is expected; an ASCII STL or a file "
                + "from another format has to be converted before upload.");
        }

        // A count that cannot be right is caught before it is trusted to size an array: the value
        // sits at a fixed offset in every file, including files that are not STL at all, and a
        // wrong one asks for hundreds of gigabytes.
        var expected = (long)HeaderBytes + (long)triangleCount * TriangleBytes;
        if (stream.CanSeek && stream.Length != expected)
        {
            throw new MeshIOException(
                $"This is not a binary STL: it declares {triangleCount} triangles, which needs "
                + $"{expected} bytes, and the file is {stream.Length}.");
        }

        var welder = new VertexWelder(triangleCount * 3);
        var indices = new List<int>(triangleCount * 3);
        var buffer = new byte[TriangleBytes];
        var degenerate = 0;

        for (var t = 0; t < triangleCount; t++)
        {
            stream.ReadExactly(buffer);

            // The file's own face normal is skipped deliberately. Exporters disagree about it,
            // some write zeros, and a normal per triangle cannot be shared between the triangles
            // meeting at a vertex — so it is recomputed later from the geometry, which is the one
            // description of the surface that is certainly true.
            var a = welder.Add(buffer.AsSpan(12));
            var b = welder.Add(buffer.AsSpan(24));
            var c = welder.Add(buffer.AsSpan(36));

            // A triangle whose corners are not three distinct points has no surface. It is not an
            // error — it is what happens when a surveyed passage narrows to nothing — but it draws
            // nothing, so it is dropped here rather than carried through every later stage.
            if (a == b || b == c || a == c)
            {
                degenerate++;
                continue;
            }

            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        if (indices.Count == 0)
        {
            throw new MeshIOException(
                $"All {triangleCount} triangles in this file are degenerate, so there is no surface "
                + "to show.");
        }

        var mesh = welder.Build(indices);
        return new MeshReadResult(mesh, triangleCount, degenerate, CoarsestStep(mesh.Positions));
    }

    /// <summary>
    /// The finest step the source coordinates could still express, on whichever axis is worst.
    ///
    /// <para>
    /// Read from the magnitudes actually present rather than from the file's declared units: what
    /// costs precision is how far the numbers are from zero, and a mesh centred on a projected
    /// origin is far from zero no matter what it is measuring.
    /// </para>
    /// </summary>
    private static double CoarsestStep(double[] positions)
    {
        var worst = 0.0;
        for (var axis = 0; axis < 3; axis++)
        {
            var largest = 0.0f;
            for (var i = axis; i < positions.Length; i += 3)
            {
                var magnitude = Math.Abs((float)positions[i]);
                if (magnitude > largest)
                {
                    largest = magnitude;
                }
            }

            var step = (double)MathF.BitIncrement(largest) - largest;
            if (step > worst)
            {
                worst = step;
            }
        }

        return worst;
    }
}

/// <summary>
/// Gives every distinct corner one index, comparing the stored bytes rather than the values.
///
/// <para>
/// Comparing bit patterns is what makes the welding exact and repeatable. Two corners a file means
/// to be the same point were written from the same number, so their four bytes are identical; a
/// tolerance would additionally merge points that a survey deliberately recorded a few millimetres
/// apart, and would make the result depend on the order the triangles happen to arrive in.
/// </para>
/// </summary>
internal sealed class VertexWelder(int expectedCorners)
{
    private readonly Dictionary<(uint X, uint Y, uint Z), int> _seen = new(expectedCorners / 4);
    private readonly List<double> _positions = new(expectedCorners / 2);

    /// <summary>Interns one corner, given the twelve bytes holding its three floats.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Add(ReadOnlySpan<byte> corner)
    {
        var key = (
            BinaryPrimitives.ReadUInt32LittleEndian(corner),
            BinaryPrimitives.ReadUInt32LittleEndian(corner[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(corner[8..]));

        if (_seen.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var index = _positions.Count / 3;
        _positions.Add(BitConverter.UInt32BitsToSingle(key.Item1));
        _positions.Add(BitConverter.UInt32BitsToSingle(key.Item2));
        _positions.Add(BitConverter.UInt32BitsToSingle(key.Item3));
        _seen[key] = index;
        return index;
    }

    /// <summary>
    /// Builds the mesh, keeping only vertices some surviving triangle still uses.
    ///
    /// <para>
    /// Dropping degenerate triangles orphans the vertices that only they referenced — in a loch
    /// export, where more than half the triangles can be degenerate, that is a large share of the
    /// vertex list. They would be sent to every viewer and drawn by nothing.
    /// </para>
    /// </summary>
    public IndexedMesh Build(List<int> indices)
    {
        var remap = new int[_positions.Count / 3];
        Array.Fill(remap, -1);

        var kept = new List<double>(_positions.Count);
        var compacted = new int[indices.Count];
        for (var i = 0; i < indices.Count; i++)
        {
            var old = indices[i];
            if (remap[old] < 0)
            {
                remap[old] = kept.Count / 3;
                kept.Add(_positions[(old * 3) + 0]);
                kept.Add(_positions[(old * 3) + 1]);
                kept.Add(_positions[(old * 3) + 2]);
            }

            compacted[i] = remap[old];
        }

        return new IndexedMesh(kept.ToArray(), compacted);
    }
}
