// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import * as fake from './cesiumTestDouble.ts';
// The text of the one module allowed to import the library, read as a string.
import sceneModuleSource from './scene3dContext.ts?raw';

// The scene module's unit tests run against a hand-written stand-in for the 3D engine, because the
// engine needs a graphics context the test runner does not have. A stand-in nobody compares with
// the real thing drifts: an upgrade renames or removes a member, or changes what a plain value
// class does, and the unit suite stays green against a fiction.
//
// This file is that comparison, and it is the one place besides the module that owns the engine
// where the real library is loaded. It can be, because the pure parts of the library — maths,
// colours, positions, rectangles, the enumerations — do not need a graphics context; only the
// widget, models and primitives do, and those are covered for their SHAPE (does the member the
// stand-in offers still exist?) rather than for their behaviour. Behaviour of the drawing classes
// is only provable in a real browser, which is what the browser specs for the 3D scene are for.
//
// It is a test file, so it sits outside the module-boundary graph that forbids importing the
// library anywhere but the scene module.

type Loose = Record<string, unknown>;
const real = (await vi.importActual('cesium')) as Loose;
const fakes = fake as unknown as Loose;

/**
 * Exports of the stand-in that exist only so tests can drive or inspect it. The library has no
 * such names; listing them here, with the reason, is what keeps "every export must exist in the
 * library" strict for everything else.
 */
const TEST_ONLY_EXPORTS: Record<string, string> = {
  engineState: 'the recorder tests read back what the scene asked the engine for',
  VENDOR_DEFAULT_TOKEN: 'the value a test sees the scene clear from the library default token',
  CesiumMath: 'the file-internal name of the `Math` export, which would shadow the global',
  FakeEvent: 'the file-internal name of the `Event` export, which would shadow the DOM one',
};

/**
 * Members the stand-in adds to an otherwise real name so a test can reach into it: a way to raise
 * an event by hand, to deliver a queued response, to forget what was requested. The key is
 * `ExportName.member`; a `static:` or `proto:` prefix says where it lives.
 */
const TEST_ONLY_MEMBERS = new Set([
  'Event.proto:raise',
  'ScreenSpaceEventHandler.proto:raise',
  'GroundPolylinePrimitive.static:deliverTerrainHeights',
  'GroundPolylinePrimitive.static:forget',
  'GroundPolylinePrimitive.static:terrainHeightRequests',
  'GroundPolylinePrimitive.static:terrainHeightsArrived',
  'Model.static:deliver',
  'Model.static:isWaitingFor',
  'Model.static:forget',
  'Model.static:waiting',
]);

const BUILT_IN_FUNCTION_KEYS = new Set(['length', 'name', 'prototype']);

function ownNames(value: object): string[] {
  return Object.getOwnPropertyNames(value).filter((key) => !BUILT_IN_FUNCTION_KEYS.has(key));
}

function isObjectLike(value: unknown): value is object {
  return value !== null && (typeof value === 'object' || typeof value === 'function');
}

