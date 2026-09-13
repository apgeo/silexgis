// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TrackingEvent, TrackingState } from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import TrackingReplayBar from './TrackingReplayBar.tsx';

// What decides how big this strip's controls are drawn. Mocked rather than driven by a media query,
// as the rest of this application tests its finger layouts; false by default, which is the machine
// every other test in this file is being read on.
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: vi.fn(() => false) }));

const ANA = 'caver-ana';
const ARMED = '2026-09-12T06:00:00Z';
const CLOSED = '2026-09-12T06:30:00Z';
const at = (iso: string) => Date.parse(iso);

function tracking(overrides: Partial<TrackingState> = {}): TrackingState {
  return {
    state: 'closed',
    surveyModelId: 'model-1',
    referenceStationName: null,
    depthFilter: [],
    armedAt: ARMED,
    closedAt: CLOSED,
    positionsWithheld: false,
    teams: [],
    participants: [],
    ...overrides,
  } as TrackingState;
}

let sequence = 0;
const event = (recordedAt: string, overrides: Partial<TrackingEvent> = {}): TrackingEvent =>
  ({
    id: `event-${sequence++}`,
    caverId: ANA,
    teamId: null,
    kind: 'note',
    surveyModelId: 'model-1',
    stationName: null,
    depthEnteredM: null,
    note: null,
    recordedAt,
    ...overrides,
  }) as unknown as TrackingEvent;

/** Newest first, as the server lists them and as every caller hands them over. */
const LOG: TrackingEvent[] = [
  event('2026-09-12T06:20:00Z', { note: 'water rising in the meander' }),
  event('2026-09-12T06:05:00Z', { note: 'radio check' }),
  event('2026-09-12T06:02:00Z', { kind: 'entered' }),
];

/** The moment the bar last settled on, and whether it is still engaged. */
let moment: number | null = null;
let engagedNow = false;

interface HarnessProps {
  events: readonly TrackingEvent[] | undefined;
  loading: boolean;
  failed: boolean;
  watch: TrackingState;
}

// No defaults on the component itself, and none in `show` either: a default parameter fires on an
// explicit `undefined`, which is precisely the case half of these tests are about.
function Harness({ events, loading, failed, watch }: HarnessProps) {
  const [engaged, setEngaged] = useState(false);
  const [instant, setInstant] = useState<number | null>(null);
  moment = instant;
  engagedNow = engaged;
  return (
    <TrackingReplayBar
      tracking={watch}
      events={events}
      loading={loading}
      failed={failed}
      engaged={engaged}
      onEngagedChange={(next) => {
        setEngaged(next);
        if (!next) {
          setInstant(null);
        }
      }}
      at={instant}
      onAtChange={setInstant}
      nameOf={(caverId) => (caverId === ANA ? 'Ana' : 'Somebody else')}
    />
  );
}

const show = (props: Partial<HarnessProps> = {}) =>
  render(
    <Harness
      {...{ events: LOG, loading: false, failed: false, watch: tracking() }}
      {...props}
    />,
  );

const engage = () => fireEvent.click(screen.getByTestId('trip-tracking-replay-open'));

beforeEach(() => {
  moment = null;
  engagedNow = false;
  vi.mocked(useCoarsePointer).mockReturnValue(false);
});

afterEach(cleanup);

