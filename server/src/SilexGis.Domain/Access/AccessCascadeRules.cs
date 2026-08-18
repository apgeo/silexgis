// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// The rules a grant obeys when it reaches from one thing to the many things that thing
/// gathers — sharing a camp reaching the trips inside it. The rules live here, away from
/// any endpoint, because both of them are security-bearing and neither is testable through
/// a route: one says an action that may never ride along is refused rather than quietly
/// dropped, and the other says what a refusal is allowed to disclose.
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
    /// What a cascade that cannot be applied in full says: how many of the gathered rows
    /// refused it, and never which.
    /// </summary>
    /// <remarks>
    /// All-or-nothing, and counted. Naming the rows would disclose them: a camp gathers trips
    /// its organiser may not be able to read at all, and a refusal that named one would tell
    /// them it exists — which is exactly what every other refusal in this application takes
    /// care not to do, answering "not found" rather than "forbidden" for a row the caller
    /// cannot see. A count says enough to act on (ask the owners, or share fewer trips)
    /// without saying anything the caller could not already learn.
    /// </remarks>
    public static string IncompleteDetail(int refusedCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(refusedCount, 1);
        return $"Nothing was shared — you do not hold enough on {refusedCount} of this camp's "
            + "trips to pass this on, and the refusal does not name them.";
    }
}
