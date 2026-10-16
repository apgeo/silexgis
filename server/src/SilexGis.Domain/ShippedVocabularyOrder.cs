// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// The display order a shipped vocabulary row is seeded with: its position in its seed list.
/// </summary>
/// <remarks>
/// The startup seeder inserts a missing shipped row with this order and never re-sorts a row that
/// already exists, because an administrator may have reordered the vocabulary since. So a code
/// inserted in the middle of a seed list gives a fresh installation a different order from an
/// upgraded one, silently and permanently. Seed lists are therefore append-only, and the domain
/// tests pin every list's codes in order so that a middle insert, a removal or a reorder fails
/// the fast gate instead of reaching an installation.
/// </remarks>
public static class ShippedVocabularyOrder
{
    public static int Of(int position) => (position + 1) * 10;
}
