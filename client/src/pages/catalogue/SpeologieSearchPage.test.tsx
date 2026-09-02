// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SpeologieCave } from '../../api/hooks.ts';

const notHere: SpeologieCave = {
  id: 63,
  title: 'Sistemul Vărășoaia',
  slug: 'V5',
  url: 'https://www.speologie.org/V5',
  county: 'BH',
  locality: 'Padiş',
  mountain: 'bihor',
  length: 28000,
  depth: 653,
  negativeDepth: 653,
  altitude: 1367,
  protectionClass: 'B',
  science: null,
  rockCode: '00',
  sump: true,
  vanished: false,
  protectedAreaCode: null,
  hydroNumber: '21',
  hydroBasinId: 605,
  hydroBasin: {
    id: 605,
    parentId: 604,
    name: '3440 - Bazinul Padiş',
    label: 'Bazinul Padiş',
    path: 'Munţii Apuseni › Munţii Bihorului › Bazinele închise şi platourile înalte › Bazinul Padiş',
    depth: 4,
  },
  description: null,
  alreadyImported: false,
  existingCaveId: null,
};

/** Already here and readable — the row must offer the way to the cave it became. */
const hereAndVisible: SpeologieCave = {
  ...notHere,
  id: 64,
  title: 'Peștera Urșilor',
  slug: 'pestera-ursilor',
  url: 'https://www.speologie.org/pestera-ursilor',
  alreadyImported: true,
  existingCaveId: 'cave-uuid-1',
};

/**
 * Already here but not readable by this caller. The presence has to be reported anyway — it is
 * the answer to "would importing this make a duplicate" — while the cave itself stays unnamed.
 */
const hereAndHidden: SpeologieCave = {
  ...notHere,
  id: 65,
  title: 'Avenul lui Adam',
  slug: 'avenul-lui-adam',
  url: null,
  alreadyImported: true,
  existingCaveId: null,
  vanished: true,
};

let searchResult: {
  data?: unknown;
  isFetching: boolean;
  error?: unknown;
} = { isFetching: false };

let configured = true;
const searchCalls: unknown[] = [];
const navigate = vi.fn();

/** Two levels of the catalogue's own tree, which its programmatic interface does not publish. */
const basins = [
  { id: 604, parentId: null, name: '344 - Bazinele închise', label: 'Bazinele închise', path: 'Bazinele închise', depth: 1 },
  {
    id: 605,
    parentId: 604,
    name: '3440 - Bazinul Padiş',
    label: 'Bazinul Padiş',
    path: 'Bazinele închise › Bazinul Padiş',
    depth: 2,
  },
];

vi.mock('../../api/hooks.ts', () => ({
  useSpeologieStatus: () => ({
    data: { configured, maxPageSize: 50, maxSelection: 100, portalUrl: 'https://www.speologie.org' },
  }),
  useSpeologieSearch: (params: unknown) => {
    searchCalls.push(params);
    return searchResult;
  },
  useSpeologieCave: () => ({ data: undefined, isPending: false }),
  useSpeologieBasins: () => ({ data: basins, isPending: false }),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => navigate };
});

const { default: SpeologieSearchPage } = await import('./SpeologieSearchPage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <App>
        <SpeologieSearchPage />
      </App>
    </MemoryRouter>,
  );
}

afterEach(cleanup);

