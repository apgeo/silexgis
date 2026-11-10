// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;

namespace SilexGis.Domain.Scans;

/// <summary>
/// A surface made of a scan, set down the way a wall mesh is: its vertices turned from the scan's
/// tile frame into east-north-up metres about the tiles' zero point, by the scan's placement.
/// </summary>
/// <remarks>
/// <para>
/// A wall mesh carries its turn in its vertices — it is placed once, when it is made, as an
/// uploaded one is — while a scan carries its turn beside its tiles, as a heading, pitch and roll.
/// So a surface written in the tile frame is rewritten here with the placement's rotation applied,
/// and from there it is an ordinary STL in plain metres about a named point: the mesh path reads,
/// converts and draws it exactly as it does an uploaded file declared that way, with the placement's
/// zero point as the declared origin and its height as the declared height.
/// </para>
/// <para>
/// Only binary STL is taken — the worker writes nothing else — and the file is streamed facet by
/// facet, so a surface of two million triangles is never held whole. Coordinates stay 32-bit, as
/// the format has them: a cave passage is metres to a few kilometres from its zero point, where a
/// float still resolves a fraction of a millimetre.
/// </para>
/// </remarks>
public static class ScanSurfaceStl
{
    private const int HeaderBytes = 80;
    private const int FacetBytes = 50;

    /// <summary>
    /// Writes <paramref name="input"/>, a binary STL in the tile frame, to <paramref name="output"/>
    /// with every corner and normal turned by <paramref name="placement"/>. Answers the number of
    /// triangles written. Throws <see cref="InvalidDataException"/> for a file that is not a
    /// complete binary STL — shorter than its count says, or holding nothing.
    /// </summary>
    public static long Place(Stream input, Stream output, ScanPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(placement);

        var header = new byte[HeaderBytes + 4];
        if (ReadFully(input, header) != header.Length)
        {
            throw new InvalidDataException("The surface is shorter than an STL header.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(HeaderBytes));
        if (count == 0)
        {
            throw new InvalidDataException("The surface holds no triangles.");
        }

        var r = placement.Rotation();
        output.Write(header);
        var facet = new byte[FacetBytes];
        for (long i = 0; i < count; i++)
        {
            if (ReadFully(input, facet) != FacetBytes)
            {
                throw new InvalidDataException($"The surface ends after {i:N0} of the {count:N0} triangles its header counts.");
            }

            // The normal first, then the three corners: four triples of little-endian floats.
            for (var triple = 0; triple < 4; triple++)
            {
                var at = facet.AsSpan(triple * 12, 12);
                double x = BinaryPrimitives.ReadSingleLittleEndian(at);
                double y = BinaryPrimitives.ReadSingleLittleEndian(at[4..]);
                double z = BinaryPrimitives.ReadSingleLittleEndian(at[8..]);
                BinaryPrimitives.WriteSingleLittleEndian(at, (float)((r[0, 0] * x) + (r[0, 1] * y) + (r[0, 2] * z)));
                BinaryPrimitives.WriteSingleLittleEndian(at[4..], (float)((r[1, 0] * x) + (r[1, 1] * y) + (r[1, 2] * z)));
                BinaryPrimitives.WriteSingleLittleEndian(at[8..], (float)((r[2, 0] * x) + (r[2, 1] * y) + (r[2, 2] * z)));
            }

            output.Write(facet);
        }

        return count;
    }

    private static int ReadFully(Stream input, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = input.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return read;
    }
}
