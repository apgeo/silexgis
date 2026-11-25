// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TrackingState } from '../../api/hooks.ts';

// A file of its own, rather than one more case beside the card's other tests: antd says a warning
// once per process and never again, so a case that ran earlier in a shared file and set the warning
// off would leave this one nothing to catch, and it would pass whatever the card did.

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    surveyModelCanPlaceACaver: actual.surveyModelCanPlaceACaver,
    surveyModelPlacingObstacle: actual.surveyModelPlacingObstacle,
    useSetTripTracking: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCreateTrackingTeam: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useRenameTrackingTeam: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useDeleteTrackingTeam: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useSurveyModelsForCaves: () => ({ data: [], isPending: false, isError: false, refetch: vi.fn() }),
    useCaveNames: () => new Map<string, string>(),
    useCaveExactLocationAccess: () => ({ withheld: false, isPending: false }),
  };
});
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => false }));

const { default: TrackingConfigCard } = await import('./TrackingConfigCard.tsx');

function watch(overrides: Partial<TrackingState> = {}): TrackingState {
  return {
    state: 'armed',
    surveyModelId: '44444444-4444-4444-4444-444444444444',
    caveFeatureId: null,
    surveyModelMissing: false,
    referenceStationName: 'E0',
    depthFilter: ['main'],
    armedAt: '2026-09-12T06:00:00Z',
    firstArmedAt: '2026-09-12T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    publishesRealNames: true,
    publishedAt: null,
    publishedUntil: null,
    quietAfterSeconds: null,
    teams: [],
    participants: [],
    ...overrides,
  };
}

function card(tracking: TrackingState) {
  return (
    <App>
      <TrackingConfigCard
        tripLogId="trip-1"
        caveIds={['cave-1']}
        cavesWithheld={0}
        tracking={tracking}
        canEdit={false}
        calloutState="none"
        onStale={() => {}}
      />
    </App>
  );
}

afterEach(cleanup);

/**
 * A reader who may not write the trip is drawn no form, so when the watch is read again there are
 * no fields to bring up to date. Reaching for the form anyway makes antd say, through
 * `console.error`, that the form is connected to nothing — and the application's error sweep files
 * that as a defect on every trip page such a reader opens.
 */
it('does not reach for a form it never drew for a reader who may not write the trip', async () => {
  const consoleError = vi.spyOn(console, 'error');
  // antd reports an unconnected form from a timer, not while the card renders; on a fake clock
  // every timer the card set going is run, whatever its delay and however loaded the machine.
  vi.useFakeTimers();
  try {
    const { rerender } = render(card(watch()));
    rerender(card(watch({ surveyModelId: '55555555-5555-5555-5555-555555555555', referenceStationName: 'st.12' })));
    act(() => {
      vi.runAllTimers();
    });

    const unconnected = consoleError.mock.calls
      .map(([first]) => String(first))
      .filter((line) => line.includes('not connected to any Form element'));
    expect(unconnected).toEqual([]);
  } finally {
    vi.useRealTimers();
    consoleError.mockRestore();
  }
});
