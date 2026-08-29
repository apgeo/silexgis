// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ResourceRef } from './resourceRef.ts';
import {
  addressOf,
  allControls,
  canRevealHere,
  localControls,
  registerViewControl,
  resetViewControlsForTests,
  reveal,
} from './viewTargets.ts';

const feature: ResourceRef = { targetType: 'feature', targetId: 'f1' };
const station: ResourceRef = { targetType: 'surveyModel', targetId: 'm1', anchorKind: 'modelStation' };

function control(id: string, accepts: (ref: ResourceRef) => boolean = () => true) {
  const shown: ResourceRef[] = [];
  const detach = registerViewControl({
    id,
    kind: 'map2d',
    labelKey: `viewLinks.controls.${id}`,
    canReveal: accepts,
    reveal: (ref) => shown.push(ref),
  });

  return { shown, detach, address: addressOf({ id }) };
}

beforeEach(() => {
  resetViewControlsForTests();
});

describe('the views links can be sent to', () => {
  it('is a set, not a stack: every view that can show a thing is asked', () => {
    // This is the whole difference from the camera registry, which deliberately reaches only the
    // topmost view. A reader following a hyperlink beside two views expects both to answer.
    const map = control('map');
    const scene = control('scene');

    reveal(feature);

    expect(map.shown).toEqual([feature]);
    expect(scene.shown).toEqual([feature]);
  });

  it('sends to only the named control when one is named', () => {
    const map = control('map');
    const scene = control('scene');

    reveal(feature, [map.address]);

    expect(map.shown).toEqual([feature]);
    expect(scene.shown).toEqual([]);
  });

  it('asks each control again when the request arrives, not when the menu was drawn', () => {
    // A view can stop being able to show something between the card opening and the click: the
    // model viewer starts loading another cave, the map is closed. Trusting the roster would
    // make it act on a request it can no longer answer.
    let ready = false;
    const viewer = control('viewer', () => ready);

    reveal(station, [viewer.address]);
    expect(viewer.shown).toEqual([]);

    ready = true;
    reveal(station, [viewer.address]);
    expect(viewer.shown).toEqual([station]);
  });

  it('stops reaching a view once it is gone', () => {
    const map = control('map');
    map.detach();

    reveal(feature);

    expect(map.shown).toEqual([]);
    expect(localControls()).toHaveLength(0);
  });

  it('survives a detach being run twice', () => {
    // React runs an effect's cleanup and setup twice in development to surface exactly this. A
    // second cleanup that removed somebody else's registration would leave a mounted view
    // unreachable for the rest of the session, with nothing to say why.
    const first = control('map');
    first.detach();
    const second = control('map');
    first.detach();

    reveal(feature);
    expect(second.shown).toEqual([feature]);
  });

  it('reports which local controls could answer, for greying out a menu', () => {
    const map = control('map', (ref) => ref.targetType === 'feature');
    const viewer = control('viewer', (ref) => ref.targetType === 'surveyModel');

    expect(canRevealHere(feature)).toEqual(new Set([map.address]));
    expect(canRevealHere(station)).toEqual(new Set([viewer.address]));
  });

  it('addresses a control by its window as well as its own id', () => {
    // Two windows both showing the flat map register the same local id. Without the window in
    // the address, "show it in that one" would reach both.
    const map = control('map');
    expect(map.address).toContain(':map');
    expect(map.address.length).toBeGreaterThan('map'.length + 1);
  });

  it('marks its own window\'s controls as local', () => {
    control('map');
    expect(allControls().map((c) => c.local)).toEqual([true]);
  });
});

describe('what crosses to other windows', () => {
  it('publishes a reveal even when every control is in this window', async () => {
    // One path, so a window opened later behaves exactly like one that was open all along. The
    // bus delivers to its own window synchronously, which is why this is not a round trip.
    const posted: unknown[] = [];
    const channel = new BroadcastChannel('silexgis-workspace');
    channel.onmessage = (event) => posted.push(event.data);

    control('map');
    reveal(feature);
    await vi.waitFor(() => expect(posted.length).toBeGreaterThan(0));

    expect(posted).toContainEqual(expect.objectContaining({ kind: 'reveal', ref: feature }));
    channel.close();
  });
});
