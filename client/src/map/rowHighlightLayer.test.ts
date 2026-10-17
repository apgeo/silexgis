// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import { LineString, Point } from 'ol/geom';
import { afterEach, describe, expect, it } from 'vitest';
import { announceRowHover } from '../workspace/tableMapLink.ts';
import { getEntranceSource } from './entranceLayer.ts';
import { getSurfaceFeatureSource } from './featureLayer.ts';
import { attachRowHighlight, getRowHighlightSource, highlightedGeometries } from './rowHighlightLayer.ts';

function entrance(id: string, caveId: string, at: [number, number]): Feature {
  return new Feature({ geometry: new Point(at), id, caveId });
}

afterEach(() => {
  announceRowHover(null);
  getEntranceSource().clear(true);
  getSurfaceFeatureSource().clear(true);
});

describe('which drawn things a pointed-at row names', () => {
  it("rings a cave's drawn entrances by the cave's id, and an entrance or feature by its own", () => {
    const entrances = [
      entrance('e1', 'cave-a', [1, 1]),
      entrance('e2', 'cave-a', [2, 2]),
      entrance('e3', 'cave-b', [3, 3]),
    ];
    const features = [new Feature({ geometry: new LineString([[0, 0], [5, 5]]), id: 'f1' })];

    expect(highlightedGeometries(new Set(['cave-a']), entrances, features)).toHaveLength(2);
    expect(highlightedGeometries(new Set(['e3']), entrances, features)).toHaveLength(1);
    expect(highlightedGeometries(new Set(['f1']), entrances, features)[0]).toBeInstanceOf(LineString);
    expect(highlightedGeometries(new Set(), entrances, features)).toEqual([]);
  });

  it('never rings a cluster: it is a place on the screen, and says nothing about which row is in it', () => {
    const cluster = new Feature({ geometry: new Point([1, 1]), cluster: true, count: 4 });
    // Even a cluster that happened to carry an id would say where a member is, finer than the
    // cluster cell the reader was shown.
    cluster.set('id', 'cave-a');
    expect(highlightedGeometries(new Set(['cave-a']), [cluster], [])).toEqual([]);
  });

  it('marks a copy where the map drew the thing, and takes nothing from the row itself', () => {
    const drawn = entrance('e1', 'cave-a', [10, 20]);
    const [ring] = highlightedGeometries(new Set(['cave-a']), [drawn], []);
    expect((ring as Point).getCoordinates()).toEqual([10, 20]);
    // A copy, so the ring can be cleared without touching the entrance layer's own feature.
    expect(ring).not.toBe(drawn.getGeometry());
  });
});

describe('the row highlight overlay', () => {
  it('follows what a list announces, and clears when the list stops pointing', () => {
    getEntranceSource().addFeatures([entrance('e1', 'cave-a', [1, 1]), entrance('e2', 'cave-b', [2, 2])]);
    const detach = attachRowHighlight();

    announceRowHover(['cave-b']);
    const ringed = getRowHighlightSource().getFeatures();
    expect(ringed).toHaveLength(1);
    expect((ringed[0].getGeometry() as Point).getCoordinates()).toEqual([2, 2]);

    announceRowHover(null);
    expect(getRowHighlightSource().getFeatures()).toHaveLength(0);
    detach();
  });

  it('follows the entrance layer when it reloads, so a ring is not left where the old features were', () => {
    const detach = attachRowHighlight();
    announceRowHover(['cave-a']);
    expect(getRowHighlightSource().getFeatures()).toHaveLength(0);

    // The pan that brings the cave into the window reloads the layer; the ring arrives with it.
    getEntranceSource().addFeatures([entrance('e1', 'cave-a', [7, 8])]);
    getEntranceSource().changed();
    expect(getRowHighlightSource().getFeatures()).toHaveLength(1);
    detach();
  });

  it('draws nothing after it is detached', () => {
    getEntranceSource().addFeatures([entrance('e1', 'cave-a', [1, 1])]);
    const detach = attachRowHighlight();
    detach();
    announceRowHover(['cave-a']);
    expect(getRowHighlightSource().getFeatures()).toHaveLength(0);
  });
});
