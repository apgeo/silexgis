// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const { canSpy, doorSpy } = vi.hoisted(() => ({ canSpy: vi.fn(), doorSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCan: (domain: string, action: string) => canSpy(domain, action),
  useCreateDoor: (domain: string) => doorSpy(domain),
  useCaves: () => ({ data: { items: [], page: 1, pageSize: 20, totalItems: 0 }, isFetching: false }),
  useCaveTypes: () => ({ data: [] }),
  useTags: () => ({ data: [] }),
}));

// Neither is what this is about, and each reads the server on its own account.
vi.mock('../../components/statistics/CaveDistributionPanel.tsx', () => ({ default: () => null }));
vi.mock('../../components/caves/KarstLinkExportModal.tsx', () => ({ default: () => null }));

const { default: CaveListPage } = await import('./CaveListPage.tsx');

function Address() {
  const { pathname } = useLocation();
  return <span data-testid="address">{pathname}</span>;
}

function show() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/caves']}>
        <CaveListPage />
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
