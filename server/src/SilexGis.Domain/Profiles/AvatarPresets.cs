// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Profiles;

/// <summary>
/// The built-in avatars a user can pick instead of uploading one. The ids are the stored wire
/// values; the artwork ships with the client keyed by the same ids, so the set has one home.
/// </summary>
public static class AvatarPresets
{
    public static IReadOnlyList<string> Ids { get; } =
        ["bat", "helmet", "lamp", "rope", "karst", "stalactite", "compass", "sump"];

    public static bool IsKnown(string id) => Ids.Contains(id, StringComparer.Ordinal);
}
