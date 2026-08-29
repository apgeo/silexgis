// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// One straight piece of surveyed passage, measured. This is the substrate every cave survey
/// statistic reads — orientation, dip, hypsometry, topology and the per-cave indices all reduce
/// to a set of these rows.
///
/// <para>
/// There is exactly one row shape and there are two producers of it
/// (<see cref="SurveySegmentSql"/> over parsed survey legs, <see cref="CenterlineSegmentSql"/>
/// over a reduced centerline). That is deliberate: two shapes would let a future statistic be
/// computed one way here and another way there, and the two answers would differ by a few
/// percent with nothing in either to say why.
/// </para>
///
/// <para>
/// <b>Lengths.</b> <see cref="PlanLengthM"/> is the map distance between the ends, measured on
/// the spheroid by the database, so it is in metres without any projected system having been
/// chosen. <see cref="SlopeLengthM"/> is the real length along the passage, derived from the
/// plan length and the altitude difference. Both producers compute them the same way, so the two
/// substrates are comparable; <see cref="FileLengthM"/> carries the surveyor's own stated length
/// where there is one, which is the more exact figure but exists for only one of the two.
/// </para>
///
/// <para>
/// <b>Altitudes may be absent.</b> A centerline uploaded as a plan drawing is legal and carries
/// no third coordinate at all. When <see cref="HasZ"/> is false the four vertical fields are
/// null rather than zero — a flat cave and a cave with no recorded altitudes are different
/// answers, and reporting the second as the first is confident nonsense.
/// </para>
/// </summary>
/// <param name="Basis">Which body of line work this segment came out of. Never
/// <see cref="SurveySegmentBasis.Unavailable"/> on a row.</param>
/// <param name="SurveyModelId">The parsed survey file this leg came out of; null for a segment cut
/// out of a reduced centerline. A cave may hold several uploaded survey files — a corrected
/// re-export is a new upload rather than a replacement — and exactly one of them answers for the
/// cave, so this names which one did. Without it, an answer measured over one upload and an answer
/// measured over another are indistinguishable.</param>
/// <param name="ShotId">The survey leg this is, where the segment is a leg; null for a segment
/// cut out of a reduced centerline, which has no identity of its own.</param>
/// <param name="FromStationName">Station the leg starts at, as the file named it; null for a
/// centerline segment, and null for a leg whose endpoint the file did not resolve.</param>
/// <param name="ToStationName">Station the leg ends at; see <paramref name="FromStationName"/>.</param>
/// <param name="PathIndex">Which polyline of the reduced centerline this segment belongs to.
/// Null for a survey leg: legs form a network, not an ordered path, and a consumer that wants
/// paths reconstructs them from the station names.</param>
/// <param name="SegmentIndex">Position within <paramref name="PathIndex"/> for a centerline
/// segment; for a survey leg, its position in the model's leg order, which orders the rows but
/// means nothing more than that.</param>
/// <param name="IsDuplicate">The file marked this leg as passage already surveyed on another
/// trip. The row is kept because the passage is real and the topology needs it — but it must
/// never be added into a length sum, or the passage is counted twice. See
/// <see cref="SurveySegments.SlopeLengthSumM"/>.</param>
/// <param name="HasZ">Whether the line work this came from carried altitudes at all.</param>
/// <param name="PlanLengthM">Map distance between the ends, metres on the spheroid.</param>
/// <param name="FileLengthM">The length the survey file itself stated for this leg, metres;
/// null for a centerline segment. Kept because projecting the ends into longitude and latitude
/// does not preserve it, so it is the more exact of the two figures where it exists.</param>
/// <param name="AzimuthDegrees">True bearing from start to end, degrees clockwise from north,
/// measured on the spheroid. Null when the two ends coincide, which a survey export does contain
/// (a station written twice). Note this is a <i>direction</i>: passage trend is axial, and
/// folding it is the consumer's job.</param>
/// <param name="DeltaZM">Altitude of the end minus altitude of the start, metres. Null when
/// <see cref="HasZ"/> is false.</param>
/// <param name="SlopeLengthM">Real length along the passage, metres. Null when
/// <see cref="HasZ"/> is false — with no altitudes there is no slope length, only a plan
/// length.</param>
/// <param name="DipDegrees">Inclination from start to end, degrees, positive upwards, in
/// −90..90. Null when <see cref="HasZ"/> is false or the segment has no extent at all.</param>
/// <param name="MidZM">Altitude of the segment's midpoint, metres — what an elevation histogram
/// bins on. Null when <see cref="HasZ"/> is false.</param>
public sealed record SurveySegmentRow(
    SurveySegmentBasis Basis,
    Guid? SurveyModelId,
    long? ShotId,
    string? FromStationName,
    string? ToStationName,
    int? PathIndex,
    int SegmentIndex,
    bool IsDuplicate,
    bool HasZ,
    double FromLongitude,
    double FromLatitude,
    double ToLongitude,
    double ToLatitude,
    double PlanLengthM,
    double? FileLengthM,
    double? AzimuthDegrees,
    double? DeltaZM,
    double? SlopeLengthM,
    double? DipDegrees,
    double? MidZM);

