// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { StatisticsSubject, TripStatistics } from '../../api/hooks.ts';

const { statisticsSpy } = vi.hoisted(() => ({
  statisticsSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useTripStatistics: (...args: unknown[]) => statisticsSpy(...args),
}));

vi.mock('../../api/download.ts', () => ({
  downloadFile: vi.fn(() => Promise.resolve()),
  tripStatisticsExportUrl: (subject: string, id: string) => `/api/v1/stats/${subject}/${id}/export`,
}));

const { default: TripStatisticsPanel } = await import('./TripStatisticsPanel.tsx');

const totals: TripStatistics = {
  trips: 12,
  people: 8,
  places: 5,
  firstVisits: 3,
  incidents: 1,
  undergroundMinutes: 750,
  personTrips: 20,
  timedPersonTrips: 14,
  lengthSurveyedM: 412.5,
  ropeMetresM: 60,
  surveyStations: 47,
  earliestTripDate: '2026-05-03',
  latestTripDate: '2026-06-01',
  photographs: 9,
  trackedTrips: 4,
  watchUndergroundMinutes: 330,
  watchTimedPersonTrips: 6,
};

function show(subject: StatisticsSubject, data: TripStatistics | undefined, isError = false) {
  statisticsSpy.mockReturnValue({ data, isLoading: false, isError });
  return render(
    <App>
      <TripStatisticsPanel subject={subject} id="subject-1" />
    </App>,
  );
}

afterEach(cleanup);

describe('TripStatisticsPanel', () => {
  it('says the figures are only what this reader may see', () => {
    show('cavingGroup', totals);

    // The sentence is the whole reason two colleagues comparing screens do not file a bug and
    // "fix" it by taking the filter off. Losing it is a regression, not a wording change.
    expect(screen.getByText(/Counted over the trips you may read/)).toBeTruthy();
  });

  it('states how much of the hours figure had times to work from', () => {
    show('cavingGroup', totals);

    expect(screen.getByText(/14 of 20/)).toBeTruthy();
    expect(screen.getByText('12.5 h')).toBeTruthy();
  });

  it('leaves out how many people were on a person’s own trips', () => {
    show('caver', totals);
    expect(screen.queryByText('People')).toBeNull();

    show('cave', totals);
    expect(screen.getByText('People')).toBeTruthy();
  });

  it('leaves the two figures that say nothing off a cave’s own panel', () => {
    show('cave', totals);

    // Every trip counted for a cave names that cave, so its places can only be one and its first
    // visits can only repeat its people. Drawn, each would read as a fact of its own.
    expect(screen.queryByText('Places')).toBeNull();
    expect(screen.queryByText('First visits')).toBeNull();
    // What the cave's panel is for is all still there, the people figure included — the one the
    // dropped tile would have duplicated.
    expect(screen.getByText('Trips')).toBeTruthy();
    expect(screen.getByText('People')).toBeTruthy();
    expect(screen.getByText('Hours underground')).toBeTruthy();
  });

  it.each(['caver', 'cavingGroup', 'expedition'] as const)(
    'keeps both figures where they can vary: %s',
    (subject) => {
      show(subject, totals);

      // The absence on a cave's panel is that subject and nothing wider: a person reaches many
      // places, and a club's or a camp's first visits are a figure in their own right.
      expect(screen.getByText('Places')).toBeTruthy();
      expect(screen.getByText('First visits')).toBeTruthy();
    },
  );

  it('adds a camp up through the same panel, sentence and all', () => {
    show('expedition', totals);

    // A camp is a fourth subject rather than a surface of its own, so it cannot come to state a
    // figure the other three withhold. The sentence carries more weight here than anywhere else:
    // a camp's totals are the ones several people sit down and compare.
    expect(statisticsSpy).toHaveBeenCalledWith('expedition', 'subject-1');
    expect(screen.getByText('People')).toBeTruthy();
    expect(screen.getByText(/Counted over the trips you may read/)).toBeTruthy();
  });

  it('shows nothing at all when the subject may not be read', () => {
    const { container } = show('caver', undefined, true);
    expect(container.textContent).toBe('');
  });
});
