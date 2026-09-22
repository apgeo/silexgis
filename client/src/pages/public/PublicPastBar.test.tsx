// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import type { PublicPastTrack } from '../../api/hooks.ts';
import PublicPastBar from './PublicPastBar.tsx';
import type { PastTripPlayback } from './usePastTripPlayback.ts';

/**
 * The strip itself, driven directly.
 *
 * <b>What is proved here is what the two pages around it cannot prove cheaply.</b> Both of the
 * surfaces that draw this strip reach it through a whole page and a fetch; the two statements below
 * are about the strip's own arithmetic over a trip of a size no page test would build — a record so
 * long the response could not carry it, and one whose reports are too many to mark.
 */

const at = (iso: string) => Date.parse(iso);

function track(overrides: Partial<PublicPastTrack> = {}): PublicPastTrack {
  return {
    title: 'Peștera Demo Mare, the 2019 push',
    tripDate: '2019-07-06',
    tripDateEnd: null,
    armedAt: '2019-07-06T08:00:00Z',
    closedAt: '2019-07-06T18:00:00Z',
    positionsWithheld: false,
    trackTruncated: false,
    model: null,
    teams: [],
    participants: [],
    ...overrides,
  };
}

/** A trip with `count` reports, one a minute from the armed instant. */
function everyMinute(count: number): number[] {
  const first = at('2019-07-06T08:00:00Z');
  return Array.from({ length: count }, (_, index) => first + index * 60_000);
}

function playback(moments: readonly number[], overrides: Partial<PastTripPlayback> = {}) {
  const span = { from: moments[0], to: moments[moments.length - 1] };
  return {
    tripLogId: 'trip-1',
    engaged: true,
    track: track(),
    loading: false,
    failed: false,
    span,
    moments,
    at: span.from,
    setAt: () => {},
    envelope: null,
    follow: null,
    setFollow: () => {},
    open: () => {},
    backToNow: () => {},
    ...overrides,
  } satisfies PastTripPlayback;
}

const marks = () => document.querySelectorAll('.public-past-mark').length;

afterEach(cleanup);

describe('the rail a past trip is scrubbed on', () => {
  it('marks every report while the marks are still marks', () => {
    render(<PublicPastBar playback={playback(everyMinute(20))} liveState="closed" cavers={[]} />);

    expect(marks()).toBe(20);
  });

  it('draws none at all for a record with too many to tell apart', () => {
    // The dot is 6px and a response carries up to two thousand reports: drawn, they are a solid
    // bar that says less than a bare rail does, at the cost of one absolutely-positioned element
    // each, re-laid-out on every one of the five renders a second the clock produces. Stepping
    // from report to report is what the two arrows beside the handle are for, and they still do.
    render(<PublicPastBar playback={playback(everyMinute(600))} liveState="closed" cavers={[]} />);

    expect(marks()).toBe(0);
    expect(screen.getByTestId('public-past-report-next')).toBeEnabled();
  });
});

describe('a record the response could not carry whole', () => {
  it('says so, where a reader cannot miss it', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(3), { track: track({ trackTruncated: true }) })}
        liveState="closed"
        cavers={[]}
      />,
    );

    // In the banner rather than beside the transport: reaching the end of the rail must not read
    // as reaching the end of the trip.
    expect(screen.getByTestId('public-past-banner')).toContainElement(
      screen.getByTestId('public-past-truncated'),
    );
  });

  it('says nothing of the kind about a record that arrived whole', () => {
    render(<PublicPastBar playback={playback(everyMinute(3))} liveState="closed" cavers={[]} />);

    expect(screen.queryByTestId('public-past-truncated')).toBeNull();
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
  });
});
