// SPDX-License-Identifier: AGPL-3.0-or-later

/** One run of a snippet, and whether the search is what put it there. */
export interface SnippetPart {
  text: string;
  matched: boolean;
}

const START = '[[';
const STOP = ']]';

/**
 * Splits a content-search snippet into the stretches that matched and the stretches around
 * them.
 *
 * The server wraps matched words in `[[`…`]]` rather than in markup, so the snippet travels
 * as data: nothing between the database and this function is tempted to treat a document's
 * own words as something to render. Turning the markers into emphasis is a decision made
 * here, once, where the result is already going through React's escaping.
 *
 * Unbalanced markers are treated as ordinary text rather than as an error. A document may
 * legitimately contain `[[`, and a snippet is a quotation — showing it slightly wrong is
 * better than showing nothing.
 */
export function splitSnippet(snippet: string): SnippetPart[] {
  const parts: SnippetPart[] = [];
  let cursor = 0;

  while (cursor < snippet.length) {
    const start = snippet.indexOf(START, cursor);
    if (start === -1) {
      break;
    }
    const stop = snippet.indexOf(STOP, start + START.length);
    if (stop === -1) {
      break;
    }

    if (start > cursor) {
      parts.push({ text: snippet.slice(cursor, start), matched: false });
    }
    parts.push({ text: snippet.slice(start + START.length, stop), matched: true });
    cursor = stop + STOP.length;
  }

  if (cursor < snippet.length) {
    parts.push({ text: snippet.slice(cursor), matched: false });
  }
  return parts;
}
