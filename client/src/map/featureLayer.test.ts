// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature, { type FeatureLike } from 'ol/Feature';
import LineString from 'ol/geom/LineString';
import MultiPoint from 'ol/geom/MultiPoint';
import Point from 'ol/geom/Point';
import type Style from 'ol/style/Style';
import { describe, expect, it } from 'vitest';
import { createSurfaceFeatureLayer, SAVE_REFUSED, setFeatureTypeSymbols } from './featureLayer.ts';

describe('feature styling', () => {
  const styleFn = createSurfaceFeatureLayer().getStyle() as (f: FeatureLike, r: number) => Style | Style[];

  // A registered symbol makes point-like features style via a (lazily-loaded) icon, which
  // avoids constructing OpenLayers' canvas-rendered fallback circle in the jsdom test env.
  setFeatureTypeSymbols([
    { id: 7, code: 'doline', name: 'Doline', symbolFile: 'doline.png' },
  ] as Parameters<typeof setFeatureTypeSymbols>[0]);

  function stylesFor(feature: Feature): Style[] {
    const result = styleFn(feature, 1);
    return Array.isArray(result) ? result : result ? [result] : [];
  }

  function pointLike(geom: Point | MultiPoint): Feature {
    // Server rows carry the symbol file directly in their properties.
    const feature = new Feature(geom);
    feature.set('symbol', 'doline.png');
    return feature;
  }

  it('renders MultiPoint with a point image, like Point (not an invisible stroke/fill)', () => {
    expect(stylesFor(pointLike(new MultiPoint([[0, 0], [1, 1]]))).some((s) => s.getImage())).toBe(true);
    expect(stylesFor(pointLike(new Point([0, 0]))).some((s) => s.getImage())).toBe(true);
  });

  it('styles a pending locally drawn point through the catalog by featureTypeId', () => {
    const pending = new Feature(new Point([0, 0]));
    pending.set('pendingNew', true);
    pending.set('featureTypeId', 7);
    expect(stylesFor(pending).some((s) => s.getImage())).toBe(true);
  });

  it('marks an edit the server refused, so it can be told from the ones that saved', () => {
    const point = pointLike(new Point([0, 0]));
    const plain = stylesFor(point);
    point.set(SAVE_REFUSED, true);
    const marked = stylesFor(point);
    expect(marked).toHaveLength(plain.length + 1);
    expect(marked.slice(1)).toEqual(plain);

    const line = new Feature(new LineString([[0, 0], [1, 1]]));
    const plainLine = stylesFor(line);
    line.set(SAVE_REFUSED, true);
    const markedLine = stylesFor(line);
    expect(markedLine).toHaveLength(plainLine.length + 1);
    expect(markedLine[0].getStroke()?.getWidth()).toBeGreaterThan(plainLine[0].getStroke()!.getWidth()!);
  });

  it('renders lines with a stroke and no image', () => {
    const line = stylesFor(new Feature(new LineString([[0, 0], [1, 1]])));
    expect(line.some((s) => s.getImage())).toBe(false);
    expect(line.some((s) => s.getStroke())).toBe(true);
  });
});
