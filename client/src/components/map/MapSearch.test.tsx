// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

/** One hit inside a paginated document, and one in a file that arrived whole. */
const hits = [
  {
    id: 'doc-paged',
    title: 'Raport de tură',
    fileId: 'file-paged',
    mimeType: 'application/pdf',
    versionId: 'ver-1',
    versionNumber: 2,
    isCurrentVersion: true,
    pageNumber: 7,
    division: 'page',
    snippet: 'sifonul [[terminal]] a fost trecut',
  },
  {
    id: 'doc-whole',
    title: 'Notiță',
    fileId: 'file-whole',
    mimeType: 'text/plain',
    versionId: 'ver-2',
    versionNumber: 1,
    isCurrentVersion: true,
    // The server numbers every page row from 1, including the single row a whole file gets.
    // Nothing may print that number, which is what this fixture is here to catch.
    pageNumber: 1,
    division: 'whole',
    snippet: 'sifonul [[terminal]]',
  },
];

vi.mock('../../api/hooks.ts', () => ({
  useSearch: () => ({
    data: { features: [], trips: [], documents: { items: hits, totalItems: hits.length, page: 1, pageSize: 10 } },
  }),
  useNominatim: () => ({ data: [] }),
}));
vi.mock('../../hooks/useDebouncedValue.ts', () => ({ useDebouncedValue: (value: string) => value }));
vi.mock('../../map/mapContext.ts', () => ({ flyTo: vi.fn() }));

const { default: MapSearch } = await import('./MapSearch.tsx');

afterEach(cleanup);

function Where() {
  const location = useLocation();
  return <div data-testid="where">{`${location.pathname}${location.search}`}</div>;
}

function pick(label: string) {
  render(
    <MemoryRouter initialEntries={['/map']}>
      <App>
        <MapSearch />
        <Where />
      </App>
    </MemoryRouter>,
  );
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'terminal' } });
  fireEvent.click(screen.getByText(label));
  return screen.getByTestId('where').textContent;
}

describe('MapSearch', () => {
  it('sends a content hit to the document, carrying the matched page where pages are real', () => {
    // The page number is the whole point of the hit: a search that found one paragraph in a
    // long report and then handed over a file had thrown away the thing it knew.
    expect(pick('Raport de tură')).toBe('/documents/doc-paged?page=7');
  });

  it('claims no position for a document that arrived whole', () => {
    // A text file is stored as one page row, but it has no pages — saying "page 1" would be
    // this interface inventing a fact the file never stated.
    expect(pick('Notiță')).toBe('/documents/doc-whole');
  });
});
