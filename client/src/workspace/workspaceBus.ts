// SPDX-License-Identifier: AGPL-3.0-or-later

// Cross-window workspace synchronization over BroadcastChannel. The bus carries
// REFERENCES only (ids, extents) — never entity payloads; each window fetches through
// its own TanStack Query cache. Same-window subscribers are invoked directly so a
// single-window session needs no channel round-trip.

import type { WorkspaceSelection } from '../stores/workspaceStore.ts';

/**
 * Which kind of view an event came from.
 *
 * The bus delivers to the publisher's own window as well as to the others, so an event with no
 * sender on it cannot be told apart from an echo of itself. Two views that follow each other need
 * that distinction; the two that do not (a pop-out publishing a pick) leave it off.
 */
export type ViewKind = 'map2d' | 'scene3d';

/** Longitude/latitude bounds in degrees, west, south, east, north — the same order both views use. */
export type ViewExtent = [number, number, number, number];

export type WorkspaceEvent =
  | { kind: 'selection'; selection: WorkspaceSelection | null; origin?: ViewKind }
  | { kind: 'fly-to'; lon: number; lat: number; zoom?: number }
  /** What one view is showing, as ground rather than as a camera: the other views frame it their own way. */
  | { kind: 'extent'; origin: ViewKind; bounds: ViewExtent; zoom: number }
  /**
   * A view that has just opened, asking where everybody else is looking.
   *
   * A window that opens on its own — the 3D scene popped out to a second monitor — has no memory
   * of anything said before it existed, and the bus retains nothing: without asking, it opens at
   * its own default and then announces that default as though its viewer had chosen it, sending
   * the window it was opened from off to the middle of nowhere. Asking costs one message and is
   * answered only by a view that has been open long enough to be the one worth following.
   */
  | { kind: 'view-hello'; origin: ViewKind };

type Listener = (event: WorkspaceEvent) => void;

const CHANNEL = 'silexgis-workspace';

const listeners = new Set<Listener>();
let channel: BroadcastChannel | null = null;

function ensureChannel(): BroadcastChannel {
  if (!channel) {
    channel = new BroadcastChannel(CHANNEL);
    channel.onmessage = (message: MessageEvent<WorkspaceEvent>) => {
      for (const listener of listeners) {
        listener(message.data);
      }
    };
  }

  return channel;
}

/** Publishes to every OTHER window; local subscribers are notified synchronously. */
export function publish(event: WorkspaceEvent): void {
  ensureChannel().postMessage(event);
  for (const listener of listeners) {
    listener(event);
  }
}

export function subscribe(listener: Listener): () => void {
  ensureChannel();
  listeners.add(listener);
  return () => listeners.delete(listener);
}
