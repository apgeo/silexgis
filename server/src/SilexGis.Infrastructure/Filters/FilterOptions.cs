// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Filters;

namespace SilexGis.Infrastructure.Filters;

/// <summary>
/// The installation's settings for the filter model.
/// </summary>
public sealed class FilterOptions
{
    public const string SectionName = "Filters";

    /// <summary>
    /// The largest radius a "within" condition may ask about, in metres.
    /// </summary>
    /// <remarks>
    /// A cost guard, not a protection: every row is measured on the position its reader is shown,
    /// whatever the radius. Raising it widens the box the database prefilters on, so a wide question
    /// visits more rows and answers more slowly. Values below one metre or above
    /// <see cref="SpatialConditions.LargestMaxRadiusMeters"/> are refused when the application starts.
    /// </remarks>
    public double MaxRadiusMeters { get; set; } = SpatialConditions.DefaultMaxRadiusMeters;
}
