// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * The viewer's own Romanian, as every vendored copy carries it. The viewer reads these strings
 * itself, so no application key and no i18n test reaches them: a wrong word here ("by depth" for
 * the height colouring) or a misspelt "înălțime" stays until somebody reads the 3D toolbar.
 */
const caveviewDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'public', 'caveview');
const copies = readdirSync(caveviewDir).filter((name) => name.startsWith('v'));

type Lang = {
  settings: { shading: { height: string } };
  surface: { terrain: { shading: { height: string } } };
  hud: { height: string };
};

describe('the vendored viewer Romanian', () => {
  it.each(copies)('%s names the height colouring and spells "înălțime"', (copy) => {
    const text = readFileSync(join(caveviewDir, copy, 'lib', 'lang-ro.json'), 'utf8');
    expect(text).not.toContain('înalțime');
    const lang = JSON.parse(text) as Lang;
    expect(lang.settings.shading.height).toBe('după înălțime');
    expect(lang.surface.terrain.shading.height).toBe('după înălțime/alt.');
    expect(lang.hud.height).toBe('înălțime');
  });
});
