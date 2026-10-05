// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>A stored layout as the choice below sees it: which purpose it is bound to, and whether it is the installation's chosen one.</summary>
public readonly record struct ReportTemplateCandidate(Guid Id, long? TripTypeId, bool IsDefault);

/// <summary>
/// Which stored layout a write-up is built in when nobody named one.
/// </summary>
/// <remarks>
/// A trip purpose may carry its own layout — a survey trip's write-up has different lines from a
/// training day's — and that layout wins for a trip of that purpose over the layout the
/// installation chose for everything; a trip whose purpose carries none gets the installation's
/// choice; and with neither, the caller falls back to the layout the product ships. The rule is
/// written here, over rows already read, so that the one place that reads layouts for a trip and
/// for a camp applies it rather than each surface growing its own reading of it.
/// </remarks>
public static class ReportTemplateChoice
{
    /// <summary>
    /// The layout to build in, out of <paramref name="candidates"/>, for a trip of
    /// <paramref name="tripTypeId"/> (null for a camp, or a trip recorded under no purpose); null
    /// when nothing stored applies and the shipped layout is the answer.
    /// </summary>
    public static Guid? Pick(IEnumerable<ReportTemplateCandidate> candidates, long? tripTypeId)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        Guid? own = null;
        Guid? installation = null;
        foreach (var candidate in candidates)
        {
            if (tripTypeId is { } purpose && candidate.TripTypeId == purpose)
            {
                own ??= candidate.Id;
            }
            else if (candidate.IsDefault)
            {
                installation ??= candidate.Id;
            }
        }

        return own ?? installation;
    }
}
