// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { entrancePalette, surfaceFeaturePalette } from '../map/markerPalette.ts';
import { clusterIcon, entranceIcon, surfaceFeatureIcon } from './markerIcons3d.ts';

/** The icons are data URLs; this reads back the document inside one. */
function svgOf(image: string): string {
  expect(image.startsWith('data:image/svg+xml;utf8,')).toBe(true);
  return decodeURIComponent(image.slice('data:image/svg+xml;utf8,'.length));
}

describe('entranceIcon', () => {
  it('draws an exact entrance in the same colour the flat map fills it with', () => {
    const svg = svgOf(entranceIcon(false).image);

    expect(svg).toContain(`fill='${entrancePalette.point}'`);
    expect(svg).toContain(`stroke='${entrancePalette.stroke}'`);
    expect(svg).not.toContain('stroke-dasharray');
  });

  it('marks an approximate entrance the way the flat map does — warm, larger, dashed ring', () => {
    const exact = svgOf(entranceIcon(false).image);
    const approximate = svgOf(entranceIcon(true).image);

    expect(approximate).toContain(`fill='${entrancePalette.approximate}'`);
    expect(approximate).toContain('stroke-dasharray');
    expect(radiusOf(approximate)).toBeGreaterThan(radiusOf(exact));
  });

  it('hands back the same icon for the same kind, so the renderer caches one image', () => {
    expect(entranceIcon(false)).toBe(entranceIcon(false));
    expect(entranceIcon(true)).not.toBe(entranceIcon(false));
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

  it('falls back to a plain dot for a type with no symbol of its own', () => {
    for (const missing of [null, undefined, '']) {
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
