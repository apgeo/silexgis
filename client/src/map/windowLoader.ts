// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type View from 'ol/View';
import { transformExtent } from 'ol/proj';

/** A map window in longitude/latitude: west, south, east, north. */
export type LonLatWindow = [number, number, number, number];

/** How long the map has to stay still before its window is asked for. */
export const WINDOW_LOAD_DELAY_MS = 250;

export interface WindowLoaderOptions<T> {
  /** Whether the layer wants answers at all; checked before asking and again when the answer lands. */
  isEnabled: () => boolean;
  /** Called once a request has been started, before it is answered. */
  onStart: () => void;
  /** Asks for the window; the signal is aborted when a newer window supersedes this one. */
  fetch: (window: LonLatWindow, view: View, signal: AbortSignal) => Promise<T>;
  /** The answer for the window still on screen. */
  onAnswer: (answer: T) => void;
  /** A failure for the window still on screen. */
  onError: (error: unknown) => void;
}

export interface WindowLoader {
  /** Asks for the current window at once. */
  reload: () => void;
  /** Stops listening, cancels the pending delay and aborts the request in flight. */
  detach: () => void;
}

/**
 * Loads a layer for the map's window whenever the map comes to rest.
 *
 * The rules every windowed overlay needs, kept in one place so that the layers cannot drift
 * apart: a pan waits for the map to stay still before asking; a newer window aborts the request
 * it supersedes, so a slow answer for a window already left can never be drawn over the one on
 * screen; and an answer or failure that lands after the layer was switched off is dropped, since
 * a hidden layer states nothing.
 */
export function attachWindowLoader<T>(map: Map, options: WindowLoaderOptions<T>): WindowLoader {
  let timer: number | undefined;
  let inFlight: AbortController | undefined;

  const load = async () => {
    if (!options.isEnabled()) {
      return;
    }
    const size = map.getSize();
    if (!size) {
      return;
    }
    const view = map.getView();
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    inFlight?.abort();
    const controller = new AbortController();
    inFlight = controller;
    options.onStart();
    try {
      const answer = await options.fetch([extent[0], extent[1], extent[2], extent[3]], view, controller.signal);
      if (controller.signal.aborted || !options.isEnabled()) {
        return;
      }
      options.onAnswer(answer);
    } catch (error) {
      if (controller.signal.aborted || !options.isEnabled()) {
        return;
      }
      options.onError(error);
    } finally {
      if (inFlight === controller) {
        inFlight = undefined;
      }
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), WINDOW_LOAD_DELAY_MS);
  };

  map.on('moveend', onMoveEnd);
  return {
    reload: () => void load(),
    detach: () => {
      map.un('moveend', onMoveEnd);
      window.clearTimeout(timer);
      inFlight?.abort();
    },
  };
}
