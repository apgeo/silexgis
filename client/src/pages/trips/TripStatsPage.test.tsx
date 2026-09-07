// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripStats } from '../../api/hooks.ts';

/**
 * The insights page, checked on the one thing it can most easily be wrong about.
 *
 * Every figure here is counted over the trips the reader may open, so a title that says only what
 * it is counting — "Trips per year" — is a claim about the whole archive made out of one reader's
 * share of it. Worse, the scope control moves the population under the same title. So the titles
 * carry the population, the page says whose totals these are, and both are asserted rather than
 * left to review.
 */

const { statsSpy } = vi.hoisted(() => ({ statsSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useTripLogStats: (params: unknown) => statsSpy(params),
  useTripTypes: () => ({ data: [{ id: 3, code: 'survey', name: 'Survey' }] }),
}));

// The charts are exercised against real rendered elements in their own file. Here they would only
// be four more things to wait for, so they stand in as the labels they were handed — which is
// what this page is responsible for working out.
vi.mock('../../components/statistics/TripInsightCharts.tsx', () => ({
  TripYearChart: ({ years }: { years: { year: number }[] }) => (
    <div data-testid="chart-trip-years">{years.map((y) => y.year).join(' ')}</div>
  ),
  TripBreakdownChart: ({ values, testId }: { values: { label: string }[]; testId: string }) => (
    <div data-testid={testId}>{values.map((v) => v.label).join(' | ')}</div>
  ),
}));

const { default: TripStatsPage } = await import('./TripStatsPage.tsx');

function stats(overrides: Partial<TripStats> = {}): TripStats {
  return {
    matching: 4,
    overall: 12,
    years: [
      { year: 2024, trips: 1, newAreas: 1, areasSoFar: 1 },
      { year: 2025, trips: 3, newAreas: 1, areasSoFar: 2 },
    ],
    types: { overlapping: false, distinct: 2, values: [{ value: '3', label: null, count: 3 }, { value: '', label: null, count: 1 }] },
    areas: { overlapping: true, distinct: 60, values: [{ value: 'a1', label: 'Padiș', count: 3 }] },
    participants: { overlapping: true, distinct: 2, values: [{ value: 'p1', label: 'Ana Pop', count: 4 }] },
    ...overrides,
  };
}

/** Reports the address, so the scope toggle can be shown to be a link and not a mood. */
function Address() {
  const location = useLocation();
  return <div data-testid="address">{`${location.pathname}${location.search}`}</div>;
}

function renderAt(search: string) {
  return render(
    <MemoryRouter initialEntries={[`/trip-logs/stats${search}`]}>
      <TripStatsPage />
      <Address />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  statsSpy.mockReset();
  statsSpy.mockReturnValue({ data: stats(), isError: false });
});

afterEach(cleanup);

describe('the trip insights page', () => {
  it('says the totals are the reader’s and not the archive’s', () => {
    renderAt('?states=done');

    expect(screen.getByText(/Counted over the trips you may read/)).toBeTruthy();
  });

  it('names the population in every card title, not only above them', () => {
    renderAt('?states=done');

    // The filtered population, spelled as a fraction of what this reader may open at all.
    expect(screen.getByText(/Trips per year, over the 4 of 12 trips you can read/)).toBeTruthy();
    expect(screen.getByText(/What they were for, over the 4 of 12 trips you can read/)).toBeTruthy();
    expect(screen.getByText(/Where they went, over the 4 of 12 trips you can read/)).toBeTruthy();
    expect(screen.getByText(/Who was on them, over the 4 of 12 trips you can read/)).toBeTruthy();
  });

  it('asks over the filter it arrived with', () => {
    renderAt('?states=done&types=3');

    expect(statsSpy).toHaveBeenCalledWith(expect.objectContaining({ states: 'done', types: '3' }));
  });

  it('moves the titles when the scope moves, and asks over everything the reader may read', () => {
    renderAt('?states=done');

    statsSpy.mockReturnValue({ data: stats({ matching: 12 }), isError: false });
    fireEvent.click(screen.getByText('All trips'));

    // The population in the title is now the unnarrowed one. A title that stayed put here is the
    // defect this assertion exists for: the charts would be of everything under a heading that
    // still said the filter.
    expect(screen.getByText(/Trips per year, over all 12 trips you can read/)).toBeTruthy();
    expect(screen.queryByText(/over the 4 of 12 trips/)).toBeNull();
    // And the narrowing is gone from the request, not merely from the wording.
    expect(statsSpy).toHaveBeenLastCalledWith(expect.objectContaining({ states: undefined }));
    // The filter itself stays in the address, so switching back loses nothing and the view is
    // still a link somebody can send.
    expect(screen.getByTestId('address').textContent).toContain('states=done');
    expect(screen.getByTestId('address').textContent).toContain('scope=all');
  });

  it('says a breakdown adds up to more than the trips, where it does', () => {
    renderAt('');

    expect(screen.getAllByText(/add up to more than the 4 trips counted/).length).toBe(2);
  });

  it('says when it is showing the largest few of many', () => {
    renderAt('');

    expect(screen.getByText(/Showing the 1 largest of 60/)).toBeTruthy();
  });

  it('names the purpose a trip had, and says so when it had none', () => {
    renderAt('');

    expect(screen.getByTestId('chart-trip-types').textContent).toContain('Survey');
    expect(screen.getByTestId('chart-trip-types').textContent).toContain('Not recorded');
  });

  it('shows nothing rather than empty charts when the filter matched nothing', () => {
    statsSpy.mockReturnValue({
      data: stats({ matching: 0, years: [], types: { overlapping: false, distinct: 0, values: [] } }),
      isError: false,
    });
    renderAt('?states=done');

    expect(screen.getByTestId('trip-stats-empty')).toBeTruthy();
    expect(screen.queryByTestId('chart-trip-years')).toBeNull();
  });

  it('says the totals could not be worked out rather than drawing zeroes', () => {
    statsSpy.mockReturnValue({ data: undefined, isError: true });
    renderAt('');

    expect(screen.getByTestId('trip-stats-error')).toBeTruthy();
    expect(screen.queryByTestId('chart-trip-years')).toBeNull();
  });
});
