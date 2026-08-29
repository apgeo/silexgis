// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The link-annotated text format, on this side of the wire.
 *
 * This file is one half of a contract whose other half is written in C#. What it computes —
 * where each block begins in the character stream that resource-link anchors index into — has
 * to agree with the server to the character, because the browser is what turns a reader's drag
 * across a sentence into a pair of offsets, and the server is what those offsets are resolved
 * against for search, for re-anchoring and for every other reader of the same document. A
 * disagreement of one character does not fail: it draws every highlight in the document
 * slightly off, and nothing anywhere reports it.
 *
 * That agreement is affordable only because the format was chosen to make it trivial. The
 * stream is the blocks' own text joined by a blank line, and a block's text is plain characters
 * with no markup in it, so "where does block *n* start" is a sum rather than an interpretation.
 * A format that stored markup would put a tag-stripper on each side of the wire and require the
 * two of them to strip identically for ever.
 *
 * Offsets are UTF-16 code units, which is what `String.prototype.length` counts here and what
 * `string.Length` counts there. They agree across surrogate pairs by construction. Nothing here
 * may be "corrected" to count characters, runes or bytes.
 */

import type { AnnotatedText } from '../api/hooks.ts';

// Reached through the API layer rather than through the generated schema directly: the schema is
// that layer's to read, and everything else in the application sees it only as the types that
// layer re-exports.
type GeneratedBlock = AnnotatedText['blocks'][number];
type GeneratedBlockType = GeneratedBlock['type'];
type GeneratedMarkKind = NonNullable<GeneratedBlock['marks']>[number]['kind'];

/** Block kinds, in the wire spelling the stored file carries. */
export const ANNOTATED_BLOCK_TYPES = ['p', 'h1', 'h2', 'h3', 'ul', 'ol', 'quote', 'code'] as const;

export type AnnotatedBlockType = (typeof ANNOTATED_BLOCK_TYPES)[number];

/** Inline emphasis. Typographic only — a hyperlink is a resource link, not a mark. */
export const ANNOTATED_MARK_KINDS = ['b', 'i', 'u', 'code'] as const;

export type AnnotatedMarkKind = (typeof ANNOTATED_MARK_KINDS)[number];

/** A run of emphasis, in offsets local to its block. `end` is exclusive. */
export interface AnnotatedMark {
  start: number;
  end: number;
  kind: AnnotatedMarkKind;
}

export interface AnnotatedBlock {
  type: AnnotatedBlockType;
  text: string;
  marks?: AnnotatedMark[] | null;
}

/** What separates two blocks in the canonical stream: one blank line. */
export const BLOCK_SEPARATOR = '\n\n';

/** The character stream this document's anchors are measured against. */
export function canonicalText(blocks: readonly AnnotatedBlock[]): string {
  return blocks.map((block) => block.text).join(BLOCK_SEPARATOR);
}

/**
 * Where each block begins in {@link canonicalText}, by block index.
 *
 * Computed once for a document and carried, rather than derived at each use: it is read by the
 * highlighter for every block on every render and by the selection reader on every drag, and
 * "the lengths before me plus two per separator" is arithmetic that gets written slightly
 * differently each time somebody writes it.
 */
export function blockStarts(blocks: readonly AnnotatedBlock[]): number[] {
  const starts: number[] = [];
  let at = 0;
  for (const block of blocks) {
    starts.push(at);
    at += block.text.length + BLOCK_SEPARATOR.length;
  }

  return starts;
}

/**
 * Which block an offset falls in, or -1 when it falls outside every block — which a valid
 * offset can, because the separators between blocks are part of the stream and belong to no
 * block. A selection that starts in the blank line between two paragraphs lands there.
 */
export function blockAtOffset(
  blocks: readonly AnnotatedBlock[],
  starts: readonly number[],
  offset: number,
): number {
  let low = 0;
  let high = starts.length - 1;
  while (low <= high) {
    const middle = (low + high) >> 1;
    const start = starts[middle];
    if (offset < start) {
      high = middle - 1;
    } else if (offset >= start + blocks[middle].text.length) {
      low = middle + 1;
    } else {
      return middle;
    }
  }

  return -1;
}

