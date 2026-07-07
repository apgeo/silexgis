// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>Installation-wide access behavior.</summary>
public sealed class AccessOptions
{
    public const string SectionName = "Access";

    /// <summary>Grid size (meters) for obfuscating protected cave locations.</summary>
    public double LocationGridMeters { get; set; } = 5000;
}