/// <summary>
/// The set of segments a cave answered with, and which body of line work answered.
/// <see cref="Basis"/> is repeated here rather than read off the first row so that an empty
/// answer still says something: no segments and <see cref="SurveySegmentBasis.Unavailable"/>
/// means the cave has no line work to measure, which is not the same as a cave whose passage
/// happens to be short.
/// </summary>
public sealed record SurveySegmentSet(SurveySegmentBasis Basis, IReadOnlyList<SurveySegmentRow> Segments)
{
    /// <summary>The answer for a cave with nothing to measure.</summary>
    public static SurveySegmentSet Empty { get; } = new(SurveySegmentBasis.Unavailable, []);

    /// <summary>
    /// Whether the line work carried altitudes. False for an empty set, and false when the
    /// source was a plan-only drawing — the vertical statistics are refused in both cases.
    /// </summary>
    public bool HasZ => Segments.Count > 0 && Segments[0].HasZ;

    /// <summary>
    /// Which uploaded survey file answered, when one did; null for the reduction and for an empty
    /// set. A cave may hold several, only one of them measures it, and which one is not a detail:
    /// two answers about one cave mean the same thing only if this is the same in both.
    /// </summary>
    public Guid? SurveyModelId => Segments.Count > 0 ? Segments[0].SurveyModelId : null;
}

/// <summary>
/// The arithmetic over a set of segments that must not be spelled twice, because spelling it
/// twice is how the same cave gets two lengths.
/// </summary>
public static class SurveySegments
{
    /// <summary>
    /// Total real length of passage, metres — duplicated legs excluded, because a leg the file
    /// marks as already surveyed is the same passage walked again and adding it counts the
    /// passage twice.
    /// </summary>
    /// <remarks>
    /// Falls back to the plan length for a segment with no altitudes, so a plan-only centerline
    /// still reports the length it can honestly claim rather than nothing.
    /// </remarks>
    public static double SlopeLengthSumM(IEnumerable<SurveySegmentRow> segments) =>
        segments.Where(s => !s.IsDuplicate).Sum(s => s.SlopeLengthM ?? s.PlanLengthM);

    /// <summary>
    /// Total plan length of passage, metres — the same rule about duplicates applies.
    /// </summary>
    public static double PlanLengthSumM(IEnumerable<SurveySegmentRow> segments) =>
        segments.Where(s => !s.IsDuplicate).Sum(s => s.PlanLengthM);

    /// <summary>
    /// The measured rows as the index arithmetic wants them. A straight translation — every field
    /// is carried over unchanged, and no measurement is made here — so that the arithmetic can live
    /// where it has no database under it and there is still only one place that decides what a
    /// measured piece of passage is.
    /// </summary>
    public static IReadOnlyList<PassageSegment> AsPassage(IEnumerable<SurveySegmentRow> segments) =>
    [
        .. segments.Select(s => new PassageSegment(
            s.PathIndex,
            s.SegmentIndex,
            s.IsDuplicate,
            s.FromLongitude,
            s.FromLatitude,
            s.ToLongitude,
            s.ToLatitude,
            s.PlanLengthM,
            s.SlopeLengthM,
            s.DeltaZM,
            s.MidZM)),
    ];
}
