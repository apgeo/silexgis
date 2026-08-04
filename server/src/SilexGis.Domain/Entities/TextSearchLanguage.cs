// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Maps a document's language code onto the PostgreSQL text-search configuration that indexes
/// and parses text in that language.
/// <para>
/// This is a table rather than a switch in code so that teaching the archive Hungarian or German
/// is a row and a configuration, not a release: PostgreSQL ships around two dozen Snowball
/// configurations, and every one of them is reachable from here. A code with no row, or a row
/// naming a configuration this server does not have, falls back to the accent-folding
/// language-neutral configuration - degraded stemming rather than a failed index.
/// </para>
/// </summary>
public class TextSearchLanguage
{
    /// <summary>Primary language subtag, lower-case (see the document language rules).</summary>
    public required string Code { get; set; }

    /// <summary>
    /// Name of the PostgreSQL text-search configuration to use. The configurations this
    /// installation creates fold accents as part of parsing, so that a Romanian archive answers
    /// "pestera" with "peșteră" while snippets keep the diacritics the author wrote.
    /// </summary>
    public required string Configuration { get; set; }
}
