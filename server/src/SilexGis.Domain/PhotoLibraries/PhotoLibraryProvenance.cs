// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;

namespace SilexGis.Domain.PhotoLibraries;

/// <summary>
/// What a registry object carries about the photograph its position came from.
///
/// <para>
/// A neighbouring photo library is a separate product with its own database and its own accounts,
/// and nothing of it is stored here — no picture, no title, no copy of its index. What is stored is
/// the answer to one question somebody will ask years from now: <em>where did this cave's position
/// come from?</em> Two values answer it — which library, and what that library called the
/// photograph — and they are enough to go back and look, for as long as the library keeps the
/// photograph.
/// </para>
/// <para>
/// Flat and prefixed, following what this application already does with a third-party source's own
/// identifiers: what a foreign system said stays distinguishable from what this one decided, and a
/// later integration cannot collide with these keys. Being flat is what makes them queryable —
/// "everything that came from that library" is one containment test on a jsonb column rather than a
/// join against a table nobody built.
/// </para>
/// <para>
/// This is a data-quality record and not a confidentiality one. It says where a position was read,
/// which is the same thing the position itself says; it does not widen who may see either.
/// </para>
/// </summary>
public static class PhotoLibraryProvenance
{
    /// <summary>The keys written into a feature's properties bag, and the whole of what is written.</summary>
    public static class Keys
    {
        /// <summary>
        /// Which library, named the way an address names it rather than by any identifier a
        /// refactoring tool may change — so the stored value still means something to a person
        /// reading it beside a URL long after this code has moved on.
        /// </summary>
        public const string Source = "photoLibrarySource";

        /// <summary>
        /// What that library called the photograph. Deliberately kept verbatim and never parsed:
        /// one product names a photograph and the picture of it with the same string and the other
        /// does not, and a value re-derived here would be a guess about somebody else's scheme.
        /// </summary>
        public const string Reference = "photoLibraryReference";
    }

    /// <summary>
    /// The properties bag a feature created from a photograph starts with.
    /// </summary>
    /// <param name="source">The library, as the address names it — <c>immich</c>, <c>photoprism</c>.</param>
    /// <param name="reference">The library's own name for the photograph.</param>
    public static JsonObject Properties(string source, string reference) => new()
    {
        [Keys.Source] = source,
        [Keys.Reference] = reference,
    };
}
