// SPDX-License-Identifier: AGPL-3.0-or-later
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import en from '../i18n/locales/en.json';
import ro from '../i18n/locales/ro.json';
import {
  SYMBOL_SHAPES,
  bundledSymbolFiles,
  catalogueEntryOf,
  parseSymbol,
  shapeReference,
  symbolCatalogue,
  symbolGroups,
  symbolImage,
} from './symbolLibrary.ts';

const folder = path.resolve(process.cwd(), 'public/feature_symbols');

function svgOf(src: string): string {
  expect(src.startsWith('data:image/svg+xml;utf8,')).toBe(true);
  return decodeURIComponent(src.slice('data:image/svg+xml;utf8,'.length));
}

function lookup(dictionary: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], dictionary);
}

describe('the symbol catalogue', () => {
  it('offers exactly the symbol files bundled beside the application', () => {
    const files = readdirSync(folder).filter((name) => /\.(png|svg)$/.test(name)).sort();

    expect([...bundledSymbolFiles].sort()).toEqual(files);
  });

  it('offers every drawn shape, each once', () => {
    const shapes = symbolCatalogue.filter((entry) => entry.group === 'shapes').map((entry) => entry.reference);

    expect([...shapes].sort()).toEqual(SYMBOL_SHAPES.map((shape) => `shape:${shape}`).sort());
    expect(new Set(symbolCatalogue.map((entry) => entry.reference)).size).toBe(symbolCatalogue.length);
  });

  it('names every entry and every group in English and Romanian', () => {
    const keys = [...symbolCatalogue.map((entry) => entry.nameKey), ...symbolGroups.map((group) => group.labelKey)];
    for (const key of keys) {
      expect(typeof lookup(en, key), `en ${key}`).toBe('string');
      expect(typeof lookup(ro, key), `ro ${key}`).toBe('string');
    }
  });

  it('records where every bundled file came from', () => {
    const provenance = readFileSync(path.join(folder, 'PROVENANCE.md'), 'utf8');
    for (const file of bundledSymbolFiles.filter((name) => name.endsWith('.svg'))) {
      expect(provenance, file).toContain(file);
    }
  });

  it('ships SVG files a browser can draw without fetching anything else', () => {
    for (const file of bundledSymbolFiles.filter((name) => name.endsWith('.svg'))) {
      const svg = readFileSync(path.join(folder, file), 'utf8');
      const root = /^<svg [^>]*>/.exec(svg)?.[0] ?? '';
      // A size of its own, or the flat map has nothing to scale and draws it at an arbitrary one.
      expect(root, file).toMatch(/ width="[\d.]+"/);
      expect(root, file).toMatch(/ height="[\d.]+"/);
      // An <img> renders no script and loads nothing an SVG points at, so neither may be relied on.
      expect(svg, file).not.toMatch(/<script|xlink:href="http|href="http|<image|param\(/);
    }
  });
});

describe('parseSymbol', () => {
  it('reads files and drawn shapes, with and without a colour', () => {
    expect(parseSymbol('pit.png')).toEqual({ kind: 'file', file: 'pit.png' });
    expect(parseSymbol('shape:cave')).toEqual({ kind: 'shape', shape: 'cave' });
    expect(parseSymbol('shape:triangle:d46b08')).toEqual({ kind: 'shape', shape: 'triangle', color: '#d46b08' });
  });

  it('refuses paths, addresses, unknown shapes and malformed colours', () => {
    for (const bad of ['../pit.png', '/feature_symbols/pit.png', 'https://x.test/a.svg', 'shape:hexagon', 'shape:cave:D46B08', 'shape:cave:123', 'pit.gif', '', null, undefined]) {
      expect(parseSymbol(bad), String(bad)).toBeUndefined();
    }
  });

  it('round-trips a shape reference built with a colour', () => {
    expect(shapeReference('star', '#A1B2C3')).toBe('shape:star:a1b2c3');
    expect(shapeReference('star')).toBe('shape:star');
    expect(catalogueEntryOf('shape:star:a1b2c3')?.reference).toBe('shape:star');
  });
});

describe('symbolImage', () => {
  it('draws a shape in the layer colour unless the reference carries its own', () => {
    expect(svgOf(symbolImage('shape:square', { color: '#146262' })!.src)).toContain("fill='#146262'");
    expect(svgOf(symbolImage('shape:square:aa0000', { color: '#146262' })!.src)).toContain("fill='#aa0000'");
  });

  it('draws every shape at twice its size and halves it, for a sharp marker on a dense display', () => {
    for (const shape of SYMBOL_SHAPES) {
      const image = symbolImage(`shape:${shape}`, { color: '#146262' })!;
      expect(image.scale).toBe(0.5);
      expect(svgOf(image.src)).toContain(`width='${image.width}'`);
    }
  });

  it('dashes the casing of an approximate position, so colour is never the only cue', () => {
    for (const shape of SYMBOL_SHAPES) {
      expect(svgOf(symbolImage(`shape:${shape}`, { color: '#d46b08', approximate: true })!.src), shape).toContain(
        'stroke-dasharray',
      );
      expect(svgOf(symbolImage(`shape:${shape}`, { color: '#d46b08' })!.src), shape).not.toContain('stroke-dasharray');
    }
  });

  it('points a bundled file at the folder beside the application, at half size', () => {
    expect(symbolImage('osm_cave.svg', { color: '#000000' })).toMatchObject({
      src: '/feature_symbols/osm_cave.svg',
      scale: 0.5,
    });
  });

  it('answers nothing for a file this build does not ship, so the layer draws its fallback', () => {
    expect(symbolImage('removed_long_ago.png', { color: '#000000' })).toBeUndefined();
    expect(symbolImage('not a reference', { color: '#000000' })).toBeUndefined();
  });

  it('hands back the very same image for the same request, so renderers decode it once', () => {
    const first = symbolImage('shape:cave', { color: '#146262' });
    expect(symbolImage('shape:cave', { color: '#146262' })).toBe(first);
    expect(symbolImage('shape:cave', { color: '#146262', approximate: true })).not.toBe(first);
    expect(symbolImage('pit.png', { color: '#111111' })).toBe(symbolImage('pit.png', { color: '#222222' }));
  });
});
