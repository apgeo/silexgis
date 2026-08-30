// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// What a survey file says about a station, decoded into one vocabulary. Stored as an integer
/// bit set and never renumbered.
/// </summary>
/// <remarks>
/// <para>
/// The two compiled formats assign different bits to overlapping ideas — the same concept is bit
/// 2 in one file and bit 4 in the other, and each carries concepts the other has no word for. So
/// what is stored here is this application's own numbering, written by an explicit mapping rather
/// than by casting whatever number the reader happened to produce: a stored row means whatever
/// this enum meant on the day it was written, and a reader that renumbers its own vocabulary must
/// not be able to change that meaning underneath rows already in the database.
/// </para>
/// <para>
/// The file's untranslated bits are kept alongside in <see cref="SurveyStation.RawFlags"/>. A flag
/// this application does not yet understand is still evidence, and the alternative to keeping it
/// is re-reading the uploaded file to recover it.
/// </para>
/// </remarks>
[Flags]
public enum SurveyStationFlags
{
    None = 0,

    /// <summary>The station is above ground.</summary>
    Surface = 1,

    /// <summary>The station is on an underground leg. May combine with <see cref="Surface"/> at an entrance.</summary>
    Underground = 2,

    /// <summary>The station is a cave entrance.</summary>
    Entrance = 4,

    /// <summary>The station is exported from its survey, so other surveys may connect to it.</summary>
    Exported = 8,

    /// <summary>The station is a fixed control point — a position the survey was tied to.</summary>
    Fixed = 16,

    /// <summary>The station is a lead: passage seen and not yet surveyed.</summary>
    Continuation = 32,

    /// <summary>The station carries passage-wall geometry.</summary>
    HasWalls = 64,

    /// <summary>The station has no name of its own in the file.</summary>
    Anonymous = 128,

    /// <summary>The station lies on the passage wall rather than on the centerline.</summary>
    Wall = 256,
}

/// <summary>
/// One survey station read out of an uploaded compiled survey, positioned in the world.
///
/// <para>
/// Identity is the station's <see cref="Name"/> within its survey model, never the numeric id the
/// file gives it: one of the two formats numbers its stations by the order they appear in the file,
/// so those numbers are reassigned by every re-export and name nothing stable. The file's number is
/// kept as <see cref="FileStationId"/> because it is what the file's own shot records point at, but
/// nothing keys on it.
/// </para>
///
/// <para>
/// A station is a cave coordinate as much as the cave's own point is, so these rows travel only on
/// the paths that already withhold a survey model from callers without exact-location access.
/// </para>
/// </summary>
public class SurveyStation
{
    public long Id { get; set; }

    /// <summary>The survey model this station was read out of.</summary>
    public Guid SurveyModelId { get; set; }

    /// <summary>
    /// The station's name, unique within its survey model. Where a format names stations only
    /// within their own survey, the name stored here is qualified with the survey it belongs to,
    /// because two surveys in one file routinely both have a station called "1".
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The owning survey, as the file labels it, or null where the format does not say.
    /// Descriptive only — <see cref="Name"/> is what identifies the station.
    /// </summary>
    public string? SurveyName { get; set; }

    /// <summary>
    /// The number the file gives this station. Data, not identity: one format assigns these by file
    /// order and so renumbers them on every re-export.
    /// </summary>
    public long? FileStationId { get; set; }

    /// <summary>
    /// Where the station is, as longitude/latitude with altitude in metres.
    ///
    /// <para>
    /// The altitude is not decoration. A deep cave has distinct stations sharing a plan position,
    /// and treating two of them as one point is how a skeleton ends up longer than the cave it was
    /// built from. The column type demands three dimensions for that reason.
    /// </para>
    /// </summary>
    public required Point Position { get; set; }

    /// <summary>What the file says about this station, in this application's vocabulary.</summary>
    public SurveyStationFlags Flags { get; set; }

    /// <summary>The flag bits exactly as the file stored them, in the file's own numbering.</summary>
    public long RawFlags { get; set; }

    /// <summary>The station is a cave entrance.</summary>
    public bool IsEntrance => (Flags & SurveyStationFlags.Entrance) != 0;

    /// <summary>The station is a fixed control point the survey was tied to.</summary>
    public bool IsFixed => (Flags & SurveyStationFlags.Fixed) != 0;
}
