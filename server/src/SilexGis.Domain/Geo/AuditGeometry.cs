// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace SilexGis.Domain.Geo;

/// <summary>
/// How a geometry is written into the change history: in full when it is small enough to read,
/// as a summary when it is not.
/// </summary>
/// <remarks>
/// <para>
/// A created or deleted row is recorded with every field it had, and a cave survey's line work is
/// a geometry of hundreds of thousands of points — well over a megabyte of text, written once when
/// the row is created and again when it is deleted. Nobody reads that text in a history list, and
/// it is the bulk of the table. So a geometry whose text would exceed <see cref="TextLimit"/>
/// characters is recorded as its shape type, its number of points and a SHA-256 fingerprint of its
/// bytes: enough to say that it changed, how big it was, and whether two versions are identical.
/// </para>
/// <para>
/// Small geometries — an entrance point, a hand-drawn outline — stay in full, because the history
/// is where "the entrance moved from here to there" is read and put back.
/// </para>
/// <para>
/// <b>The summary carries no position.</b> No bounding box, no centre, no first coordinate: any of
/// them would put a protected cave's whereabouts into the history in a field the history's own
/// protection does not recognise as a position. That protection removes the whole property for a
/// row whose position the reader may not see, summary or not.
/// </para>
/// </remarks>
public static class AuditGeometry
{
    /// <summary>Longest geometry text recorded in full. A point is about 40 characters.</summary>
    public const int TextLimit = 4000;

    /// <summary>
    /// Fewest characters any point can take in the text form ("0 0" plus a separator), so a
    /// geometry with more points than <see cref="TextLimit"/> / this is known to be too long
    /// without writing the text out first.
    /// </summary>
    private const int MinCharactersPerPoint = 4;

    /// <summary>The geometry as text, or its summary when the text would be longer than the limit.</summary>
    public static object Record(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.NumPoints <= TextLimit / MinCharactersPerPoint)
        {
            var text = geometry.AsText();
            if (text.Length <= TextLimit)
            {
                return text;
            }
        }

        return Summary(geometry);
    }

    /// <summary>
    /// The summary of a geometry: shape type, point count and the fingerprint of its
    /// little-endian WKB with altitudes, so two versions differing only in Z are told apart.
    /// </summary>
    public static Dictionary<string, object?> Summary(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var bytes = new WKBWriter(ByteOrder.LittleEndian, handleSRID: false, emitZ: true).Write(geometry);
        return new Dictionary<string, object?>
        {
            ["geometryType"] = geometry.GeometryType,
            ["points"] = geometry.NumPoints,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ["omitted"] = true,
        };
    }
}
