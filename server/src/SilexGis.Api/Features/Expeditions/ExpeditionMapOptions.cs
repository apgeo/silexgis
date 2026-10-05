// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Expeditions;

/// <summary>The one size a camp's map answer is bounded by.</summary>
public sealed class ExpeditionMapOptions
{
    public const string SectionName = "ExpeditionMap";

    /// <summary>
    /// Safety cap on entrance points in one answer. Ordered by id before the cap bites, so a camp
    /// over the cap draws the same points every time it is opened rather than an arbitrary subset
    /// that changes under the reader; past it the answer says it was cut short, with a yes or no
    /// and never a count. A setting so an installation can move it and a test can lower it.
    /// </summary>
    public int MaxPoints { get; set; } = 2000;
}
