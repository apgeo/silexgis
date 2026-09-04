// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { WorkspaceSelection } from '../stores/workspaceStore.ts';
import { attachViewSync, resetViewSyncMemory, sameSelection, type ViewExtent } from './viewSync.ts';

const romania: ViewExtent = [25.2, 45.6, 25.4, 45.8];
const elsewhere: ViewExtent = [22.1, 45.1, 22.4, 45.4];

/**
 * A view that answers an extent the way a real one does: never with the extent it was given.
 *
 * That is the whole difficulty. The flat map fits a box with padding and a zoom cap, the scene
 * sizes its box off the middle of a tilted screen — so there is no value the two agree on, and a
 * pair of views that simply echoed each other would drift apart for ever rather than settle.
 */
function noisyView(kind: 'map2d' | 'scene3d') {
  const seen: { bounds: ViewExtent; zoom: number }[] = [];
  const sync = attachViewSync(kind, {
    onExtent: (bounds, zoom) => {
      seen.push({ bounds, zoom });
      // Applying the box moves this view's camera, which is what its own publisher watches for.
      sync.publishExtent(bounds.map((n) => n + 0.01) as ViewExtent, zoom);
    },
  });
  return { seen, sync };
}

beforeEach(() => {
  vi.useFakeTimers();
  resetViewSyncMemory();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('extent, both ways, without a loop', () => {
  it('reaches the other view in one hop and stops there', () => {
    const map = noisyView('map2d');
    const scene = noisyView('scene3d');

    map.sync.publishExtent(romania, 14);

    // The scene followed; the flat map did not follow its own announcement, and the scene's own
    // resulting move was not announced back because it was following at the time.
    expect(scene.seen).toHaveLength(1);
    expect(scene.seen[0].bounds).toEqual(romania);
    expect(map.seen).toHaveLength(0);

    map.sync.detach();
    scene.sync.detach();
  });

  it('lets the follower speak again once its move has settled', () => {
    const map = noisyView('map2d');
    const scene = noisyView('scene3d');
    map.sync.publishExtent(romania, 14);
    expect(map.seen).toHaveLength(0);

    // The viewer now navigates the scene themselves, well after it stopped following.
    vi.advanceTimersByTime(1500);
    scene.sync.publishExtent(elsewhere, 11);

    expect(map.seen).toHaveLength(1);
    expect(map.seen[0].bounds).toEqual(elsewhere);
    map.sync.detach();
    scene.sync.detach();
  });

  it('does not sync two views of the same kind to each other', () => {
    // Two flat maps have no fixed point to converge on and nothing to say which should give way,
    // so they would drift against each other indefinitely. Deliberately not synced.
    const one = noisyView('map2d');
    const two = noisyView('map2d');

    one.sync.publishExtent(romania, 14);

    expect(two.seen).toHaveLength(0);
    one.sync.detach();
    two.sync.detach();
  });

  it('opens a late view on the ground the others are already showing', async () => {
    const map = noisyView('map2d');
    map.sync.publishExtent(romania, 14);

    const scene = noisyView('scene3d');
    // Delivered a turn later, so the view is holding its handle before anything calls back in.
    expect(scene.seen).toHaveLength(0);
    await Promise.resolve();

    expect(scene.seen).toEqual([{ bounds: romania, zoom: 14 }]);
    map.sync.detach();
    scene.sync.detach();
  });

  it('does not open a view that went away before the turn came round', async () => {
    const map = noisyView('map2d');
    map.sync.publishExtent(romania, 14);

    const scene = noisyView('scene3d');
    scene.sync.detach();
    await Promise.resolve();

    expect(scene.seen).toHaveLength(0);
    map.sync.detach();
  });

  it('keeps a view quiet for a move it was given rather than one its viewer made', () => {
    // Opening a saved view. The document says where both views stand and each is put back by the
    // code that reads it, so neither move is news — and a map that announced its restored position
    // would send the scene off to frame the map's box, undoing the camera the same document had
    // just restored.
    const map = noisyView('map2d');
    const scene = noisyView('scene3d');

    map.sync.muteUntilSettled();
    map.sync.publishExtent(romania, 14);

    expect(scene.seen).toHaveLength(0);

    // And it is a settling period, not a switch: the viewer's next move is shared as usual.
    vi.advanceTimersByTime(1500);
    map.sync.publishExtent(elsewhere, 11);
    expect(scene.seen).toEqual([{ bounds: elsewhere, zoom: 11 }]);

    map.sync.detach();
    scene.sync.detach();
  });

  it('asks the other windows where to look instead of announcing where it opened', async () => {
    // A pop-out window. The bus reaches it, but the memory of the exchange is per-window, so
    // without asking it would open at its own default, announce that default as though its viewer
    // had chosen it, and drag the window it was opened from out to the middle of nowhere.
    const heardByMap: ViewExtent[] = [];
    const map = attachViewSync('map2d', {
      onExtent: (bounds) => heardByMap.push(bounds),
      currentExtent: () => ({ bounds: romania, zoom: 14 }),
    });
    // Long enough to be the view worth following rather than one still finding its feet.
    vi.advanceTimersByTime(1500);
    // Standing in for the second window: same bus, none of this window's memory.
    resetViewSyncMemory();

    const seen: { bounds: ViewExtent; zoom: number }[] = [];
    const scene = attachViewSync('scene3d', {
      onExtent: (bounds, zoom) => seen.push({ bounds, zoom }),
      currentExtent: () => ({ bounds: elsewhere, zoom: 8 }),
    });
    await Promise.resolve();

    expect(seen).toEqual([{ bounds: romania, zoom: 14 }]);
    // And the fresh view's own opening position never went out: it did not answer its own
    // question, and following the answer keeps it quiet while it moves.
    expect(heardByMap).toHaveLength(0);
    scene.publishExtent(elsewhere, 8);
    expect(heardByMap).toHaveLength(0);

    map.detach();
    scene.detach();
  });

  it('does not answer for the others while it has only just opened itself', async () => {
    // Two windows opened together. Neither is the established one, so neither places the other at
    // a default it has no reason to prefer; each stays where its own URL or saved view put it.
    const heardByMap: ViewExtent[] = [];
    const map = attachViewSync('map2d', {
      onExtent: (bounds) => heardByMap.push(bounds),
      currentExtent: () => ({ bounds: romania, zoom: 14 }),
    });
    const seen: ViewExtent[] = [];
    const scene = attachViewSync('scene3d', {
      onExtent: (bounds) => seen.push(bounds),
      currentExtent: () => ({ bounds: elsewhere, zoom: 8 }),
    });

    await Promise.resolve();

    expect(seen).toHaveLength(0);
    expect(heardByMap).toHaveLength(0);
    map.detach();
    scene.detach();
  });

  it('says nothing more after it is detached', () => {
    const map = noisyView('map2d');
    const scene = noisyView('scene3d');
    scene.sync.detach();

    map.sync.publishExtent(romania, 14);

    expect(scene.seen).toHaveLength(0);
    map.sync.detach();
  });
});

describe('selection, both ways, without an echo', () => {
  const cave: WorkspaceSelection = { kind: 'cave', caveId: 'cave-1' };

  it('reaches the other view and not the one that published it', () => {
    const heardByMap: (WorkspaceSelection | null)[] = [];
    const heardByScene: (WorkspaceSelection | null)[] = [];
    const map = attachViewSync('map2d', { onSelection: (s) => heardByMap.push(s) });
    const scene = attachViewSync('scene3d', { onSelection: (s) => heardByScene.push(s) });

    map.publishSelection(cave);

    expect(heardByScene).toEqual([cave]);
    expect(heardByMap).toHaveLength(0);
    map.detach();
    scene.detach();
  });

  it('ignores a repeat of what it already believes', () => {
    // Each view builds a fresh object for every pick, so reference equality would report every
    // echo as a change and re-render the detail panel for nothing.
    const heard: (WorkspaceSelection | null)[] = [];
    const map = attachViewSync('map2d', { onSelection: (s) => heard.push(s) });
    const scene = attachViewSync('scene3d', {});

    scene.publishSelection({ kind: 'cave', caveId: 'cave-1' });
    scene.publishSelection({ kind: 'cave', caveId: 'cave-1' });
    scene.publishSelection({ kind: 'cave', caveId: 'cave-2' });

    expect(heard).toEqual([{ kind: 'cave', caveId: 'cave-1' }, { kind: 'cave', caveId: 'cave-2' }]);
    map.detach();
    scene.detach();
  });

  it('delivers a repeat once the view has been changed from somewhere off the bus', () => {
    // The workspace selection is also written by panels that draw no map and announce nothing —
    // a row clicked in the features table, a feature deleted in the detail panel. A view that
    // remembered only what it had been told over the bus would compare the next arrival against
    // a selection it no longer holds and drop it, leaving the window showing something else with
    // no way back to that cave except picking a different one first.
    let showing: WorkspaceSelection | null = null;
    const map = attachViewSync('map2d', {
      onSelection: (s) => {
        showing = s;
      },
      currentSelection: () => showing,
    });
    const scene = attachViewSync('scene3d', {});

    scene.publishSelection({ kind: 'cave', caveId: 'cave-1' });
    expect(showing).toEqual({ kind: 'cave', caveId: 'cave-1' });

    // The panel beside the map picks something else, without publishing anything.
    showing = { kind: 'feature', featureId: 'feature-9' };
    scene.publishSelection({ kind: 'cave', caveId: 'cave-1' });

    expect(showing).toEqual({ kind: 'cave', caveId: 'cave-1' });
    map.detach();
    scene.detach();
  });

  it('reaches another window showing the same kind of view', () => {
    // A pick has one exact value, so unlike an extent it has somewhere to converge: the second
    // delivery finds the view already showing it and stops there. Filtering these out by kind
    // would leave two windows of the same view disagreeing about what is selected for as long as
    // they were both open.
    let showing: WorkspaceSelection | null = null;
    const other = attachViewSync('scene3d', {
      onSelection: (s) => {
        showing = s;
      },
      currentSelection: () => showing,
    });
    const scene = attachViewSync('scene3d', {});

    scene.publishSelection(cave);

    expect(showing).toEqual(cave);
    other.detach();
    scene.detach();
  });

  it('carries a cleared selection', () => {
    const heard: (WorkspaceSelection | null)[] = [];
    const map = attachViewSync('map2d', { onSelection: (s) => heard.push(s) });
    const scene = attachViewSync('scene3d', {});

    scene.publishSelection(cave);
    scene.publishSelection(null);

    expect(heard).toEqual([cave, null]);
    map.detach();
    scene.detach();
  });
});

describe('sameSelection', () => {
  it('compares by value across every kind', () => {
    expect(sameSelection(null, null)).toBe(true);
    expect(sameSelection({ kind: 'cave', caveId: 'a' }, { kind: 'cave', caveId: 'a' })).toBe(true);
    expect(sameSelection({ kind: 'cave', caveId: 'a' }, { kind: 'cave', caveId: 'b' })).toBe(false);
    expect(sameSelection({ kind: 'cave', caveId: 'a' }, null)).toBe(false);
    expect(sameSelection({ kind: 'feature', featureId: 'f' }, { kind: 'cave', caveId: 'f' })).toBe(false);
    expect(
      sameSelection(
        { kind: 'entrance', entranceId: 'e', caveId: 'c' },
        { kind: 'entrance', entranceId: 'e', caveId: 'c' },
      ),
    ).toBe(true);
    expect(
      sameSelection(
        { kind: 'cluster', lon: 25, lat: 45, count: 4, zoom: 9 },
        { kind: 'cluster', lon: 25, lat: 45, count: 4, zoom: 10 },
      ),
    ).toBe(false);
  });
});

describe('a view that has stepped out of the extent exchange', () => {
  /**
   * A view whose participation is switchable, and which reports what it was told rather than
   * echoing — the noise above is about loop-freedom and only gets in the way here.
   */
  function switchableView(kind: 'map2d' | 'scene3d', coupled = { yes: true }) {
    const seen: { bounds: ViewExtent; zoom: number }[] = [];
    const sync = attachViewSync(
      kind,
      { onExtent: (bounds, zoom) => seen.push({ bounds, zoom }), currentExtent: () => here },
      { followsExtent: () => coupled.yes },
    );
    let here: { bounds: ViewExtent; zoom: number } | undefined;
    return {
      seen,
      sync,
      coupled,
      standAt: (bounds: ViewExtent, zoom: number) => {
        here = { bounds, zoom };
      },
    };
  }

  it('does not follow the other view', () => {
    const scene = switchableView('scene3d', { yes: false });
    const map = noisyView('map2d');

    map.sync.publishExtent(romania, 14);

    expect(scene.seen).toHaveLength(0);
  });

  it('does not drag the other view along behind it either', () => {
    // The half that is easy to leave out. A view that stops following but keeps announcing is
    // still coupled — in one direction — and the other view goes on chasing it, which is exactly
    // what somebody who switched coupling off was trying to stop.
    const scene = switchableView('scene3d', { yes: false });
    const map = noisyView('map2d');

    scene.sync.publishExtent(elsewhere, 12);

    expect(map.seen).toHaveLength(0);
  });

  it('does not answer a view that opens and asks where to look', async () => {
    // The third way out, and the one with no bus message of its own to notice: the answer to
    // `view-hello` deliberately bypasses the follow-mute, so a gate placed only on the ordinary
    // publish would let an uncoupled scene place a newly opened window at its own private camera.
    //
    // The await matters and is not tidiness. The question a new view asks goes out on a microtask,
    // so a synchronous body asserts before it has been asked and the test passes without the
    // exchange ever happening — which it did, until removing the gate failed to turn it red.
    const scene = switchableView('scene3d', { yes: false });
    scene.standAt(elsewhere, 12);
    vi.advanceTimersByTime(2000); // past the join grace, so it would otherwise be entitled to answer

    const map = noisyView('map2d');
    await Promise.resolve();

    expect(map.seen).toHaveLength(0);
  });

  it('leaves no trace for the next view to open to be placed by', async () => {
    // A view moving on its own must not write itself into the memory that places later views, or
    // the uncoupling leaks out through the next window instead of through the bus. Awaited for the
    // same reason as above: the replay that would carry the trace happens on a microtask.
    const scene = switchableView('scene3d', { yes: false });
    scene.sync.publishExtent(elsewhere, 12);

    const map = noisyView('map2d');
    await Promise.resolve();

    expect(map.seen).toHaveLength(0);
  });

  it('still passes selections across', () => {
    // Uncoupling the cameras is not asking to stop sharing what is picked; the detail panel
    // beside the map is fed by this channel and must not go dead.
    const picked: (WorkspaceSelection | null)[] = [];
    const coupled = { yes: false };
    attachViewSync('map2d', { onSelection: (s) => picked.push(s) }, { followsExtent: () => coupled.yes });
    const scene = attachViewSync('scene3d', {}, { followsExtent: () => coupled.yes });

    scene.publishSelection({ kind: 'feature', featureId: 'f-1' });

    expect(picked).toEqual([{ kind: 'feature', featureId: 'f-1' }]);
  });

  it('comes back to where the other view is standing when it rejoins', () => {
    const coupled = { yes: true };
    const scene = switchableView('scene3d', coupled);
    const map = noisyView('map2d');

    // The map moves while the scene is out of the exchange, so the scene does not see it.
    coupled.yes = false;
    map.sync.publishExtent(romania, 14);
    expect(scene.seen).toHaveLength(0);

    // Rejoining is what closes the gap. The scene goes to the map, not the other way round.
    coupled.yes = true;
    scene.sync.rejoin();

    expect(scene.seen).toHaveLength(1);
    expect(scene.seen[0].bounds).toEqual(romania);
    expect(scene.seen[0].zoom).toBe(14);
  });

  it('asks where to look when it rejoins and this window remembers nothing', () => {
    // A window opened while already uncoupled has no memory of the exchange to replay, so the
    // rejoin has to be a question rather than a recall — and the answer comes from another window.
    const coupled = { yes: false };
    const scene = switchableView('scene3d', coupled);
    const map = switchableView('map2d');
    map.standAt(romania, 13);
    vi.advanceTimersByTime(2000); // the map is established and may answer

    coupled.yes = true;
    scene.sync.rejoin();

    expect(scene.seen).toEqual([{ bounds: romania, zoom: 13 }]);
  });
});
