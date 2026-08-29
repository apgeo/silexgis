// SPDX-License-Identifier: AGPL-3.0-or-later
import type { GlobalRange } from './ranges.ts';

/**
 * Turning what somebody dragged across on screen into a pair of offsets into the document's
 * character stream.
 *
 * The rendered text is cut into pieces — see `ranges.ts` — and each is drawn as an element
 * stamped with where it begins in the stream. So the drawing carries its own coordinates, and
 * reading a selection is a matter of asking which pieces it touched rather than of counting
 * characters through a DOM whose shape depends on how the highlights happened to overlap.
 *
 * <b>The selection's own endpoints are not trusted to be inside anything.</b> A browser will
 * happily report a selection anchored on a paragraph element, on the container, or on a node
 * that carries no offset at all: dragging past the end of the last line, selecting with the
 * keyboard, a triple-click, and every one of them is an ordinary thing for a reader to do.
 * Deriving the answer from the elements the selection *intersects*, and using its endpoints only
 * to sharpen the two ends, is what makes those cases produce the passage the reader saw
 * highlighted rather than nothing at all.
 */

/** The attribute a rendered piece carries: where its first character sits in the stream. */
export const PIECE_OFFSET_ATTRIBUTE = 'data-tl-at';

/** Whether an element is a rendered piece of the stream. */
function pieceStart(element: Element): number | null {
  const raw = element.getAttribute(PIECE_OFFSET_ATTRIBUTE);
  if (raw === null) {
    return null;
  }

  const value = Number(raw);
  return Number.isInteger(value) && value >= 0 ? value : null;
}

/**
 * How many characters of `piece` lie before a DOM point inside it.
 *
 * A piece may hold nested elements — emphasis is drawn inside one — so this walks its text
 * nodes rather than assuming a single one.
 */
function charactersBefore(piece: Element, node: Node, offset: number): number {
  if (node === piece) {
    // The point is between children, and `offset` counts children rather than characters.
    let before = 0;
    for (let i = 0; i < offset && i < piece.childNodes.length; i++) {
      before += piece.childNodes[i].textContent?.length ?? 0;
    }

    return before;
  }

  const walker = document.createTreeWalker(piece, NodeFilter.SHOW_TEXT);
  let before = 0;
  for (let text = walker.nextNode(); text !== null; text = walker.nextNode()) {
    if (text === node) {
      return before + offset;
    }

    before += text.textContent?.length ?? 0;
  }

  // The point named a node this piece does not contain; the piece's whole length is the best
  // available answer and is never wrong by more than the piece.
  return piece.textContent?.length ?? 0;
}

/** Whether a DOM point lies inside an element. */
function contains(element: Element, node: Node): boolean {
  return element === node || element.contains(node);
}

/**
 * The stream range a DOM range covers, or null when it covers no rendered text.
 *
 * Collapsed selections return null: a caret is not a passage, and treating one as an empty range
 * would let a stray click create a link anchored to nothing.
 */
export function rangeToStreamRange(root: Element, range: Range): GlobalRange | null {
  if (range.collapsed) {
    return null;
  }

  const touched: { element: Element; start: number }[] = [];
  root.querySelectorAll(`[${PIECE_OFFSET_ATTRIBUTE}]`).forEach((element) => {
    const start = pieceStart(element);
    if (start !== null && range.intersectsNode(element)) {
      touched.push({ element, start });
    }
  });

  if (touched.length === 0) {
    return null;
  }

  // Document order is not guaranteed by anything here — the pieces come back in it today, but
  // the answer must not depend on that — and a range's two ends are not guaranteed to be the
  // lowest and highest offsets either once a selection has been dragged backwards.
  touched.sort((a, b) => a.start - b.start);
  const first = touched[0];
  const last = touched[touched.length - 1];

  const start = contains(first.element, range.startContainer)
    ? first.start + charactersBefore(first.element, range.startContainer, range.startOffset)
    : first.start;

  const lastLength = last.element.textContent?.length ?? 0;
  const end = contains(last.element, range.endContainer)
    ? last.start + charactersBefore(last.element, range.endContainer, range.endOffset)
    : last.start + lastLength;

  return end > start ? { start, end } : null;
}

/**
 * The stream range the reader currently has selected inside `root`, or null when there is none.
 *
 * A selection that starts inside the text and ends outside it — dragging out of the panel, which
 * is how anybody selects to the end of a document — is kept and clamped to what is inside, rather
 * than discarded. The alternative is a reader who drags a little too far and gets nothing, with
 * no way to tell why.
 */
export function readStreamSelection(root: Element): GlobalRange | null {
  const selection = root.ownerDocument.getSelection();
  if (selection === null || selection.rangeCount === 0) {
    return null;
  }

  const range = selection.getRangeAt(0);
  if (!range.intersectsNode(root)) {
    return null;
  }

  return rangeToStreamRange(root, range);
}
