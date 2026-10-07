// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { DEFAULT_MOVIE_SETTINGS, MOVIE_LABEL_SIZE_RANGE } from './movieSettings.ts';
import { applyMovieView, movieLabelDevicePixels, movieViewMode, turnToMovieView, type MovieViewViewer } from './movieView.ts';

// A viewer's own numbers for the shadings, deliberately not the ones any real viewer uses: the
// settings name a shading, and what is written to the viewer has to be this viewer's number for it.
const HEIGHT = 40;
const LENGTH = 41;
const DEPTH = 49;
const CONSTANTS = {
  CAMERA_PERSPECTIVE: 1,
  CAMERA_ORTHOGRAPHIC: 2,
  SHADING_HEIGHT: HEIGHT,
  SHADING_LENGTH: LENGTH,
  SHADING_INCLINATION: 42,
  SHADING_SINGLE: 44,
  SHADING_SURVEY: 45,
  SHADING_DEPTH: DEPTH,
  SHADING_DEPTH_CURSOR: 51,
};

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
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shading: 'length' }, CONSTANTS, HEIGHT);
    expect(shown.shadingMode).toBe(LENGTH);

    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shading: null }, CONSTANTS, HEIGHT);
    expect(shown.shadingMode).toBe(HEIGHT);
  });

  it('writes a named shading as the loaded viewer’s own number for it, whatever that number is', () => {
    const shown = viewer(HEIGHT);
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shading: 'survey' }, CONSTANTS, HEIGHT);
    expect(shown.shadingMode).toBe(45);
    // The same name on a viewer that numbers its shadings differently.
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shading: 'survey' }, { ...CONSTANTS, SHADING_SURVEY: 7 }, HEIGHT);
    expect(shown.shadingMode).toBe(7);
  });

  it('leaves a shading the loaded viewer has no constant for to the viewer', () => {
    const shown = viewer(LENGTH);
    const older = { ...CONSTANTS, SHADING_SURVEY: undefined } as unknown as typeof CONSTANTS;
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shading: 'survey' }, older, HEIGHT);
    expect(shown.shadingMode).toBe(HEIGHT);
  });

  it('leaves the shading as it stands when it is not told what the viewer’s own was', () => {
    const shown = viewer(LENGTH);
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shading: null }, CONSTANTS);
    expect(shown.shadingMode).toBe(LENGTH);
  });

  describe('the surface over the cave', () => {
    /** A viewer that records, in order, every property written to it. */
    function recording(state: Record<string, unknown>): { shown: MovieViewViewer; writes: string[] } {
      const writes: string[] = [];
      const shown = new Proxy(
        { ...(viewer(HEIGHT) as unknown as Record<string, unknown>), ...state },
        {
          set(target, key, value) {
            writes.push(String(key));
            target[String(key)] = value;
            return true;
          },
        },
      ) as unknown as MovieViewViewer;
      return { shown, writes };
    }

    it('is drawn only when asked for, on a model whose file carries one — and is put back', () => {
      const { shown } = recording({ hasTerrain: true, hasRealTerrain: true, terrain: false });
      // The default leaves it off.
      applyMovieView(shown, DEFAULT_MOVIE_SETTINGS.view, CONSTANTS, HEIGHT);
      expect(shown.terrain).toBe(false);

      const restore = applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, terrain: true }, CONSTANTS, HEIGHT);
      expect(shown.terrain).toBe(true);
      restore();
      expect(shown.terrain).toBe(false);
    });

    it('is switched off when the viewer shows it on its own and the movie does not ask for it', () => {
      // The viewer re-applies the view its reader saved as its default on every load.
      const { shown } = recording({ hasTerrain: true, hasRealTerrain: true, terrain: true });
      const restore = applyMovieView(shown, DEFAULT_MOVIE_SETTINGS.view, CONSTANTS, HEIGHT);
      expect(shown.terrain).toBe(false);
      restore();
      expect(shown.terrain).toBe(true);
    });

    it('is never drawn from the flat plane the viewer lays under a survey with a coordinate system', () => {
      // That plane is made of tiles asked for again as the camera moves: asked for or not, it is off.
      const { shown } = recording({ hasTerrain: true, hasRealTerrain: false, terrain: true });
      applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, terrain: true }, CONSTANTS, HEIGHT);
      expect(shown.terrain).toBe(false);
    });

    it('is not written at all on a model with no terrain', () => {
      const { shown, writes } = recording({ hasTerrain: false, hasRealTerrain: null });
      applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, terrain: true, shading: 'length' }, CONSTANTS, HEIGHT);
      expect(writes).not.toContain('terrain');
      expect(writes).toContain('shadingMode');
    });

    it('is written before the shading, which a depth shading is measured from', () => {
      const { shown, writes } = recording({ hasTerrain: true, hasRealTerrain: true, terrain: false });
      applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, terrain: true, shading: 'depth' }, CONSTANTS, HEIGHT);
      expect(shown.shadingMode).toBe(DEPTH);
      expect(writes.indexOf('terrain')).toBeGreaterThanOrEqual(0);
      expect(writes.indexOf('terrain')).toBeLessThan(writes.indexOf('shadingMode'));
    });
  });

  it('never sets a depth shading on a model without terrain, even as the viewer’s own', () => {
    const shown = viewer(HEIGHT);
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shading: 'depth' }, CONSTANTS, HEIGHT);
    expect(shown.shadingMode).toBe(HEIGHT);
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, shading: null }, CONSTANTS, DEPTH);
    expect(shown.shadingMode).toBe(HEIGHT);

    // The same choice on a model that does stand on terrain is drawn.
    const onTerrain = { ...viewer(HEIGHT), hasRealTerrain: true } as MovieViewViewer;
    applyMovieView(onTerrain, { ...DEFAULT_MOVIE_SETTINGS.view, shading: 'depth' }, CONSTANTS, HEIGHT);
    expect(onTerrain.shadingMode).toBe(DEPTH);
  });
});

