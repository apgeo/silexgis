// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import Point from 'ol/geom/Point';
import Icon from 'ol/style/Icon';
import CircleStyle from 'ol/style/Circle';
import { afterEach, describe, expect, it } from 'vitest';
import { entranceStyle, getEntranceSource } from './entranceLayer.ts';
import { setEntranceSymbols } from './entranceSymbols.ts';
import { entrancePalette } from './markerPalette.ts';

function entrance(properties: Record<string, unknown> = {}): Feature {
  return new Feature({ geometry: new Point([0, 0]), ...properties });
}

function svgOf(src: string): string {
  return decodeURIComponent(src.slice('data:image/svg+xml;utf8,'.length));
}

describe('entranceStyle', () => {
  afterEach(() => setEntranceSymbols(null));

  it('draws an entrance with the cave-entrance symbol by default', () => {
    const image = entranceStyle(entrance()).getImage();

    expect(image).toBeInstanceOf(Icon);
    const svg = svgOf((image as Icon).getSrc()!);
    expect(svg).toContain('<path');
    expect(svg).toContain(`fill='${entrancePalette.point}'`);
  });

  it('draws an approximate entrance in the protection orange with a dashed casing', () => {
    const svg = svgOf((entranceStyle(entrance({ approximate: true })).getImage() as Icon).getSrc()!);

    expect(svg).toContain(`fill='${entrancePalette.approximate}'`);
    expect(svg).toContain('stroke-dasharray');
  });

  it('shares one style between every entrance drawn alike, however many there are', () => {
    const first = entranceStyle(entrance());
    for (let i = 0; i < 500; i++) {
      expect(entranceStyle(entrance({ id: String(i) }))).toBe(first);
    }
    expect(entranceStyle(entrance({ approximate: true }))).not.toBe(first);
  });

  it('follows the installation\'s choice and restyles what is already loaded', () => {
    let changed = 0;
    const source = getEntranceSource();
    const listener = () => changed++;
    source.on('change', listener);

    setEntranceSymbols({ entranceSymbol: 'osm_cave.svg', approximateEntranceSymbol: 'shape:ring' });

    expect((entranceStyle(entrance()).getImage() as Icon).getSrc()).toBe('/feature_symbols/osm_cave.svg');
    expect(changed).toBeGreaterThan(0);
    source.un('change', listener);
  });

  it('falls back to the plain dot when the choice names a file this build does not ship', () => {
    setEntranceSymbols({ entranceSymbol: 'gone.svg', approximateEntranceSymbol: 'gone.svg' });

    expect(entranceStyle(entrance()).getImage()).toBeInstanceOf(CircleStyle);
  });

  it('keeps the cluster bubble with its count, whatever the entrance symbol', () => {
    const style = entranceStyle(entrance({ cluster: true, count: 12 }));

    expect(style.getImage()).toBeInstanceOf(CircleStyle);
    expect(style.getText()?.getText()).toBe('12');
    expect(entranceStyle(entrance({ cluster: true, count: 12 }))).toBe(style);
  });
});
