// SPDX-License-Identifier: AGPL-3.0-or-later
import { theme } from 'antd';
import { describe, expect, it } from 'vitest';

import { DEFAULT_APPEARANCE } from '../../stores/uiPrefsStore.ts';
import { buildThemeConfig } from '../../theme.ts';
import { paletteFor } from './chartTheme.ts';

const palettes = (['light', 'dark'] as const).map((mode) => ({
  mode,
  palette: paletteFor(theme.getDesignToken(buildThemeConfig({ ...DEFAULT_APPEARANCE, theme: mode }))),
}));

/**
 * A colour that carries a meaning has to be distinguishable from every other colour that carries
 * one. Two marks drawn the same colour is not a small blemish here: the legend is the only thing
 * that makes a colour mean anything, and two entries with one swatch make it say the wrong thing
 * in a way that looks exactly as deliberate as the right thing.
 */
describe('the chart palette keeps its meanings apart', () => {
  for (const { mode, palette } of palettes) {
    it(`gives the fitted line a colour no group can take (${mode})`, () => {
      expect(palette.series).not.toContain(palette.fit);
    });

    it(`keeps the marks that belong to no group out of the categorical range (${mode})`, () => {
      expect(palette.series).not.toContain(palette.unclassified);
      expect(palette.unclassified).not.toBe(palette.fit);
    });

    it(`gives every group its own colour (${mode})`, () => {
      expect(new Set(palette.series).size).toBe(palette.series.length);
    });
  }
});
