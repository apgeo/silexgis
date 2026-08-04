// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { splitSnippet } from './searchSnippet.ts';

describe('splitSnippet', () => {
  it('separates the matched words from the words around them', () => {
    expect(splitSnippet('galeria din [[Peștera]] Urșilor')).toEqual([
      { text: 'galeria din ', matched: false },
      { text: 'Peștera', matched: true },
      { text: ' Urșilor', matched: false },
    ]);
  });

  it('keeps the diacritics the author wrote, whatever the search that found them looked like', () => {
    // The query that produced this hit was "pestera"; the quotation is the document's own text.
    const parts = splitSnippet('[[peșteră]] cu două intrări');
    expect(parts[0]).toEqual({ text: 'peșteră', matched: true });
  });

  it('marks several matches in one snippet, including one that opens it', () => {
    expect(splitSnippet('[[apa]] curge prin [[apa]]')).toEqual([
      { text: 'apa', matched: true },
      { text: ' curge prin ', matched: false },
      { text: 'apa', matched: true },
    ]);
  });

  it('treats an unclosed marker as ordinary text rather than dropping the rest of the quotation', () => {
    // A document may legitimately contain the marker; showing the quotation slightly wrong
    // beats showing nothing of it.
    expect(splitSnippet('cota [[500 m')).toEqual([{ text: 'cota [[500 m', matched: false }]);
  });

  it('returns nothing for an empty snippet', () => {
    expect(splitSnippet('')).toEqual([]);
  });
});
