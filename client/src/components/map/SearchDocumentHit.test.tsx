// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import type { SearchDocumentItem } from '../../api/hooks.ts';
import SearchDocumentHit from './SearchDocumentHit.tsx';

function hit(overrides: Partial<SearchDocumentItem> = {}): SearchDocumentItem {
  return {
    id: 'doc-1',
    title: 'Raport de cartare',
    fileId: 'file-1',
    mimeType: 'application/pdf',
    versionId: 'ver-1',
    versionNumber: 2,
    isCurrentVersion: true,
    pageNumber: 7,
    division: 'page',
    snippet: 'galeria din [[Peștera]] Urșilor',
    ...overrides,
  };
}

afterEach(cleanup);

describe('SearchDocumentHit', () => {
  it('shows the document, the quoted match and the matched words emphasised', () => {
    render(<SearchDocumentHit hit={hit()} />);
    expect(screen.getByText('Raport de cartare')).toBeTruthy();
    // The quotation keeps the diacritics the author wrote, whatever was typed to find it.
    const marked = screen.getByText('Peștera');
    expect(marked.tagName).toBe('MARK');
  });

  it('places a hit in a PDF on its page', () => {
    render(<SearchDocumentHit hit={hit()} />);
    expect(screen.getByText('page 7')).toBeTruthy();
  });

  it('names a spreadsheet division a sheet and a presentation division a slide', () => {
    render(<SearchDocumentHit hit={hit({ division: 'sheet', pageNumber: 3 })} />);
    expect(screen.getByText('sheet 3')).toBeTruthy();
    cleanup();
    render(<SearchDocumentHit hit={hit({ division: 'slide', pageNumber: 4 })} />);
    expect(screen.getByText('slide 4')).toBeTruthy();
  });

  it('claims no position for a format that numbers nothing, even though the row says page one', () => {
    // The rule this pins: a word-processor document is stored whole as page one however long
    // it is, so "page 1" would be a fact the file never stated. The positive case above proves
    // the label appears when the format does number its divisions, so its absence here is the
    // format's answer rather than a component that never renders it.
    render(<SearchDocumentHit hit={hit({ division: 'whole', pageNumber: 1 })} />);
    expect(screen.getByText('Raport de cartare')).toBeTruthy();
    expect(screen.queryByText('page 1')).toBeNull();
  });

  it('says when the match is in a revision that has been replaced', () => {
    render(<SearchDocumentHit hit={hit({ isCurrentVersion: false })} />);
    expect(screen.getByText('version 2, replaced')).toBeTruthy();
  });
});
