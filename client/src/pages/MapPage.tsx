// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { Group, Panel, Separator } from 'react-resizable-panels';
import { useFeatureTypes, useGeofiles, useMapLayers, useMe, useRasterMaps } from '../api/hooks.ts';
import EditToolbar from '../components/map/EditToolbar.tsx';
import LayerPanel from '../components/map/LayerPanel.tsx';
import MapSearch from '../components/map/MapSearch.tsx';
import SelectionPanel from '../components/map/SelectionPanel.tsx';
import { setActiveBaseLayer, syncBaseLayers } from '../map/baseLayers.ts';
import { ENTRANCE_LAYER_ID, attachEntranceLoader, createEntranceLayer } from '../map/entranceLayer.ts';
import {
  SURFACE_FEATURE_LAYER_ID,
  attachSurfaceFeatureLoader,
  createSurfaceFeatureLayer,
  setFeatureTypeSymbols,
  setSelectedSurfaceFeature,
} from '../map/featureLayer.ts';
import { attachGeofileLoader, syncGeofileLayers } from '../map/geofileLayers.ts';
import { syncRasterLayers } from '../map/rasterLayers.ts';
import { attachHoverTooltip } from '../map/hoverTooltip.ts';
import { getWorkspaceMap } from '../map/mapContext.ts';
import { MapEditController } from '../map/mapEdit.ts';
import { attachSelection } from '../map/selection.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import './MapPage.css';

