// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { WorkspaceEvent } from '../workspace/workspaceBus.ts';

/**
 * Two windows of the app, each with its own copy of the roster module, joined by a stand-in for
 * the BroadcastChannel: a window hears its own messages at once and the other window's one task
 * later, which is how the real channel behaves on one machine.
 */
type Listener = (event: WorkspaceEvent) => void;
const hub = { windows: [] as Set<(event: unknown) => void>[], sent: [] as string[] };

function busOfOneWindow() {
  const own = new Set<Listener>();
  hub.windows.push(own as Set<(event: unknown) => void>);
  return {
    publish(event: WorkspaceEvent) {
      hub.sent.push(event.kind);
      for (const other of hub.windows) {
        if (other !== own) {
          setTimeout(() => {
            for (const listener of other) {
              listener(event);
            }
          }, 0);
        }
      }
      for (const listener of own) {
        listener(event);
      }
    },
    subscribe(listener: Listener) {
      own.add(listener);
      return () => own.delete(listener);
    },
  };
}

// A fresh module graph per window, each with a bus of its own on the shared hub.
async function openWindow() {
  vi.resetModules();
  vi.doMock('../workspace/workspaceBus.ts', busOfOneWindow);
  return import('./viewTargets.ts');
}

afterEach(() => {
  hub.windows.length = 0;
  hub.sent.length = 0;
});

describe('the roster between two windows', () => {
  it('settles after one roll call instead of asking again for every answer', async () => {
    const reader = await openWindow();
    const mapWindow = await openWindow();
    mapWindow.registerViewControl({
      id: 'map',
      kind: 'map2d',
      labelKey: 'viewLinks.controls.map',
      canReveal: () => true,
      reveal: () => undefined,
    });

    // What a reading panel does: read the roster when it opens, and follow it while it is open.
    let reads = 0;
    reader.subscribeToRoster(() => {
      reads += 1;
      void reader.refreshRoster();
    });
    const first = await reader.refreshRoster();
    await new Promise((resolve) => setTimeout(resolve, 500));

    expect(first.map((control) => control.address)).toEqual([mapWindow.addressOf({ id: 'map' })]);
    // One answer that changed nothing is not news: the panel is told once, when the map window
    // first answered, and the windows fall quiet rather than trading roll calls for as long as
    // the text stays open.
    expect(reads).toBeLessThanOrEqual(1);
    expect(hub.sent.filter((kind) => kind === 'controls-roll-call').length).toBeLessThanOrEqual(2);
  });

  it('still tells a reader when the other window gains or loses a view', async () => {
    const reader = await openWindow();
    const mapWindow = await openWindow();
    let reads = 0;
    reader.subscribeToRoster(() => {
      reads += 1;
    });

    const detach = mapWindow.registerViewControl({
      id: 'map',
      kind: 'map2d',
      labelKey: 'viewLinks.controls.map',
      canReveal: () => true,
      reveal: () => undefined,
    });
    await vi.waitFor(() => expect(reader.allControls()).toHaveLength(1));
    detach();
    await vi.waitFor(() => expect(reader.allControls()).toHaveLength(0));
    expect(reads).toBe(2);
  });
});
