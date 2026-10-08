// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// The rules a grant obeys when it reaches from one thing to the many things that thing
/// gathers — sharing a camp reaching the trips inside it. The rules live here, away from
/// any endpoint, because all of them are security-bearing and none is testable through
/// a route: one says an action that may never ride along is refused rather than quietly
/// dropped, and the others say what a refusal, and an answer that skipped some of the rows,
/// are allowed to disclose.
/// </summary>
public static class AccessCascadeRules
{
    public const string ExactLocationRefusedCode = "access_cascade.exact_location_refused";

    public const string IncompleteCode = "access_cascade.incomplete";

    /// <summary>
    /// Actions a cascaded grant may never carry, whatever the granter holds.
    /// </summary>
    /// <remarks>
    /// Exact view is inert on a trip rule anyway — it is only ever asked in the feature world,
    /// against the protected roots above a cave — so omitting it would look like enough. It is
    /// not: omission is a habit and this is a rule. What it guards against is a later writer
    /// widening the cascade to emit feature-world rules for the caves a camp visited, at which
    /// point the flag stops being inert and hands out coordinates that every other path in the
    /// application refuses to show. Refused where the request is read, so it never reaches the
    /// code that would have to remember to drop it.
    /// </remarks>
    public const AccessAction NeverCascaded = AccessAction.ViewExactLocation;

    /// <summary>True when a proposed action set carries something a cascade may never pass on.</summary>
    public static bool CarriesNeverCascaded(AccessAction actions) => (actions & NeverCascaded) != 0;

    /// <summary>What a request carrying <see cref="NeverCascaded"/> is refused with.</summary>
    public const string ExactLocationRefusedMessage =
        "Exact location is never passed on by sharing, whoever holds it.";

    /// <summary>
    /// What a cascade that reached none of the gathered rows says: how many refused it, and
    /// never which.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cascade is taken row by row: each gathered row answers for itself, the ones that accept
    /// are written and the ones that refuse are skipped. This refusal is what is left of the old
    /// all-or-nothing rule — the case in which every row refused, so there is nothing to write
    /// and nowhere for the grant to live. Answering that as a success would say something had
    /// been shared when nothing had.
    /// </para>
    /// <para>
    /// Counted, and built from the count alone. When every row refused, "which ones" is already
    /// answered by "all of them", so a list would add nothing a count does not — and the count
    /// is the one thing that is safe to say about rows the caller may not be able to read at
    /// all. A partial application is where naming earns its keep; see <see cref="Disclose"/>.
    /// </para>
    /// </remarks>
    public static string IncompleteDetail(int refusedCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(refusedCount, 1);
        var trips = refusedCount == 1 ? "the 1 trip" : $"any of the {refusedCount} trips";
        return $"Nothing was shared — you do not hold enough on {trips} this camp gathers to "
            + "pass this on, and the refusal does not name them.";
    }

    /// <summary>
    /// What may be said about the rows a cascade skipped: the ones the caller may read are
    /// handed back to be named, and the rest come back as a number and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A camp gathers trips its organiser may not be able to read. Telling them which of their
    /// own readable trips the sharing did not reach is something they can act on — ask that
    /// trip's owner — and discloses nothing, since they can already open it. Naming one they
    /// cannot read would tell them it exists and what it is called, which every other refusal in
    /// this application takes care not to do. So the decision to name is made here, from the
    /// caller's own right to read each row, and what was not named leaves as a count: there is
    /// no value in the answer through which such a row's identity could travel.
    /// </para>
    /// <para>
    /// The count is not withheld as well, because it is not new: how many trips a camp gathers,
    /// readable or not, is already on the camp's own page for whoever may administer it.
    /// </para>
    /// </remarks>
    /// <param name="skipped">Every skipped row, each with whether the caller may read it.</param>
    public static CascadeDisclosure<T> Disclose<T>(IEnumerable<(T Row, bool CallerMayRead)> skipped)
    {
        var named = new List<T>();
        var notNamed = 0;
        foreach (var (row, callerMayRead) in skipped)
        {
            if (callerMayRead)
            {
                named.Add(row);
            }
            else
            {
                notNamed++;
            }
        }

        return new CascadeDisclosure<T>(named, notNamed);
    }
}

/// <summary>Why one gathered row did not take a cascaded grant.</summary>
/// <remarks>
/// Said only about a row the caller may read. It is a fact about the caller's own rights on a
/// thing they can already open, so it tells them nothing new — and it decides what they have to
/// do about it, which differs: one is somebody else's consent, the other is asking for less.
/// </remarks>
public enum CascadeSkipReason
{
    /// <summary>The caller may not write rules on this row at all; whoever may has to let them.</summary>
    NotAdministered = 0,

    /// <summary>The caller may write rules here, but the grant carries more than they hold on it.</summary>
    BeyondHolding = 1,
}

/// <summary>The skipped rows a caller may be told about, and how many more there were.</summary>
/// <param name="Named">Rows the caller may read, and may therefore be shown.</param>
/// <param name="NotNamed">How many further rows were skipped. A number, never an identity.</param>
public sealed record CascadeDisclosure<T>(IReadOnlyList<T> Named, int NotNamed);
