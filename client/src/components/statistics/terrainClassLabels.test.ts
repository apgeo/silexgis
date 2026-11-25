// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { terrainClassKey } from './terrainClassLabels.ts';

describe('terrainClassKey', () => {
  it('names a landform class apart from the position class of the same code', () => {
    expect(terrainClassKey('landform', 'hollow')).toBe('terrain.landforms.hollow');
    expect(terrainClassKey('position', 'hollow')).toBe('terrainStats.classes.hollow');
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      expect(t(terrainClassKey('landform', 'hollow'))).not.toBe(t(terrainClassKey('position', 'hollow')));
    }
  });
});
