// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { DashboardSummary, MapViewInfo, TripLogInfo } from '../../api/hooks.ts';

const summary: DashboardSummary = {
  counts: { caves: 12, features: 34, tripLogs: 5, geofiles: 2 },
  recentActivity: [
    { kind: 'tripLog', id: 't1', name: 'Winter camp', updatedAt: '2026-07-15T10:00:00Z' },
    { kind: 'cave', id: 'c1', name: 'Peștera Mare', updatedAt: '2026-07-14T10:00:00Z' },
    { kind: 'feature', id: 'f1', name: null, updatedAt: '2026-07-13T10:00:00Z' },
    { kind: 'caveEntrance', id: 'e1', name: 'Intrarea de sus', updatedAt: '2026-07-12T10:00:00Z' },
    { kind: 'expedition', id: 'x1', name: 'Bihor summer camp', updatedAt: '2026-07-11T10:00:00Z' },
  ],
};

const views: MapViewInfo[] = [
  {
    id: 'v1', name: 'Bihor', description: null, config: {}, isHome: true,
    ownerUserId: 'u1', cavingGroupId: null, visibility: 'private', shareToken: null,
    createdAt: '2026-07-01T10:00:00Z', updatedAt: '2026-07-01T10:00:00Z',
  } as unknown as MapViewInfo,
];

/**
 * Two trips the reader is on. The panel asks for these without naming anybody: the request
 * carries a page size and nothing else, and these assertions check that, because a member
 * naming a person would be the first half of undoing a refusal the server takes seriously.
 */
const upcoming = {
  items: [
    { id: 'tr1', title: 'Sunday at Vântului', tripDate: '2026-09-06', tripDateEnd: null, state: 'confirmed' },
    { id: 'tr2', title: 'Called-off recce', tripDate: '2026-09-20', tripDateEnd: null, state: 'cancelled' },
  ] as unknown as TripLogInfo[],
  page: 1,
  pageSize: 5,
  totalItems: 2,
};

const myTripsSpy = vi.fn();
const upcomingQuery = vi.fn(() => ({ data: upcoming, isLoading: false, isError: false }));
const canCreate = vi.fn(() => true);
const refetch = vi.fn();
const summaryQuery = vi.fn(() => ({ data: summary, isLoading: false, isError: false, refetch }));

vi.mock('../../api/hooks.ts', () => ({
  useDashboardSummary: () => summaryQuery(),
  useMapViews: () => ({ data: views }),
  useMyTripLogs: (params: unknown) => {
    myTripsSpy(params);
    return upcomingQuery();
  },
  // One switch for all three quick-action domains: these tests exercise the card as a
  // whole, not the per-domain split.
  useCan: () => canCreate(),
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
  myTripsSpy.mockClear();
  summaryQuery.mockReturnValue({ data: summary, isLoading: false, isError: false, refetch });
  upcomingQuery.mockReturnValue({ data: upcoming, isLoading: false, isError: false });
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

  it('opens a camp row on the camp, not through the feature resolver', () => {
    renderPage();
    fireEvent.click(screen.getByText('Bihor summer camp'));
    // A camp is not a feature: routed through the feature resolver this row would ask the
    // server about a feature that does not exist and land on an error message instead of a
    // page. The label beside it names the kind so the row reads as a camp, not a trip.
    expect(screen.getByTestId('location')).toHaveTextContent('/expeditions/x1');
    expect(screen.getAllByText(/^Camp · /).length).toBe(1);
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

  it('asks for the coming trips without naming anybody, and opens one on its page', () => {
    renderPage();

    // The whole request, asserted whole. Whose trips these are is worked out on the server from
    // the caller; a page that sent an identifier — under any name — would put back the question
    // "where has this named person been", answerable out of trips the asker may never open.
    expect(myTripsSpy).toHaveBeenCalledWith({ pageSize: 5 });
    const asked = myTripsSpy.mock.calls[0][0] as Record<string, unknown>;
    for (const forbidden of ['caverId', 'participantCaverId', 'participantId', 'userId', 'subjectCaverId', 'for']) {
      expect(asked).not.toHaveProperty(forbidden);
    }

    expect(screen.getByText('Sunday at Vântului')).toBeInTheDocument();
    // A trip that has been called off stays on the panel and says so, rather than vanishing and
    // leaving somebody to turn up.
    expect(screen.getByText('Cancelled')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Sunday at Vântului'));
    expect(screen.getByTestId('location')).toHaveTextContent('/trip-logs/tr1');
  });

  it('sends the panel link to the full list of trips the reader is on', () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'All my trips' }));
    expect(screen.getByTestId('location')).toHaveTextContent('/trip-logs/mine');
  });

  it('reports coming trips it could not read instead of an empty diary', () => {
    upcomingQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderPage();

    // The same distinction the activity feed draws: an empty panel is a claim about this
    // reader's diary, and a request that failed has not made it.
    expect(screen.getByText('Your trips could not be loaded.')).toBeInTheDocument();
    expect(screen.queryByText(/Nothing coming up/)).not.toBeInTheDocument();
  });

  it('hides quick actions from users who cannot create content', () => {
    canCreate.mockReturnValue(false);
    renderPage();
    expect(screen.queryByText('Quick actions')).not.toBeInTheDocument();
    // The rest of the dashboard still renders for read-only users.
    expect(screen.getByText('Recent activity')).toBeInTheDocument();
  });
});
