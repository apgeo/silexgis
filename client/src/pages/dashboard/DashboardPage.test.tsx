// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { DashboardSummary, MapViewInfo } from '../../api/hooks.ts';

const summary: DashboardSummary = {
  counts: { caves: 12, surfaceFeatures: 34, tripLogs: 5, geofiles: 2 },
  recentActivity: [
    { kind: 'tripLog', id: 't1', name: 'Winter camp', updatedAt: '2026-07-15T10:00:00Z' },
    { kind: 'cave', id: 'c1', name: 'Peștera Mare', updatedAt: '2026-07-14T10:00:00Z' },
    { kind: 'surfaceFeature', id: 'f1', name: null, updatedAt: '2026-07-13T10:00:00Z' },
  ],
};

const views: MapViewInfo[] = [
  {
    id: 'v1', name: 'Bihor', description: null, config: {}, isHome: true,
    ownerUserId: 'u1', teamId: null, visibility: 'private', shareToken: null,
    createdAt: '2026-07-01T10:00:00Z', updatedAt: '2026-07-01T10:00:00Z',
  } as unknown as MapViewInfo,
];

const canCreate = vi.fn(() => true);

vi.mock('../../api/hooks.ts', () => ({
  useDashboardSummary: () => ({ data: summary, isLoading: false }),
  useMapViews: () => ({ data: views }),
  useCanCreateContent: () => canCreate(),
}));

// Imported after the mock so the component binds to the mocked hooks.
const { default: DashboardPage } = await import('./DashboardPage.tsx');

const renderPage = () =>
  render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  );

// The suite does not enable RTL's automatic cleanup; without this each render's DOM
// would stack up and the text queries would match several times over.
afterEach(cleanup);

describe('DashboardPage', () => {
  it('shows the visibility-filtered counts as tiles', () => {
    renderPage();
    expect(screen.getByText('12')).toBeInTheDocument();
    expect(screen.getByText('34')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
  });

  it('lists recent activity with a fallback label for unnamed records', () => {
    renderPage();
    expect(screen.getByText('Winter camp')).toBeInTheDocument();
    expect(screen.getByText('Peștera Mare')).toBeInTheDocument();
    // Surface features are often unnamed; the row must still be readable.
    expect(screen.getByText('Untitled')).toBeInTheDocument();
  });

  it('links each saved view to the map', () => {
    renderPage();
    expect(screen.getByRole('button', { name: /Bihor/ })).toBeInTheDocument();
  });

  it('hides quick actions from users who cannot create content', () => {
    canCreate.mockReturnValueOnce(false);
    renderPage();
    expect(screen.queryByText('Quick actions')).not.toBeInTheDocument();
    // The rest of the dashboard still renders for read-only users.
    expect(screen.getByText('Recent activity')).toBeInTheDocument();
  });
});
