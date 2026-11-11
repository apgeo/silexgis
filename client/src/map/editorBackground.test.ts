// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { MapLayerInfo } from '../api/hooks.ts';
import { chooseBackground, defaultBasemap } from './editorBackground.ts';

const layer = (id: number, overrides: Partial<MapLayerInfo> = {}) =>
  ({
    id,
    name: `Layer ${id}`,
    layerKind: 'xyz',
    urlTemplate: 'https://tiles.example/{z}/{x}/{y}.png',
    isBase: true,
    isDefault: false,
    ...overrides,
  }) as MapLayerInfo;

describe('the background of a map of its own', () => {
  const catalogue = [layer(1), layer(2, { isDefault: true }), layer(3, { isBase: false })];

  it('is the one showing on the workspace map', () => {
    expect(chooseBackground(catalogue, 1)?.id).toBe(1);
  });

  it('is the installation’s default when the workspace map shows none, or an overlay', () => {
    expect(chooseBackground(catalogue, undefined)?.id).toBe(2);
    expect(chooseBackground(catalogue, 3)?.id).toBe(2);
    expect(defaultBasemap([layer(5), layer(6)])?.id).toBe(5);
  });

  it('is none when the installation publishes no background, so the fallback is drawn', () => {
    expect(chooseBackground([], 1)).toBeNull();
    expect(chooseBackground(undefined, undefined)).toBeNull();
  });
});
