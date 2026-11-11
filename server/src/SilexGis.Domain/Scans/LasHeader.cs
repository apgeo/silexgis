// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Scans;

/// <summary>Three ordinates of a LAS header: a scale, an offset, or one corner of the extent.</summary>
public readonly record struct LasVector3(double X, double Y, double Z);

/// <summary>
/// One variable-length record as the header lists it: who wrote it, which record of theirs it is,
/// and how long its payload is. The payload itself is read only for the records this reader
/// understands; the list is kept so a card or a log can say what else the file carries.
/// </summary>
/// <param name="UserId">The writer's identifier, such as <c>LASF_Projection</c> or <c>laszip encoded</c>.</param>
/// <param name="RecordId">The record's number within that writer's namespace.</param>
/// <param name="Description">The free-text description the writer put in the record header.</param>
/// <param name="DataLength">The payload's length in bytes.</param>
public sealed record LasVariableLengthRecord(string UserId, ushort RecordId, string Description, int DataLength);

/// <summary>What kind of coordinate system the file's GeoTIFF keys name.</summary>
public enum LasCrsKind
{
    /// <summary>The keys name no model type, or one this reader does not recognise.</summary>
    Unknown = 0,

    /// <summary>A projected system: eastings and northings in metres (or feet).</summary>
    Projected = 1,

    /// <summary>A geographic system: longitudes and latitudes in degrees.</summary>
    Geographic = 2,

    /// <summary>An earth-centred Cartesian system.</summary>
    Geocentric = 3,
}

/// <summary>
/// The coordinate reference system a LAS file states for itself, as it states it. Present only when
/// the file carries at least one of the two ways of saying so; a file with neither has coordinates
/// in a frame nobody but its author knows, which the reader reports as a null system, not as an
/// error.
/// </summary>
/// <remarks>
/// A file may carry GeoTIFF keys, an OGC WKT string, or both. The EPSG codes come from the keys; the
/// WKT is kept verbatim because translating it to a code needs a projection library this layer does
/// not have. A reserved key value that means "user-defined" is reported as no code, and the WKT, when
/// there is one, then says what the system is.
/// </remarks>
/// <param name="Kind">What the GeoTIFF model-type key says, when there are keys.</param>
/// <param name="HorizontalEpsg">The EPSG code of the projected or geographic system, when the keys name one.</param>
/// <param name="VerticalEpsg">The EPSG code of the vertical system, when the keys name one.</param>
/// <param name="Wkt">The OGC WKT string, when the file carries one, as written.</param>
public sealed record LasCoordinateSystem(LasCrsKind Kind, int? HorizontalEpsg, int? VerticalEpsg, string? Wkt)
{
    /// <summary>Whether the file says enough to place its points on the earth without further information.</summary>
    public bool IsGeoreferenced => HorizontalEpsg is not null || !string.IsNullOrWhiteSpace(Wkt);
}