describe('SpeologieSearchPage', () => {
  beforeEach(() => {
    configured = true;
    searchCalls.length = 0;
    navigate.mockReset();
    searchResult = {
      data: {
        items: [notHere, hereAndVisible, hereAndHidden],
        page: 1,
        pageSize: 25,
        hasMore: false,
        spellings: ['ursilor'],
        scannedCount: 3,
      },
      isFetching: false,
    };
  });

  it('says nothing can be searched when the installation has no key, and disables the search', () => {
    configured = false;
    searchResult = { isFetching: false };
    show();

    expect(screen.getByTestId('speologie-not-configured')).toBeTruthy();
    expect(screen.getByTestId('speologie-search').closest('button')?.disabled).toBe(true);
  });

  it('does not ask anything until a term or a county is given', () => {
    show();
    // The hook is called on render, but with an empty question — it is the hook that declines to
    // fire, and the page must not send a search nobody asked for.
    expect(searchCalls[0]).toEqual({ q: undefined, county: undefined, page: 1, pageSize: 25 });
  });

  it('searches for what was typed', async () => {
    show();

    fireEvent.change(screen.getByTestId('speologie-term'), { target: { value: 'ursilor' } });
    fireEvent.click(screen.getByTestId('speologie-search'));

    await waitFor(() => {
      expect(searchCalls.at(-1)).toMatchObject({ q: 'ursilor', page: 1 });
    });
  });

  it('names a cave already here, and does not name one the caller may not see', () => {
    show();

    // Two rows are already imported; only the readable one becomes a link to the cave.
    const links = screen.getAllByText('Already imported');
    expect(links.length).toBe(2);
    expect(links.filter((el) => el.closest('a')).length).toBe(1);
  });

  it('offers the catalogue page as an outward link on the row', () => {
    show();
    const outward = screen
      .getAllByRole('link')
      .filter((el) => el.getAttribute('href') === 'https://www.speologie.org/V5');
    expect(outward.length).toBeGreaterThan(0);
    expect(outward[0].getAttribute('target')).toBe('_blank');
    // Never hand the far end this application's addresses.
    expect(outward[0].getAttribute('rel')).toContain('noreferrer');
  });

  it('sends one cave to the import screen through the address, so a reload keeps it', () => {
    show();
    fireEvent.click(screen.getByTestId('speologie-import-63'));
    expect(navigate).toHaveBeenCalledWith('/catalogue/speologie/import?ids=63');
  });

  it('calls the button on an already-imported row a refresh rather than an import', () => {
    show();
    expect(screen.getByTestId('speologie-import-64').textContent).toContain('Refresh');
    expect(screen.getByTestId('speologie-import-63').textContent).toContain('Import');
  });

  it('says which spellings were actually searched for when more than one was', () => {
    searchResult = {
      data: {
        items: [],
        page: 1,
        pageSize: 25,
        hasMore: false,
        spellings: ['ursilor', 'urșilor', 'urşilor'],
        scannedCount: 0,
      },
      isFetching: false,
    };
    show();

    const note = screen.getByTestId('speologie-spellings');
    expect(note.textContent).toContain('urșilor');
    expect(note.textContent).toContain('urşilor');
    expect(note.textContent).toContain('3');
  });

  it('does not claim a search was widened when only one spelling was asked about', () => {
    show();
    expect(screen.queryByTestId('speologie-spellings')).toBeNull();
  });

  it('tells an administrator when the key was refused, rather than blaming the search', () => {
    searchResult = { isFetching: false, error: new ApiError(503, 'speologie.unauthorized') };
    show();
    expect(screen.getByText(/did not accept this installation's API key/)).toBeTruthy();
  });

  it('names a few of the spellings it used rather than all of a wide expansion', () => {
    // The server asks about up to two dozen spellings of one word. Printing them all turns the
    // sentence that explains the search into a wall of near-identical words nobody reads.
    const many = Array.from({ length: 24 }, (_, i) => `spelling${i}`);
    searchResult = {
      data: { items: [], page: 1, pageSize: 25, hasMore: false, spellings: many, scannedCount: 0 },
      isFetching: false,
    };
    show();

    const note = screen.getByTestId('speologie-spellings');
    expect(note.textContent).toContain('24');
    expect(note.textContent).toContain('spelling0');
    expect(note.textContent).not.toContain('spelling9');
  });

  it('offers the basin tree as a filter and sends the chosen basin with the search', async () => {
    show();

    fireEvent.change(screen.getByTestId('speologie-term'), { target: { value: 'padis' } });
    fireEvent.mouseDown(within(screen.getByTestId('speologie-basin')).getByRole('combobox'));
    fireEvent.click(await screen.findByTitle('Bazinele închise › Bazinul Padiş'));
    fireEvent.click(screen.getByTestId('speologie-search'));

    await waitFor(() => {
      expect(searchCalls.at(-1)).toMatchObject({ q: 'padis', basin: 605 });
    });
  });

  it('says how much was read to produce a basin-narrowed answer', async () => {
    // The catalogue cannot search by basin, so the narrowing happens here over what the search
    // brought back. "4 caves" and "4 caves out of 100 read" are different answers, and only one
    // of them is honest about what was not looked at.
    searchResult = {
      data: {
        items: [notHere],
        page: 1,
        pageSize: 100,
        hasMore: false,
        spellings: ['padis'],
        scannedCount: 100,
      },
      isFetching: false,
    };
    show();

    fireEvent.change(screen.getByTestId('speologie-term'), { target: { value: 'padis' } });
    fireEvent.mouseDown(within(screen.getByTestId('speologie-basin')).getByRole('combobox'));
    fireEvent.click(await screen.findByTitle('Bazinele închise › Bazinul Padiş'));
    fireEvent.click(screen.getByTestId('speologie-search'));

    const notice = await screen.findByTestId('speologie-basin-narrowed');
    expect(notice.textContent).toContain('100');
    expect(notice.textContent).toContain('cannot search by basin');
  });

  it('shows the basin a cave belongs to rather than its number', () => {
    show();
    expect(screen.getAllByText('Bazinul Padiş').length).toBeGreaterThan(0);
  });
});
