// SPDX-License-Identifier: AGPL-3.0-or-later
import { beforeEach, describe, expect, it } from 'vitest';
import { entranceLabel, surfaceFeatureLabel } from './featureLabels.ts';
import { setFeatureTypeCatalog } from './featureTypeCatalog.ts';

type FeatureTypes = Parameters<typeof setFeatureTypeCatalog>[0];

beforeEach(() => {
  setFeatureTypeCatalog([
    { id: 7, code: 'sinkhole', name: 'Sinkhole', symbolFile: 'sinkhole.svg' },
  ] as FeatureTypes);
});

describe('entranceLabel', () => {
  it('names the entrance and the cave it belongs to', () => {
    expect(entranceLabel({ name: 'Intrarea Mică', caveName: 'Peștera Demo' })).toBe(
      'Intrarea Mică — Peștera Demo',
    );
  });

  it('says the name once when the entrance carries its cave name', () => {
    // The server already falls the entrance's name back to its cave's, so the two are equal for an
    // unnamed entrance and repeating it would read as a stutter.
    expect(entranceLabel({ name: 'Peștera Demo', caveName: 'Peștera Demo' })).toBe('Peștera Demo');
  });

  it('falls back to the cave when the entrance has no name of its own', () => {
    expect(entranceLabel({ caveName: 'Peștera Demo' })).toBe('Peștera Demo');
  });

  it('has nothing to say about an entrance with no names at all', () => {
    expect(entranceLabel({})).toBeUndefined();
    // An empty string is not a name; left alone it would draw an empty chip over the scene.
    expect(entranceLabel({ name: '', caveName: '' })).toBeUndefined();
  });
});

describe('surfaceFeatureLabel', () => {
  it('names a feature and what kind of thing it is', () => {
    expect(surfaceFeatureLabel({ name: 'Dolina Demo', typeCode: 'sinkhole' })).toBe(
      'Dolina Demo — Sinkhole',
    );
  });

  it('names an unnamed feature by its kind, which is the useful half', () => {
    expect(surfaceFeatureLabel({ typeCode: 'sinkhole' })).toBe('Sinkhole');
  });

  it('resolves the kind of a feature drawn but not yet saved, which knows only its type id', () => {
    expect(surfaceFeatureLabel({ name: 'Nou', featureTypeId: 7 })).toBe('Nou — Sinkhole');
  });

  it('has nothing to say when neither a name nor a known kind is available', () => {
    expect(surfaceFeatureLabel({ typeCode: 'not-in-the-catalog' })).toBeUndefined();
  });
});
