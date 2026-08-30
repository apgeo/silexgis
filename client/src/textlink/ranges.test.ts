// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { AnnotatedBlock } from './blocks.ts';
import {
  anchorFor,
  CONTEXT_LENGTH,
  locateAnchor,
  rangesOverlap,
  sliceBlock,
  type GlobalRange,
} from './ranges.ts';

interface Marked {
  id: string;
  range: GlobalRange;
}

const rangeOf = (item: Marked) => item.range;
const block = (text: string, marks?: AnnotatedBlock['marks']): AnnotatedBlock => ({ type: 'p', text, marks });

/** The pieces as `text → the ids covering it`, which is what the drawing actually depends on. */
function layout(pieces: ReturnType<typeof sliceBlock<Marked>>): [string, string[]][] {
  return pieces.map((piece) => [piece.text, piece.covering.map((c) => c.id)]);
}

describe('cutting a block into pieces', () => {
  it('leaves plain text as one piece', () => {
    expect(layout(sliceBlock(block('plain text'), 0, [], rangeOf))).toEqual([['plain text', []]]);
  });

  it('splits around one passage', () => {
    const pieces = sliceBlock(block('abcdefgh'), 0, [{ id: 'a', range: { start: 2, end: 5 } }], rangeOf);
    expect(layout(pieces)).toEqual([
      ['ab', []],
      ['cde', ['a']],
      ['fgh', []],
    ]);
  });

  it('gives every overlapping passage its own share, including the stretch they share', () => {
    // The case a nested-elements implementation cannot represent: two passages that overlap
    // without either containing the other. Flattening to a partition has no such case, and both
    // passages are named on the stretch they share rather than one of them being dropped.
    const pieces = sliceBlock(
      block('abcdefghij'),
      0,
      [
        { id: 'a', range: { start: 1, end: 6 } },
        { id: 'b', range: { start: 4, end: 9 } },
      ],
      rangeOf,
    );

    expect(layout(pieces)).toEqual([
      ['a', []],
      ['bcd', ['a']],
      ['ef', ['a', 'b']],
      ['ghi', ['b']],
      ['j', []],
    ]);
  });

  it('clips a passage that runs across the block boundary', () => {
    // A passage selected across two paragraphs covers the separator too. Each block draws its
    // own share and neither draws characters it does not hold.
    const pieces = sliceBlock(block('second'), 10, [{ id: 'a', range: { start: 4, end: 13 } }], rangeOf);
    expect(layout(pieces)).toEqual([
      ['sec', ['a']],
      ['ond', []],
    ]);
  });

  it('ignores a passage that only touches the block end to end', () => {
    // `end` is exclusive, so a passage ending exactly where the block begins covers nothing of it.
    expect(layout(sliceBlock(block('abc'), 10, [{ id: 'a', range: { start: 4, end: 10 } }], rangeOf)))
      .toEqual([['abc', []]]);
  });

  it('splits on emphasis as well, so a bold word inside a passage stays one passage', () => {
    const pieces = sliceBlock(
      block('the sump here', [{ start: 4, end: 8, kind: 'b' }]),
      0,
      [{ id: 'a', range: { start: 0, end: 13 } }],
      rangeOf,
    );

    expect(pieces.map((piece) => [piece.text, piece.marks])).toEqual([
      ['the ', []],
      ['sump', ['b']],
      [' here', []],
    ]);
    expect(pieces.every((piece) => piece.covering.length === 1)).toBe(true);
  });

  it('reports the stream offset of every piece', () => {
    const pieces = sliceBlock(block('abcdefgh'), 100, [{ id: 'a', range: { start: 102, end: 105 } }], rangeOf);
    expect(pieces.map((piece) => piece.start)).toEqual([100, 102, 105]);
  });
});

describe('whether two ranges overlap', () => {
  it('is false for ranges that merely touch', () => {
    expect(rangesOverlap({ start: 0, end: 5 }, { start: 5, end: 9 })).toBe(false);
    expect(rangesOverlap({ start: 0, end: 6 }, { start: 5, end: 9 })).toBe(true);
  });
});

describe('anchoring a passage', () => {
  const stream = 'The entrance is above the scree. The passage widens after the sump.';

  it('records the words and the text on either side of them', () => {
    const at = stream.indexOf('The passage widens');
    const anchor = anchorFor(stream, { start: at, end: at + 18 });

    expect(anchor.quote).toBe('The passage widens');
    expect(anchor.suffix).toBe(' after the sump.');

    // Bounded on both sides: context is evidence, not a copy of the document, and an
    // unbounded one would store the whole text again on every link in a long paragraph.
    expect(anchor.prefix).toBe('he entrance is above the scree. ');
    expect(anchor.prefix).toHaveLength(CONTEXT_LENGTH);
    expect(stream.slice(0, at)).toMatch(/he entrance is above the scree\. $/);
  });

  it('finds a passage again after an edit above it moves every offset', () => {
    const at = stream.indexOf('The passage widens');
    const anchor = anchorFor(stream, { start: at, end: at + 18 });

    // Without re-locating, the stored offsets still resolve, still land inside the text, and
    // name the wrong words — with nothing on screen to say so.
    const edited = `A new opening.\n\n${stream}`;
    const located = locateAnchor(edited, anchor);

    expect(located).not.toBeNull();
    expect(edited.slice(located!.start, located!.end)).toBe('The passage widens');
  });

  it('gives nothing when the words are gone, rather than a nearby guess', () => {
    const anchor = anchorFor(stream, { start: 32, end: 50 });
    expect(locateAnchor('Nothing of the kind is written here.', anchor)).toBeNull();
  });

  it('uses the recorded context to tell two copies of one sentence apart', () => {
    const twice =
      'In the north branch the roof lowers. Take the left fork. '
      + 'In the south branch the roof lowers. Take the right fork.';
    const second = twice.lastIndexOf('the roof lowers');
    const anchor = anchorFor(twice, { start: second, end: second + 15 });

    const edited = `A preface.\n\n${twice}`;
    const located = locateAnchor(edited, anchor);

    expect(located).not.toBeNull();
    expect(edited.slice(0, located!.start)).toMatch(/In the south branch $/);
  });
});
