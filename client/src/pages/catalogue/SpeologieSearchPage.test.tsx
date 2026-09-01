// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
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

vi.mock('../../api/hooks.ts', () => ({
  useSpeologieStatus: () => ({
    data: { configured, maxPageSize: 50, maxSelection: 100, portalUrl: 'https://www.speologie.org' },
  }),
  useSpeologieSearch: (params: unknown) => {
    searchCalls.push(params);
    return searchResult;
  },
  useSpeologieCave: () => ({ data: undefined, isPending: false }),
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
      },
      isFetching: false,
    };
    show();

    const note = screen.getByTestId('speologie-spellings');
    expect(note.textContent).toContain('urșilor');
    expect(note.textContent).toContain('urşilor');
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
});
