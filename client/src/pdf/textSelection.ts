// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Turning "the reader dragged across these words" into something that still points at them
 * next year.
 *
 * Two texts are involved and they are never identical. One is what the browser laid out from
 * the file: runs of glyphs in the order the page draws them, with whatever spacing the layout
 * engine inserted between them. The other is what the server read out of the same file and put
 * in the search index — a different program, a different order in places, different whitespace
 * almost everywhere. A durable anchor has to be measured against the *server's* text, because
 * that is the one that survives being re-read, and it is the one every other part of the
 * system already works against.
 *
 * So the browser's job here is only to say *which words*, and the matching below finds those
 * words in the server's text. Whitespace is compared loosely, because the two programs
 * disagree about it constantly and nobody selecting a sentence means anything by the number of
 * spaces in it; everything else is compared exactly, because a match that is nearly right is
 * an anchor pointing at the wrong sentence, which is worse than no anchor at all.
 */

/** How much text on either side is kept to tell two identical quotes apart. */
export const CONTEXT_LENGTH = 32;

/** What a selection in the viewer says: the words, and enough of their surroundings to place them. */
export interface CapturedQuote {
  page: number;
  quote: string;
  prefix: string;
  suffix: string;
}

/** Where a quote was found in the extracted text. Offsets are into that text, end exclusive. */
export interface QuoteLocation {
  start: number;
  end: number;
}

/**
 * A durable text anchor, in the shape the server stores: the offsets are the fast path and the
 * quote is what outlives them.
 */
export interface TextRangeAnchor {
  page: number;
  start: number;
  end: number;
  quote: string;
  prefix?: string;
  suffix?: string;
}

/**
 * Collapses every run of whitespace to one space and trims the ends, keeping — for each
 * character of the result — the offset it came from in the input. The map is what lets a match
 * found in the collapsed form be reported as offsets into the original.
 */
function collapse(text: string): { value: string; offsets: number[] } {
  const chars: string[] = [];
  const offsets: number[] = [];
  let pendingSpace = false;

  for (let i = 0; i < text.length; i += 1) {
    const ch = text[i];
    if (/\s/.test(ch)) {
      pendingSpace = chars.length > 0;
      continue;
    }
    if (pendingSpace) {
      chars.push(' ');
      // A space stands for the run it replaced; it starts where that run started.
      offsets.push(i);
      pendingSpace = false;
    }
    chars.push(ch);
    offsets.push(i);
  }

  return { value: chars.join(''), offsets };
}

/** The offset just past the character the collapsed position `index` came from. */
function endOffset(offsets: number[], index: number, sourceLength: number): number {
  return index < offsets.length ? offsets[index] + 1 : sourceLength;
}

/**
 * Finds a quote in an extracted text stream and reports where it is.
 *
 * When the same words occur more than once, the context decides: the occurrence whose
 * surroundings agree best with what the reader had on screen wins. A quote that occurs once
 * needs no context at all, which is why context is optional — an anchor composed by something
 * that never captured any is still locatable.
 *
 * Returns null when the words are not there. That is a real answer, not a failure to try: it
 * happens when the page has not been read, when the reader selected something the extractor
 * never saw (a figure caption drawn as an image), or when the selection crossed a page break.
 * Storing an offset anyway would produce an anchor that points confidently at the wrong place.
 */
export function locateQuote(
  extractedText: string,
  quote: string,
  context?: { prefix?: string; suffix?: string },
): QuoteLocation | null {
  const needle = collapse(quote).value;
  if (needle.length === 0) {
    return null;
  }

  const haystack = collapse(extractedText);
  const prefix = collapse(context?.prefix ?? '').value;
  const suffix = collapse(context?.suffix ?? '').value;

  let best: { at: number; score: number } | null = null;
  for (let at = haystack.value.indexOf(needle); at !== -1; at = haystack.value.indexOf(needle, at + 1)) {
    // Longest common tail of what precedes, plus longest common head of what follows. An
    // occurrence with no context to compare scores zero and still wins if it is the only one.
    //
    // One character more than the context is taken, and the edge trimmed, because collapsing
    // drops whitespace at the very ends: a captured prefix of "right branch: " arrives here as
    // "right branch:", and comparing that against a window that still ends in a space would
    // find nothing in common and leave two identical quotes indistinguishable.
    const before = haystack.value.slice(Math.max(0, at - prefix.length - 1), at).trimEnd();
    const after = haystack.value
      .slice(at + needle.length, at + needle.length + suffix.length + 1)
      .trimStart();
    const score = commonSuffixLength(before, prefix) + commonPrefixLength(after, suffix);
    if (best === null || score > best.score) {
      best = { at, score };
    }
  }

  if (best === null) {
    return null;
  }

  return {
    start: haystack.offsets[best.at],
    end: endOffset(haystack.offsets, best.at + needle.length - 1, extractedText.length),
  };
}

function commonPrefixLength(a: string, b: string): number {
  let n = 0;
  while (n < a.length && n < b.length && a[n] === b[n]) {
    n += 1;
  }
  return n;
}

function commonSuffixLength(a: string, b: string): number {
  let n = 0;
  while (n < a.length && n < b.length && a[a.length - 1 - n] === b[b.length - 1 - n]) {
    n += 1;
  }
  return n;
}

/**
 * Composes the stored anchor from what the viewer captured and what the server holds, or null
 * when the two do not agree about the words. The caller shows that as a refusal; it never
 * falls back to storing the quote without offsets, because a payload the server would reject
 * is not an improvement on saying so.
 */
export function textRangeAnchorFor(
  captured: CapturedQuote,
  extractedText: string,
): TextRangeAnchor | null {
  const located = locateQuote(extractedText, captured.quote, captured);
  if (located === null) {
    return null;
  }

  return {
    page: captured.page,
    start: located.start,
    end: located.end,
    quote: captured.quote,
    ...(captured.prefix ? { prefix: captured.prefix } : {}),
    ...(captured.suffix ? { suffix: captured.suffix } : {}),
  };
}

/**
 * Reads the current selection out of a text layer: the words, and the text on either side of
 * them within the same layer.
 *
 * The context is taken from the layer's own text rather than from the extracted stream,
 * because at capture time the extracted stream is exactly what is not known yet — that is the
 * round trip this avoids making on every mouse-up. It is only ever used to choose between
 * repeated occurrences, so an approximation of the surroundings is worth as much as an exact
 * one.
 *
 * Returns null when nothing is selected, when the selection is empty once whitespace is
 * ignored, or when it reaches outside the layer — a drag that started on the page and ended on
 * the toolbar selects the toolbar too, and anchoring that would be nonsense.
 */
export function capturedQuoteFrom(
  selection: Selection | null,
  layer: HTMLElement,
  page: number,
): CapturedQuote | null {
  if (selection === null || selection.rangeCount === 0 || selection.isCollapsed) {
    return null;
  }

  const range = selection.getRangeAt(0);
  if (!layer.contains(range.commonAncestorContainer)) {
    return null;
  }

  const quote = range.toString();
  if (collapse(quote).value.length === 0) {
    return null;
  }

  const before = document.createRange();
  before.selectNodeContents(layer);
  before.setEnd(range.startContainer, range.startOffset);

  const after = document.createRange();
  after.selectNodeContents(layer);
  after.setStart(range.endContainer, range.endOffset);

  return {
    page,
    quote,
    prefix: before.toString().slice(-CONTEXT_LENGTH),
    suffix: after.toString().slice(0, CONTEXT_LENGTH),
  };
}
