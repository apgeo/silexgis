// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Audit-trail vocabulary of the feature world. A feature edit produces ONE audit row —
/// the supertype and subtype diffs are merged by the audit interceptor (they share the
/// id, and one id has exactly one kind, so property names cannot collide) — typed with
/// the kind-qualified name ("Feature:Cave", "Feature:CaveEntrance", …). Pointers that do
/// not know the kind (attachment/tagging/satellite audit roots) use the bare
/// <see cref="RootName"/>; readers match feature rows by id and the "Feature" prefix.
/// These strings are a stored contract — never rename.
/// </summary>
public static class FeatureAudit
{
    /// <summary>Kind-agnostic name used in root pointers and prefix matching.</summary>
    public const string RootName = nameof(Feature);

    public static string TypeName(FeatureKind kind) => $"{RootName}:{kind}";

    public static bool IsFeatureType(string entityType) =>
        entityType == RootName || entityType.StartsWith($"{RootName}:", StringComparison.Ordinal);
}
