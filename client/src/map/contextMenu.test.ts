// SPDX-License-Identifier: AGPL-3.0-or-later
import Map from 'ol/Map';
import View from 'ol/View';
import BaseEvent from 'ol/events/Event';
import { fromLonLat } from 'ol/proj';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { attachContextMenu, type MapContextMenuTarget } from './contextMenu.ts';

// The long-press timer is why this file exists: iOS Safari raises no contextmenu over a
// canvas at all, so on an iPhone the menu only appears if this code times the press
// itself. The e2e suite cannot prove the WebKit path, so the thresholds and the
// dedupe/cancel rules are pinned here.

function pointerEvent(type: string, x: number, y: number, pointerType = 'touch'): Event {
  // jsdom has no PointerEvent constructor; the handlers only read these four fields.
  const event = new MouseEvent(type, { clientX: x, clientY: y, bubbles: true });
  return Object.assign(event, { pointerType, isPrimary: true });
}

let map: Map;
let viewport: HTMLElement;
let opened: MapContextMenuTarget[];
let detach: () => void;

beforeEach(() => {
  vi.useFakeTimers();
  map = new Map({ view: new View({ center: [0, 0], zoom: 10 }) });
  // The map is never rendered, so it cannot resolve a pixel into a coordinate itself.
  vi.spyOn(map, 'getEventPixel').mockReturnValue([40, 60]);
  vi.spyOn(map, 'getEventCoordinate').mockReturnValue(fromLonLat([25.5, 45.25]));
  viewport = map.getViewport();
  opened = [];
  detach = attachContextMenu(map, (target) => opened.push(target));
});

afterEach(() => {
  detach();
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('right-click', () => {
  it('opens the menu at the click and suppresses the browser one', () => {
    const event = new MouseEvent('contextmenu', { clientX: 40, clientY: 60, cancelable: true });
    viewport.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
    expect(opened).toEqual([{ pixel: [40, 60], lonLat: [25.5, 45.25] }]);
  });
});

describe('long press', () => {
  it('opens the menu once the finger has been held still', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(549);
    expect(opened).toHaveLength(0);

    vi.advanceTimersByTime(1);
    expect(opened).toEqual([{ pixel: [40, 60], lonLat: [25.5, 45.25] }]);
  });

  it('does not fire for a mouse, which has a real contextmenu of its own', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60, 'mouse'));
    vi.advanceTimersByTime(1_000);
    expect(opened).toHaveLength(0);
  });

  it('treats a moved finger as a pan, not a press', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(300);
    viewport.dispatchEvent(pointerEvent('pointermove', 52, 60));
    vi.advanceTimersByTime(1_000);

    expect(opened).toHaveLength(0);
  });

  it('tolerates the wobble of a finger that is trying to hold still', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    viewport.dispatchEvent(pointerEvent('pointermove', 44, 63));
    vi.advanceTimersByTime(1_000);

    expect(opened).toHaveLength(1);
  });

  it('does not fire for a tap', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(100);
    viewport.dispatchEvent(pointerEvent('pointerup', 40, 60));
    vi.advanceTimersByTime(1_000);

    expect(opened).toHaveLength(0);
  });

  it('does not fire when the gesture is taken over, as a pinch or a scroll does', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    viewport.dispatchEvent(pointerEvent('pointercancel', 40, 60));
    vi.advanceTimersByTime(1_000);

    expect(opened).toHaveLength(0);
  });

  it('stops the lift from also selecting whatever was under the finger', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(600);

    // OL emulates click/singleclick from this event unless its default is prevented.
    const up = new BaseEvent('pointerup');
    map.dispatchEvent(up);
    expect(up.defaultPrevented).toBe(true);

    // One press, one suppression: the next tap still selects.
    const next = new BaseEvent('pointerup');
    map.dispatchEvent(next);
    expect(next.defaultPrevented).toBeFalsy();
  });

  it('keeps the lift away from the interactions, not just from the emulated click', () => {
    // Preventing the default only stops OL emulating a click. The interactions still see
    // the event unless a listener returns false — and an armed Draw takes a pointerup as
    // a vertex, dropped right under the menu the same press just opened.
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(600);

    expect(map.dispatchEvent(new BaseEvent('pointerup'))).toBe(false);
  });

  it('leaves an ordinary tap free to select', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(100);
    viewport.dispatchEvent(pointerEvent('pointerup', 40, 60));

    const up = new BaseEvent('pointerup');
    map.dispatchEvent(up);
    expect(up.defaultPrevented).toBeFalsy();
  });

  it('does not leave the suppression armed when a press is never lifted', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(600);
    // No pointerup arrives — the browser handed the gesture elsewhere. The next touch
    // must not have its click eaten by the stale listener.
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));

    const up = new BaseEvent('pointerup');
    map.dispatchEvent(up);
    expect(up.defaultPrevented).toBeFalsy();
  });
});

describe('long press against a native contextmenu', () => {
  // Android Chrome raises both from one gesture, at thresholds close enough to race.

  it('ignores a native event that follows our own', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(600);
    expect(opened).toHaveLength(1);

    viewport.dispatchEvent(new MouseEvent('contextmenu', { clientX: 40, clientY: 60, cancelable: true }));
    expect(opened).toHaveLength(1);
  });

  it('drops our own timer when the native event gets there first', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(500);
    viewport.dispatchEvent(new MouseEvent('contextmenu', { clientX: 40, clientY: 60, cancelable: true }));
    expect(opened).toHaveLength(1);

    vi.advanceTimersByTime(1_000);
    expect(opened).toHaveLength(1);
  });

  it('still opens for a right-click well after a press', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    vi.advanceTimersByTime(600);
    vi.advanceTimersByTime(400);

    viewport.dispatchEvent(new MouseEvent('contextmenu', { clientX: 40, clientY: 60, cancelable: true }));
    expect(opened).toHaveLength(2);
  });
});

describe('detach', () => {
  it('stops a press that is already being timed', () => {
    viewport.dispatchEvent(pointerEvent('pointerdown', 40, 60));
    detach();
    vi.advanceTimersByTime(1_000);

    expect(opened).toHaveLength(0);
  });
});
