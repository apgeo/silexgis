// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  capturedQuoteFrom,
  locateQuote,
  textRangeAnchorFor,
  type CapturedQuote,
} from './textSelection.ts';

// The two texts these functions sit between are never identical: one is laid out by a renderer
// in the browser, the other read out of the same file by a different program on the server.
// Every case below is one of the ways they differ in practice.

describe('locating a quote in the extracted text', () => {
  const page =
    'The survey continued past the second sump.\nBeyond it the passage widens into a chamber\n'
    + 'roughly forty metres across.';

  it('finds a phrase and reports where it is', () => {
    const found = locateQuote(page, 'the passage widens');

    expect(found).not.toBeNull();
    expect(page.slice(found!.start, found!.end)).toBe('the passage widens');
  });

  it('finds it across a line break the renderer showed as a space', () => {
    // The browser hands back what it laid out; the server kept the newline the file had.
    const found = locateQuote(page, 'into a chamber roughly forty metres');

    expect(found).not.toBeNull();
    expect(page.slice(found!.start, found!.end)).toBe('into a chamber\nroughly forty metres');
  });

  it('ignores how much whitespace either side used', () => {
    const spaced = 'The   survey    continued\t\tpast the second sump.';
    const found = locateQuote(spaced, 'survey continued past');

    expect(found).not.toBeNull();
    expect(spaced.slice(found!.start, found!.end)).toBe('survey    continued\t\tpast');
  });

  it('says so when the words are not there rather than guessing', () => {
    // A caption drawn inside a figure is on the page and not in the text.
    expect(locateQuote(page, 'Figure 3: plan of the chamber')).toBeNull();
  });

  it('refuses a selection that is only whitespace', () => {
    expect(locateQuote(page, '   \n ')).toBeNull();
  });

  it('picks the occurrence whose surroundings match when the words repeat', () => {
    const repeated = 'left branch: the sump is dry. right branch: the sump is dry. end.';
    const first = locateQuote(repeated, 'the sump is dry', { prefix: 'left branch: ' });
    const second = locateQuote(repeated, 'the sump is dry', { prefix: 'right branch: ' });

    expect(first!.start).toBe(repeated.indexOf('the sump is dry'));
    expect(second!.start).toBe(repeated.lastIndexOf('the sump is dry'));
    expect(first!.start).not.toBe(second!.start);
  });

  it('still locates a repeated quote that arrived without any context', () => {
    const repeated = 'the sump is dry. the sump is dry.';

    expect(locateQuote(repeated, 'the sump is dry')).not.toBeNull();
  });
});

describe('composing the stored anchor', () => {
  const page = 'Beyond it the passage widens into a chamber.';
  const captured: CapturedQuote = {
    page: 4,
    quote: 'the passage widens',
    prefix: 'Beyond it ',
    suffix: ' into a chamber.',
  };

  it('pairs the quote with offsets into the server text', () => {
    const anchor = textRangeAnchorFor(captured, page);

    expect(anchor).toEqual({
      page: 4,
      start: page.indexOf('the passage widens'),
      end: page.indexOf('the passage widens') + 'the passage widens'.length,
      quote: 'the passage widens',
      prefix: 'Beyond it ',
      suffix: ' into a chamber.',
    });
  });

  it('leaves empty context out of the payload rather than storing blank fields', () => {
    const anchor = textRangeAnchorFor({ ...captured, prefix: '', suffix: '' }, page);

    expect(anchor).not.toHaveProperty('prefix');
    expect(anchor).not.toHaveProperty('suffix');
  });

  it('refuses rather than storing a quote with no offsets', () => {
    // Storing the quote alone would be a payload the server rejects; storing an offset that
    // was not found would be a link pointing confidently at the wrong sentence.
    expect(textRangeAnchorFor(captured, 'a page about something else entirely')).toBeNull();
  });

  it('produces a forward, non-empty range, which is what the server requires', () => {
    const anchor = textRangeAnchorFor(captured, page)!;

    expect(anchor.end).toBeGreaterThan(anchor.start);
  });
});

describe('reading a selection out of a text layer', () => {
  /** A layer holding one run per line, which is how the library lays a page out. */
  function layerWith(lines: string[]): HTMLElement {
    const layer = document.createElement('div');
    for (const line of lines) {
      const span = document.createElement('span');
      span.textContent = line;
      layer.append(span);
    }
    document.body.append(layer);
    return layer;
  }

  function select(node: Node, start: number, end: number): Selection {
    const range = document.createRange();
    range.setStart(node, start);
    range.setEnd(node, end);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
    return selection;
  }

  it('captures the words and the text on either side of them', () => {
    const layer = layerWith(['Beyond it the passage widens into a chamber.']);
    const text = layer.firstChild!.firstChild!;
    const selection = select(text, 10, 28);

    const captured = capturedQuoteFrom(selection, layer, 7);

    expect(captured).toEqual({
      page: 7,
      quote: 'the passage widens',
      prefix: 'Beyond it ',
      suffix: ' into a chamber.',
    });
  });

  it('reports nothing when the selection is empty', () => {
    const layer = layerWith(['Beyond it the passage widens.']);
    const selection = select(layer.firstChild!.firstChild!, 4, 4);

    expect(capturedQuoteFrom(selection, layer, 1)).toBeNull();
  });

  it('reports nothing for a drag that ended outside the page', () => {
    // A drag that starts on the page and ends on the toolbar selects the toolbar as well;
    // anchoring a link to that would be nonsense.
    const layer = layerWith(['Beyond it the passage widens.']);
    const elsewhere = document.createElement('p');
    elsewhere.textContent = 'Add member';
    document.body.append(elsewhere);

    const range = document.createRange();
    range.setStart(layer.firstChild!.firstChild!, 0);
    range.setEnd(elsewhere.firstChild!, 3);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);

    expect(capturedQuoteFrom(selection, layer, 1)).toBeNull();
  });

  it('reports nothing when nothing is selected at all', () => {
    const layer = layerWith(['Beyond it.']);
    window.getSelection()!.removeAllRanges();

    expect(capturedQuoteFrom(window.getSelection(), layer, 1)).toBeNull();
  });
});