/// <summary>
/// What the public header and the variable-length records of a LAS or LAZ file say about the cloud,
/// read without touching a single point. Everything here is also what a LAZ file states in clear:
/// LAZ compresses the point records and leaves the header and the records before them as LAS wrote
/// them.
/// </summary>
/// <param name="VersionMajor">The format's major version; always 1.</param>
/// <param name="VersionMinor">The format's minor version, 0 to 4.</param>
/// <param name="GlobalEncoding">The global-encoding bit field as written; its bit 0 says how GPS time is kept.</param>
/// <param name="SystemIdentifier">The hardware or process that produced the points, as the writer named it.</param>
/// <param name="GeneratingSoftware">The software that wrote the file, as it named itself.</param>
/// <param name="CreatedOn">The creation date the header carries, when its day and year read as one.</param>
/// <param name="HeaderSize">The public header's size in bytes; the first variable-length record starts there.</param>
/// <param name="OffsetToPointData">Where the point records begin; everything before is header and records.</param>
/// <param name="DeclaredVlrCount">How many variable-length records the header says follow it.</param>
/// <param name="PointFormat">The point data record format, 0 to 10, with the compression bit removed.</param>
/// <param name="CompressionBitSet">Whether the format byte carried the bit a LAZ writer sets on it.</param>
/// <param name="PointRecordLength">The length of one point record in bytes, extra bytes included.</param>
/// <param name="LegacyPointCount">The 32-bit point count every version carries; zero in a 1.4 file whose count does not fit it.</param>
/// <param name="ExtendedPointCount">The 64-bit point count a 1.4 header carries; null before 1.4.</param>
/// <param name="Scale">What a stored integer ordinate is multiplied by, per axis.</param>
/// <param name="Offset">What is added to a scaled ordinate, per axis.</param>
/// <param name="Min">The smallest ordinate per axis, in the file's own frame.</param>
/// <param name="Max">The largest ordinate per axis, in the file's own frame.</param>
/// <param name="Vlrs">Every variable-length record found before the points, in file order.</param>
/// <param name="CoordinateSystem">The coordinate system the file states, or null when it states none.</param>
/// <param name="HasLaszipVlr">Whether a LASzip record is among the records, which is how a LAZ file announces its codec.</param>
public sealed record LasHeader(
    byte VersionMajor,
    byte VersionMinor,
    ushort GlobalEncoding,
    string SystemIdentifier,
    string GeneratingSoftware,
    DateOnly? CreatedOn,
    ushort HeaderSize,
    uint OffsetToPointData,
    uint DeclaredVlrCount,
    byte PointFormat,
    bool CompressionBitSet,
    ushort PointRecordLength,
    uint LegacyPointCount,
    long? ExtendedPointCount,
    LasVector3 Scale,
    LasVector3 Offset,
    LasVector3 Min,
    LasVector3 Max,
    IReadOnlyList<LasVariableLengthRecord> Vlrs,
    LasCoordinateSystem? CoordinateSystem,
    bool HasLaszipVlr)
{
    /// <summary>The version as it is usually written, such as <c>1.4</c>.</summary>
    public string Version => $"{VersionMajor}.{VersionMinor}";

    /// <summary>
    /// Whether the points are LAZ-compressed. Either sign is taken as enough: the compression bit
    /// on the format byte is what every LAZ writer sets, and the LASzip record is what every LAZ
    /// reader looks for; a file with one and not the other was written by a tool that got half of
    /// the convention right, and refusing to call it compressed would send it to a plain LAS reader
    /// that cannot read it.
    /// </summary>
    public bool IsCompressed => CompressionBitSet || HasLaszipVlr;

    /// <summary>
    /// How many points the file holds: the 64-bit count when the header carries one and it is set,
    /// otherwise the 32-bit count every version carries.
    /// </summary>
    public long PointCount => ExtendedPointCount is > 0 ? ExtendedPointCount.Value : LegacyPointCount;

    /// <summary>Whether each point carries red, green and blue; true for formats 2, 3, 5, 7, 8 and 10.</summary>
    public bool HasRgb => LasPointFormats.HasRgb(PointFormat);

    /// <summary>Whether each point carries a near-infrared value; true for formats 8 and 10.</summary>
    public bool HasNir => LasPointFormats.HasNir(PointFormat);

    /// <summary>Whether each point carries a GPS time; true for every format but 0 and 2.</summary>
    public bool HasGpsTime => LasPointFormats.HasGpsTime(PointFormat);

    /// <summary>Whether each point points at a waveform packet; true for formats 4, 5, 9 and 10.</summary>
    public bool HasWaveform => LasPointFormats.HasWaveform(PointFormat);

    /// <summary>
    /// How many bytes of each point record are beyond what its format defines: the "extra bytes"
    /// a writer may append per point, described by a record of their own.
    /// </summary>
    public int ExtraBytesPerPoint => PointRecordLength - LasPointFormats.MinimumRecordLength(PointFormat);

    /// <summary>The extent's size per axis, in the file's own units.</summary>
    public LasVector3 Size => new(Max.X - Min.X, Max.Y - Min.Y, Max.Z - Min.Z);

    /// <summary>
    /// Whether the writer filled the extent in. The specification requires it, but a writer that
    /// never updates the bounds leaves all six at zero — a file of over a hundred thousand points
    /// written by libLAS 1.8 was seen doing exactly that — and a card that showed such a cloud as
    /// a point would mislead. An empty cloud has nothing to bound and counts as stated.
    /// </summary>
    public bool ExtentStated => PointCount == 0 || Min != default || Max != default;
}

/// <summary>
/// What the LAS specification fixes for each point data record format: its minimum length and
/// which attributes it carries. Every standard format carries an intensity field, so whether a
/// cloud "has intensity" is not a question the header answers — only the values can.
/// </summary>
public static class LasPointFormats
{
    /// <summary>The highest format number the specification defines.</summary>
    public const byte Highest = 10;

    /// <summary>The bit a LAZ writer sets on the format byte to say the points are compressed.</summary>
    public const byte CompressionBit = 0x80;

    /// <summary>
    /// The bit some earlier LAZ writers set instead of, or beside, <see cref="CompressionBit"/>.
    /// The specification reserves both; a plain LAS writer leaves both clear.
    /// </summary>
    public const byte LegacyCompressionBit = 0x40;

    /// <summary>The minimum record length of each format, from the specification's record layouts.</summary>
    private static readonly int[] MinimumLengths = [20, 28, 26, 34, 57, 63, 30, 36, 38, 59, 67];

    /// <summary>Whether this is a format the specification defines.</summary>
    public static bool IsDefined(byte format) => format <= Highest;

    /// <summary>The shortest a record of a defined format can be.</summary>
    public static int MinimumRecordLength(byte format) =>
        IsDefined(format) ? MinimumLengths[format] : throw new ArgumentOutOfRangeException(nameof(format));

    /// <inheritdoc cref="LasHeader.HasRgb"/>
    public static bool HasRgb(byte format) => format is 2 or 3 or 5 or 7 or 8 or 10;

    /// <inheritdoc cref="LasHeader.HasNir"/>
    public static bool HasNir(byte format) => format is 8 or 10;

    /// <inheritdoc cref="LasHeader.HasGpsTime"/>
    public static bool HasGpsTime(byte format) => format is not (0 or 2);

    /// <inheritdoc cref="LasHeader.HasWaveform"/>
    public static bool HasWaveform(byte format) => format is 4 or 5 or 9 or 10;
}

/// <summary>Why a file could not be read as a LAS or LAZ file.</summary>
public enum LasRefusalKind
{
    /// <summary>The file ends before the header, a record, or its points do.</summary>
    Truncated = 0,

    /// <summary>The file does not begin the way a LAS file does.</summary>
    NotLas = 1,

    /// <summary>A LAS file of a version or point format this reader does not know.</summary>
    Unsupported = 2,

    /// <summary>A LAS file whose header contradicts itself or the file's length.</summary>
    Inconsistent = 3,
}

/// <summary>A refusal to read a file as a point cloud, with the reason in words a person can act on.</summary>
/// <param name="Kind">Which class of problem it is.</param>
/// <param name="Detail">What exactly was found; names the field or record, never a position.</param>
public sealed record LasRefusal(LasRefusalKind Kind, string Detail);
