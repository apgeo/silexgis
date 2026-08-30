// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  blockAtOffset,
  blockRuns,
  blockStarts,
  blocksFromPlainText,
  bodyProblem,
  canonicalText,
  type AnnotatedBlock,
} from './blocks.ts';

const p = (text: string): AnnotatedBlock => ({ type: 'p', text });

describe('the canonical stream', () => {
  it('is the blocks joined by a blank line', () => {
    expect(canonicalText([p('First.'), p('Second.')])).toBe('First.\n\nSecond.');
  });

  it('starts each block where that block\'s own characters begin', () => {
    // The offset contract in one assertion, and the same one the server asserts on its side.
    // Everything the reader does with an anchor is this arithmetic run one way or the other, so
    // a change this does not catch puts every highlight in the application on the wrong words.
    const blocks = [p('First.'), p('Second.'), p('Third.')];
    const stream = canonicalText(blocks);
    const starts = blockStarts(blocks);

    blocks.forEach((block, index) => {
      expect(stream.slice(starts[index], starts[index] + block.text.length)).toBe(block.text);
    });
  });

  it('counts UTF-16 code units, so an emoji is two', () => {
    // Matches `string.Length` on the server. Anything that "corrected" either side to count
    // characters would move every anchor past the first astral character.
    const blocks = [p('Sala 🜃 mare'), p('after')];
    expect(blocks[0].text.length).toBe(12);
    expect(blockStarts(blocks)[1]).toBe(14);
    expect(canonicalText(blocks).slice(14)).toBe('after');
  });
});

describe('finding a block by offset', () => {
  const blocks = [p('First.'), p('Second.'), p('Third.')];
  const starts = blockStarts(blocks);

  it('finds the block an offset falls in', () => {
    expect(blockAtOffset(blocks, starts, 0)).toBe(0);
    expect(blockAtOffset(blocks, starts, 5)).toBe(0);
    expect(blockAtOffset(blocks, starts, 8)).toBe(1);
    expect(blockAtOffset(blocks, starts, starts[2])).toBe(2);
  });

  it('reports no block for the blank line between two', () => {
    // The separator is part of the stream and belongs to no block. A selection dragged from the
    // end of one paragraph into the next starts here, and answering "block 0" would silently
    // put its first character inside text it is not in.
    expect(blockAtOffset(blocks, starts, 6)).toBe(-1);
    expect(blockAtOffset(blocks, starts, 7)).toBe(-1);
    expect(blockAtOffset(blocks, starts, 999)).toBe(-1);
  });
});

describe('grouping blocks for drawing', () => {
  it('gathers consecutive items of one list kind and leaves the offsets alone', () => {
    const blocks: AnnotatedBlock[] = [
      p('Intro'),
      { type: 'ul', text: 'one' },
      { type: 'ul', text: 'two' },
      { type: 'ol', text: 'first' },
      p('Outro'),
    ];

    expect(blockRuns(blocks)).toEqual([
      { list: null, indexes: [0] },
      { list: 'ul', indexes: [1, 2] },
      { list: 'ol', indexes: [3] },
      { list: null, indexes: [4] },
    ]);

    // Wrapping is a fact about drawing and never about the stream: the members keep their own
    // indexes, so nothing about a list moves an offset.
    expect(blockStarts(blocks)[4]).toBe(canonicalText(blocks).indexOf('Outro'));
  });
});

describe('what the server would refuse', () => {
  it('accepts an ordinary body', () => {
    expect(bodyProblem([{ type: 'h2', text: 'Galeria Mare' }, p('Prose.')])).toBeNull();
  });

  it.each([
    ['an empty body', []],
    ['an empty block', [p('')]],
    ['leading space', [p(' x')]],
    ['trailing space', [p('x ')]],
    ['an embedded newline', [p('two\nlines')]],
  ])('refuses %s', (_name, blocks) => {
    expect(bodyProblem(blocks as AnnotatedBlock[])).not.toBeNull();
  });
});

describe('pasting plain text', () => {
  it('splits on blank lines and flattens wrapping into spaces', () => {
    const blocks = blocksFromPlainText('  First para,\nwrapped.\n\n\nSecond para.  \n');

    // Refusable-before-the-round-trip is the whole point of mirroring the rules: whatever the
    // paste box produces must already be something the server accepts.
    expect(bodyProblem(blocks)).toBeNull();
    expect(blocks.map((b) => b.text)).toEqual(['First para, wrapped.', 'Second para.']);
  });

  it('produces nothing from nothing rather than an empty block', () => {
    expect(blocksFromPlainText('   \n\n  ')).toEqual([]);
  });
});
