// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const { canSpy, doorSpy } = vi.hoisted(() => ({ canSpy: vi.fn(), doorSpy: vi.fn() }));

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
  useFeatures: () => ({ data: { items: [SINKHOLE], page: 1, pageSize: 20, totalItems: 1 }, isFetching: false }),
  useTags: () => ({ data: [] }),
}));

// The map's own module builds a map; the list only ever asks it to frame something.
vi.mock('../../map/mapContext.ts', () => ({ fitGeoJsonGeometry: vi.fn() }));
vi.mock('../../components/ConfigureLink.tsx', () => ({ default: () => null }));

const { default: FeatureListPage } = await import('./FeatureListPage.tsx');

function Address() {
  const { pathname } = useLocation();
  return <span data-testid="address">{pathname}</span>;
}

function show() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/features']}>
        <FeatureListPage />
        <Address />
      </MemoryRouter>
    </AntApp>,
  );
}

const NO_DOOR = { canCreate: false, unbound: false, cavingGroups: [] };

afterEach(cleanup);
beforeEach(() => {
  canSpy.mockReset().mockReturnValue(false);
  doorSpy.mockReset().mockReturnValue(NO_DOOR);
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
