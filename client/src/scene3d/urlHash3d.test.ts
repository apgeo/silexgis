// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formatMapHash, parseMapHash } from '../map/urlHash.ts';
import type { Scene3DCameraState } from './scene3dEngine.ts';
import {
  attachScene3dHash,
  formatScene3dHash,
  hasScene3dHash,
  parseScene3dHash,
} from './urlHash3d.ts';

const aPosition = { lat: 45.68321, lon: 25.30612, height: -184, heading: 137.5, pitch: -22.4 };

describe('parseScene3dHash', () => {
  it('parses a well-formed position', () => {
    expect(parseScene3dHash('#3d/45.68321/25.30612/-184/137.5/-22.4')).toEqual(aPosition);
  });

  it('accepts a camera below the surface, which is the position most worth sharing', () => {
    expect(parseScene3dHash('#3d/45.5/25.4/-620/0/-10')?.height).toBe(-620);
  });

  it('returns null for an empty, short or malformed hash', () => {
    expect(parseScene3dHash('')).toBeNull();
    expect(parseScene3dHash('#3d/45.5/25.4')).toBeNull();
    expect(parseScene3dHash('#3d/45.5/25.4/900/0/-10/7')).toBeNull();
    expect(parseScene3dHash('#3D/45.5/25.4/900/0/-10')).toBeNull();
    expect(parseScene3dHash('#/caves/42')).toBeNull();
  });

  it('rejects positions that are not on the globe', () => {
    expect(parseScene3dHash('#3d/91/25/900/0/-10')).toBeNull();
    expect(parseScene3dHash('#3d/45/181/900/0/-10')).toBeNull();
    expect(parseScene3dHash('#3d/45/25/900/0/-120')).toBeNull(); // pitch past straight down
  });

  it('folds a heading of one full turn back to zero', () => {
    expect(parseScene3dHash('#3d/45/25/900/360/-10')?.heading).toBe(0);
  });
});

describe('the two hashes cannot be confused', () => {
  // Non-collision is a property of the two regular expressions, both anchored at each end: the
  // flat map's first group takes digits only, so the literal `3d` fails it on the first character;
  // this one is anchored on that same literal, so a bare number cannot reach it. It is asserted
  // rather than assumed because "cannot collide by construction" is exactly the kind of claim that
  // stops being true when somebody relaxes an anchor.
  it('a 3D position is not a map position', () => {
    expect(parseMapHash('#3d/45.68321/25.30612/-184/137.5/-22.4')).toBeNull();
    expect(parseMapHash(formatScene3dHash(aPosition))).toBeNull();
  });

  it('a map position is not a 3D position', () => {
    expect(parseScene3dHash('#14.00/45.70000/25.30000')).toBeNull();
    expect(parseScene3dHash(formatMapHash({ zoom: 13.5, lat: 45.88, lon: 25.3 }))).toBeNull();
  });
});

describe('formatScene3dHash', () => {
  it('round-trips to about a metre and a tenth of a degree', () => {
    const written = formatScene3dHash({
      lat: 45.6832149,
      lon: 25.3061299,
      height: -184.37,
      heading: 137.55,
      pitch: -22.44,
    });
    const parsed = parseScene3dHash(written)!;
    expect(parsed.lat).toBeCloseTo(45.68321, 5);
    expect(parsed.lon).toBeCloseTo(25.30613, 5);
    expect(Math.abs(parsed.height + 184.37)).toBeLessThanOrEqual(1);
    expect(Math.abs(parsed.heading - 137.55)).toBeLessThanOrEqual(0.05);
    expect(Math.abs(parsed.pitch + 22.44)).toBeLessThanOrEqual(0.05);
  });
});

