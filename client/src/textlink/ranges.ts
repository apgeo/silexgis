// SPDX-License-Identifier: AGPL-3.0-or-later
import type { AnnotatedBlock, AnnotatedMarkKind } from './blocks.ts';

/**
 * Cutting a block's text into the pieces it has to be drawn in.
 *
 * A block is one string, but what covers it is not one thing: any number of links may mark
 * passages inside it, those passages may overlap each other, and emphasis runs across the same
 * characters independently of all of them. Drawing that means finding the stretches over which
 * *everything* is constant — the same set of links, the same emphasis — and drawing each as one
 * element.
 *
 * The naive alternative, nesting one span per link inside another, is what makes overlapping
 * annotations impossible: two passages that overlap without either containing the other cannot
 * both be an element in a tree, so an implementation that tries ends up silently dropping one of
 * them, or splitting one and leaving the reader unable to tell that the two halves are the same
 * link. Flattening to a partition has no such case — every piece names all of its links, and a
 * link that was split is still one link on the pieces that carry it.
 *
 * Everything here is offsets and strings. It does not know what a link is, only that things have
 * ranges, which is what lets the same code lay out a PDF's text layer when that arrives.
 */

/** Anything with a span in the document's character stream. */
export interface GlobalRange {
  /** Offset into the canonical stream. */
  start: number;
  /** Exclusive end of the same. */
  end: number;
}

/** One stretch of a block over which the set of links and the emphasis are both constant. */
export interface TextPiece<T> {
  /** The characters themselves. */
  text: string;
  /** Where this piece begins in the canonical stream. */
  start: number;
  /** Everything covering this piece, in the order it was given. Empty for plain text. */
  covering: T[];
  /** Emphasis over this piece. */
  marks: AnnotatedMarkKind[];
}

/**
 * Cuts one block into pieces.
 *
 * @param block the block being drawn
 * @param blockStart where it begins in the canonical stream
 * @param ranged everything with a range that might touch it — the whole document's worth is
 *   fine; what does not overlap is skipped
 * @param rangeOf how to read a range off one of them
 */
export function sliceBlock<T>(
  block: AnnotatedBlock,
  blockStart: number,
  ranged: readonly T[],
  rangeOf: (item: T) => GlobalRange,
): TextPiece<T>[] {
  const blockEnd = blockStart + block.text.length;

  // Everything is reduced to block-local offsets here and stays that way until the pieces are
  // built. Mixing the two coordinate systems is the mistake this ordering exists to prevent.
  const overlapping: { item: T; from: number; to: number }[] = [];
  for (const item of ranged) {
    const range = rangeOf(item);
    const from = Math.max(range.start, blockStart) - blockStart;
    const to = Math.min(range.end, blockEnd) - blockStart;
    if (to > from) {
      overlapping.push({ item, from, to });
    }
  }

  const marks = (block.marks ?? []).filter(
    (mark) => mark.end > mark.start && mark.start < block.text.length && mark.end > 0,
  );

  if (overlapping.length === 0 && marks.length === 0) {
    return block.text.length === 0
      ? []
      : [{ text: block.text, start: blockStart, covering: [], marks: [] }];
  }

  // Every edge of every range is a place the drawing might have to change. Sorting the distinct
  // ones gives the coarsest partition in which nothing changes inside a piece.
  const edges = new Set<number>([0, block.text.length]);
  for (const { from, to } of overlapping) {
    edges.add(from);
    edges.add(to);
  }

  for (const mark of marks) {
    edges.add(Math.max(0, mark.start));
    edges.add(Math.min(block.text.length, mark.end));
  }

  const boundaries = [...edges].sort((a, b) => a - b);

  const pieces: TextPiece<T>[] = [];
  for (let i = 0; i < boundaries.length - 1; i++) {
    const from = boundaries[i];
    const to = boundaries[i + 1];
    if (to <= from) {
      continue;
    }

    pieces.push({
      text: block.text.slice(from, to),
      start: blockStart + from,
      // Containment is tested at the piece's start alone. A piece lies entirely inside or
      // entirely outside every range by construction — that is what the boundaries above are —
      // so one point decides, and testing an interval against an interval here would be a
      // second, weaker statement of the same fact.
      covering: overlapping.filter((o) => o.from <= from && o.to > from).map((o) => o.item),
      marks: marks.filter((m) => m.start <= from && m.end > from).map((m) => m.kind),
    });
  }

  return pieces;
}

