// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The shape a surveyor recorded the passage cross-section as, in this application's own
/// numbering.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no member for "the file did not say". Only one of the two compiled
/// formats records a section shape at all, and the one that does uses its own zero to mean the
/// surveyor left it unstated — so absence is the column being null, and every named member here
/// means the file positively said this. Giving absence a number as well would produce two ways to
/// store the same fact and a reader that has to know both.
/// </para>
/// <para>
/// Stored as a smallint and never renumbered: a row records what this enum meant on the day it was
/// written, and the numbers here are this application's, assigned by an explicit mapping from
/// whatever number the file used. A format that renumbers its own vocabulary must not be able to
/// change the meaning of rows already in the database.
/// </para>
/// </remarks>
public enum SurveySectionShape : short
{
    /// <summary>An elliptical cross-section.</summary>
    Oval = 1,

    /// <summary>A rectangular cross-section.</summary>
    Square = 2,

    /// <summary>A cross-section widest at the centerline and tapering above and below.</summary>
    Diamond = 3,

    /// <summary>A flat-floored cross-section arched above.</summary>
    Tunnel = 4,
}

/// <summary>
/// How wide and how tall the passage is at one survey station: the distances from the station to
/// the left, right, upper and lower walls, in metres.
///
/// <para>
/// One shape for both compiled formats, because the two disagree about what a cross-section hangs
/// off and neither shape can hold the other. One format measures walls at each end of a leg, so a
/// reading belongs to a station *and* to the leg it was taken along, and it carries a section
/// shape. The other records runs of cross-sections keyed by station name alone, with no leg and no
/// shape. Storing the first shape loses the second format's readings; storing the second loses
/// which leg the first format's reading was taken along. So the station is the key,
/// <see cref="ShotId"/> is null where the format has no such relationship, and
/// <see cref="Section"/> is null where the format has no such word.
/// </para>
///
/// <para>
/// Consequently a station may carry more than one reading — two legs meeting at a station each
/// measure its walls, and they need not agree. That is data, not a conflict to resolve on the way
/// in, so nothing here is unique per station.
/// </para>
///
/// <para>
/// A dimension the surveyor did not measure is null and never a number. Both formats say
/// "not measured" with a negative value, and the two do not even use the same negative; storing
/// one as if it were a measurement produces negative passage widths, and anything computed from
/// them — a mean width, a volume — comes out wrong in a way that reads as data rather than as a
/// bug. The database refuses a negative dimension for that reason.
/// </para>
///
/// <para>
/// A reading is taken at a station, so it is a cave coordinate by association and travels only on
/// the paths that already withhold a survey model from callers without exact-location access.
/// </para>
/// </summary>
public class SurveyLrud
{
    public long Id { get; set; }

    /// <summary>The survey model this reading was read out of.</summary>
    public Guid SurveyModelId { get; set; }

    /// <summary>
    /// The name of the station the walls were measured from, as
    /// <see cref="SurveyStation.Name"/> spells it. The key, because it is the one thing both
    /// formats state and the one thing that survives a re-export.
    /// </summary>
    public required string StationName { get; set; }

    /// <summary>
    /// The leg the reading was taken along, where the format says. Null for the format that keys
    /// its cross-sections by station alone — the relationship does not exist there, and inventing
    /// one would mean guessing which of a station's legs a reading belonged to.
    /// </summary>
    public long? ShotId { get; set; }

    /// <summary>
    /// The leg the reading was taken along. Present as a navigation because both rows are written
    /// in the same unit of work and the leg's id is assigned by the database: without this, the
    /// legs would have to be saved before the readings could name them.
    /// </summary>
    public SurveyShot? Shot { get; set; }

    /// <summary>The cross-section shape the file recorded, or null where the format has no word for one.</summary>
    public SurveySectionShape? Section { get; set; }

    /// <summary>Distance to the left-hand wall in metres, or null if it was not measured.</summary>
    public double? LeftM { get; set; }

    /// <summary>Distance to the right-hand wall in metres, or null if it was not measured.</summary>
    public double? RightM { get; set; }

    /// <summary>Distance to the ceiling in metres, or null if it was not measured.</summary>
    public double? UpM { get; set; }

    /// <summary>Distance to the floor in metres, or null if it was not measured.</summary>
    public double? DownM { get; set; }

    /// <summary>How wide the passage is here, where both walls were measured.</summary>
    public double? WidthM => LeftM is { } left && RightM is { } right ? left + right : null;

    /// <summary>How tall the passage is here, where both floor and ceiling were measured.</summary>
    public double? HeightM => UpM is { } up && DownM is { } down ? up + down : null;
}