describe('TrackingReplayBar', () => {
  it('offers nothing for a watch that was never armed', () => {
    // There is no stretch of time to play, so there is nothing to press.
    show({ watch: tracking({ armedAt: null, closedAt: null }) });
    expect(screen.queryByTestId('trip-tracking-replay-open')).toBeNull();
  });

  it('opens at the moment the watch was armed', () => {
    show();
    engage();

    expect(engagedNow).toBe(true);
    // The start of the trip is the one moment that needs no explanation.
    expect(moment).toBe(at(ARMED));
    expect(screen.getByTestId('trip-tracking-replay-scrub')).toBeTruthy();
  });

  /**
   * The rule this control may not soften. Reports arrive newest first, so a replay over part of a
   * log opens with the party already underground at places nothing on screen says they walked to,
   * and animates that as confidently as it animates anything else. Until the whole log is in there
   * is nothing to scrub, and a read that failed is said rather than worked around.
   */
  it('replays nothing over a log it has not finished reading, or could not read', () => {
    const waiting = show({ events: undefined, loading: true });
    engage();
    expect(screen.getByTestId('trip-tracking-replay-loading')).toBeTruthy();
    expect(screen.queryByTestId('trip-tracking-replay-scrub')).toBeNull();
    // Nothing has been settled on, so the panel is still showing the live watch.
    expect(moment).toBeNull();
    waiting.unmount();

    show({ events: undefined, failed: true });
    engage();
    expect(screen.getByTestId('trip-tracking-replay-unavailable')).toBeTruthy();
    expect(screen.queryByTestId('trip-tracking-replay-scrub')).toBeNull();
    expect(moment).toBeNull();

    // And the way back out is offered from the refusal itself.
    fireEvent.click(screen.getByTestId('trip-tracking-replay-leave'));
    expect(engagedNow).toBe(false);
  });

  it('says there is nothing to replay where the watch covers no time', () => {
    show({ events: [], watch: tracking({ closedAt: ARMED }) });
    engage();
    expect(screen.getByTestId('trip-tracking-replay-empty')).toBeTruthy();
    expect(moment).toBeNull();
  });

  it('leaves the replay without having settled anything on the way out', () => {
    show();
    engage();
    expect(moment).toBe(at(ARMED));

    fireEvent.click(screen.getByTestId('trip-tracking-replay-leave'));

    // The live watch is what the panel shows again, and it is told so in the same breath: a
    // replay that left a moment behind it would be a panel quietly showing an hour-old party.
    expect(engagedNow).toBe(false);
    expect(moment).toBeNull();
  });

  it('marks the reports that said something in words, and steps between them', () => {
    show();
    engage();

    // Two reports carried words; the third is somebody going in and says nothing.
    expect(document.querySelectorAll('.ant-slider-dot')).toHaveLength(2);

    const note = () => screen.getByTestId('trip-tracking-replay-note').textContent ?? '';
    expect(note()).toContain('Nothing had been said in words');
    expect(screen.getByTestId('trip-tracking-replay-note-previous')).toBeDisabled();

    fireEvent.click(screen.getByTestId('trip-tracking-replay-note-next'));
    expect(moment).toBe(at('2026-09-12T06:05:00Z'));
    expect(note()).toContain('Ana');
    expect(note()).toContain('radio check');

    fireEvent.click(screen.getByTestId('trip-tracking-replay-note-next'));
    expect(moment).toBe(at('2026-09-12T06:20:00Z'));
    expect(note()).toContain('water rising in the meander');
    // Nothing was said after this one.
    expect(screen.getByTestId('trip-tracking-replay-note-next')).toBeDisabled();

    fireEvent.click(screen.getByTestId('trip-tracking-replay-note-previous'));
    expect(moment).toBe(at('2026-09-12T06:05:00Z'));
  });

  /**
   * Sized on the pointer and never on the width: a phone held sideways has the width of a desk and
   * a finger either way, so a layout that branched on room would hand that viewer the desk's
   * twenty-four-pixel targets. Guarded as a whole rather than control by control, because what is
   * easy to get wrong here is not sizing nothing — it is sizing the two arrows that step between
   * notes and leaving the transport, the speed and the way out at the desk size, so that the
   * control somebody presses most is the one their finger misses.
   */
  describe('drawn for a finger', () => {
    const LARGE = 'ant-btn-lg';

    it('sizes every control for a finger, not only the ones that step between notes', () => {
      vi.mocked(useCoarsePointer).mockReturnValue(true);
      show();

      // Before a replay is open there is one control, pressed with the same finger.
      expect(screen.getByTestId('trip-tracking-replay-open')).toHaveClass(LARGE);
      engage();

      for (const control of [
        'trip-tracking-replay-play',
        'trip-tracking-replay-leave',
        'trip-tracking-replay-note-previous',
        'trip-tracking-replay-note-next',
      ]) {
        expect(screen.getByTestId(control)).toHaveClass(LARGE);
      }
      // Four speeds side by side is the easiest thing on the strip to mis-hit, and the one a
      // coordinator reaches for mid-trip.
      expect(screen.getByTestId('trip-tracking-replay-speed')).toHaveClass('ant-segmented-lg');
    });

    it('keeps the dense chrome where there is a mouse', () => {
      // The strip stands over a survey panel in a card, and on a desk the room it takes is worth
      // more than the hit tolerance nothing there needs.
      show();
      engage();

      expect(screen.getByTestId('trip-tracking-replay-play')).toHaveClass('ant-btn-sm');
      expect(screen.getByTestId('trip-tracking-replay-speed')).toHaveClass('ant-segmented-sm');
    });
  });

  describe('playing', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('advances the moment against the real clock, and stops at the end', () => {
      show();
      engage();

      fireEvent.click(screen.getByTestId('trip-tracking-replay-play'));
      act(() => {
        vi.advanceTimersByTime(1000);
      });

      // A second of watching is a minute of the trip at the speed it opens on.
      expect(moment).toBe(at(ARMED) + 60_000);

      act(() => {
        vi.advanceTimersByTime(60_000);
      });

      // Stopped at the end rather than wrapped: sliding from the last moment back to the entrance
      // would read as the party going in again.
      expect(moment).toBe(at(CLOSED));
      expect(screen.getByTestId('trip-tracking-replay-play')).toHaveAttribute('aria-label', 'Play');
    });

    it('stops playing when the replay is left, and writes nothing on the way', () => {
      show();
      engage();
      fireEvent.click(screen.getByTestId('trip-tracking-replay-play'));
      act(() => {
        vi.advanceTimersByTime(1000);
      });

      fireEvent.click(screen.getByTestId('trip-tracking-replay-leave'));
      act(() => {
        vi.advanceTimersByTime(10_000);
      });

      // The clock is not still running against a panel that has gone back to the live watch.
      expect(moment).toBeNull();
      expect(engagedNow).toBe(false);
    });
  });
});
