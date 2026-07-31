// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// One section of administrator-editable configuration, stored as a JSON document.
/// </summary>
/// <remarks>
/// A row exists only once an administrator has saved that section. Until then the values come
/// from the deployment's configuration, so an installation that is happy with environment
/// variables never grows a row it has to keep in step with them.
/// </remarks>
public class AppSetting : ITimestamped
{
    /// <summary>Section name — see <see cref="Settings.AppSettingSections"/>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The section serialised as JSON.</summary>
    public string Value { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
