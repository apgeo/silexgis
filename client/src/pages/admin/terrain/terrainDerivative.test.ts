// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from '../../../i18n/locales/en.json';
import {
  altitudeValid,
  azimuthValid,
  colourRampProblem,
  contourIntervalValid,
  defaultDerivativeName,
  describeDerivativeSettings,
  hexToRgb,
  parseDerivativeSettings,
  zFactorValid,
} from './terrainDerivative.ts';

/** English, looked up by key, with the interpolations the two numbered lines carry filled in. */
const t = ((key: string, values?: Record<string, unknown>) => {
  const text = key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown>)?.[part], en);
  if (typeof text !== 'string') {
    return key;
  }
  return Object.entries(values ?? {}).reduce(
    (said, [name, value]) => said.replaceAll(`{{${name}}}`, String(value)),
    text,
  );
}) as never;

/**
 * The record the server stores beside a picture, as its own serializer writes it: every choice
 * as its number, every setting present whether or not the picture read it.
 */
const STORED_HILLSHADE =
  '{"derivative":0,"lighting":0,"azimuthDegrees":315,"altitudeDegrees":45,"zFactor":1,"surfaceFit":0,"slopeUnit":0,"ruggednessFit":0,"computeEdges":true,"colourRamp":[]}';

describe('the bounds a request is held to', () => {
  // Each end is the server's own, including which ends are open: a light on the horizon lights
  // nothing, and 360° is 0° written twice. A form that let either through would queue a request
  // the server then refuses, read by whoever asked as a broken installation.
  it('measures the light clockwise from north, from 0 up to but not including 360', () => {
    expect(azimuthValid(0)).toBe(true);
    expect(azimuthValid(359.9)).toBe(true);
    expect(azimuthValid(360)).toBe(false);
    expect(azimuthValid(-1)).toBe(false);
    expect(azimuthValid(Number.NaN)).toBe(false);
  });

  it('stands the light above the horizon and no higher than overhead', () => {
    expect(altitudeValid(0)).toBe(false);
    expect(altitudeValid(0.5)).toBe(true);
    expect(altitudeValid(90)).toBe(true);
    expect(altitudeValid(90.1)).toBe(false);
  });

  it('exaggerates heights by more than nothing and by at most a hundred times', () => {
    expect(zFactorValid(0)).toBe(false);
    expect(zFactorValid(0.1)).toBe(true);
    expect(zFactorValid(100)).toBe(true);
    expect(zFactorValid(101)).toBe(false);
    expect(zFactorValid(Number.POSITIVE_INFINITY)).toBe(false);
  });

  it('refuses a ramp for each of the three reasons the server has, in its order', () => {
    expect(colourRampProblem([{ elevation: 100, colour: '#000000' }])).toBe('tooShort');
    expect(
      colourRampProblem([
        { elevation: 100, colour: '#000000' },
        { elevation: null, colour: '#ffffff' },
      ]),
    ).toBe('heightMissing');
    expect(
      colourRampProblem([
        { elevation: 100, colour: '#000000' },
        { elevation: 100, colour: '#ffffff' },
      ]),
    ).toBe('duplicate');
    expect(
      colourRampProblem([
        { elevation: 100, colour: '#000000' },
        { elevation: 900, colour: '#ffffff' },
      ]),
    ).toBeNull();
  });

  it('spaces contour lines from one metre of height to a thousand', () => {
    expect(contourIntervalValid(0)).toBe(false);
    expect(contourIntervalValid(0.9)).toBe(false);
    expect(contourIntervalValid(1)).toBe(true);
    expect(contourIntervalValid(20)).toBe(true);
    expect(contourIntervalValid(1000)).toBe(true);
    expect(contourIntervalValid(1000.5)).toBe(false);
    expect(contourIntervalValid(-20)).toBe(false);
    expect(contourIntervalValid(Number.NaN)).toBe(false);
  });

  it('turns a picker colour into the three bytes a stop is stored as', () => {
    expect(hexToRgb('#2f855a')).toEqual({ red: 47, green: 133, blue: 90 });
    expect(hexToRgb('fff')).toEqual({ red: 255, green: 255, blue: 255 });
  });
});

describe('the name a picture opens with', () => {
  it('is what its kind is called', () => {
    expect(defaultDerivativeName('slope', t)).toBe('Steepness');
    expect(defaultDerivativeName('aspect', t)).toBe('Facing');
    expect(defaultDerivativeName('hillshade', t)).toBe('Shaded relief');
    expect(defaultDerivativeName('contours', t)).toBe('Contour lines');
  });
});

describe('reading the settings stored beside a picture', () => {
  it('reads a choice whether it was written as its number or as its name', () => {
    expect(parseDerivativeSettings(STORED_HILLSHADE)).toMatchObject({
      lighting: 'single',
      azimuthDegrees: 315,
      surfaceFit: 'horn',
      computeEdges: true,
    });
    expect(parseDerivativeSettings('{"lighting":"multidirectional","surfaceFit":1}')).toMatchObject({
      lighting: 'multidirectional',
      surfaceFit: 'zevenbergenThorne',
    });
  });

  it('leaves out what it cannot read rather than failing the row', () => {
    expect(parseDerivativeSettings('not json')).toEqual({});
    expect(parseDerivativeSettings('{"lighting":"sideways","zFactor":"two"}')).toEqual({
      lighting: undefined,
      azimuthDegrees: undefined,
      altitudeDegrees: undefined,
      zFactor: undefined,
      surfaceFit: undefined,
      slopeUnit: undefined,
      ruggednessFit: undefined,
      computeEdges: undefined,
      colourRamp: undefined,
      contourIntervalMetres: undefined,
    });
  });

  // The stored record carries a light direction for every picture, at its default. A slope map
  // "lit from the north-west" is a sentence about nothing.
  it('says only what the kind of picture reads, and leaves the ordinary defaults unsaid', () => {
    expect(describeDerivativeSettings('hillshade', STORED_HILLSHADE, t)).toBe(
      'One light · light from 315° · 45° up · Horn (eight neighbours)',
    );
    expect(describeDerivativeSettings('slope', STORED_HILLSHADE.replace('"slopeUnit":0', '"slopeUnit":1'), t)).toBe(
      'Horn (eight neighbours) · Rise over run, as a percentage',
    );
    expect(describeDerivativeSettings('roughness', STORED_HILLSHADE.replace('true', 'false'), t)).toBe(
      'edges left blank',
    );
    // Four lights stand at fixed compass points, so no direction is reported with them.
    expect(
      describeDerivativeSettings('hillshade', STORED_HILLSHADE.replace('"lighting":0', '"lighting":1').replace('"zFactor":1', '"zFactor":2'), t),
    ).toBe('Four lights · 45° up · heights ×2 · Horn (eight neighbours)');
    expect(
      describeDerivativeSettings('colourRelief', '{"colourRamp":[{"elevation":1},{"elevation":2}],"computeEdges":true}', t),
    ).toBe('2 colour stops');
    // Contour lines say their spacing and nothing else: they have no outermost ring of cells, so
    // a record that says the ring was left blank is not a fact about them.
    expect(
      describeDerivativeSettings('contours', '{"derivative":7,"computeEdges":false,"contourIntervalMetres":25}', t),
    ).toBe('a line every 25 m');
    // And a spacing in the record of a picture that draws no lines is not reported for it.
    expect(
      describeDerivativeSettings('roughness', '{"computeEdges":true,"contourIntervalMetres":25}', t),
    ).toBe('');
  });
});