describe('the engine stand-in against the real library', () => {
  const mirrored = Object.entries(fakes).filter(([name]) => !(name in TEST_ONLY_EXPORTS));

  it('only lists test-only exports that the stand-in really has', () => {
    for (const name of Object.keys(TEST_ONLY_EXPORTS)) {
      expect(name in fakes, name).toBe(true);
    }
  });

  it('offers nothing under a name the library does not export', () => {
    const missing = mirrored.map(([name]) => name).filter((name) => real[name] === undefined);
    expect(missing).toEqual([]);
  });

  it('offers no static member, enumeration value or prototype method the library lacks', () => {
    const extra: string[] = [];
    for (const [name, standIn] of mirrored) {
      const actual = real[name];
      if (!isObjectLike(standIn) || !isObjectLike(actual)) continue;
      for (const key of ownNames(standIn)) {
        if (!(key in actual) && !TEST_ONLY_MEMBERS.has(`${name}.static:${key}`)) {
          extra.push(`${name}.static:${key}`);
        }
      }
      const standInProto = (standIn as { prototype?: object }).prototype;
      const actualProto = (actual as { prototype?: object }).prototype;
      if (standInProto && actualProto) {
        for (const key of ownNames(standInProto)) {
          if (key === 'constructor') continue;
          if (!(key in actualProto) && !TEST_ONLY_MEMBERS.has(`${name}.proto:${key}`)) {
            extra.push(`${name}.proto:${key}`);
          }
        }
      }
    }
    expect(extra).toEqual([]);
  });

  it('does not carry a test-only allowance for a member that is no longer there', () => {
    const stale = [...TEST_ONLY_MEMBERS].filter((entry) => {
      const [name, member] = entry.split('.');
      const [where, key] = member.split(':');
      const standIn = fakes[name] as { prototype?: object } | undefined;
      const target = where === 'proto' ? standIn?.prototype : standIn;
      return !target || !Object.getOwnPropertyNames(target).includes(key);
    });
    expect(stale).toEqual([]);
  });

  it('offers every name the scene module imports from the library, and the library has them too', () => {
    const block = /import\s*\{([^}]*)\}\s*from\s*'cesium'/.exec(sceneModuleSource);
    expect(block, 'the scene module imports from the library in one named-import block').not.toBeNull();
    const names = (block?.[1] ?? '')
      .split(',')
      .map((part) => part.trim())
      .filter((part) => part !== '' && !part.startsWith('type '))
      .map((part) => part.split(/\s+as\s+/)[0]);
    expect(names.length).toBeGreaterThan(20);
    expect(names.filter((name) => real[name] === undefined)).toEqual([]);
    expect(names.filter((name) => fakes[name] === undefined)).toEqual([]);
  });

  // ---- behaviour of the parts that need no graphics context -----------------

  it('converts degrees and radians the way the library does', () => {
    const actual = real.Math as { toDegrees(r: number): number; toRadians(d: number): number };
    for (const value of [0, 1, -90, 45.5, 180, 360]) {
      expect(fake.Math.toRadians(value)).toBeCloseTo(actual.toRadians(value), 12);
      expect(fake.Math.toDegrees(value)).toBeCloseTo(actual.toDegrees(value), 12);
    }
  });

  it('round-trips a position through a Cartographic, height included', () => {
    const actual = real as unknown as {
      Cartesian3: { fromDegrees(lon: number, lat: number, h?: number): unknown };
      Cartographic: { fromCartesian(p: unknown): { longitude: number; latitude: number; height: number } | undefined };
    };
    for (const [lon, lat, height] of [
      [25.3, 45.7, 1200],
      [-3.2, 51.5, 0],
      [25.3, 45.7, -150],
    ] as const) {
      const fromReal = actual.Cartographic.fromCartesian(actual.Cartesian3.fromDegrees(lon, lat, height));
      const fromFake = fake.Cartographic.fromCartesian(fake.Cartesian3.fromDegrees(lon, lat, height));
      expect(fromReal).toBeDefined();
      expect(fromFake?.longitude).toBeCloseTo(fromReal?.longitude ?? NaN, 9);
      expect(fromFake?.latitude).toBeCloseTo(fromReal?.latitude ?? NaN, 9);
      expect(fromFake?.height).toBeCloseTo(fromReal?.height ?? NaN, 3);
    }
    // Known difference: handed nothing, the stand-in answers undefined while the library's
    // development build throws (its production build strips that check). The scene module only
    // passes a camera position or a picked point it has already tested, so it never relies on
    // either; the line below keeps the stand-in's leniency from being mistaken for the library's.
    expect(() => actual.Cartographic.fromCartesian(undefined)).toThrow();
    expect(fake.Cartographic.fromCartesian(undefined)).toBeUndefined();
  });

  it('keeps a colour immutable under withAlpha and reports the alpha it was given', () => {
    const Real = real.Color as {
      fromCssColorString(css: string): { alpha: number; withAlpha(a: number): { alpha: number } };
      WHITE: { alpha: number };
    };
    const original = Real.fromCssColorString('#336699');
    const faded = original.withAlpha(0.25);
    expect(faded.alpha).toBe(0.25);
    expect(original.alpha).toBe(1);
    expect(Real.WHITE.alpha).toBe(1);

    const standIn = fake.Color.fromCssColorString('#336699');
    const standInFaded = standIn.withAlpha(0.25);
    expect(standInFaded.alpha).toBe(0.25);
    expect(standIn.alpha).toBe(1);
    expect(fake.Color.WHITE.alpha).toBe(1);
    expect(standInFaded).not.toBe(standIn);
  });

  it('stores a credit as the markup and the on-screen flag it was given', () => {
    const Real = real.Credit as new (html: string, show?: boolean) => { html: string; showOnScreen: boolean };
    for (const [html, show] of [['<b>x</b>', true], ['', false]] as const) {
      const actual = new Real(html, show);
      const standIn = new fake.Credit(html, show);
      expect(standIn.html).toBe(actual.html);
      expect(standIn.showOnScreen).toBe(actual.showOnScreen);
    }
  });

  it('stores a screen position as x and y', () => {
    const Real = real.Cartesian2 as new (x: number, y: number) => { x: number; y: number };
    const actual = new Real(3, 4);
    const standIn = new fake.Cartesian2(3, 4);
    expect([standIn.x, standIn.y]).toEqual([actual.x, actual.y]);
  });

  it('keeps the four fields of a near/far scalar under the library names', () => {
    const Real = real.NearFarScalar as new (a: number, b: number, c: number, d: number) => Loose;
    const actual = new Real(1, 2, 3, 4);
    const standIn = new fake.NearFarScalar(1, 2, 3, 4);
    for (const key of ['near', 'nearValue', 'far', 'farValue']) {
      expect((standIn as unknown as Loose)[key], key).toBe(actual[key]);
    }
  });

  it('starts a perspective frustum at the library default near plane', () => {
    const Real = real.PerspectiveFrustum as new () => { near: number };
    expect(new fake.PerspectiveFrustum().near).toBe(new Real().near);
  });

  it('declares the enumeration values the scene module branches on', () => {
    for (const name of ['HeightReference', 'VerticalOrigin', 'ScreenSpaceEventType'] as const) {
      const actual = real[name] as Loose;
      const standIn = fakes[name] as Loose;
      for (const key of Object.keys(standIn)) {
        expect(actual[key], `${name}.${key}`).toBeDefined();
      }
      // Distinct members stay distinct; the stand-in may use other values than the library's
      // numbers, but two names that were different must not collapse into one.
      expect(new Set(Object.values(standIn)).size).toBe(Object.keys(standIn).length);
      expect(new Set(Object.values(actual)).size).toBe(Object.keys(actual).length);
    }
  });

  it('records a rectangle in degrees where the library records radians (a known, deliberate difference)', () => {
    const actual = (real.Rectangle as { fromDegrees(w: number, s: number, e: number, n: number): Loose }).fromDegrees(
      24,
      45,
      26,
      46,
    );
    const standIn = fake.Rectangle.fromDegrees(24, 45, 26, 46);
    expect(standIn).toEqual({ west: 24, south: 45, east: 26, north: 46 });
    // If an upgrade ever makes the library keep degrees, the stand-in and the tests that read its
    // rectangle in degrees are right by accident and this line is the place to say so.
    expect(actual.west as number).toBeCloseTo(fake.Math.toRadians(standIn.west), 12);
    expect(actual.north as number).toBeCloseTo(fake.Math.toRadians(standIn.north), 12);
  });
});
