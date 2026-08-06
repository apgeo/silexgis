// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import { claimSceneSurface, sceneSurfaceClaimCount, sceneSurfaceElement } from './sceneSurface.ts';

const released: (() => void)[] = [];

function claim(slot: HTMLElement, onHeld?: (held: boolean) => void) {
  const release = claimSceneSurface(slot, onHeld);
  released.push(release);
  return release;
}

function slot(): HTMLElement {
  return document.createElement('div');
}

afterEach(() => {
  for (const release of released.splice(0)) {
    release();
  }
  expect(sceneSurfaceClaimCount()).toBe(0);
});

describe('the window has one drawing surface', () => {
  it('and it is the same element however many views ask for it', () => {
    // This is what makes two scenes in one window impossible rather than merely unlikely: whoever
    // builds a scene is always handed this element, so there is never a second one to build in.
    const first = sceneSurfaceElement();
    claim(slot());
    claim(slot());
    expect(sceneSurfaceElement()).toBe(first);
  });

  it('moves into whichever view asked for it last', () => {
    const route = slot();
    const panel = slot();

    claim(route);
    expect(route.firstElementChild).toBe(sceneSurfaceElement());

    claim(panel);
    expect(panel.firstElementChild).toBe(sceneSurfaceElement());
    expect(route.childElementCount).toBe(0);
  });

  it('hands it back to the previous view when the newer one goes away', () => {
    // A route change mounts the arriving view before the leaving one tears down. A plain
    // last-writer-wins slot would leave the surface homeless the moment the leaving view's
    // cleanup ran, which is the ordinary case rather than an edge one.
    const route = slot();
    const panel = slot();
    claim(route);
    const releasePanel = claim(panel);

    releasePanel();

    expect(route.firstElementChild).toBe(sceneSurfaceElement());
  });

  it('does not take the surface off a view that has it', () => {
    const route = slot();
    const panel = slot();
    const releaseRoute = claim(route);
    claim(panel);

    releaseRoute(); // the older view leaves while the newer one is showing the scene

    expect(panel.firstElementChild).toBe(sceneSurfaceElement());
  });

  it('takes the surface out of the document when nobody is showing it', () => {
    const only = slot();
    const release = claim(only);

    release();

    expect(only.childElementCount).toBe(0);
    expect(sceneSurfaceElement().parentElement).toBeNull();
  });

  it('tells a view when it gains and loses the surface', () => {
    // So a view whose box is empty can say why, instead of showing an unexplained rectangle.
    const routeHeld: boolean[] = [];
    claim(slot(), (held) => routeHeld.push(held));
    expect(routeHeld).toEqual([true]);

    const releasePanel = claim(slot());
    expect(routeHeld).toEqual([true, false]);

    releasePanel();
    expect(routeHeld).toEqual([true, false, true]);
  });

  it('survives a release that runs twice', () => {
    // React runs an effect's cleanup twice in development to surface exactly this.
    const only = slot();
    const release = claim(only);
    release();
    release();
    expect(sceneSurfaceClaimCount()).toBe(0);
  });
});
