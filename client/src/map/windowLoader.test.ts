// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type Map from 'ol/Map';
import { fromLonLat } from 'ol/proj';
import { attachWindowLoader, WINDOW_LOAD_DELAY_MS, type LonLatWindow } from './windowLoader.ts';

describe('loading a layer for the window the map rests on', () => {
  let moveEnd: (() => void) | undefined;
  let size: number[] | undefined = [800, 600];
  const map = {
    getSize: () => size,
    getView: () => ({ calculateExtent: () => [...fromLonLat([25, 45]), ...fromLonLat([26, 46])] }),
    on: (_type: string, listener: () => void) => {
      moveEnd = listener;
    },
    un: () => {
      moveEnd = undefined;
    },
  } as unknown as Map;

  beforeEach(() => {
    vi.useFakeTimers();
    size = [800, 600];
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function loader(over: { enabled?: () => boolean; fetch?: (signal: AbortSignal) => Promise<string> } = {}) {
    const calls = {
      started: 0,
      windows: [] as LonLatWindow[],
      signals: [] as AbortSignal[],
      answers: [] as string[],
      errors: [] as unknown[],
    };
    const handle = attachWindowLoader<string>(map, {
      isEnabled: over.enabled ?? (() => true),
      onStart: () => {
        calls.started += 1;
      },
      fetch: (window, _view, signal) => {
        calls.windows.push(window);
        calls.signals.push(signal);
        return over.fetch ? over.fetch(signal) : Promise.resolve(`answer ${calls.windows.length}`);
      },
      onAnswer: (answer) => calls.answers.push(answer),
      onError: (error) => calls.errors.push(error),
    });
    return { calls, handle };
  }

  it('asks once the map has stayed still, in longitude and latitude, and only once for a burst of moves', async () => {
    const { calls, handle } = loader();
    moveEnd?.();
    await vi.advanceTimersByTimeAsync(WINDOW_LOAD_DELAY_MS - 50);
    moveEnd?.();
    await vi.advanceTimersByTimeAsync(WINDOW_LOAD_DELAY_MS - 50);
    expect(calls.windows).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(100);
    expect(calls.windows).toHaveLength(1);
    const [west, south, east, north] = calls.windows[0];
    expect(west).toBeCloseTo(25, 6);
    expect(south).toBeCloseTo(45, 6);
    expect(east).toBeCloseTo(26, 6);
    expect(north).toBeCloseTo(46, 6);
    expect(calls.answers).toEqual(['answer 1']);
    handle.detach();
  });

  it('asks nothing while the layer is off or the map has no size yet', async () => {
    let enabled = false;
    const { calls, handle } = loader({ enabled: () => enabled });
    handle.reload();
    await vi.advanceTimersByTimeAsync(0);
    enabled = true;
    size = undefined;
    handle.reload();
    await vi.advanceTimersByTimeAsync(0);
    expect(calls.started).toBe(0);
    size = [800, 600];
    handle.reload();
    await vi.advanceTimersByTimeAsync(0);
    expect(calls.answers).toEqual(['answer 1']);
    handle.detach();
  });

  it('aborts the request a newer window supersedes and hands on only the newer answer', async () => {
    const { calls, handle } = loader({
      fetch: (signal) =>
        new Promise<string>((resolve, reject) => {
          const label = `answer ${calls.signals.length}`;
          signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
          setTimeout(() => resolve(label), 1000);
        }),
    });
    handle.reload();
    await vi.advanceTimersByTimeAsync(10);
    handle.reload();
    expect(calls.signals[0].aborted).toBe(true);
    await vi.advanceTimersByTimeAsync(2000);
    expect(calls.answers).toEqual(['answer 2']);
    expect(calls.errors).toEqual([]);
    handle.detach();
  });

  it('drops an answer or a failure that lands after the layer was switched off', async () => {
    let enabled = true;
    let fail = false;
    const { calls, handle } = loader({
      enabled: () => enabled,
      fetch: () =>
        new Promise<string>((resolve, reject) => setTimeout(() => (fail ? reject(new Error('down')) : resolve('late')), 100)),
    });
    handle.reload();
    enabled = false;
    await vi.advanceTimersByTimeAsync(200);
    enabled = true;
    fail = true;
    handle.reload();
    enabled = false;
    await vi.advanceTimersByTimeAsync(200);
    expect(calls.started).toBe(2);
    expect(calls.answers).toEqual([]);
    expect(calls.errors).toEqual([]);
    handle.detach();
  });

  it('hands on a failure of the window still on screen', async () => {
    const failure = new Error('down');
    const { calls, handle } = loader({ fetch: () => Promise.reject(failure) });
    handle.reload();
    await vi.advanceTimersByTimeAsync(0);
    expect(calls.errors).toEqual([failure]);
    handle.detach();
  });

  it('stops listening and aborts the request in flight once detached', async () => {
    const { calls, handle } = loader({ fetch: () => new Promise<string>(() => undefined) });
    handle.reload();
    await vi.advanceTimersByTimeAsync(0);
    handle.detach();
    expect(calls.signals[0].aborted).toBe(true);
    expect(moveEnd).toBeUndefined();
  });
});
