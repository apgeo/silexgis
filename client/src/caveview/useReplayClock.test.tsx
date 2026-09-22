// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useReplayClock, type ReplayClockOptions } from './useReplayClock.ts';

/**
 * The clock both replays run on, driven through a harness rather than through either strip.
 *
 * The two strips that use it look nothing alike and should not; what has to be identical is the
 * arithmetic, so it is proved here once instead of twice through two sets of markup.
 */
function Harness({ span, engaged }: Pick<ReplayClockOptions, 'span' | 'engaged'>) {
  const [at, setAt] = useState<number | null>(span?.from ?? null);
  const clock = useReplayClock({ span, at, onAtChange: setAt, engaged });
  return (
    <div>
      <span data-testid="at">{at}</span>
      <span data-testid="playing">{String(clock.playing)}</span>
      <span data-testid="step">{clock.step}</span>
      <button type="button" onClick={clock.toggle} data-testid="toggle" />
      <button type="button" onClick={() => clock.scrubTo(span!.from + 1000)} data-testid="scrub" />
    </div>
  );
}

const SPAN = { from: 1_000_000, to: 1_000_000 + 600_000 };
const at = () => Number(screen.getByTestId('at').textContent);
const playing = () => screen.getByTestId('playing').textContent;

beforeEach(() => vi.useFakeTimers());
afterEach(() => {
  vi.useRealTimers();
  cleanup();
});

describe('the clock a replay runs on', () => {
  it('advances against the real clock at the speed it opens on', () => {
    render(<Harness span={SPAN} engaged />);
    fireEvent.click(screen.getByTestId('toggle'));
    act(() => void vi.advanceTimersByTime(1000));

    // A second of watching is a minute of the trip.
    expect(at()).toBe(SPAN.from + 60_000);
  });

  it('stops at the end rather than wrapping', () => {
    render(<Harness span={SPAN} engaged />);
    fireEvent.click(screen.getByTestId('toggle'));
    act(() => void vi.advanceTimersByTime(60_000));

    // Sliding from the last moment back to the entrance would read as the party going in again.
    expect(at()).toBe(SPAN.to);
    expect(playing()).toBe('false');
  });

  it('starts the trip again when play is pressed at the very end', () => {
    render(<Harness span={SPAN} engaged />);
    fireEvent.click(screen.getByTestId('toggle'));
    act(() => void vi.advanceTimersByTime(60_000));
    fireEvent.click(screen.getByTestId('toggle'));

    expect(at()).toBe(SPAN.from);
    expect(playing()).toBe('true');
  });

  it('stops when the handle is taken, because a drag is somebody taking the wheel', () => {
    render(<Harness span={SPAN} engaged />);
    fireEvent.click(screen.getByTestId('toggle'));
    fireEvent.click(screen.getByTestId('scrub'));
    act(() => void vi.advanceTimersByTime(10_000));

    expect(playing()).toBe('false');
    expect(at()).toBe(SPAN.from + 1000);
  });

  it('runs no clock against a replay nobody is looking at', () => {
    const { rerender } = render(<Harness span={SPAN} engaged />);
    fireEvent.click(screen.getByTestId('toggle'));
    act(() => void vi.advanceTimersByTime(1000));
    const stopped = at();

    rerender(<Harness span={SPAN} engaged={false} />);
    act(() => void vi.advanceTimersByTime(10_000));

    expect(playing()).toBe('false');
    expect(at()).toBe(stopped);
  });

  it('gives the handle the same number of stops whatever the trip’s length', () => {
    render(<Harness span={{ from: 0, to: 2_000_000 }} engaged />);
    expect(Number(screen.getByTestId('step').textContent)).toBe(2000);
  });

  it('has nothing to run when there is nothing to play', () => {
    render(<Harness span={null} engaged />);
    fireEvent.click(screen.getByTestId('toggle'));
    act(() => void vi.advanceTimersByTime(10_000));

    // Pressing play over a trip with no stretch of time cannot invent one.
    expect(screen.getByTestId('at').textContent).toBe('');
  });
});
