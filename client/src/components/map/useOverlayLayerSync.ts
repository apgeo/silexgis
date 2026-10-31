// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import type { EventsKey } from 'ol/events';
import type BaseLayer from 'ol/layer/Base';
import { unByKey } from 'ol/Observable';
import { syncTileOverlays } from '../../map/baseLayers.ts';
import { setMapDeclutter } from '../../map/declutter.ts';
import { CENTERLINE_LAYER_ID } from '../../map/centerlineLayer.ts';
import { ENTRANCE_LAYER_ID } from '../../map/entranceLayer.ts';
import { SURFACE_FEATURE_LAYER_ID } from '../../map/featureLayer.ts';
import { ENTRANCE_HEATMAP_LAYER_ID } from '../../map/heatmapLayer.ts';
import { KARST_DENSITY_LAYER_ID } from '../../map/karstDensityLayer.ts';
import { KARST_INDEX_LAYER_ID } from '../../map/karstIndexLayer.ts';
import { GEOFILE_LAYER_PREFIX, syncGeofileLayers } from '../../map/geofileLayers.ts';
import { RASTER_LAYER_PREFIX, syncRasterLayers } from '../../map/rasterLayers.ts';
import { syncTerrainDerivativeLayers, terrainDerivativeIdOf } from '../../map/terrainDerivativeLayers.ts';
import { applyPendingOverlayOrder, getOverlayGroup, getWorkspaceMap, setLayerOpacity } from '../../map/mapContext.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

/** The inputs the store-to-map overlay passes follow. */
export interface OverlayLayerSyncInput {
  layers: Parameters<typeof syncTileOverlays>[1] | undefined;
  language: string;
  importedGeofiles: Parameters<typeof syncGeofileLayers>[1];
  readyRasters: Parameters<typeof syncRasterLayers>[0];
  derivativeLayers: Parameters<typeof syncTerrainDerivativeLayers>[0];
  visibleGeofileIds: string[];
  visibleTileOverlayIds: number[];
  visibleRasterIds: string[];
  visibleTerrainDerivativeIds: string[];
  overlayOpacity: Record<string, number>;
  tileOverlayOpacity: Record<number, number>;
  rasterOpacity: Record<string, number>;
  declutterLabels: boolean;
}

/**
 * Keeps the map's overlay layers (imported files, tile overlays, rasters, computed pictures of the
 * ground, label decluttering and the built-in vector layers' opacity) in step with the workspace
 * store, and mirrors opacity changes made on the layers themselves back into it.
 */
