// SPDX-License-Identifier: AGPL-3.0-or-later
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { passageDepthPalette } from '../../map/markerPalette.ts';
import type { PassageDepthLoadState } from '../../map/passageDepthLayer.ts';
import { PassageDepthLegend } from './PassageDepthControls.tsx';
import { passageDepthClassLabels } from './passageDepthLabels.ts';

const state: PassageDepthLoadState = {
  status: 'ok',
  hasTerrain: true,
  caveCount: 2,
  truncated: false,
  maxCaves: 25,
  classEdgesM: [0, 10, 25, 50, 100, 200],
};

describe('the passage depth key', () => {
  it('has one row per class the server defines, in metres, and one palette colour for each', async () => {
    await i18n.changeLanguage('en');
    render(<PassageDepthLegend state={state} />);
    const rows = screen.getAllByTestId('map-passage-depth-legend-row');
    expect(rows).toHaveLength(state.classEdgesM.length + 1);
    expect(passageDepthPalette.classes).toHaveLength(rows.length);
    expect(rows[0].textContent).toContain('above its ground');
    expect(rows[1].textContent).toContain('0 – 10 m');
    expect(rows[rows.length - 1].textContent).toContain('More than 200 m');
  });

  it('labels the classes in Romanian as well', async () => {
    await i18n.changeLanguage('ro');
    const labels = passageDepthClassLabels([0, 10], (key, options) => String(i18n.t(key, options)));
    expect(labels).toHaveLength(3);
    expect(labels[2]).toContain('Peste 10 m');
    await i18n.changeLanguage('en');
  });
});
