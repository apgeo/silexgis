// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// The part of a trip that answers to a narrower audience than the trip itself.
/// </summary>
/// <remarks>
/// <para>
/// <em>That</em> something went wrong is told to everyone who may read the trip: a club counts
/// incidents, finds them and notices a run of them, and a fact nobody can count is a fact
/// nobody reviews. <em>What</em> went wrong is a different question with a different audience —
/// a near-miss account names an identifiable member's mistake — so the safety section is told
/// only to callers who may change the trip. Being allowed to read the trip is not enough;
/// being allowed to write it is the test, because that is the group already trusted with what
/// the trip says about the people on it.
/// </para>
/// <para>
/// One home, because a trip shows this section on two surfaces: the live record, and the change
/// history, where the same text would otherwise arrive as an old/new pair in a diff. A rule
/// stated twice is how one surface comes to disagree with the other, and a narrower audience is
/// only as narrow as its widest emitter.
/// </para>
/// <para>
/// Withheld as <c>null</c> rather than as an empty object, deliberately. A stored section is
/// always an object — a trip that says nothing says <c>{}</c> — so nothing at all can only mean
/// "not yours to read", and a surface drawing it can say so instead of showing an empty section
/// that reads as "nothing happened". That difference matters most on exactly the record where it
/// matters at all.
/// </para>
/// </remarks>
public static class TripDisclosure
{
    /// <summary>
    /// The safety section as this caller may have it: the stored object and the schema version
    /// it was measured against, or neither.
    /// </summary>
    /// <param name="trip">The trip being read.</param>
    /// <param name="mayWrite">Whether this caller may write <paramref name="trip"/>.</param>
    public static (string? Section, int? SchemaVersion) Safety(TripLog trip, bool mayWrite) =>
        mayWrite ? (trip.Safety, trip.SafetySchemaVersion) : (null, null);

    /// <summary>
    /// Properties of a trip that only a caller who may write it is told, for surfaces that work
    /// in property names rather than in the record itself. Named through <c>nameof</c> so
    /// renaming one is a compile error here rather than a silent disclosure.
    /// </summary>
    public static readonly string[] WriterOnly =
        [nameof(TripLog.Safety), nameof(TripLog.SafetySchemaVersion)];
}
