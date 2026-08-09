// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Documents;

/// <summary>
/// What a shelf says about whatever lands on it: the kind, the read audience, the tags and
/// the metadata it expects. One shelf's own settings, or — where it has none — the nearest
/// shelf above it that has.
/// </summary>
/// <param name="DocumentTypeId">The kind, or null when nothing up the chain names one.</param>
/// <param name="Visibility">The read audience, or null when nothing up the chain names one.</param>
/// <param name="TagIds">Tags to apply, deduplicated, nearest shelf first.</param>
/// <param name="RequiredMetadataKeys">
/// Keys a document filed here is expected to carry. Unlike the three above this
/// <em>accumulates</em> down the chain rather than being answered by the nearest shelf: a
/// requirement written on "Club archive" is a requirement of everything inside it, and a
/// sub-shelf adding one of its own is narrowing, not replacing.
/// </param>
public readonly record struct CabinetDefaults(
    long? DocumentTypeId,
    Visibility? Visibility,
    IReadOnlyList<long> TagIds,
    IReadOnlyList<string> RequiredMetadataKeys)
{
    /// <summary>A shelf that says nothing, and what an unfiled upload is measured against.</summary>
    public static CabinetDefaults None { get; } = new(null, null, [], []);
}

/// <summary>
/// How a shelf's settings and its ancestors' combine into the answer an upload actually gets.
///
/// <para>
/// Pure, and separate from the write path, because the rule is the interesting part and it is
/// asked in three places that must not drift: the upload that applies it, the dialog that
/// shows the uploader what is about to be applied, and the shelf editor that has to explain
/// which of the values on screen are this shelf's own and which are inherited.
/// </para>
/// </summary>
public static class CabinetDefaultRules
{
    /// <summary>
    /// The effective defaults for a shelf, given it and its ancestors in any order.
    /// </summary>
    /// <param name="cabinetId">The shelf being filed into.</param>
    /// <param name="chain">
    /// The shelf itself and every cabinet above it. Ancestry is read from each row's own
    /// ancestor array rather than by walking parents, so the caller can fetch the whole chain
    /// in one query and hand it over unordered.
    /// </param>
    /// <remarks>
    /// Nearest wins for the three single-valued settings, which is what makes a tree of
    /// defaults useful: "Club archive" says everything inside it is club-visible, and "Club
    /// archive / Private surveys" overrides that for its own corner without the outer shelf
    /// having to know it exists.
    /// </remarks>
    public static CabinetDefaults Resolve(Guid cabinetId, IReadOnlyCollection<Cabinet> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var self = chain.FirstOrDefault(c => c.Id == cabinetId);
        if (self is null)
        {
            return CabinetDefaults.None;
        }

        // The shelf itself first, then its ancestors innermost-first. The stored array is
        // root-first and includes the shelf, so reversing it and dropping the shelf gives the
        // walk outward — and a row whose array is empty (nothing has stamped it yet) still
        // answers with its own settings rather than throwing.
        var byId = chain.ToDictionary(c => c.Id);
        var walk = new List<Cabinet> { self };
        for (var i = self.AncestorIds.Length - 1; i >= 0; i--)
        {
            if (self.AncestorIds[i] != cabinetId && byId.TryGetValue(self.AncestorIds[i], out var ancestor))
            {
                walk.Add(ancestor);
            }
        }

        long? documentTypeId = null;
        Visibility? visibility = null;
        var tagIds = new List<long>();
        var seenTags = new HashSet<long>();
        var requiredKeys = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cabinet in walk)
        {
            documentTypeId ??= cabinet.DefaultDocumentTypeId;
            visibility ??= cabinet.DefaultVisibility;

            foreach (var tagId in cabinet.DefaultTagIds)
            {
                if (seenTags.Add(tagId))
                {
                    tagIds.Add(tagId);
                }
            }

            // Accumulated rather than answered by the nearest shelf: a requirement written
            // higher up governs everything below it, so a sub-shelf listing none of its own
            // does not thereby excuse its documents from the archive's.
            foreach (var key in cabinet.RequiredMetadataKeys)
            {
                if (seenKeys.Add(key))
                {
                    requiredKeys.Add(key);
                }
            }
        }

        return new CabinetDefaults(documentTypeId, visibility, tagIds, requiredKeys);
    }

    /// <summary>
    /// The expected keys a document's metadata does not answer. Empty means complete, which
    /// is also the answer for a shelf that expects nothing.
    /// </summary>
    /// <param name="presentKeys">
    /// The keys the document's metadata object actually holds. A key present but empty counts
    /// as missing: a required field left blank is exactly the case the checklist is for, and
    /// treating it as answered would make the mark useless.
    /// </param>
    public static IReadOnlyList<string> MissingMetadataKeys(
        IReadOnlyList<string> requiredKeys, IReadOnlyCollection<string> presentKeys)
    {
        ArgumentNullException.ThrowIfNull(requiredKeys);
        ArgumentNullException.ThrowIfNull(presentKeys);

        if (requiredKeys.Count == 0)
        {
            return [];
        }

        var present = new HashSet<string>(presentKeys, StringComparer.Ordinal);
        return [.. requiredKeys.Where(key => !present.Contains(key))];
    }
}