/** Map workspace v1: fixed resizable panes; docking comes later. */
export default function MapPage() {
  const mapTarget = useRef<HTMLDivElement>(null);
  const { data: layers } = useMapLayers();
  const { data: featureTypes } = useFeatureTypes();
  const { data: me } = useMe();
  const [activeBaseId, setActiveBaseId] = useState<number>();
  const [entrancesVisible, setEntrancesVisible] = useState(true);
  const [surfaceFeaturesVisible, setSurfaceFeaturesVisible] = useState(true);
  const [editController, setEditController] = useState<MapEditController | null>(null);
  const selection = useWorkspaceStore((s) => s.selection);
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const visibleGeofileIds = useWorkspaceStore((s) => s.visibleGeofileIds);
  const setGeofileVisible = useWorkspaceStore((s) => s.setGeofileVisible);
  const { data: geofilePage } = useGeofiles({ pageSize: 100 });
  const importedGeofiles = useMemo(
    () => (geofilePage?.items ?? []).filter((g) => g.importStatus === 'imported'),
    [geofilePage],
  );
  const visibleRasterIds = useWorkspaceStore((s) => s.visibleRasterIds);
  const setRasterVisible = useWorkspaceStore((s) => s.setRasterVisible);
  const rasterOpacity = useWorkspaceStore((s) => s.rasterOpacity);
  const setRasterOpacity = useWorkspaceStore((s) => s.setRasterOpacity);
  const { data: rasterPage } = useRasterMaps({ pageSize: 100 });
  const readyRasters = useMemo(
    () => (rasterPage?.items ?? []).filter((r) => r.status === 'ready'),
    [rasterPage],
  );
  const canEdit = me?.roles.some((r) => ['Admin', 'Manager', 'Editor'].includes(r)) ?? false;

  useEffect(() => {
    const map = getWorkspaceMap();
    map.setTarget(mapTarget.current ?? undefined);

    for (const [id, create] of [
      [ENTRANCE_LAYER_ID, createEntranceLayer],
      [SURFACE_FEATURE_LAYER_ID, createSurfaceFeatureLayer],
    ] as const) {
      if (!map.getLayers().getArray().some((l) => l.get('id') === id)) {
        map.addLayer(create());
      }
    }

    const detachLoader = attachEntranceLoader(map);
    const detachFeatureLoader = attachSurfaceFeatureLoader(map);
    const detachGeofileLoader = attachGeofileLoader(map);
    const detachSelection = attachSelection(map, setSelection);
    const detachHover = attachHoverTooltip(map);
    const controller = new MapEditController(map);
    setEditController(controller);
    return () => {
      controller.dispose();
      setEditController(null);
      detachLoader();
      detachFeatureLoader();
      detachGeofileLoader();
      detachSelection();
      detachHover();
      map.setTarget(undefined);
    };
  }, [setSelection]);

  // Geofile overlays follow the workspace selection of visible geofiles.
  useEffect(() => {
    syncGeofileLayers(getWorkspaceMap(), importedGeofiles, new Set(visibleGeofileIds));
  }, [importedGeofiles, visibleGeofileIds]);

  // Raster overlays likewise, with per-map opacity.
  useEffect(() => {
    syncRasterLayers(
      getWorkspaceMap(),
      readyRasters,
      new Set(visibleRasterIds),
      new globalThis.Map(Object.entries(rasterOpacity)),
    );
  }, [readyRasters, visibleRasterIds, rasterOpacity]);

  // Highlight follows the workspace selection (also when set from the features table).
  useEffect(() => {
    setSelectedSurfaceFeature(selection?.kind === 'feature' ? selection.featureId : null);
  }, [selection]);

  useEffect(() => {
    if (featureTypes) {
      setFeatureTypeSymbols(featureTypes);
    }
  }, [featureTypes]);

  useEffect(() => {
    if (layers && activeBaseId === undefined) {
      const initial = layers.find((l) => l.isDefault && l.isBase) ?? layers.find((l) => l.isBase);
      if (initial) {
        setActiveBaseId(Number(initial.id));
      }
    }
  }, [layers, activeBaseId]);

  useEffect(() => {
    if (layers && activeBaseId !== undefined) {
      syncBaseLayers(getWorkspaceMap(), layers, activeBaseId);
    }
  }, [layers, activeBaseId]);

  useEffect(() => {
    const layer = getWorkspaceMap()
      .getLayers()
      .getArray()
      .find((l) => l.get('id') === ENTRANCE_LAYER_ID);
    layer?.setVisible(entrancesVisible);
  }, [entrancesVisible]);

  useEffect(() => {
    const layer = getWorkspaceMap()
      .getLayers()
      .getArray()
      .find((l) => l.get('id') === SURFACE_FEATURE_LAYER_ID);
    layer?.setVisible(surfaceFeaturesVisible);
  }, [surfaceFeaturesVisible]);

  return (
    <Group orientation="horizontal" className="map-workspace">
      {/* Panel sizes: bare numbers mean pixels in react-resizable-panels v4 — use percent strings. */}
      <Panel defaultSize="16%" minSize="10%" className="map-workspace-panel">
        <LayerPanel
          layers={layers ?? []}
          activeBaseId={activeBaseId}
          onBaseChange={(id) => {
            setActiveBaseId(id);
            setActiveBaseLayer(getWorkspaceMap(), id);
          }}
          entrancesVisible={entrancesVisible}
          onEntrancesVisibleChange={setEntrancesVisible}
          surfaceFeaturesVisible={surfaceFeaturesVisible}
          onSurfaceFeaturesVisibleChange={setSurfaceFeaturesVisible}
          geofiles={importedGeofiles}
          visibleGeofileIds={visibleGeofileIds}
          onGeofileVisibleChange={setGeofileVisible}
          rasters={readyRasters}
          visibleRasterIds={visibleRasterIds}
          onRasterVisibleChange={setRasterVisible}
          rasterOpacity={rasterOpacity}
          onRasterOpacityChange={setRasterOpacity}
        />
      </Panel>
      <Separator className="map-workspace-handle" />
      <Panel minSize="30%">
        <div className="map-canvas-wrap">
          <div ref={mapTarget} className="map-canvas" data-testid="map-canvas" />
          <div className="map-search-overlay">
            <MapSearch />
          </div>
          {canEdit && editController && (
            <div className="map-edit-overlay">
              <EditToolbar controller={editController} />
            </div>
          )}
        </div>
      </Panel>
      <Separator className="map-workspace-handle" />
      <Panel defaultSize="22%" minSize="12%" className="map-workspace-panel">
        <SelectionPanel />
      </Panel>
    </Group>
  );
}