export function useOverlayLayerSync({
  layers,
  language,
  importedGeofiles,
  readyRasters,
  derivativeLayers,
  visibleGeofileIds,
  visibleTileOverlayIds,
  visibleRasterIds,
  visibleTerrainDerivativeIds,
  overlayOpacity,
  tileOverlayOpacity,
  rasterOpacity,
  declutterLabels,
}: OverlayLayerSyncInput) {
  // Geofile overlays follow the workspace selection of visible geofiles, with per-file opacity.
  useEffect(() => {
    syncGeofileLayers(
      getWorkspaceMap(),
      importedGeofiles,
      new Set(visibleGeofileIds),
      new globalThis.Map(Object.entries(overlayOpacity)),
    );
    // A just-applied saved view may prescribe a stacking that includes these layers.
    applyPendingOverlayOrder();
  }, [importedGeofiles, visibleGeofileIds, overlayOpacity]);

  // Built-in vector overlays (entrances, surface features, centerlines, heatmap) follow their opacity.
  useEffect(() => {
    for (const id of [
      ENTRANCE_LAYER_ID,
      SURFACE_FEATURE_LAYER_ID,
      CENTERLINE_LAYER_ID,
      ENTRANCE_HEATMAP_LAYER_ID,
      KARST_DENSITY_LAYER_ID,
      KARST_INDEX_LAYER_ID,
    ]) {
      setLayerOpacity(id, overlayOpacity[id] ?? 1);
    }
  }, [overlayOpacity]);

  // Tile overlays from the catalogue: several at once, above the basemap and below the data.
  useEffect(() => {
    if (layers) {
      syncTileOverlays(getWorkspaceMap(), layers, new Set(visibleTileOverlayIds), tileOverlayOpacity, language);
    }
  }, [layers, visibleTileOverlayIds, tileOverlayOpacity, language]);

  // Whether crowded labels give way to each other. Applied to the map rather than held only in the
  // store, and applied on mount as well as on change, because a layer built before this ran would
  // otherwise be drawn under whatever the module last remembered.
  useEffect(() => {
    setMapDeclutter(getWorkspaceMap(), declutterLabels);
  }, [declutterLabels]);

  // Raster overlays likewise, with per-map opacity.
  useEffect(() => {
    syncRasterLayers(readyRasters, new Set(visibleRasterIds), new globalThis.Map(Object.entries(rasterOpacity)));
    applyPendingOverlayOrder();
  }, [readyRasters, visibleRasterIds, rasterOpacity]);

  // The computed pictures of the ground, likewise. A pass of their own rather than a branch of the
  // one above: each pass removes every layer carrying its own prefix that it does not want, so one
  // pass over both sets would have each half delete the other's layers.
  //
  // Their transparency is kept in the overlay slice, keyed by the picture's own id — the same key
  // the mirror below writes back when a composer slider is dragged. Read from one slice and
  // written to another, a transparency somebody set by hand would be forgotten every time the
  // listing is re-read for fresh addresses, and the layer would snap back to fully opaque.
  useEffect(() => {
    syncTerrainDerivativeLayers(
      derivativeLayers,
      new Set(visibleTerrainDerivativeIds),
      new globalThis.Map(Object.entries(overlayOpacity)),
    );
    applyPendingOverlayOrder();
  }, [derivativeLayers, visibleTerrainDerivativeIds, overlayOpacity]);

  // Mirrors opacity changes made on the OL layers (the composer's transparency
  // sliders write layer.setOpacity directly) back into the workspace store, which
  // owns persistence (saved views) and re-creation of geofile/raster layers.
  // The value guard stops the write-back loop with the store→OL effects above.
  useEffect(() => {
    const collection = getOverlayGroup().getLayers();
    const bound = new globalThis.Map<BaseLayer, EventsKey>();

    const mirror = (layer: BaseLayer) => {
      const id = layer.get('id') as string | undefined;
      if (!id) {
        return;
      }
      const opacity = Math.round(layer.getOpacity() * 100) / 100;
      const state = useWorkspaceStore.getState();
      if (id.startsWith(RASTER_LAYER_PREFIX)) {
        const rasterId = id.slice(RASTER_LAYER_PREFIX.length);
        if (state.rasterOpacity[rasterId] !== opacity) {
          state.setRasterOpacity(rasterId, opacity);
        }
      } else {
        // One computed picture of the ground is several map layers, one per raster of the build,
        // so its transparency is remembered against the picture rather than against whichever of
        // its files the slider happened to be over.
        const derivativeId = terrainDerivativeIdOf(id);
        const key = derivativeId
          ?? (id.startsWith(GEOFILE_LAYER_PREFIX) ? id.slice(GEOFILE_LAYER_PREFIX.length) : id);
        if ((state.overlayOpacity[key] ?? 1) !== opacity) {
          state.setOverlayOpacity(key, opacity);
        }
      }
    };

    const bind = (layer: BaseLayer) => {
      bound.set(layer, layer.on('change:opacity', () => mirror(layer)));
    };
    const unbind = (layer: BaseLayer) => {
      const key = bound.get(layer);
      if (key) {
        unByKey(key);
        bound.delete(layer);
      }
    };

    collection.getArray().forEach(bind);
    const addKey = collection.on('add', (e) => bind(e.element as BaseLayer));
    const removeKey = collection.on('remove', (e) => unbind(e.element as BaseLayer));
    return () => {
      unByKey([addKey, removeKey]);
      [...bound.values()].forEach((key) => unByKey(key));
      bound.clear();
    };
  }, []);
}
