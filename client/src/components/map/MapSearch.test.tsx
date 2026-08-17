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
  {
    id: 'doc-sheet',
    title: 'Inventar',
    fileId: 'file-sheet',
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    versionId: 'ver-3',
    versionNumber: 1,
    isCurrentVersion: true,
    pageNumber: 3,
    division: 'sheet',
    snippet: 'sifonul [[terminal]] în coloana a treia',
  },
];

/** A camp that ran a fortnight, and one that lasted the day it started. */
const camps = [
  { id: 'camp-long', name: 'Bihor summer camp', startDate: '2026-07-18', endDate: '2026-08-01' },
  { id: 'camp-day', name: 'Winter recce', startDate: '2026-12-05', endDate: null },
];

/** Everything matched at once: features are covered elsewhere, so this fixture leaves them out. */
const everything = {
  features: [] as unknown[],
  trips: [] as unknown[],
  expeditions: camps as unknown[],
  documents: { items: hits as unknown[], totalItems: hits.length, page: 1, pageSize: 10 },
};

/** What the box is told the server answered. Reset after each test to the fixture above. */
let searchData: typeof everything = everything;

vi.mock('../../api/hooks.ts', () => ({
  useSearch: () => ({ data: searchData }),
  useNominatim: () => ({ data: [] }),
}));
vi.mock('../../hooks/useDebouncedValue.ts', () => ({ useDebouncedValue: (value: string) => value }));
vi.mock('../../map/mapContext.ts', () => ({ flyTo: vi.fn() }));

const { default: MapSearch } = await import('./MapSearch.tsx');

afterEach(() => {
  cleanup();
  searchData = everything;
});

function Where() {
  const location = useLocation();
  return <div data-testid="where">{`${location.pathname}${location.search}`}</div>;
}

function show() {
  render(
    <MemoryRouter initialEntries={['/map']}>
      <App>
        <MapSearch />
        <Where />
      </App>
    </MemoryRouter>,
  );
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'terminal' } });
}

function pick(label: string) {
  show();
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

  it('opens a camp on its own page, and dates a one-day camp with one date', () => {
    // A camp is not a feature and does not resolve through the feature router, so the section
    // is proved by following it rather than by its label being present.
    expect(pick('Bihor summer camp — 2026-07-18 – 2026-08-01')).toBe('/expeditions/camp-long');
    cleanup();

    // No end date means the camp lasted one day — printing a range with the same date twice
    // would say something the record never said.
    expect(pick('Winter recce — 2026-12-05')).toBe('/expeditions/camp-day');
  });

  it('says nothing about the text index when a camp is what the search found', () => {
    // The sentence about scanned pages is an answer to "why did this find nothing", and it is
    // only true when the search found nothing. Beside a camp it names, it reads as a fault.
    // Every section the box renders has to be consulted here, or a section added later leaves
    // this hint firing on a search that plainly succeeded.
    searchData = {
      ...everything,
      documents: { items: [], totalItems: 0, page: 1, pageSize: 10 },
    };
    show();

    expect(screen.getByText('Bihor summer camp — 2026-07-18 – 2026-08-01')).toBeTruthy();
    expect(screen.queryByText(/No document text matched/)).toBeNull();
  });

  it('still owns up to the text index when nothing at all matched', () => {
    // Paired with the case above, so a fix that simply stopped ever saying it could not pass.
    searchData = {
      features: [],
      trips: [],
      expeditions: [],
      documents: { items: [], totalItems: 0, page: 1, pageSize: 10 },
    };
    show();

    expect(screen.getByText(/No document text matched/)).toBeTruthy();
  });

  it('carries no page for a division that does not index the pictures a reader is shown', () => {
    // A worksheet is a real division and the hit says so in its label — but the pages drawn for
    // a spreadsheet come from a copy that paginated it afresh, where sheet three may start on
    // page forty. Sending the reader to "page 3" of that copy would open a page unrelated to
    // what they searched for, so the document is opened at its beginning instead.
    expect(pick('Inventar')).toBe('/documents/doc-sheet');
    cleanup();

    // Paired with the case that does index them, so a rule that simply stopped carrying page
    // numbers at all could not pass this.
    expect(pick('Raport de tură')).toBe('/documents/doc-paged?page=7');
  });
});
