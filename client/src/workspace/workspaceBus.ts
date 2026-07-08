// SPDX-License-Identifier: AGPL-3.0-or-later

// Cross-window workspace synchronization over BroadcastChannel. The bus carries
// REFERENCES only (ids, extents) — never entity payloads; each window fetches through
// its own TanStack Query cache. Same-window subscribers are invoked directly so a
// single-window session needs no channel round-trip.

import type { WorkspaceSelection } from '../stores/workspaceStore.ts';

export type WorkspaceEvent =
  | { kind: 'selection'; selection: WorkspaceSelection | null }
  | { kind: 'fly-to'; lon: number; lat: number; zoom?: number };

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
