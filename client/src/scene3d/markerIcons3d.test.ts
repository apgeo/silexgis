// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import { setEntranceSymbols } from '../map/entranceSymbols.ts';
import { entrancePalette, surfaceFeaturePalette } from '../map/markerPalette.ts';
import { clusterIcon, entranceIcon, surfaceFeatureIcon } from './markerIcons3d.ts';

/** The icons are data URLs; this reads back the document inside one. */
function svgOf(image: string): string {
  expect(image.startsWith('data:image/svg+xml;utf8,')).toBe(true);
  return decodeURIComponent(image.slice('data:image/svg+xml;utf8,'.length));
}

describe('entranceIcon', () => {
  afterEach(() => setEntranceSymbols(null));

  it('draws an exact entrance as the cave-entrance glyph, in the colour the flat map uses', () => {
    const svg = svgOf(entranceIcon(false).image);

    expect(svg).toContain('<path');
    expect(svg).toContain(`fill='${entrancePalette.point}'`);
    expect(svg).toContain(`stroke='${entrancePalette.stroke}'`);
    expect(svg).not.toContain('stroke-dasharray');
  });

  it('marks an approximate entrance warm and with a dashed casing, so colour is not the only cue', () => {
    const approximate = svgOf(entranceIcon(true).image);

    expect(approximate).toContain(`fill='${entrancePalette.approximate}'`);
    expect(approximate).toContain('stroke-dasharray');
  });

  it('follows the installation\'s choice of symbol, the same file the flat map draws', () => {
    setEntranceSymbols({ entranceSymbol: 'qgis_poi_cave.svg', approximateEntranceSymbol: 'shape:triangle:aa0000' });

    expect(entranceIcon(false)).toEqual({ image: '/feature_symbols/qgis_poi_cave.svg', scale: 0.5 });
    expect(svgOf(entranceIcon(true).image)).toContain("fill='#aa0000'");
  });

  it('falls back to the plain dot when the choice names a file this build does not ship', () => {
    setEntranceSymbols({ entranceSymbol: 'gone.svg', approximateEntranceSymbol: 'gone.svg' });

    const exact = svgOf(entranceIcon(false).image);
    const approximate = svgOf(entranceIcon(true).image);
    expect(exact).toContain('<circle');
    expect(radiusOf(approximate)).toBeGreaterThan(radiusOf(exact));
  });

  it('hands back the same image for the same kind, so the renderer caches one texture', () => {
    expect(entranceIcon(false).image).toBe(entranceIcon(false).image);
    expect(entranceIcon(true).image).not.toBe(entranceIcon(false).image);
  });
});

describe('clusterIcon', () => {
  it('prints the count inside the bubble', () => {
    expect(svgOf(clusterIcon(42).image)).toContain('>42</text>');
  });

  it('grows with the count, and stops growing where the flat map stops', () => {
    const small = radiusOf(svgOf(clusterIcon(4).image));
    const large = radiusOf(svgOf(clusterIcon(400).image));
    const huge = radiusOf(svgOf(clusterIcon(40_000).image));

    expect(large).toBeGreaterThan(small);
    expect(large).toBe(24);
    expect(huge).toBe(24);
  });

  it('is drawn in the cluster colour, not the entrance one', () => {
    expect(svgOf(clusterIcon(3).image)).toContain(`fill='${entrancePalette.cluster}'`);
  });

  it('survives a count the server could not have meant', () => {
    expect(svgOf(clusterIcon(Number.NaN).image)).toContain('>0</text>');
    expect(svgOf(clusterIcon(-5).image)).toContain('>0</text>');
  });
});

describe('surfaceFeatureIcon', () => {
  it('points at the same symbol file the flat map draws, at the same size', () => {
    const icon = surfaceFeatureIcon('sinkhole.png');

    expect(icon.image).toBe('/feature_symbols/sinkhole.png');
    expect(icon.scale).toBe(0.5);
  });

  it('draws a drawn shape in the features\' colour unless the reference names one', () => {
    expect(svgOf(surfaceFeatureIcon('shape:star').image)).toContain(`fill='${surfaceFeaturePalette.point}'`);
    expect(svgOf(surfaceFeatureIcon('shape:star:123abc').image)).toContain("fill='#123abc'");
  });

  it('falls back to a plain dot for a type with no symbol of its own, or one not shipped', () => {
    for (const missing of [null, undefined, '', 'not-shipped.png', '../etc/passwd']) {
      const icon = surfaceFeatureIcon(missing);
      expect(svgOf(icon.image)).toContain(`fill='${surfaceFeaturePalette.point}'`);
      expect(icon.scale).toBe(1);
    }
  });
});

function radiusOf(svg: string): number {
  const match = /r='([\d.]+)'/.exec(svg);
  expect(match).not.toBeNull();
  return Number(match![1]);
}
