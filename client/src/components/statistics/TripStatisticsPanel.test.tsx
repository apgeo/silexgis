// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen, within } from '@testing-library/react';
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

  it('shows what the tracking logs say as a second source, beside the roster hours and not in them', () => {
    show('cavingGroup', totals);

    expect(within(screen.getByTestId('trip-statistics-trackedTrips')).getByText('4')).toBeTruthy();
    const watch = screen.getByTestId('trip-statistics-watchHours');
    expect(within(watch).getByText('Hours underground, from tracking')).toBeTruthy();
    expect(within(watch).getByText('5.5 h')).toBeTruthy();

    // The roster's own figure is exactly what it was: 750 minutes, not 750 + 330. A tile reading
    // 18 h would be the two sources added, which counts the same hours twice.
    expect(within(screen.getByTestId('trip-statistics-hours')).getByText('12.5 h')).toBeTruthy();
    expect(screen.queryByText('18 h')).toBeNull();

    // And the sentence that says so, with what the log's figure rests on.
    const note = screen.getByTestId('trip-statistics-watch-note');
    expect(note.textContent).toMatch(/second count/);
    expect(note.textContent).toMatch(/not added/);
    expect(note.textContent).toMatch(/cover: 6\./);
  });

  it('draws a dash, never 0 h, where a log has entries and no exit after any of them', () => {
    show('cavingGroup', { ...totals, trackedTrips: 2, watchUndergroundMinutes: 0, watchTimedPersonTrips: 0 });

    const watch = screen.getByTestId('trip-statistics-watchHours');
    expect(within(watch).getByText('—')).toBeTruthy();
    expect(within(watch).queryByText('0 h')).toBeNull();
    // The trips were tracked all the same, and the sentence says the hours rest on nobody.
    expect(within(screen.getByTestId('trip-statistics-trackedTrips')).getByText('2')).toBeTruthy();
    expect(screen.getByTestId('trip-statistics-watch-note').textContent).toMatch(/cover: 0\./);
  });

  it('says nothing about tracking where no trip in scope was tracked', () => {
    show('caver', { ...totals, trackedTrips: 0, watchUndergroundMinutes: 0, watchTimedPersonTrips: 0 });

    expect(screen.queryByTestId('trip-statistics-watch-note')).toBeNull();
    expect(within(screen.getByTestId('trip-statistics-trackedTrips')).getByText('0')).toBeTruthy();
    expect(within(screen.getByTestId('trip-statistics-watchHours')).getByText('—')).toBeTruthy();
  });

  it('shows nothing at all when the subject may not be read', () => {
    const { container } = show('caver', undefined, true);
    expect(container.textContent).toBe('');
  });
});