describe('turnToMovieView', () => {
  const VIEWS = { VIEW_PLAN: 1, VIEW_ELEVATION_N: 2, VIEW_ELEVATION_S: 3, VIEW_ELEVATION_E: 4, VIEW_ELEVATION_W: 5 };

  it('names each starting view by the viewer\'s own constant', () => {
    expect(movieViewMode('plan', VIEWS)).toBe(1);
    expect(movieViewMode('north', VIEWS)).toBe(2);
    expect(movieViewMode('south', VIEWS)).toBe(3);
    expect(movieViewMode('east', VIEWS)).toBe(4);
    expect(movieViewMode('west', VIEWS)).toBe(5);
  });

  it('turns the viewer to it every time it is asked, since the viewer cannot say where it looks from', () => {
    const written: (number | 'settle')[] = [];
    const shown = {
      get view() { return 1; },
      set view(mode: number) { written.push(mode); },
      setCameraAngles: (angles: { azimuth?: number; polar?: number }) => {
        if (angles.azimuth === undefined && angles.polar === undefined) written.push('settle');
      },
    };
    turnToMovieView(shown, 'north', VIEWS);
    turnToMovieView(shown, 'north', VIEWS);
    turnToMovieView(shown, 'plan', VIEWS);
    // Each turn first brings one still under way to its end: the viewer ignores a turn asked for
    // while another is running.
    expect(written).toEqual(['settle', 2, 'settle', 2, 'settle', 1]);
  });

  it('is not among the settings written on every change', () => {
    const written: number[] = [];
    const shown = Object.defineProperty(viewer(HEIGHT), 'view', {
      get: () => 1,
      set: (mode: number) => written.push(mode),
    });
    applyMovieView(shown, { ...DEFAULT_MOVIE_SETTINGS.view, direction: 'east' }, CONSTANTS, HEIGHT);
    expect(written).toEqual([]);
  });
});

describe('movieLabelDevicePixels', () => {
  it('draws a label at its share of the picture, held to what the atlas can draw', () => {
    expect(movieLabelDevicePixels(16, 1)).toBe(16);
    expect(movieLabelDevicePixels(16, 2)).toBe(32);
    expect(movieLabelDevicePixels(16, 5)).toBe(MOVIE_LABEL_SIZE_RANGE.max);
  });
});
