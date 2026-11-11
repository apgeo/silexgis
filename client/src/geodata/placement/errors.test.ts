// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { PLACEMENT_ERROR_CODES, placementErrorKey } from './errors.ts';

function lookup(tree: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], tree);
}

describe('the sentence for a refusal of a map-sheet route', () => {
  it('turns a known code into its own key', () => {
    expect(placementErrorKey('georeferenced_map.placement_too_few_points')).toBe(
      'geodata.placement.errors.placementTooFewPoints',
    );
    expect(placementErrorKey('georeferenced_map.busy')).toBe('geodata.placement.errors.busy');
  });

  it('falls back to the generic sentence for anything else', () => {
    expect(placementErrorKey('georeferenced_map.something_new')).toBe('geodata.placement.errors.generic');
    expect(placementErrorKey('reslink.not_found')).toBe('geodata.placement.errors.generic');
    expect(placementErrorKey(undefined)).toBe('geodata.placement.errors.generic');
  });

  it('has a sentence in both languages for every code it knows', () => {
    const keys = [...PLACEMENT_ERROR_CODES.map((code) => placementErrorKey(`georeferenced_map.${code}`)), 'geodata.placement.errors.generic'];
    for (const key of keys) {
      expect(typeof lookup(en, key), key).toBe('string');
      expect(typeof lookup(ro, key), key).toBe('string');
    }
  });
});
