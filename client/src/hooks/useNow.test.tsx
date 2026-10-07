// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NOW_TICK_MS, useNow } from './useNow.ts';

const START = Date.parse('2026-09-14T12:00:00Z');

/** Puts the document in the foreground or the background, the way a browser reports a tab switch. */
function show(visible: boolean) {
  Object.defineProperty(document, 'hidden', { configurable: true, get: () => !visible });
  act(() => {
    document.dispatchEvent(new Event('visibilitychange'));
  });
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(START);
  Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  // The override above shadows the prototype's own answer; removing it gives the document back.
  Reflect.deleteProperty(document, 'hidden');
});

describe('the moment a page measures its gaps from', () => {
  it('moves on by itself, with nothing else causing a render', () => {
    let renders = 0;
    const { result } = renderHook(() => {
      renders++;
      return useNow();
    });
    expect(result.current).toBe(START);
    const before = renders;

    act(() => {
      vi.advanceTimersByTime(NOW_TICK_MS);
    });

    expect(result.current).toBe(START + NOW_TICK_MS);
    expect(renders).toBeGreaterThan(before);
  });

  it('is the time of the render and not of the last tick', () => {
    // A read landing between two ticks redraws the page; what it is measured from must be that
    // moment, or a report five seconds old is dated from before it was made.
    const { result, rerender } = renderHook(() => useNow());

    act(() => {
      vi.advanceTimersByTime(5_000);
    });
    rerender();

    expect(result.current).toBe(START + 5_000);
  });

  it('does not tick while the page is hidden, and catches up the moment it is looked at', () => {
    let renders = 0;
    const { result } = renderHook(() => {
      renders++;
      return useNow();
    });

    show(false);
    const whileHidden = renders;
    act(() => {
      vi.advanceTimersByTime(10 * NOW_TICK_MS);
    });
    // The positive half first: time really did pass, so a count that stood still is the timer
    // being stopped and not the clock.
    expect(Date.now()).toBe(START + 10 * NOW_TICK_MS);
    expect(renders).toBe(whileHidden);
    expect(vi.getTimerCount()).toBe(0);

    show(true);

    // At once, before any interval: the first glance is the one the figure is for.
    expect(result.current).toBe(START + 10 * NOW_TICK_MS);
    expect(vi.getTimerCount()).toBe(1);

    act(() => {
      vi.advanceTimersByTime(NOW_TICK_MS);
    });
    expect(result.current).toBe(START + 11 * NOW_TICK_MS);
  });

  it('starts no timer on a page that opens in the background', () => {
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => true });
    renderHook(() => useNow());

    expect(vi.getTimerCount()).toBe(0);

    show(true);
    expect(vi.getTimerCount()).toBe(1);
  });

  it('keeps one timer however often the page is shown, and none once it is gone', () => {
    const { unmount } = renderHook(() => useNow());
    expect(vi.getTimerCount()).toBe(1);

    // A browser may report "visible" twice in a row; a second interval would double the ticks
    // and the first would never be cleared.
    show(true);
    show(true);
    expect(vi.getTimerCount()).toBe(1);

    unmount();
    expect(vi.getTimerCount()).toBe(0);
  });
});
