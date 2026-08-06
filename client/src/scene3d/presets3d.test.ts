// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { metersBetween, normalizeCamera3DState, type Camera3DState } from './camera3d.ts';
import { activePreset, CARDINAL_PITCH_DEGREES, presetCamera } from './presets3d.ts';

const looking: Camera3DState = {
  eye: { lon: 25.3, lat: 45.68, height: 1800 },
  heading: 20,
  pitch: -45,
  roll: 7,
  target: { lon: 25.31, lat: 45.7, height: 900 },
  projection: 'perspective',
};

describe('camera presets', () => {
  it('turns the camera around what it is already looking at', () => {
    const top = presetCamera('top', looking);
    expect(top.target).toEqual(looking.target);
    expect(top.eye.lon).toBeCloseTo(looking.target!.lon, 6);
    expect(top.eye.lat).toBeCloseTo(looking.target!.lat, 6);
    expect(top.pitch).toBe(-90);
  });

  it('keeps the viewer as far from the cave as they already were', () => {
    // A preset changes the direction the cave is seen from and nothing else. Moving the camera in
    // as well would make it a "zoom to" button, and the viewer would have to find their place
    // again after every press.
    const before = metersBetween(looking.eye, looking.target!);
    for (const preset of ['top', 'north', 'south', 'east', 'west'] as const) {
      const after = presetCamera(preset, looking);
      expect(metersBetween(after.eye, after.target!)).toBeCloseTo(before, 0);
    }
  });

  it('puts the viewer on the side of the cave the preset is named after', () => {
    const north = presetCamera('north', looking);
    expect(north.eye.lat).toBeGreaterThan(looking.target!.lat);
    expect(north.heading).toBe(180); // standing north, facing south

    const east = presetCamera('east', looking);
    expect(east.eye.lon).toBeGreaterThan(looking.target!.lon);
    expect(east.heading).toBe(270);

    const west = presetCamera('west', looking);
    expect(west.eye.lon).toBeLessThan(looking.target!.lon);

    const south = presetCamera('south', looking);
    expect(south.eye.lat).toBeLessThan(looking.target!.lat);
  });

  it('tilts the compass views slightly down rather than dead level', () => {
    // Not aesthetic. A camera looking exactly at the horizon has sky in the middle of the screen,
    // and the middle of the screen decides which patch of ground gets requested and where the next
    // preset pivots — so a level view would ask the server about the limb of the planet.
    const north = presetCamera('north', looking);
    expect(north.pitch).toBe(CARDINAL_PITCH_DEGREES);
    expect(north.pitch).toBeLessThan(0);
    expect(north.eye.height).toBeGreaterThan(looking.target!.height);
  });

  it('undoes any tilt of the camera about its own axis', () => {
    expect(presetCamera('north', looking).roll).toBe(0);
  });

  it('works from a camera that is looking at nothing', () => {
    // Pointed at the sky there is no ground in the middle of the screen, and the pivot falls back
    // to the point below the camera. Reporting "no preset is possible" instead would leave the
    // buttons dead exactly when a viewer who has lost their bearings most wants them.
    const atTheSky: Camera3DState = {
      eye: { lon: 25.3, lat: 45.7, height: 2000 },
      heading: 0,
      pitch: 20,
      roll: 0,
      projection: 'perspective',
    };
    const top = presetCamera('top', atTheSky);
    expect(top.pitch).toBe(-90);
    expect(top.eye.height).toBeCloseTo(2000, 0);
  });

  it('does not collapse when the camera sits on the thing it is looking at', () => {
    const onTop: Camera3DState = {
      eye: { lon: 25.3, lat: 45.7, height: 900 },
      heading: 0,
      pitch: -90,
      roll: 0,
      target: { lon: 25.3, lat: 45.7, height: 900 },
      projection: 'perspective',
    };
    // Turning a zero-length arm leaves the camera exactly where it was, so a press would do
    // nothing visible. A floor under the distance is what keeps the button honest.
    expect(metersBetween(presetCamera('north', onTop).eye, onTop.target!)).toBeGreaterThan(0);
  });

  it('keeps the projection the viewer chose', () => {
    const orthographic: Camera3DState = { ...looking, projection: 'orthographic', orthoHalfWidth: 500 };
    expect(presetCamera('top', orthographic).projection).toBe('orthographic');
    expect(presetCamera('top', orthographic).orthoHalfWidth).toBe(500);
  });
});

describe('which preset a camera is at', () => {
  it('is the one it was just placed at', () => {
    for (const preset of ['top', 'north', 'south', 'east', 'west'] as const) {
      expect(activePreset(presetCamera(preset, looking))).toBe(preset);
    }
  });

  it('is none of them the moment the viewer drags', () => {
    // Presets do not latch. Nothing re-applies one, so the answer comes from looking at the
    // camera — and a camera the viewer has moved is not at a preset any more.
    const dragged = { ...presetCamera('north', looking), heading: 174 };
    expect(activePreset(dragged)).toBeUndefined();
  });

  it('does not care which way a plan view happens to be facing', () => {
    // Straight down, every heading shows the same thing, and a renderer is free to report
    // whichever one the camera is holding.
    const spun = { ...presetCamera('top', looking), heading: 212 };
    expect(activePreset(spun)).toBe('top');
  });

  it('is none of them when the camera is tilted about its own axis', () => {
    expect(activePreset({ ...presetCamera('north', looking), roll: 15 })).toBeUndefined();
  });

  it('survives a camera read back from a renderer that calls "level" a whole turn', () => {
    // The value a renderer hands back for a level camera at a tilt. Written down unfolded it says
    // the camera is upside down, and every compass view would then be reported as "not the current
    // one" the instant it was pressed — the button placing the camera and the button lighting up
    // are the same press.
    const asRead = normalizeCamera3DState({ ...presetCamera('north', looking), roll: 360 });
    expect(asRead).not.toBeNull();
    expect(activePreset(asRead!)).toBe('north');
  });
});
