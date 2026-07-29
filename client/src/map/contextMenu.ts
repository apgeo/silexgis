// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import { unByKey } from 'ol/Observable';
import { listenOnce, type EventsKey } from 'ol/events';
import { toLonLat } from 'ol/proj';

export interface MapContextMenuTarget {
  /** Pixel position relative to the map viewport, for anchoring the menu. */
  pixel: [number, number];
  /** Click coordinate in EPSG:4326, rounded to ~0.1 m. */
  lonLat: [number, number];
}

/** Roughly the browsers' own long-press threshold, so the two feel like one gesture. */
const LONG_PRESS_MS = 550;
/** Beyond this the finger is panning the map, not pressing a spot on it. */
const MOVE_TOLERANCE_PX = 8;
/** A native contextmenu this close behind our own is the same press, reported twice. */
const NATIVE_DEDUPE_MS = 300;

/**
 * Binds the "menu at this spot" gesture on the map viewport: right-click on desktop,
 * long-press on touch.
 *
 * Long-press cannot be left to the browser. Android Chrome does synthesize a `contextmenu`
 * from it, but iOS Safari never does so over a canvas — no event of any kind arrives — so
 * the gesture is timed here from pointer events and the native one is de-duplicated where
 * it exists. The two paths can also race (Android's fires near our own threshold), so each
 * cancels the other rather than only guarding one direction.
 *
 * Position is reported both as a viewport pixel (menu anchor) and as lon/lat (what the
 * menu's actions operate on).
 */
export function attachContextMenu(
  map: Map,
  onOpen: (target: MapContextMenuTarget) => void,
): () => void {
  const viewport = map.getViewport();
  let synthesizedAt = 0;
  let pressTimer: number | undefined;
  let pressStart: PointerEvent | null = null;
  let clickSuppressKey: EventsKey | null = null;

  const open = (event: MouseEvent) => {
    const [x, y] = map.getEventPixel(event);
    const [lon, lat] = toLonLat(map.getEventCoordinate(event));
    onOpen({ pixel: [x, y], lonLat: [Number(lon.toFixed(6)), Number(lat.toFixed(6))] });
  };

  const cancelPress = () => {
    if (pressTimer !== undefined) {
      window.clearTimeout(pressTimer);
      pressTimer = undefined;
    }
    pressStart = null;
  };

  const releaseClickSuppression = () => {
    if (clickSuppressKey) {
      unByKey(clickSuppressKey);
      clickSuppressKey = null;
    }
  };

  const onContextMenu = (event: MouseEvent) => {
    event.preventDefault();
    // Android: the native event won this race — drop our pending timer so the menu is
    // not opened a second time, re-anchored, half a second later.
    cancelPress();
    if (Date.now() - synthesizedAt < NATIVE_DEDUPE_MS) {
      return;
    }
    open(event);
  };

  const onPointerDown = (event: PointerEvent) => {
    // A suppression armed by a press whose lift never arrived must not eat this gesture.
    releaseClickSuppression();
    cancelPress();
    if (event.pointerType !== 'touch' || !event.isPrimary) {
      return;
    }
    pressStart = event;
    pressTimer = window.setTimeout(() => {
      pressTimer = undefined;
      const pressed = pressStart;
      pressStart = null;
      if (!pressed) {
        return;
      }
      synthesizedAt = Date.now();
      // OL emulates click/singleclick from the pointerup unless a listener prevents the
      // default on it — the lift that ends this press must not also select whatever the
      // finger happened to rest on. A cancelled pointer reaches the same handler, so the
      // listener always gets consumed. Registered untyped because OL dispatches
      // 'pointerup' as a map event but lists only the click/move family in its public
      // event-type union.
      clickSuppressKey = listenOnce(map, 'pointerup', (mapEvent) => {
        clickSuppressKey = null;
        mapEvent.preventDefault();
        // Preventing the default only stops the emulated click; the lift still reaches
        // the interactions themselves, and an armed Draw reads it as a vertex placed
        // under the menu that has just opened. Returning false keeps it from them too.
        return false;
      });
      open(pressed);
    }, LONG_PRESS_MS);
  };

  const onPointerMove = (event: PointerEvent) => {
    if (!pressStart) {
      return;
    }
    const moved = Math.hypot(event.clientX - pressStart.clientX, event.clientY - pressStart.clientY);
    if (moved > MOVE_TOLERANCE_PX) {
      cancelPress();
    }
  };

  viewport.addEventListener('contextmenu', onContextMenu);
  viewport.addEventListener('pointerdown', onPointerDown);
  viewport.addEventListener('pointermove', onPointerMove);
  viewport.addEventListener('pointerup', cancelPress);
  viewport.addEventListener('pointercancel', cancelPress);

  return () => {
    cancelPress();
    releaseClickSuppression();
    viewport.removeEventListener('contextmenu', onContextMenu);
    viewport.removeEventListener('pointerdown', onPointerDown);
    viewport.removeEventListener('pointermove', onPointerMove);
    viewport.removeEventListener('pointerup', cancelPress);
    viewport.removeEventListener('pointercancel', cancelPress);
  };
}
