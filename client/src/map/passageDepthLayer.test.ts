// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type Map from 'ol/Map';
import type { MultiLineString } from 'ol/geom';
import { fromLonLat, toLonLat } from 'ol/proj';
import { ApiError } from '../api/client.ts';
import type { PassageDepthWindow } from '../api/hooks.ts';
import { isHitTestable } from './hitTesting.ts';
import {
  PASSAGE_DEPTH_LAYER_ID,
  attachPassageDepthLoader,
  createPassageDepthLayer,
  getPassageDepthLoadState,
  getPassageDepthSource,
  passageDepthFeatures,
  setPassageDepthEnabled,
} from './passageDepthLayer.ts';

// Invented passage in the shape the passage-depth route answers with; none of it real.

function answer(over: Partial<PassageDepthWindow> = {}): PassageDepthWindow {
  return {
    hasTerrain: true,
    classEdgesM: [0, 10, 25, 50, 100, 200],
    caveCount: 1,
    truncated: false,
    maxCaves: 25,
    lines: [
      {
        caveId: '00000000-0000-0000-0000-000000000001',
        depthClass: 3,
        paths: [
          [
            [25, 45],
            [25.001, 45.001],
          ],
        ],
      },
    ],
    ...over,
  };
}

describe('the depth-coloured passage features', () => {
  it('draws each line from the vertices the server sent, carrying its class', () => {
    const [feature] = passageDepthFeatures(answer().lines);
    expect(feature.get('depthClass')).toBe(3);
    const line = (feature.getGeometry() as MultiLineString).getCoordinates()[0].map((c) => toLonLat(c));
    expect(line[0][0]).toBeCloseTo(25, 6);
    expect(line[1][1]).toBeCloseTo(45.001, 6);
  });

  it('is skipped by the hit test, since a click on it answers nothing', () => {
    expect(createPassageDepthLayer().get('id')).toBe(PASSAGE_DEPTH_LAYER_ID);
    expect(isHitTestable(createPassageDepthLayer())).toBe(false);
  });
});

describe('loading the passage of a window', () => {
  let moveEnd: (() => void) | undefined;
  const map = {
    getSize: () => [800, 600],
    getView: () => ({
      calculateExtent: () => [...fromLonLat([25, 45]), ...fromLonLat([26, 46])],
      getZoom: () => 10.6,
    }),
    on: (_type: string, listener: () => void) => {
      moveEnd = listener;
    },
    un: () => {
      moveEnd = undefined;
    },
  } as unknown as Map;

  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    setPassageDepthEnabled(false);
    vi.useRealTimers();
  });

  it('asks nothing while the layer is off, then asks for the window', async () => {
    const fetchWindow = vi.fn(async (_bbox: string) => answer());
    const detach = attachPassageDepthLoader(map, fetchWindow);
    moveEnd?.();
    await vi.advanceTimersByTimeAsync(300);
    expect(fetchWindow).not.toHaveBeenCalled();

    setPassageDepthEnabled(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(fetchWindow).toHaveBeenCalledTimes(1);
    expect(fetchWindow.mock.calls[0][0].split(',').map(Number)[0]).toBeCloseTo(25, 4);
    expect(getPassageDepthSource().getFeatures()).toHaveLength(1);
    expect(getPassageDepthLoadState()).toMatchObject({ status: 'ok', caveCount: 1, hasTerrain: true });
    detach();
  });

  it('says a refusal as one and leaves nothing of an earlier window drawn', async () => {
    let refuse = false;
    const fetchWindow = vi.fn(async () => {
      if (refuse) {
        throw new ApiError(403, 'access.forbidden');
      }
      return answer();
    });
    const detach = attachPassageDepthLoader(map, fetchWindow);
    setPassageDepthEnabled(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(getPassageDepthSource().getFeatures()).toHaveLength(1);

    refuse = true;
    moveEnd?.();
    await vi.advanceTimersByTimeAsync(300);
    expect(getPassageDepthSource().getFeatures()).toHaveLength(0);
    expect(getPassageDepthLoadState()).toMatchObject({ status: 'refused', refusal: 'access.forbidden', refusalStatus: 403 });
    detach();
  });

  it('keeps a window with no terrain built apart from one with no passage in it', async () => {
    const fetchWindow = vi.fn(async () => answer({ hasTerrain: false, lines: [], caveCount: 0 }));
    const detach = attachPassageDepthLoader(map, fetchWindow);
    setPassageDepthEnabled(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(getPassageDepthLoadState()).toMatchObject({ status: 'ok', hasTerrain: false });
    expect(getPassageDepthSource().getFeatures()).toHaveLength(0);
    detach();
  });
});
