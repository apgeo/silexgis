// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// The SQL fragments for working in metres, in one place so no query spells the working system's
/// code itself.
///
/// <para>
/// The rule this exists to enforce: a query that names an EPSG code inline is a query that keeps
/// naming it after an installation changes the setting, and the failure is silent — the numbers
/// stay plausible and are measured in the wrong place.
/// </para>
///
/// <para>
/// <b>Why there is no index on the projected geometry, which is the obvious thing to want.</b>
/// A spatial index over a transform would have to be an expression index, and an expression index
/// is created by a migration, which can only contain a literal. The working system is
/// configuration. So the index would be built for whatever code was current the day the migration
/// was written, and an installation that later set a different one would get an index that no
/// longer matches any query — silently, since the answers stay correct and only the speed
/// collapses. An index that is right only until somebody uses a documented setting is worse than
/// none, because nothing reports it.
/// </para>
/// <para>
/// So the shape every metric query takes instead is: <b>narrow in 4326 first, where the stored
/// GIST index applies, then transform only what survived.</b> A bounding-box overlap or a
/// <c>ST_DWithin</c> on the geography type answers in metres without leaving the stored system and
/// uses the index that already exists; the projected transform then runs over a handful of rows
/// rather than the table. The map's own centerline query already reads this way, so this is the
/// established shape here and not a new one.
/// </para>
/// </summary>
public static class SpatialSql
{
    /// <summary>
    /// A geometry expression transformed into the installation's working system.
    ///
    /// <para>
    /// Apply this <i>after</i> a filter that has already narrowed the rows in 4326 — see the note
    /// on this class. The SRID is passed as a parameter rather than interpolated so a caller
    /// cannot accidentally build the string from user input.
    /// </para>
    /// </summary>
    /// <param name="geometryExpression">The column or expression holding a 4326 geometry.</param>
    /// <param name="sridParameter">
    /// Name of the query parameter carrying the working SRID, without the marker.
    /// </param>
    public static string ToWorking(string geometryExpression, string sridParameter = "workingSrid") =>
        $"ST_Transform({geometryExpression}, @{sridParameter})";

    /// <summary>
    /// A metre-accurate "within this many metres" test that stays in the stored system, so the
    /// existing spatial index answers it.
    ///
    /// <para>
    /// This is the pre-filter the class note describes: it is what makes the transform cheap, and
    /// for a plain distance question it is often the whole answer, since the geography type
    /// measures on the spheroid without any projected system being chosen at all.
    /// </para>
    /// </summary>
    public static string WithinMetres(string left, string right, string metresParameter) =>
        $"ST_DWithin({left}::geography, {right}::geography, @{metresParameter})";

    /// <summary>
    /// A test for line work that carries real altitudes, as opposed to line work that merely has
    /// room for them.
    ///
    /// <para>
    /// A survey uploaded as a plan drawing has no third coordinate at all, and the upload path
    /// gives it one anyway — writing zero where the file said nothing — so that the result can be
    /// stored, indexed and drawn like every other shape. The consequence is that asking the
    /// database how many dimensions a geometry has answers "three" for a drawing that recorded no
    /// depth whatever. Anything vertical computed from such a shape is not a small error, it is a
    /// flat cave invented out of the storage format. So the honest test is whether any altitude is
    /// actually something, and the reduction that turns stored line work into measurable segments
    /// applies exactly this test to each coordinate it reads; this is that same rule where a query
    /// needs it, not a second and looser one.
    /// </para>
    /// </summary>
    public static string HasAltitudes(string geometryExpression) =>
        $"(ST_NDims({geometryExpression}) = 3 "
        + $"AND (ST_ZMin({geometryExpression}) <> 0 OR ST_ZMax({geometryExpression}) <> 0))";
}
