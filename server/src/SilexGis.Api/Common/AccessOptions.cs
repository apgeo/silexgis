// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>Installation-wide access behavior (05-auth-permissions.md, 06-deployment.md §3).</summary>
public sealed class AccessOptions
{
    public const string SectionName = "Access";

    /// <summary>Grid size (meters) for obfuscating protected cave locations (05 §5).</summary>
    public double LocationGridMeters { get; set; } = 5000;
}
