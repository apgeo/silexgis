// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import type { useMapConfig } from '../../api/hooks.ts';
import { CENTERLINE_LAYER_ID, setCenterlineLimits, setCenterlineOverrides, setCenterlinesEnabled } from '../../map/centerlineLayer.ts';
import { ENTRANCE_LAYER_ID } from '../../map/entranceLayer.ts';
import { SURFACE_FEATURE_LAYER_ID } from '../../map/featureLayer.ts';
import { ENTRANCE_HEATMAP_LAYER_ID } from '../../map/heatmapLayer.ts';
import {
  KARST_DENSITY_LAYER_ID,
  setKarstDensityBands,
  setKarstDensityEnabled,
  setKarstDensityFixedBounds,
  setKarstDensityMode,
  type KarstDensityBands,
  type KarstDensityMode,
} from '../../map/karstDensityLayer.ts';
import { KARST_INDEX_LAYER_ID, setKarstIndexEnabled } from '../../map/karstIndexLayer.ts';
import { PHOTO_LAYER_ID, setPhotosEnabled } from '../../map/photoLayer.ts';
import { TRIP_LAYER_ID, setTripLayerFilter, setTripsEnabled, type TripLayerFilter } from '../../map/tripLayer.ts';
import { findOverlayLayer } from '../../map/mapContext.ts';

export interface OverlayVisibilityInput {
  entrancesVisible: boolean;
  surfaceFeaturesVisible: boolean;
  centerlinesVisible: boolean;
  /** The installation's rendering limits, once read. */
  mapConfig: ReturnType<typeof useMapConfig>['data'];
  centerlineDetailZoom: number | undefined;
  centerlineMaxPaths: number | undefined;
  heatmapVisible: boolean;
  karstDensityVisible: boolean;
  karstDensityMode: KarstDensityMode;
  karstDensityBands: KarstDensityBands;
  karstIndexVisible: boolean;
  photosVisible: boolean;
  tripsVisible: boolean;
  tripFilter: TripLayerFilter;
}

/**
 * Pushes the page's overlay switches and settings into the built-in map layers and their loaders,
 * so a hidden layer is not only invisible but also stops asking the server for data.
 */
export function useOverlayVisibility({
  entrancesVisible,
  surfaceFeaturesVisible,
  centerlinesVisible,
  mapConfig,
  centerlineDetailZoom,
  centerlineMaxPaths,
  heatmapVisible,
  karstDensityVisible,
  karstDensityMode,
  karstDensityBands,
  karstIndexVisible,
  photosVisible,
  tripsVisible,
  tripFilter,
}: OverlayVisibilityInput) {
  useEffect(() => {
    findOverlayLayer(ENTRANCE_LAYER_ID)?.setVisible(entrancesVisible);
  }, [entrancesVisible]);

  useEffect(() => {
    findOverlayLayer(SURFACE_FEATURE_LAYER_ID)?.setVisible(surfaceFeaturesVisible);
  }, [surfaceFeaturesVisible]);

  useEffect(() => {
    findOverlayLayer(CENTERLINE_LAYER_ID)?.setVisible(centerlinesVisible);
    setCenterlinesEnabled(centerlinesVisible); // gate the bbox loader so hidden = no fetches
  }, [centerlinesVisible]);

  // Rendering limits are the installation's, with this viewer's overrides on top.
  useEffect(() => {
    if (mapConfig) {
      setCenterlineLimits(mapConfig);
      setKarstDensityFixedBounds(mapConfig.densityFixedBands);
    }
  }, [mapConfig]);

  useEffect(() => {
    setCenterlineOverrides({ detailZoom: centerlineDetailZoom, maxPaths: centerlineMaxPaths });
  }, [centerlineDetailZoom, centerlineMaxPaths]);

  useEffect(() => {
    findOverlayLayer(ENTRANCE_HEATMAP_LAYER_ID)?.setVisible(heatmapVisible);
  }, [heatmapVisible]);

  useEffect(() => {
    findOverlayLayer(KARST_DENSITY_LAYER_ID)?.setVisible(karstDensityVisible);
    setKarstDensityEnabled(karstDensityVisible); // hidden = no fetches
  }, [karstDensityVisible]);

  useEffect(() => {
    setKarstDensityMode(karstDensityMode);
  }, [karstDensityMode]);

  useEffect(() => {
    setKarstDensityBands(karstDensityBands);
  }, [karstDensityBands]);

  useEffect(() => {
    findOverlayLayer(KARST_INDEX_LAYER_ID)?.setVisible(karstIndexVisible);
    setKarstIndexEnabled(karstIndexVisible); // hidden = no fetches
  }, [karstIndexVisible]);

  useEffect(() => {
    findOverlayLayer(PHOTO_LAYER_ID)?.setVisible(photosVisible);
    setPhotosEnabled(photosVisible); // gate the bbox loader so hidden = no fetches
  }, [photosVisible]);

  useEffect(() => {
    findOverlayLayer(TRIP_LAYER_ID)?.setVisible(tripsVisible);
    setTripsEnabled(tripsVisible); // gate the bbox loader so hidden = no fetches
  }, [tripsVisible]);

  // The narrowings reach the layer module, which is what the loader reads on every fetch — a
  // panned map must ask the same question the panel last set. Setting them re-asks immediately.
  useEffect(() => {
    setTripLayerFilter(tripFilter);
  }, [tripFilter]);
}
