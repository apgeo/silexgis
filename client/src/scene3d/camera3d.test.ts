// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import {
  applyCamera3D,
  eyeLookingAt,
  metersBetween,
  movedBy,
  normalizeCamera3DState,
  offsetBetween,
  readCamera3D,
  toCamera3DState,
  toSceneCamera,
  wrapHeading,
  wrapRoll,
  type Camera3DState,
} from './camera3d.ts';

const aCamera: Camera3DState = {
  eye: { lon: 25.3, lat: 45.7, height: 1200 },
  heading: 137.5,
  pitch: -35,
  roll: 0,
  projection: 'perspective',
};

describe('what gets written down', () => {
  it('is degrees and metres, and nothing that belongs to a renderer', () => {
    // The single property this whole module exists for. A saved view and a shared link outlive
    // the library that drew them, so every number in one has to mean something on its own.
    const written = toCamera3DState(
      { longitude: 25.3, latitude: 45.7, height: -180, heading: 90, pitch: -12.5, roll: 0 },
      {
        target: { longitude: 25.31, latitude: 45.71, height: 900 },
        projection: 'orthographic',
        orthoHalfWidth: 640,
      },
    );

    expect(written).toEqual({
      eye: { lon: 25.3, lat: 45.7, height: -180 },
      heading: 90,
      pitch: -12.5,
      roll: 0,
      target: { lon: 25.31, lat: 45.71, height: 900 },
      projection: 'orthographic',
      orthoHalfWidth: 640,
    });
    // Every angle is in the range degrees live in, not the range radians do.
    expect(Math.abs(written.pitch)).toBeGreaterThan(Math.PI);
    expect(JSON.stringify(written)).not.toMatch(/[xyz]":/);
  });

  it('round-trips through the live camera the scene is handed', () => {
    expect(toCamera3DState(toSceneCamera(aCamera), { projection: 'perspective' })).toEqual(aCamera);
  });

  it('leaves an orthographic width off a perspective camera', () => {
    // Restoring a width against a camera that has no use for one would frame the view at a size
    // nobody chose the moment the projection was next switched.
    const written = toCamera3DState(toSceneCamera(aCamera), {
      projection: 'perspective',
      orthoHalfWidth: 500,
    });
    expect(written.orthoHalfWidth).toBeUndefined();
  });

  it('folds a full turn back to zero', () => {
    // Looking straight down, a renderer can report exactly 360°, and comparing that with 0 would
    // show a camera that moved when it had not — which rewrites the URL and drops a preset.
    expect(wrapHeading(360)).toBe(0);
    expect(wrapHeading(-90)).toBe(270);
    expect(toCamera3DState({ ...toSceneCamera(aCamera), heading: 360 }, { projection: 'perspective' }).heading).toBe(0);
  });

  it('reads a level camera as level however the renderer expresses "no roll"', () => {
    // Roll is the one angle compared against zero rather than against another angle, so a whole
    // turn — which is how a renderer that measures roll from the camera's own axes reports a level
    // camera — has to read as level and not as a camera tipped right over.
    expect(wrapRoll(360)).toBe(0);
    expect(wrapRoll(-360)).toBe(0);
    expect(wrapRoll(359.5)).toBeCloseTo(-0.5, 9);
    expect(wrapRoll(180)).toBe(180);
    expect(wrapRoll(190)).toBe(-170);
    expect(wrapRoll(Number.NaN)).toBe(0);
    expect(
      toCamera3DState({ ...toSceneCamera(aCamera), roll: 360 }, { projection: 'perspective' }).roll,
    ).toBe(0);
    expect(normalizeCamera3DState({ ...aCamera, roll: 360 })?.roll).toBe(0);
  });
});

describe('reading a stored camera back', () => {
  it('accepts one this application wrote', () => {
    expect(normalizeCamera3DState(JSON.parse(JSON.stringify(aCamera)))).toEqual(aCamera);
  });

  it('refuses a document that is not one, rather than throwing', () => {
    // A saved view whose camera block is unusable must still open — it remembers layers, filters
    // and a flat-map position too, and losing all of those to one bad field is the worse failure.
    expect(normalizeCamera3DState(undefined)).toBeNull();
    expect(normalizeCamera3DState(null)).toBeNull();
    expect(normalizeCamera3DState({})).toBeNull();
    expect(normalizeCamera3DState({ eye: { lon: 25, lat: 45 }, heading: 0, pitch: 0 })).toBeNull();
    expect(normalizeCamera3DState({ ...aCamera, eye: { lon: 400, lat: 45, height: 0 } })).toBeNull();
    expect(normalizeCamera3DState({ ...aCamera, pitch: -120 })).toBeNull();
    expect(normalizeCamera3DState({ ...aCamera, heading: Number.NaN })).toBeNull();
  });

  it('reads an unknown projection as the ordinary one', () => {
    expect(normalizeCamera3DState({ ...aCamera, projection: 'isometric' })?.projection).toBe(
      'perspective',
    );
  });

  it('drops an orthographic width that is not a width', () => {
    expect(
      normalizeCamera3DState({ ...aCamera, projection: 'orthographic', orthoHalfWidth: -5 })
        ?.orthoHalfWidth,
    ).toBeUndefined();
  });
});

describe('local metric geometry', () => {
  it('measures a move it made', () => {
    const from = { lon: 25.3, lat: 45.7, height: 1000 };
    const moved = movedBy(from, { east: 300, north: 400, up: 0 });
    expect(metersBetween(from, moved)).toBeCloseTo(500, 0);
    const offset = offsetBetween(from, moved);
    expect(offset.east).toBeCloseTo(300, 0);
    expect(offset.north).toBeCloseTo(400, 0);
  });

  it('stands the eye on the far side of what it is looking at', () => {
    // The half that is easy to get backwards: a camera FACING north stands to the SOUTH of its
    // subject, otherwise every compass preset shows the cave from the opposite side.
    const target = { lon: 25.3, lat: 45.7, height: 900 };
    const eye = eyeLookingAt(target, 0, 0, 1000);
    expect(eye.lat).toBeLessThan(target.lat);
    expect(eye.lon).toBeCloseTo(target.lon, 6);
    expect(metersBetween(eye, target)).toBeCloseTo(1000, 0);
  });

  it('lifts the eye by the tilt it was given', () => {
    const target = { lon: 25.3, lat: 45.7, height: 900 };
    const eye = eyeLookingAt(target, 90, -30, 2000);
    expect(eye.height - target.height).toBeCloseTo(1000, 0); // 2000·sin(30°)
    // Facing east means standing to the west.
    expect(eye.lon).toBeLessThan(target.lon);
  });
});

describe('putting a camera back into a scene', () => {
  it('moves first and switches projection second', () => {
    // A scene told to drop perspective without being told how wide to be sizes itself from where
    // the camera is standing at that moment. Switching first would frame wherever the camera had
    // been left rather than what the saved view was about.
    const order: string[] = [];
    const engine = {
      setCamera: vi.fn(() => order.push('camera')),
      setProjection: vi.fn(() => order.push('projection')),
    };

    applyCamera3D(engine, { ...aCamera, projection: 'orthographic', orthoHalfWidth: 400 });

    expect(order).toEqual(['camera', 'projection']);
    expect(engine.setProjection).toHaveBeenCalledWith('orthographic', 400);
  });

  it('reads back everything the scene knows about its camera', () => {
    const engine = {
      getCamera: () => toSceneCamera(aCamera),
      getCameraTarget: () => ({ longitude: 25.31, latitude: 45.71, height: 800 }),
      getProjection: () => 'perspective' as const,
      getOrthoHalfWidth: () => undefined,
    };

    expect(readCamera3D(engine)).toEqual({
      ...aCamera,
      target: { lon: 25.31, lat: 45.71, height: 800 },
    });
  });
});
