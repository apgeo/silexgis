// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Documents;

/// <summary>One shipped document kind: the code installations exchange it by, and the English
/// name a client without wording of its own falls back to.</summary>
public readonly record struct DocumentTypeSeed(string Code, string Name);

/// <summary>
/// The shipped document-kind vocabulary — the single home for which codes ship. The startup
/// seeder inserts these rows, and the write path consults the same list to refuse re-coding
/// one: the seeder matches rows by code, so a shipped row given another code would be inserted
/// again under its own on the next start, and the client's label for it, which is looked up by
/// code, would fall back to whatever name the row was given. Kinds a club adds are
/// installation-local and fully editable.
/// </summary>
public static class DocumentTypeSeeds
{
    /// <summary>
    /// Append only: a shipped row's sort order is its position here (see
    /// <see cref="ShippedVocabularyOrder"/>).
    /// </summary>
    public static readonly IReadOnlyList<DocumentTypeSeed> All =
    [
        new("survey_report", "Survey report"),
        new("trip_report", "Trip report"),
        new("map", "Map / plan"),
        new("photo", "Photograph"),
        new("permit", "Permit / authorization"),
        new("article", "Article / publication"),
        new("correspondence", "Correspondence"),
        new("other", "Other document"),
    ];

    private static readonly HashSet<string> Codes = [.. All.Select(s => s.Code)];

    /// <summary>Whether a code names a shipped kind (code immutable).</summary>
    public static bool IsSeeded(string code) => Codes.Contains(code);
}
