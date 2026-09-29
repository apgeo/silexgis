// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { DEFAULT_MOVIE_SETTINGS, MOVIE_LABEL_SIZE_RANGE } from './movieSettings.ts';
import { applyMovieView, movieLabelDevicePixels, type MovieViewViewer } from './movieView.ts';

const CONSTANTS = { CAMERA_PERSPECTIVE: 1, CAMERA_ORTHOGRAPHIC: 2, SHADING_DEPTH: 9, SHADING_DEPTH_CURSOR: 11 };
const HEIGHT = 0;
const LENGTH = 1;

function viewer(shadingMode = HEIGHT): MovieViewViewer {
  return {
    shadingMode,
    cameraType: 1,
    linewidth: 0,
    zScale: 0.5,
    hasRealTerrain: false,
    fog: false,
    HUD: false,
  } as unknown as MovieViewViewer;
}

describe('applyMovieView', () => {
  it('puts the viewer’s own shading back when a chosen one is left to the viewer again', () => {
    const shown = viewer(HEIGHT);
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shadingMode: LENGTH }, CONSTANTS, HEIGHT);
    expect(shown.shadingMode).toBe(LENGTH);

    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shadingMode: null }, CONSTANTS, HEIGHT);
    expect(shown.shadingMode).toBe(HEIGHT);
  });

  it('leaves the shading as it stands when it is not told what the viewer’s own was', () => {
    const shown = viewer(LENGTH);
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shadingMode: null }, CONSTANTS);
    expect(shown.shadingMode).toBe(LENGTH);
  });

  it('never sets a depth shading on a model without terrain, even as the viewer’s own', () => {
    const shown = viewer(HEIGHT);
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shadingMode: 9 }, CONSTANTS, HEIGHT);
    expect(shown.shadingMode).toBe(HEIGHT);
  });
});

describe('movieLabelDevicePixels', () => {
  it('draws a label at its share of the picture, held to what the atlas can draw', () => {
    expect(movieLabelDevicePixels(16, 1)).toBe(16);
    expect(movieLabelDevicePixels(16, 2)).toBe(32);
    expect(movieLabelDevicePixels(16, 5)).toBe(MOVIE_LABEL_SIZE_RANGE.max);
  });
});
