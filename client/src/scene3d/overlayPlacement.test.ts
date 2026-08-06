// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { placeOverlay } from './overlayPlacement.ts';

const surface = { widthPixels: 1000, heightPixels: 600 };
const element = { widthPixels: 200, heightPixels: 40 };
const base = { surface, element, offsetPixels: 10 };

describe('placeOverlay', () => {
  it('sits to the right of the anchor and level with it', () => {
    const placement = placeOverlay({ ...base, screen: { x: 400, y: 300 } });
    expect(placement).toEqual({ visible: true, left: 410, top: 280, side: 'right' });
  });

  it('flips to the left rather than sliding off its own anchor', () => {
    // 900 + 10 + 200 runs past the right edge, so the whole box moves to the other side of the
    // anchor. Clamping instead would leave the label pointing at whatever it came to rest over.
    const placement = placeOverlay({ ...base, screen: { x: 900, y: 300 } });
    expect(placement.side).toBe('left');
    expect(placement.left).toBe(690);
  });

  it('is nudged back onto the surface vertically rather than flipped', () => {
    const top = placeOverlay({ ...base, screen: { x: 400, y: 5 } });
    expect(top.top).toBe(0);
    const bottom = placeOverlay({ ...base, screen: { x: 400, y: 595 } });
    expect(bottom.top).toBe(560);
  });

  it('shows nothing when the engine has no answer for the anchor', () => {
    expect(placeOverlay({ ...base, screen: undefined }).visible).toBe(false);
  });

  it('shows nothing for an anchor that projects to a nonsense pixel', () => {
    // The engine answers with numbers, not with a promise that they are numbers: a degenerate
    // camera can produce a division that is not finite, and a transform built from one silently
    // parks the element at the origin instead of hiding it.
    expect(placeOverlay({ ...base, screen: { x: Number.NaN, y: 10 } }).visible).toBe(false);
    expect(placeOverlay({ ...base, screen: { x: 10, y: Number.POSITIVE_INFINITY } }).visible).toBe(
      false,
    );
  });

  it('keeps an anchor that is only just off the edge, and drops one that is well past it', () => {
    // The engine tests no bounds at all, so an anchor behind the viewer or off to one side comes
    // back as an ordinary pixel outside the surface. A little slack stops a label flickering as
    // the camera moves; much more and it would be pinned to the border naming something nobody
    // can see.
    const near = placeOverlay({ ...base, screen: { x: -20, y: 300 }, marginPixels: 48 });
    expect(near.visible).toBe(true);
    const far = placeOverlay({ ...base, screen: { x: -400, y: 300 }, marginPixels: 48 });
    expect(far.visible).toBe(false);
    const below = placeOverlay({ ...base, screen: { x: 400, y: 900 }, marginPixels: 48 });
    expect(below.visible).toBe(false);
  });

  it('puts an element wider than the surface at the left edge rather than off the page', () => {
    const placement = placeOverlay({
      screen: { x: 20, y: 20 },
      surface: { widthPixels: 150, heightPixels: 100 },
      element: { widthPixels: 300, heightPixels: 40 },
      offsetPixels: 10,
    });
    expect(placement.visible).toBe(true);
    expect(placement.left).toBe(0);
  });

  it('places against a surface that has not been laid out yet without producing a negative', () => {
    // The first placement happens before the browser has painted, when a freshly mounted element
    // measures zero in both axes and the surface may too.
    const placement = placeOverlay({
      screen: { x: 0, y: 0 },
      surface: { widthPixels: 0, heightPixels: 0 },
      element: { widthPixels: 0, heightPixels: 0 },
      offsetPixels: 10,
    });
    expect(placement.left).toBe(0);
    expect(placement.top).toBe(0);
  });
});

describe('placeOverlay around the scene\'s own controls', () => {
  // A phone: the two trigger buttons in the top-right corner, and a label most of the width of the
  // screen. This is the arrangement in which a label lands on the buttons in ordinary use.
  const phone = { widthPixels: 412, heightPixels: 915 };
  const label = { widthPixels: 288, heightPixels: 56 };
  const controls = { leftPixels: 320, topPixels: 8, widthPixels: 84, heightPixels: 40 };
  const onAPhone = { surface: phone, element: label, offsetPixels: 14, reserved: controls };

  it('drops below the controls rather than being laid over them', () => {
    // Without this the label spans x 74..362 and y 2..58, which covers both buttons: the viewer
    // sees two controls they cannot read and, depending on which of the two is drawn on top, taps
    // that either do nothing or dismiss the label instead of opening what they aimed at.
    const placement = placeOverlay({ ...onAPhone, screen: { x: 60, y: 30 } });

    expect(placement.top).toBe(48);
    // Still beside its anchor rather than moved across it: the label names the marker under it,
    // and sliding sideways out of the corner would hide the very thing being named.
    expect(placement.left).toBe(74);
  });

  it('leaves a label that was never near the controls exactly where it was', () => {
    const placement = placeOverlay({ ...onAPhone, screen: { x: 60, y: 400 } });

    expect(placement).toEqual({ visible: true, left: 74, top: 372, side: 'right' });
  });

  it('climbs above the controls when that is the nearer way out', () => {
    // A strip down the whole right edge, as the desktop camera controls are: an anchor near its
    // bottom is closer to open space above than below.
    const strip = { leftPixels: 950, topPixels: 40, widthPixels: 42, heightPixels: 300 };
    const placement = placeOverlay({
      screen: { x: 780, y: 60 },
      surface: { widthPixels: 1000, heightPixels: 600 },
      element: { widthPixels: 200, heightPixels: 40 },
      offsetPixels: 10,
      reserved: strip,
    });

    expect(placement.top).toBe(0);
  });

  it('slides sideways when there is no room above or below', () => {
    // A short view whose controls run its full height — nothing above, nothing below.
    const placement = placeOverlay({
      screen: { x: 300, y: 30 },
      surface: { widthPixels: 500, heightPixels: 60 },
      element: { widthPixels: 150, heightPixels: 40 },
      offsetPixels: 10,
      reserved: { leftPixels: 440, topPixels: 0, widthPixels: 60, heightPixels: 60 },
    });

    expect(placement.left).toBe(290);
  });

  it('stays put when it cannot get clear at all, and lets the controls be drawn over it', () => {
    // A label as wide as the surface has nowhere to go. Leaving it is the honest answer: the
    // controls are drawn above this and take their own presses, so the cost is a partly hidden
    // label rather than a button that cannot be pressed.
    const placement = placeOverlay({
      screen: { x: 100, y: 30 },
      surface: { widthPixels: 400, heightPixels: 60 },
      element: { widthPixels: 400, heightPixels: 56 },
      offsetPixels: 14,
      reserved: { leftPixels: 320, topPixels: 8, widthPixels: 80, heightPixels: 40 },
    });

    expect(placement.visible).toBe(true);
    expect(placement.left).toBe(0);
    expect(placement.top).toBe(2);
  });

  it('places as if nothing were reserved when the controls have not been laid out', () => {
    // A control group that is not on screen yet measures nothing, and an empty patch must not
    // push a label about.
    const placement = placeOverlay({
      ...onAPhone,
      screen: { x: 60, y: 30 },
      reserved: { leftPixels: 0, topPixels: 0, widthPixels: 0, heightPixels: 0 },
    });

    expect(placement).toEqual({ visible: true, left: 74, top: 2, side: 'right' });
  });
});
