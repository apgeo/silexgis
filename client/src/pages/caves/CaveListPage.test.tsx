// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { caveRegistryDocument, readCaveRegistryAddress } from '../../filters/caveRegistry.ts';
import { encodeFilter } from '../../filters/url.ts';

const { canSpy, doorSpy, cavesSpy, downloadSpy, savedFilters } = vi.hoisted(() => ({
  canSpy: vi.fn(),
  doorSpy: vi.fn(),
  cavesSpy: vi.fn(),
  downloadSpy: vi.fn(),
  savedFilters: { current: [] as unknown[] },
}));

vi.mock('../../api/hooks.ts', () => ({
  useCan: (domain: string, action: string) => canSpy(domain, action),
  useCreateDoor: (domain: string) => doorSpy(domain),
  useCaves: (params: unknown) => {
    cavesSpy(params);
    return { data: { items: [], page: 1, pageSize: 20, totalItems: 0 }, isFetching: false, error: null };
  },
  useCaveTypes: () => ({ data: [{ id: 3, name: 'Karst cave' }] }),
  useTags: () => ({ data: [{ id: 1, slug: 'bats', name: 'Bats' }] }),
}));

vi.mock('../../api/hooks/savedFilters.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks/savedFilters.ts')>(
    '../../api/hooks/savedFilters.ts',
  );
  return {
    documentOf: actual.documentOf,
    useSavedFilters: () => ({ data: savedFilters.current, isSuccess: true }),
    useCreateSavedFilter: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

vi.mock('../../api/download.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/download.ts')>('../../api/download.ts');
  return {
    ...actual,
    downloadFile: (url: string) => {
      downloadSpy(url);
      return Promise.resolve();
    },
  };
});

// Neither is what this is about, and each reads the server on its own account.
vi.mock('../../components/statistics/CaveDistributionPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/caves/KarstLinkExportModal.tsx', () => ({ default: () => null }));

const { default: CaveListPage } = await import('./CaveListPage.tsx');

function Address() {
  const location = useLocation();
  return (
    <>
      <span data-testid="address">{location.pathname}</span>
      <span data-testid="search">{location.search}</span>
    </>
  );
}

function show(entry = '/caves') {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={[entry]}>
        <CaveListPage />
        <Address />
      </MemoryRouter>
    </AntApp>,
  );
}

const NO_DOOR = { canCreate: false, unbound: false, cavingGroups: [] };

afterEach(cleanup);
beforeEach(async () => {
  await i18n.changeLanguage('en');
  canSpy.mockReset().mockReturnValue(false);
  doorSpy.mockReset().mockReturnValue(NO_DOOR);
  cavesSpy.mockReset();
  downloadSpy.mockReset();
  savedFilters.current = [];
});

/**
 * The door to the deleted caves and features is offered to whoever could have deleted one. Which
 * deletions are on the page behind it is the server's decision, row by row; this only decides
 * whether the door is worth showing. An owner deletes their own cave by owning it, so "may record
 * a cave" is half of the answer — and that half is the create door's, not the right over caves as
 * such, because somebody who records caves only for their caving group owns them like anybody.
 */
describe('the door from the cave list to the deleted caves and features', () => {
  it('is not shown to somebody who may neither record a cave nor delete one', () => {
    show();

    expect(screen.queryByTestId('cave-list-deleted')).toBeNull();
  });

  it('is shown to somebody who records caves only for their caving group, and leads there', () => {
    doorSpy.mockReturnValue({ canCreate: true, unbound: false, cavingGroups: [{ id: 'g1', name: 'Speo Club' }] });
    show();

    fireEvent.click(screen.getByTestId('cave-list-deleted'));
    expect(screen.getByTestId('address').textContent).toBe('/features/deleted');
    expect(doorSpy).toHaveBeenCalledWith('features');
  });

  it('is shown to somebody who may delete caves and record none', () => {
    canSpy.mockImplementation((domain, action) => domain === 'features' && action === 'delete');
    show();

    expect(screen.getByTestId('cave-list-deleted')).toBeTruthy();
  });
});

