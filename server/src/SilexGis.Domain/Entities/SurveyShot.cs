// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Entities;

/// <summary>
/// What a survey file says about a shot, decoded into one vocabulary. Stored as an integer bit set
/// and never renumbered.
/// </summary>
/// <remarks>
/// <para>
/// The same reasoning as the station flags: the two compiled formats disagree about which bit means
/// what — a splay is bit 16 in one and bit 4 in the other — so the stored value is this
/// application's own numbering, produced by an explicit mapping and not by casting the reader's
/// number. The file's untranslated bits are kept in <see cref="SurveyShot.RawFlags"/>.
/// </para>
/// <para>
/// <see cref="Splay"/> is the one that changes an answer. Until these rows existed, a splay could
/// only be guessed at from the shape of the network, because nothing here had read a survey file's
/// own flags; a modern export is around 98% splays by component count, so a guess that is nearly
/// right is still an orientation statistic measuring the surveyor's wall-shooting habit. This is
/// the file's answer, and it is not the guess.
/// </para>
/// </remarks>
[Flags]
public enum SurveyShotFlags
{
    None = 0,

    /// <summary>The leg is above ground.</summary>
    Surface = 1,

    /// <summary>The leg duplicates passage surveyed elsewhere — real passage, counted once.</summary>
    Duplicate = 2,

    /// <summary>
    /// The leg is a splay: a shot fired from a station at the wall to measure passage shape,
    /// not a leg of the traverse.
    /// </summary>
    Splay = 4,

    /// <summary>The leg is hidden in viewers.</summary>
    NotVisible = 8,

    /// <summary>The leg carries no usable passage dimensions.</summary>
    NotLrud = 16,
}

/// <summary>
/// One survey leg read out of an uploaded compiled survey: the line between two positions, with
/// what the file said about it.
///
/// <para>
/// Endpoints name stations by <see cref="SurveyStation.Name"/> where the file lets them be
/// resolved, and are null where it does not — a splay's far end is frequently a point the file
/// gives no station for at all, and dropping those legs would throw away exactly the flags this
/// row exists to carry. There is deliberately no foreign key to the station rows for the same
/// reason.
/// </para>
///
/// <para>
/// A shot is a pair of cave coordinates, so these rows travel only on the paths that already
/// withhold a survey model from callers without exact-location access.
/// </para>
/// </summary>
public class SurveyShot
{
    public long Id { get; set; }

    /// <summary>The survey model this leg was read out of.</summary>
    public Guid SurveyModelId { get; set; }

    /// <summary>The name of the station the leg starts at, where the file resolves one.</summary>
    public string? FromStationName { get; set; }

    /// <summary>The name of the station the leg ends at, where the file resolves one.</summary>
    public string? ToStationName { get; set; }

    /// <summary>
    /// The owning survey, as the file labels it, or null where the format does not say.
    /// </summary>
    public string? SurveyName { get; set; }

    /// <summary>
    /// The leg itself: two points, longitude/latitude with altitude in metres.
    /// </summary>
    public required LineString Geom { get; set; }

    /// <summary>
    /// How long the leg is, in metres, measured in the coordinates the file was written in.
    ///
    /// <para>
    /// This is the surveyed number and it is kept because projecting the endpoints into
    /// longitude and latitude does not preserve it: a geodesic length computed back off
    /// <see cref="Geom"/> is an approximation of a figure the file already stated exactly.
    /// </para>
    /// </summary>
    public double LengthM { get; set; }

    /// <summary>What the file says about this leg, in this application's vocabulary.</summary>
    public SurveyShotFlags Flags { get; set; }

    /// <summary>The flag bits exactly as the file stored them, in the file's own numbering.</summary>
    public long RawFlags { get; set; }

    /// <summary>The leg is a wall shot rather than a leg of the traverse.</summary>
    public bool IsSplay => (Flags & SurveyShotFlags.Splay) != 0;

    /// <summary>The leg was surveyed above ground.</summary>
    public bool IsSurface => (Flags & SurveyShotFlags.Surface) != 0;

    /// <summary>The leg re-surveys passage that another leg already covers.</summary>
    public bool IsDuplicate => (Flags & SurveyShotFlags.Duplicate) != 0;
}