describe('attachScene3dHash', () => {
  let camera: Scene3DCameraState;
  let viewListener: (() => void) | undefined;
  let setCalls: { state: Scene3DCameraState; animate?: boolean }[];

  const engine = {
    getCamera: () => camera,
    setCamera: (state: Scene3DCameraState, options?: { animate?: boolean }) => {
      camera = state;
      setCalls.push({ state, animate: options?.animate });
    },
    onViewChanged: (listener: () => void) => {
      viewListener = listener;
      return () => {
        viewListener = undefined;
      };
    },
  };

  beforeEach(() => {
    vi.useFakeTimers();
    camera = { longitude: 0, latitude: 0, height: 100, heading: 0, pitch: -90, roll: 0 };
    setCalls = [];
    viewListener = undefined;
    window.history.replaceState(null, '', '/map3d');
  });

  afterEach(() => {
    vi.useRealTimers();
    window.history.replaceState(null, '', '/');
  });

  it('puts the camera back where a shared link says, without flying it there', () => {
    // An animated restore raises the camera's settled event part-way through, and the write-back
    // would then record a position the camera was merely passing through.
    window.history.replaceState(null, '', `/map3d${formatScene3dHash(aPosition)}`);

    const detach = attachScene3dHash(engine);

    expect(setCalls).toHaveLength(1);
    expect(setCalls[0].animate).toBe(false);
    expect(setCalls[0].state.latitude).toBeCloseTo(aPosition.lat, 5);
    expect(setCalls[0].state.height).toBe(aPosition.height);
    detach();
  });

  it('writes the camera into the URL once it has come to rest', () => {
    const detach = attachScene3dHash(engine);
    camera = { longitude: 25.4, latitude: 45.6, height: -300, heading: 12, pitch: -40, roll: 0 };

    viewListener?.();
    expect(window.location.hash).toBe(''); // still moving
    vi.advanceTimersByTime(300);

    expect(parseScene3dHash(window.location.hash)).toEqual({
      lat: 45.6,
      lon: 25.4,
      height: -300,
      heading: 12,
      pitch: -40,
    });
    detach();
  });

  it('never reads back what it wrote', () => {
    // The restore happens once, on attach. Nothing listens for later changes to the hash, so the
    // write-back cannot be re-consumed as an instruction — which is what a hashchange listener
    // would turn it into.
    const detach = attachScene3dHash(engine);
    camera = { longitude: 25.4, latitude: 45.6, height: -300, heading: 12, pitch: -40, roll: 0 };
    viewListener?.();
    vi.advanceTimersByTime(300);

    const movesAfterWriting = setCalls.length;
    window.dispatchEvent(new Event('hashchange'));
    vi.advanceTimersByTime(1000);

    expect(setCalls).toHaveLength(movesAfterWriting);
    detach();
  });

  it('adds no history entries as the viewer navigates', () => {
    const replace = vi.spyOn(window.history, 'replaceState');
    const push = vi.spyOn(window.history, 'pushState');
    const detach = attachScene3dHash(engine);

    camera = { longitude: 25.4, latitude: 45.6, height: -300, heading: 12, pitch: -40, roll: 0 };
    viewListener?.();
    vi.advanceTimersByTime(300);

    expect(replace).toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();
    detach();
  });

  it('stops writing once the view goes away', () => {
    const detach = attachScene3dHash(engine);
    camera = { longitude: 25.4, latitude: 45.6, height: -300, heading: 12, pitch: -40, roll: 0 };
    viewListener?.();
    detach();

    vi.advanceTimersByTime(1000);

    expect(window.location.hash).toBe('');
  });
});

describe('hasScene3dHash', () => {
  afterEach(() => window.history.replaceState(null, '', '/'));

  it('sees a shared 3D position in the address bar', () => {
    window.history.replaceState(null, '', `/${formatScene3dHash(aPosition)}`);
    expect(hasScene3dHash()).toBe(true);
  });

  it('does not mistake a flat-map position for one', () => {
    window.history.replaceState(null, '', '/#14.00/45.70000/25.30000');
    expect(hasScene3dHash()).toBe(false);
  });
});
