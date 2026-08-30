// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, fireEvent } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import type { AnnotatedBlock } from './blocks.ts';
import { canonicalText } from './blocks.ts';
import type { Highlight } from './highlights.ts';
import AnnotatedTextView from './AnnotatedTextView.tsx';
import { PIECE_OFFSET_ATTRIBUTE } from './selection.ts';

const blocks: AnnotatedBlock[] = [
  { type: 'h2', text: 'Galeria Mare' },
  {
    type: 'p',
    text: 'From the second sump the passage widens into a chamber.',
    marks: [{ start: 9, end: 20, kind: 'b' }],
  },
  { type: 'ul', text: 'Re-survey the connection.' },
  { type: 'ul', text: 'Photograph the flowstone.' },
];

const stream = canonicalText(blocks);

// This suite does not run with globals, so nothing tears a render down between tests; without
// this every query after the first one matches the previous test's tree as well.
afterEach(cleanup);

function passage(memberId: string, quote: string, over: Partial<Highlight> = {}): Highlight {
  const start = stream.indexOf(quote);
  return {
    memberId,
    linkId: `link-${memberId}`,
    shortCode: 'abcd1234',
    relationCode: 'related-to',
    relationLabel: 'Related to',
    description: null,
    mayEdit: true,
    range: { start, end: start + quote.length },
    anchorState: 'exact',
    relocated: false,
    targets: [
      {
        memberId: `${memberId}-t`,
        targetType: 'feature',
        targetId: 'f1',
        anchorKind: 'whole',
        anchor: null,
        display: { title: 'Dolina Demo', subtitle: null, route: null, thumbnailUrl: null },
        note: null,
      },
    ],
    ...over,
  };
}

describe('drawing an annotated text', () => {
  it('draws the blocks as what they are, and the emphasis inside them', () => {
    render(<AnnotatedTextView blocks={blocks} highlights={[]} dark={false} />);

    expect(screen.getByRole('heading', { name: 'Galeria Mare' })).toBeInTheDocument();
    // Consecutive items of one kind become one list, so a reader sees a list rather than four
    // paragraphs that happen to start with a bullet.
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByText('second sump').tagName).toBe('STRONG');
  });

  it('never puts stored text on the page as markup', () => {
    // The one rule this component cannot bend: prose one member typed is shown to another. The
    // format stores plain characters precisely so nothing here has to strip anything.
    const hostile: AnnotatedBlock[] = [{ type: 'p', text: '<img src=x onerror=alert(1)> & <b>bold</b>' }];
    const { container } = render(<AnnotatedTextView blocks={hostile} highlights={[]} dark={false} />);

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('b')).toBeNull();
    expect(screen.getByText('<img src=x onerror=alert(1)> & <b>bold</b>')).toBeInTheDocument();
  });

  it('marks a linked passage as a link anyone can reach', () => {
    render(<AnnotatedTextView blocks={blocks} highlights={[passage('m1', 'the passage widens')]} dark={false} />);

    const mark = screen.getByTestId('tl-passage');
    expect(mark).toHaveTextContent('the passage widens');
    // A span with a click handler does not exist for somebody not using a mouse, which for a
    // panel whose whole purpose is following links means the feature is simply absent.
    expect(mark).toHaveAttribute('role', 'link');
    expect(mark).toHaveAttribute('tabindex', '0');
    expect(mark.getAttribute('aria-label')).toContain('Dolina Demo');
  });

  it('follows a passage from the keyboard as well as from a click', () => {
    const onActivate = vi.fn();
    render(
      <AnnotatedTextView
        blocks={blocks}
        highlights={[passage('m1', 'the passage widens')]}
        dark={false}
        onActivate={onActivate}
      />,
    );

    fireEvent.click(screen.getByTestId('tl-passage'));
    fireEvent.keyDown(screen.getByTestId('tl-passage'), { key: 'Enter' });

    expect(onActivate).toHaveBeenCalledTimes(2);
    expect(onActivate.mock.calls[0][0].memberId).toBe('m1');
  });

  it('draws two overlapping passages as one stretch that names both', () => {
    // Neither contains the other, so nothing could nest them. Both are named on the words they
    // share instead of one of them being silently dropped.
    render(
      <AnnotatedTextView
        blocks={blocks}
        highlights={[passage('m1', 'the second sump the passage'), passage('m2', 'sump the passage widens')]}
        dark={false}
      />,
    );

    const marks = screen.getAllByTestId('tl-passage');
    expect(marks.length).toBeGreaterThan(1);
    expect(marks.map((m) => m.textContent).join('')).toContain('the second sump the passage widens');
  });

  it('stamps every drawn piece with where it sits in the stream', () => {
    // This is what turns a reader's drag back into offsets. Without it a selection would have to
    // be counted through a DOM whose shape depends on how the highlights happened to overlap.
    const { container } = render(
      <AnnotatedTextView blocks={blocks} highlights={[passage('m1', 'the passage widens')]} dark={false} />,
    );

    container.querySelectorAll(`[${PIECE_OFFSET_ATTRIBUTE}]`).forEach((piece) => {
      const at = Number(piece.getAttribute(PIECE_OFFSET_ATTRIBUTE));
      expect(stream.slice(at, at + (piece.textContent?.length ?? 0))).toBe(piece.textContent);
    });
  });

  it('reports a selection as offsets into the stream while editing', () => {
    const onSelectPassage = vi.fn();
    const { container } = render(
      <AnnotatedTextView
        blocks={blocks}
        highlights={[]}
        dark={false}
        onSelectPassage={onSelectPassage}
      />,
    );

    const piece = container.querySelector(`[${PIECE_OFFSET_ATTRIBUTE}]`)!;
    const range = document.createRange();
    range.setStart(piece.firstChild!, 0);
    range.setEnd(piece.firstChild!, 7);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);

    fireEvent.mouseUp(container.querySelector('.tl-root')!);

    expect(onSelectPassage).toHaveBeenCalledOnce();
    const [reported, quote] = onSelectPassage.mock.calls[0];
    expect(quote).toBe(stream.slice(reported.start, reported.end));
    expect(quote).toBe('Galeria');
  });

  it('says nothing about a caret', () => {
    // A collapsed selection is not a passage; treating one as an empty range would let a stray
    // click start a link anchored to nothing.
    const onSelectPassage = vi.fn();
    const { container } = render(
      <AnnotatedTextView blocks={blocks} highlights={[]} dark={false} onSelectPassage={onSelectPassage} />,
    );

    const piece = container.querySelector(`[${PIECE_OFFSET_ATTRIBUTE}]`)!;
    const range = document.createRange();
    range.setStart(piece.firstChild!, 3);
    range.collapse(true);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);

    fireEvent.mouseUp(container.querySelector('.tl-root')!);

    expect(onSelectPassage).not.toHaveBeenCalled();
  });
});
