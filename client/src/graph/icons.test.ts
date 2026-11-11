// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { GRAPH_ICONS, iconDataUri, iconSvg } from './icons.ts';

describe('graph icons', () => {
  it('writes every icon as one well-formed SVG in the colour asked for, with no colour left unset', () => {
    const parser = new DOMParser();
    for (const icon of GRAPH_ICONS) {
      const svg = iconSvg(icon, '#123456');
      expect(svg, icon).not.toContain('{c}');
      const doc = parser.parseFromString(svg, 'image/svg+xml');
      expect(doc.getElementsByTagName('parsererror'), icon).toHaveLength(0);
      expect(doc.documentElement.getAttribute('stroke')).toBe('#123456');
      expect(doc.documentElement.children.length, icon).toBeGreaterThan(0);
    }
  });

  it('gives a data URI that decodes back to the same SVG and is built once per icon and colour', () => {
    const uri = iconDataUri('cave', '#ffffff');
    expect(uri.startsWith('data:image/svg+xml')).toBe(true);
    expect(decodeURIComponent(uri.slice(uri.indexOf(',') + 1))).toBe(iconSvg('cave', '#ffffff'));
    expect(iconDataUri('cave', '#ffffff')).toBe(uri);
    expect(iconDataUri('cave', '#000000')).not.toBe(uri);
  });
});
