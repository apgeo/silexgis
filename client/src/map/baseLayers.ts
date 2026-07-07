// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import type { MapLayerInfo } from '../api/hooks.ts';

const BASE_ID_PROP = 'silexgis:baseLayerId';

/** Creates/refreshes base tile layers from the server catalog (only XYZ for now). */
export function syncBaseLayers(map: Map, catalog: MapLayerInfo[], activeId: number): void {
  const existing = new Set(
    map
      .getLayers()
      .getArray()
      .map((layer) => layer.get(BASE_ID_PROP) as number | undefined)
      .filter((id) => id !== undefined),
  );

  for (const entry of catalog) {
    // int64 ids arrive as number | string from the generated contract — normalize once here.
    const entryId = Number(entry.id);
    if (entry.layerKind !== 'xyz' || !entry.isBase || existing.has(entryId)) {
      continue;
    }
    const layer = new TileLayer({
      source: new XYZ({ url: entry.urlTemplate, attributions: entry.attribution ?? undefined }),
      visible: false,
      zIndex: 0,
    });
    layer.set(BASE_ID_PROP, entryId);
    map.addLayer(layer);
  }

  setActiveBaseLayer(map, activeId);
}

export function setActiveBaseLayer(map: Map, id: number): void {
  for (const layer of map.getLayers().getArray()) {
    const baseId = layer.get(BASE_ID_PROP) as number | undefined;
    if (baseId !== undefined) {
      layer.setVisible(baseId === id);
    }
  }
}
