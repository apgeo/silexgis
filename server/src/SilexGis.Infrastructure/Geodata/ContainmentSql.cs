// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Infrastructure.Geodata;

/// <summary>
/// What "inside this feature" means when the question is about declared containment rather than
/// about geometry, in one place so no statement spells it itself.
///
/// <para>
/// <b>Declared, not geometric.</b> These fragments read the stored ancestry closure: a feature is
/// under an area because somebody said so, not because its point happens to fall within an
/// outline. The two answer different questions and the difference is visible to a reader — a cave
/// inside the outline that nobody filed under the area is deliberately not counted here, and there
/// is a separate figure for exactly that.
/// </para>
/// <para>
/// <b>The spelling is load-bearing and invisible.</b> Written as array containment with the column
/// on the left — <c>ancestor_ids @&gt; ARRAY[id]</c> — the GIN index on that column serves it.
/// Written as <c>id = ANY(ancestor_ids)</c> it reads identically, returns identical rows, and
/// plans nothing like it: a scalar compared against ANY of an array is not rewritten into an
/// indexable form, so every such statement becomes a sequential scan of every feature in the
/// installation. Nothing about the result says which form was used, which is why the rule has one
/// home and a test that plans both spellings and requires the index to appear for this one and not
/// the other.
/// </para>
/// </summary>
public static class ContainmentSql
{
    /// <summary>
    /// Under this feature by declaration — the indexable containment test on its own.
    /// </summary>
    /// <param name="parameterName">
    /// Name of the query parameter holding the ancestor's id, without its marker.
    /// </param>
    /// <param name="alias">Table alias the ancestry column is read from.</param>
    public static string Under(string parameterName, string alias = "f") =>
        $"{alias}.ancestor_ids @> ARRAY[@{parameterName}]::uuid[]";

    /// <summary>
    /// Under this feature, and not the feature itself.
    ///
    /// <para>
    /// The closure holds a row for every feature as its own ancestor, so the self-exclusion is not
    /// tidying: without it an area counts as one of the things inside it — one extra cave in every
    /// total, and an area's own outline measured among the depressions it contains.
    /// </para>
    /// </summary>
    public static string StrictlyUnder(string parameterName, string alias = "f") =>
        $"{Under(parameterName, alias)}\n          AND {alias}.id <> @{parameterName}";
}
