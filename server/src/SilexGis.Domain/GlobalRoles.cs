// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>Global roles. Seeded at startup; referenced by name.</summary>
public static class GlobalRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Editor, Viewer];
}