/**
 * Whether two ranges share any characters. Touching end-to-end is not overlapping: `end` is
 * exclusive, so a range ending where another begins covers nothing in common with it.
 */
export function rangesOverlap(a: GlobalRange, b: GlobalRange): boolean {
  return a.start < b.end && b.start < a.end;
}

/**
 * How much context to record on each side of a passage when a link is authored, so that the
 * passage can be told from an identical one elsewhere after the text is edited. Must match what
 * the server compares.
 */
export const CONTEXT_LENGTH = 32;

/** A text-range anchor payload, as the server stores and reads it. */
export interface TextRangeAnchorPayload {
  start: number;
  end: number;
  quote: string;
  prefix?: string;
  suffix?: string;
}

/**
 * The anchor for a passage of the canonical stream.
 *
 * The context on either side is recorded at authoring time and never afterwards: it is a
 * statement about what the text looked like when somebody decided these words meant something,
 * and it is what lets the passage be found again once an edit above it has moved every offset in
 * the document. Recomputing it later against the current text would make it agree with whatever
 * the text has become, which is exactly the evidence it is supposed to preserve.
 */
export function anchorFor(stream: string, range: GlobalRange): TextRangeAnchorPayload {
  const start = Math.max(0, Math.min(range.start, stream.length));
  const end = Math.max(start, Math.min(range.end, stream.length));
  const anchor: TextRangeAnchorPayload = {
    start,
    end,
    quote: stream.slice(start, end),
  };

  const prefix = stream.slice(Math.max(0, start - CONTEXT_LENGTH), start);
  if (prefix.length > 0) {
    anchor.prefix = prefix;
  }

  const suffix = stream.slice(end, Math.min(stream.length, end + CONTEXT_LENGTH));
  if (suffix.length > 0) {
    anchor.suffix = suffix;
  }

  return anchor;
}

/**
 * Where an anchor's passage is in the stream on screen, or null when it is not there.
 *
 * The offsets are believed only when the words they name are still the anchor's words. That
 * check is the whole point: an anchor whose document has been edited underneath it still holds
 * offsets that resolve, still lands inside the text, and names the wrong sentence — and a
 * reader has no way to tell that from a right one. When they disagree the quote is searched for
 * instead, and when the quote is not there either, the passage is genuinely gone and nothing is
 * drawn, which is the honest outcome.
 *
 * The server re-measures anchors when it rewrites a body, so this normally agrees with the
 * offsets immediately. It matters for the case the server cannot reach: a link authored against
 * a superseded revision, read against the current one.
 */
export function locateAnchor(stream: string, anchor: TextRangeAnchorPayload): GlobalRange | null {
  const { start, end, quote } = anchor;
  if (quote.length === 0) {
    return null;
  }

  if (start >= 0 && end === start + quote.length && stream.slice(start, end) === quote) {
    return { start, end };
  }

  let best = -1;
  let bestScore = Number.NEGATIVE_INFINITY;
  for (let at = stream.indexOf(quote); at >= 0; at = stream.indexOf(quote, at + 1)) {
    const score = contextScore(stream, anchor, at);
    if (score > bestScore) {
      bestScore = score;
      best = at;
    }
  }

  return best < 0 ? null : { start: best, end: best + quote.length };
}

/**
 * How well one occurrence matches the context recorded around the passage. Agreement dominates
 * — it is evidence about this passage — and distance from the recorded position only separates
 * occurrences the context could not. Mirrors the server's scoring, so the two agree about which
 * of two identical sentences was meant.
 */
function contextScore(stream: string, anchor: TextRangeAnchorPayload, at: number): number {
  let score = 0;
  if (anchor.prefix) {
    score += commonSuffixLength(stream.slice(0, at), anchor.prefix);
  }

  if (anchor.suffix) {
    score += commonPrefixLength(stream.slice(at + anchor.quote.length), anchor.suffix);
  }

  const scale = CONTEXT_LENGTH * 4;
  return score * scale - Math.min(Math.abs(at - anchor.start), scale - 1);
}

function commonSuffixLength(before: string, prefix: string): number {
  let n = 0;
  while (n < before.length && n < prefix.length && before[before.length - 1 - n] === prefix[prefix.length - 1 - n]) {
    n++;
  }

  return n;
}

function commonPrefixLength(after: string, suffix: string): number {
  let n = 0;
  while (n < after.length && n < suffix.length && after[n] === suffix[n]) {
    n++;
  }

  return n;
}
