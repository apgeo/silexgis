// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>
/// Public "About" information. <see cref="SourceUrl"/> must point at the source code of the
/// running (possibly modified) instance — this is the AGPL §13 compliance knob (ADR-017):
/// operators of modified instances set SILEXGIS__About__SourceUrl to their fork.
/// </summary>
public sealed class AboutOptions
{
    public const string SectionName = "About";

    public string SourceUrl { get; set; } = "https://github.com/apgeo/silexgis";
}
