// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { DashboardSummary, MapViewInfo } from '../../api/hooks.ts';

const summary: DashboardSummary = {
  counts: { caves: 12, features: 34, tripLogs: 5, geofiles: 2 },
  recentActivity: [
    { kind: 'tripLog', id: 't1', name: 'Winter camp', updatedAt: '2026-07-15T10:00:00Z' },
    { kind: 'cave', id: 'c1', name: 'Peștera Mare', updatedAt: '2026-07-14T10:00:00Z' },
    { kind: 'feature', id: 'f1', name: null, updatedAt: '2026-07-13T10:00:00Z' },
    { kind: 'caveEntrance', id: 'e1', name: 'Intrarea de sus', updatedAt: '2026-07-12T10:00:00Z' },
  ],
};

const views: MapViewInfo[] = [
  {
    id: 'v1', name: 'Bihor', description: null, config: {}, isHome: true,
    ownerUserId: 'u1', cavingGroupId: null, visibility: 'private', shareToken: null,
    createdAt: '2026-07-01T10:00:00Z', updatedAt: '2026-07-01T10:00:00Z',
  } as unknown as MapViewInfo,
];

const canCreate = vi.fn(() => true);
const refetch = vi.fn();
const summaryQuery = vi.fn(() => ({ data: summary, isLoading: false, isError: false, refetch }));

vi.mock('../../api/hooks.ts', () => ({
  useDashboardSummary: () => summaryQuery(),
  useMapViews: () => ({ data: views }),
  useCanCreateContent: () => canCreate(),
  // Imported by the feature-navigation helper the activity feed uses; only entrance or
  // centerline rows would actually call it.
  fetchFeature: vi.fn(),
}));

// Imported after the mock so the component binds to the mocked hooks.
const { default: DashboardPage } = await import('./DashboardPage.tsx');

/** Reports where the page navigated, so link targets can be asserted rather than assumed. */
function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location">{location.pathname + location.search}</div>;
}

const renderPage = () =>
  render(
    <MemoryRouter>
      <DashboardPage />
      <LocationProbe />
    </MemoryRouter>,
  );

/** The count painted on the tile with this title, so a number can be tied to its own tile. */
const countOn = (title: string) =>
  screen
    .getByText(title)
    .closest('.ant-statistic')
    ?.querySelector('.ant-statistic-content-value')?.textContent;

// The suite does not enable RTL's automatic cleanup; without this each render's DOM
// would stack up and the text queries would match several times over.
afterEach(() => {
  cleanup();
  canCreate.mockReturnValue(true);
  refetch.mockClear();
  summaryQuery.mockReturnValue({ data: summary, isLoading: false, isError: false, refetch });
});

describe('DashboardPage', () => {
  it('shows each visibility-filtered count on its own tile', () => {
    renderPage();
    // Asserted per tile, not document-wide: the tile keys deliberately differ from the DTO
    // field names, so searching the page for a bare number would pass even with every count
    // wired to the wrong tile.
    expect(countOn('Caves')).toBe('12');
    // The generic-features tile is looked up by its key: the label copy is mid-rename
    // ("Surface features" → "Features") and this assertion ties the number to the tile,
    // not to the wording.
    expect(countOn(i18n.t('dashboard.counts.features'))).toBe('34');
    expect(countOn('Trip logs')).toBe('5');
    expect(countOn('Geodata files')).toBe('2');
  });

  it('lists recent activity with a fallback label for unnamed records', () => {
    renderPage();
    expect(screen.getByText('Winter camp')).toBeInTheDocument();
    expect(screen.getByText('Peștera Mare')).toBeInTheDocument();
    expect(screen.getByText('Intrarea de sus')).toBeInTheDocument();
    // Generic features are often unnamed; the row must still be readable.
    expect(screen.getByText('Untitled')).toBeInTheDocument();
  });

  it('sends a saved view to the map as a view request', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Bihor/ }));
    // The id in the URL is the whole mechanism: the map applies the view it names on arrival.
    expect(screen.getByTestId('location')).toHaveTextContent('/map?view=v1');
  });

  it('reports a failed summary instead of painting it as an empty registry', () => {
    summaryQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch });
    renderPage();

    // The distinction this guards: a zero on every tile is a truthful-looking answer, so a
    // failed request that renders one is indistinguishable from a genuinely empty install.
    expect(countOn('Caves')).toBe('—');
    expect(countOn('Geodata files')).toBe('—');
    expect(screen.getAllByText('The dashboard could not be loaded.').length).toBeGreaterThan(0);
    expect(screen.queryByText('Nothing has been added yet.')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(refetch).toHaveBeenCalled();
  });

  it('hides quick actions from users who cannot create content', () => {
    canCreate.mockReturnValue(false);
    renderPage();
    expect(screen.queryByText('Quick actions')).not.toBeInTheDocument();
    // The rest of the dashboard still renders for read-only users.
    expect(screen.getByText('Recent activity')).toBeInTheDocument();
  });
});