/**
 * Consecutive blocks of one list kind, grouped so they can be drawn as one list.
 *
 * Grouping is a fact about how the blocks are drawn and never about the stream: the stream is
 * the blocks in order whatever is wrapped around them, so a run's members keep their own
 * indexes and their own offsets. Anything that renumbered them here would silently move every
 * anchor below the first list in the document.
 */
export interface BlockRun {
  /** 'ul'/'ol' for a list to be wrapped, null for a block that stands alone. */
  list: 'ul' | 'ol' | null;
  /** Indexes into the block array, in order. */
  indexes: number[];
}

export function blockRuns(blocks: readonly AnnotatedBlock[]): BlockRun[] {
  const runs: BlockRun[] = [];
  for (let i = 0; i < blocks.length; i++) {
    const list = blocks[i].type === 'ul' ? 'ul' : blocks[i].type === 'ol' ? 'ol' : null;
    const previous = runs.at(-1);
    if (list !== null && previous?.list === list) {
      previous.indexes.push(i);
    } else {
      runs.push({ list, indexes: [i] });
    }
  }

  return runs;
}

/**
 * A plain-text draft turned into blocks: blank-line-separated paragraphs, typographically flat.
 * The paste box and the import path both start here, and it mirrors the server's own reading of
 * the same text so that pasting and uploading the same words produce the same document.
 */
export function blocksFromPlainText(text: string): AnnotatedBlock[] {
  return text
    .replaceAll('\r\n', '\n')
    .replaceAll('\r', '\n')
    .split('\n\n')
    .map((paragraph) =>
      // A single newline inside a paragraph is wrapping rather than structure, so it becomes a
      // space: a block is one line by contract, and keeping it would make the body invalid.
      [...paragraph].map((c) => (isControl(c) ? ' ' : c)).join('').trim(),
    )
    .filter((line) => line.length > 0)
    .map((line) => ({ type: 'p', text: line }));
}

function isControl(character: string): boolean {
  const code = character.codePointAt(0) ?? 0;
  return code < 0x20 || (code >= 0x7f && code <= 0x9f);
}

/**
 * Why the server would refuse this body, or null when it would accept it. Every rule mirrors one
 * of the format's own, so a draft can be refused in the editor rather than by a round trip — and
 * each is a condition of the stream surviving storage unchanged rather than a matter of taste.
 */
export function bodyProblem(blocks: readonly AnnotatedBlock[]): string | null {
  if (blocks.length === 0) {
    return 'empty';
  }

  if (blocks.length > MAX_BLOCKS) {
    return 'tooManyBlocks';
  }

  let total = 0;
  for (const block of blocks) {
    if (block.text.length === 0) {
      return 'emptyBlock';
    }

    if (block.text.length > MAX_BLOCK_CHARACTERS) {
      return 'blockTooLong';
    }

    if ([...block.text].some(isControl)) {
      return 'controlCharacter';
    }

    if (block.text !== block.text.trim()) {
      return 'untrimmed';
    }

    total += block.text.length + BLOCK_SEPARATOR.length;
    if (total > MAX_CHARACTERS) {
      return 'tooLong';
    }
  }

  return null;
}

/**
 * The local unions above and the generated ones are the same union, asserted rather than
 * assumed. The runtime arrays are needed — an editor has to offer the block kinds, and a
 * generated type is erased — but a value added on the server and forgotten here would otherwise
 * render as an unstyled paragraph with nothing to say it was ever different.
 */
type Exactly<A, B> = [A] extends [B] ? ([B] extends [A] ? true : never) : never;
const _blockTypesMatchTheServer: Exactly<AnnotatedBlockType, GeneratedBlockType> = true;
const _markKindsMatchTheServer: Exactly<AnnotatedMarkKind, GeneratedMarkKind> = true;
void _blockTypesMatchTheServer;
void _markKindsMatchTheServer;

export const MAX_BLOCKS = 5_000;
export const MAX_BLOCK_CHARACTERS = 20_000;
export const MAX_CHARACTERS = 500_000;
