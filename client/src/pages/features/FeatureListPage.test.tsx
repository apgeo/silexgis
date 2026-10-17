// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { featureListDocument, readFeatureListAddress } from '../../filters/featureList.ts';
import { decodeFilter, encodeFilter } from '../../filters/url.ts';

const { canSpy, doorSpy, featuresSpy, downloadSpy, savedFilters } = vi.hoisted(() => ({
  canSpy: vi.fn(),
  doorSpy: vi.fn(),
  featuresSpy: vi.fn(),
  downloadSpy: vi.fn(),
  savedFilters: { current: [] as unknown[] },
}));

const SINKHOLE = {
  id: '33333333-3333-3333-3333-333333333333',
  kind: 'generic',
  featureTypeCode: 'sinkhole',
  category: 'surface',
  name: 'Dolina 4',
  geometry: null,
  locationProtected: false,
  approximateLocation: false,
  omittedLocation: false,
  visibility: 'authenticated',
  updatedAt: '2026-10-01T10:00:00Z',
};

vi.mock('../../api/hooks.ts', () => ({
  useCan: (domain: string, action: string) => canSpy(domain, action),
  useCreateDoor: (domain: string) => doorSpy(domain),
  useDeleteFeature: () => ({ mutateAsync: vi.fn() }),
  useFeatureTypes: () => ({ data: [{ id: 7, code: 'sinkhole', name: 'Sinkhole' }] }),
  useFeatures: (params: unknown) => {
    featuresSpy(params);
    return { data: { items: [SINKHOLE], page: 1, pageSize: 20, totalItems: 1 }, isFetching: false, error: null };
  },
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

// The map's own module builds a map; the list only ever asks it to frame something.
vi.mock('../../map/mapContext.ts', () => ({ fitGeoJsonGeometry: vi.fn() }));
vi.mock('../../components/ConfigureLink.tsx', () => ({ default: () => null }));

const { default: FeatureListPage } = await import('./FeatureListPage.tsx');

function Address() {
  const { pathname, search } = useLocation();
  return (
    <>
      <span data-testid="address">{pathname}</span>
      <span data-testid="search">{search}</span>
    </>
  );
}

function show(entry = '/features') {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={[entry]}>
        <FeatureListPage />
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
  featuresSpy.mockReset();
  downloadSpy.mockReset();
  savedFilters.current = [];
});

/** The address as the list last wrote it. */
function writtenAddress() {
  return readFeatureListAddress(new URLSearchParams(screen.getByTestId('search').textContent ?? ''));
}

/**
 * The list's controls compose one filter document kept in its address, and the list and its
 * export are both asked with that document's token — never with parameters of their own.
 */
describe('the feature list read from and written to its address', () => {
  it('fills the controls from a link and asks the list with that link’s filter', () => {
    const token = encodeFilter(featureListDocument({ kind: 'generic', featureTypeId: 7, tag: 'bats', search: 'dol' })!);
    show(`/features?filter=${token}&page=2`);

    expect(screen.getByDisplayValue('dol')).toBeInTheDocument();
    expect(screen.getByTestId('feature-list-kind').textContent).toContain('Feature');
    expect(screen.getByTestId('feature-list-type').textContent).toContain('Sinkhole');
    expect(screen.getByTestId('feature-list-tag').textContent).toContain('Bats');
    expect(featuresSpy).toHaveBeenLastCalledWith({ page: 2, pageSize: 20, filter: token });
  });

  it('asks with no filter at all when no control is set', () => {
    show();

    expect(featuresSpy).toHaveBeenLastCalledWith({ page: 1, pageSize: 20, filter: undefined });
  });

  it('opens unfiltered, with a notice, when the link’s filter cannot be read', () => {
    show('/features?filter=eyJ2ZXJzaW9uIjox');

    expect(screen.getByTestId('feature-list-unreadable-link')).toBeInTheDocument();
    expect(featuresSpy).toHaveBeenLastCalledWith(expect.objectContaining({ filter: undefined }));
  });

  it('writes a chosen kind into the address as a condition, back on the first page', () => {
    show('/features?page=3');

    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Kind of feature' }));
    fireEvent.click(screen.getByTitle('Feature'));

    const written = writtenAddress();
    expect(written.controls).toEqual({ kind: 'generic' });
    expect(written.page).toBe(1);
    const document = decodeFilter(new URLSearchParams(screen.getByTestId('search').textContent ?? '').get('filter'));
    expect(document?.scope[0]?.where).toEqual({
      node: 'allOf',
      of: [{ node: 'condition', field: 'kind', op: 'equals', values: [{ type: 'id', value: 'generic' }] }],
    });
  });

  it('exports with the very token the list was asked with', async () => {
    const token = encodeFilter(featureListDocument({ tag: 'bats', kind: 'generic' })!);
    show(`/features?filter=${token}`);

    fireEvent.mouseEnter(screen.getByRole('button', { name: /export/i }));
    fireEvent.click(await screen.findByText('GEOJSON'));

    expect(downloadSpy).toHaveBeenCalledTimes(1);
    const url = new URL(downloadSpy.mock.calls[0]![0] as string, 'http://x');
    expect(url.pathname).toBe('/api/v1/export/features');
    expect(url.searchParams.get('format')).toBe('geojson');
    expect(url.searchParams.get('filter')).toBe(token);
    expect([...url.searchParams.keys()].sort()).toEqual(['filter', 'format']);
  });

  it('names every control for assistive technology', () => {
    show();

    for (const name of ['Kind of feature', 'Feature type', 'Feature category', 'Tag', 'Apply a saved filter…']) {
      expect(screen.getByRole('combobox', { name })).toBeTruthy();
    }
    expect(screen.getByLabelText('Search features by name')).toBeTruthy();
  });
});

/**
 * The same door the cave list has, by the same rule: offered to whoever could have deleted a
 * feature, by the right to delete or by being able to own one.
 */
describe('the door from the feature list to the deleted caves and features', () => {
  it('is not shown to somebody who may neither record a feature nor delete one', () => {
    show();

    expect(screen.queryByTestId('feature-list-deleted')).toBeNull();
  });

  it('is shown to somebody who records features only for their caving group, and leads there', () => {
    doorSpy.mockReturnValue({ canCreate: true, unbound: false, cavingGroups: [{ id: 'g1', name: 'Speo Club' }] });
    show();

    fireEvent.click(screen.getByTestId('feature-list-deleted'));
    expect(screen.getByTestId('address').textContent).toBe('/features/deleted');
  });

  it('is shown to somebody who may delete features and record none', () => {
    canSpy.mockImplementation((domain, action) => domain === 'features' && action === 'delete');
    show();

    expect(screen.getByTestId('feature-list-deleted')).toBeTruthy();
  });
});

/**
 * A delete takes what the feature contains with it, and none of it is removed. The confirmation
 * is where somebody about to delete an area needs to read both halves.
 */
describe('deleting a feature from the list', () => {
  it('says before the delete that it can be restored, and from where', async () => {
    canSpy.mockImplementation((domain, action) => domain === 'features' && action === 'write');
    show();

    const row = screen.getByRole('row', { name: /Dolina 4/ });
    fireEvent.click(within(row).getAllByRole('button').at(-1)!);

    const dialog = await screen.findByRole('tooltip');
    expect(dialog.textContent).toContain('Delete this feature? Contained features are deleted with it.');
    expect(dialog.textContent).toContain('It can be restored, with what it contains, from Deleted caves and features.');
  });
});