describe('the registry read from its address', () => {
  it('fills the controls from a link and asks the list with that link’s filter', () => {
    const token = encodeFilter(caveRegistryDocument({ region: 'Bihor', search: 'urs', tag: 'bats' })!);
    show(`/caves?filter=${token}&page=2`);

    expect(screen.getByTestId('cave-registry-region')).toHaveValue('Bihor');
    expect(screen.getByDisplayValue('urs')).toBeInTheDocument();
    expect(cavesSpy).toHaveBeenLastCalledWith(expect.objectContaining({ filter: token, page: 2 }));
  });

  it('opens unfiltered, with a notice, when the link’s filter cannot be read', () => {
    show('/caves?filter=eyJ2ZXJzaW9uIjox');

    expect(screen.getByTestId('cave-registry-unreadable-link')).toBeInTheDocument();
    expect(cavesSpy).toHaveBeenLastCalledWith(expect.objectContaining({ filter: undefined }));
  });

  it('keeps a space typed at the end of the search while the next word is on its way', () => {
    vi.useFakeTimers();
    try {
      show('/caves');

      // The test id sits on the search control's wrapper; the box is the input inside it.
      const box = screen.getByTestId('cave-registry-search').querySelector('input')!;
      fireEvent.change(box, { target: { value: 'pestera ' } });
      act(() => {
        vi.advanceTimersByTime(1000);
      });

      // The address holds the trimmed words, and the box still holds what was typed.
      const state = readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!));
      expect(state.controls).toEqual({ search: 'pestera' });
      expect(box).toHaveValue('pestera ');
    } finally {
      vi.useRealTimers();
    }
  });

  it('writes a changed control into the address, back on the first page', () => {
    show('/caves?page=3');

    const region = screen.getByTestId('cave-registry-region');
    fireEvent.change(region, { target: { value: 'Alba' } });
    fireEvent.blur(region);

    const state = readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!));
    expect(state.controls).toEqual({ region: 'Alba' });
    expect(state.page).toBe(1);
  });

  it('takes a minimum length when Enter is pressed in its field', () => {
    show('/caves');

    const length = screen.getByTestId('cave-registry-min-length');
    fireEvent.change(length, { target: { value: '300' } });
    fireEvent.keyDown(length, { key: 'Enter', code: 'Enter', keyCode: 13 });

    const state = readCaveRegistryAddress(new URLSearchParams(screen.getByTestId('search').textContent!));
    expect(state.controls).toEqual({ minLength: 300 });
  });

  it('exports with the list’s own filter, region, tag and length included', async () => {
    const token = encodeFilter(caveRegistryDocument({ region: 'Bihor', tag: 'bats', minLength: 300 })!);
    show(`/caves?filter=${token}`);

    fireEvent.mouseEnter(screen.getByRole('button', { name: /export/i }));
    fireEvent.click(await screen.findByText('CSV'));

    expect(downloadSpy).toHaveBeenCalledTimes(1);
    const url = new URL(downloadSpy.mock.calls[0]![0] as string, 'http://x');
    expect(url.pathname).toBe('/api/v1/export/caves');
    expect(url.searchParams.get('filter')).toBe(token);
  });

  it('marks the applied saved filter as changed once the controls move', () => {
    const document = caveRegistryDocument({ region: 'Bihor' })!;
    savedFilters.current = [{
      id: 'f-1',
      name: 'Bihor',
      description: null,
      document,
      state: 'ready',
      cavingGroupId: null,
      visibility: 'private',
      mine: true,
      createdAt: '',
      updatedAt: '',
    }];

    show(`/caves?filter=${encodeFilter(document)}&saved=f-1`);
    expect(screen.getByTestId('cave-registry-active-filter')).toHaveTextContent('Saved filter: Bihor');
    expect(screen.queryByTestId('cave-registry-active-changed')).toBeNull();

    const region = screen.getByTestId('cave-registry-region');
    fireEvent.change(region, { target: { value: 'Alba' } });
    fireEvent.blur(region);

    expect(screen.getByTestId('cave-registry-active-changed')).toBeInTheDocument();
  });
});
