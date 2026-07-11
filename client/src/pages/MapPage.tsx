// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { Button, Tooltip } from 'antd';
import { CodeSandboxOutlined, ExportOutlined, LeftOutlined, RightOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Group, Panel, Separator, usePanelRef } from 'react-resizable-panels';
import { useFeatureTypes, useGeofiles, useMapLayers, useMapViews, useMe, useRasterMaps } from '../api/hooks.ts';
import BaseLayerSwitcher from '../components/map/BaseLayerSwitcher.tsx';
import EditToolbar from '../components/map/EditToolbar.tsx';
import LayerPanel from '../components/map/LayerPanel.tsx';
import ViewsPanel from '../components/map/ViewsPanel.tsx';
import MapSearch from '../components/map/MapSearch.tsx';
import SelectionPanel from '../components/map/SelectionPanel.tsx';
import { setActiveBaseLayer, syncBaseLayers } from '../map/baseLayers.ts';
import { CENTERLINE_LAYER_ID, attachCenterlineLoader, createCenterlineLayer } from '../map/centerlineLayer.ts';
import { ENTRANCE_LAYER_ID, attachEntranceLoader, createEntranceLayer, reloadEntrances } from '../map/entranceLayer.ts';
import {
  SURFACE_FEATURE_LAYER_ID,
  attachSurfaceFeatureLoader,
  createSurfaceFeatureLayer,
  reloadSurfaceFeatures,
  setFeatureTypeSymbols,
  setSelectedSurfaceFeature,
} from '../map/featureLayer.ts';
import { attachGeofileLoader, syncGeofileLayers } from '../map/geofileLayers.ts';
import { getMapTagFilter, setMapTagFilter } from '../map/mapFilters.ts';
import { applyViewConfig, captureViewConfig } from '../map/viewConfig.ts';
import { subscribe } from '../workspace/workspaceBus.ts';
import { syncRasterLayers } from '../map/rasterLayers.ts';
import { attachHoverTooltip } from '../map/hoverTooltip.ts';
import { attachUrlHash, hasMapHash } from '../map/urlHash.ts';
import { flyTo, getWorkspaceMap, setLayerOpacity } from '../map/mapContext.ts';
import { MapEditController } from '../map/mapEdit.ts';
import { attachSelection } from '../map/selection.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import './MapPage.css';

/** Map workspace v1: fixed resizable panes; docking comes later. */
export default function MapPage() {
  const { t } = useTranslation();
  const mapTarget = useRef<HTMLDivElement>(null);
  const leftPanelRef = usePanelRef();
  const rightPanelRef = usePanelRef();
  const [leftCollapsed, setLeftCollapsed] = useState(false);
  const [rightCollapsed, setRightCollapsed] = useState(false);
  const { data: layers } = useMapLayers();
  const { data: featureTypes } = useFeatureTypes();
  const { data: me } = useMe();
  const [activeBaseId, setActiveBaseId] = useState<number>();
  const [entrancesVisible, setEntrancesVisible] = useState(true);
  const [tagFilter, setTagFilter] = useState<string | null>(getMapTagFilter());
  const [surfaceFeaturesVisible, setSurfaceFeaturesVisible] = useState(true);
  const [centerlinesVisible, setCenterlinesVisible] = useState(true);
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
  const overlayOpacity = useWorkspaceStore((s) => s.overlayOpacity);
  const setOverlayOpacity = useWorkspaceStore((s) => s.setOverlayOpacity);
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
      [CENTERLINE_LAYER_ID, createCenterlineLayer],
    ] as const) {
      if (!map.getLayers().getArray().some((l) => l.get('id') === id)) {
        map.addLayer(create());
      }
    }

    const detachLoader = attachEntranceLoader(map);
    const detachFeatureLoader = attachSurfaceFeatureLoader(map);
    const detachCenterlineLoader = attachCenterlineLoader(map);
    const detachGeofileLoader = attachGeofileLoader(map);
    const detachSelection = attachSelection(map, setSelection);
    const detachHover = attachHoverTooltip(map);
    const detachUrlHash = attachUrlHash(map);
    const controller = new MapEditController(map);
    setEditController(controller);
    return () => {
      controller.dispose();
      setEditController(null);
      detachLoader();
      detachFeatureLoader();
      detachCenterlineLoader();
      detachGeofileLoader();
      detachSelection();
      detachHover();
      detachUrlHash();
      map.setTarget(undefined);
    };
  }, [setSelection]);

  // Geofile overlays follow the workspace selection of visible geofiles, with per-file opacity.
  useEffect(() => {
    syncGeofileLayers(
      getWorkspaceMap(),
      importedGeofiles,
      new Set(visibleGeofileIds),
      new globalThis.Map(Object.entries(overlayOpacity)),
    );
  }, [importedGeofiles, visibleGeofileIds, overlayOpacity]);

  // Built-in vector overlays (entrances, surface features, centerlines) follow their opacity.
  useEffect(() => {
    for (const id of [ENTRANCE_LAYER_ID, SURFACE_FEATURE_LAYER_ID, CENTERLINE_LAYER_ID]) {
      setLayerOpacity(id, overlayOpacity[id] ?? 1);
    }
  }, [overlayOpacity]);

  // Raster overlays likewise, with per-map opacity.
  useEffect(() => {
    syncRasterLayers(
      getWorkspaceMap(),
      readyRasters,
      new Set(visibleRasterIds),
      new globalThis.Map(Object.entries(rasterOpacity)),
    );
  }, [readyRasters, visibleRasterIds, rasterOpacity]);

  // Pop-out windows publish picks over the workspace bus; the main map follows.
  useEffect(() => subscribe((event) => {
    if (event.kind === 'selection') {
      setSelection(event.selection);
    } else if (event.kind === 'fly-to') {
      flyTo(event.lon, event.lat, event.zoom ?? 15);
    }
  }), [setSelection]);

  // Apply the user's home view once per session when the workspace first opens.
  const { data: savedViews } = useMapViews();
  useEffect(() => {
    if (savedViews && !sessionStorage.getItem('silexgis.homeApplied')) {
      sessionStorage.setItem('silexgis.homeApplied', '1');
      // A shareable position in the URL wins over the home view.
      const home = savedViews.find((v) => v.isHome);
      if (home && !hasMapHash()) {
        applyView(home);
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- one-shot on first data
  }, [savedViews]);

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

  useEffect(() => {
    const layer = getWorkspaceMap()
      .getLayers()
      .getArray()
      .find((l) => l.get('id') === CENTERLINE_LAYER_ID);
    layer?.setVisible(centerlinesVisible);
  }, [centerlinesVisible]);

  const captureCurrentView = () =>
    captureViewConfig({
      baseLayerId: activeBaseId,
      entrancesVisible,
      surfaceFeaturesVisible,
      centerlinesVisible,
      geofileIds: visibleGeofileIds,
      rasters: visibleRasterIds.map((id) => ({ id, opacity: rasterOpacity[id] })),
      tagFilter,
      overlayOpacity,
    });

  const applyView = (view: { config: unknown }) => {
    const ui = applyViewConfig(view.config);
    if (!ui) {
      return;
    }
    if (ui.baseLayerId !== undefined) {
      setActiveBaseId(ui.baseLayerId);
      setActiveBaseLayer(getWorkspaceMap(), ui.baseLayerId);
    }
    setEntrancesVisible(ui.entrancesVisible);
    setSurfaceFeaturesVisible(ui.surfaceFeaturesVisible);
    setCenterlinesVisible(ui.centerlinesVisible);
    for (const id of visibleGeofileIds) {
      if (!ui.geofileIds.includes(id)) {
        setGeofileVisible(id, false);
      }
    }
    for (const id of ui.geofileIds) {
      setGeofileVisible(id, true);
    }
    for (const id of visibleRasterIds) {
      if (!ui.rasters.some((r) => r.id === id)) {
        setRasterVisible(id, false);
      }
    }
    for (const raster of ui.rasters) {
      setRasterVisible(raster.id, true);
      if (raster.opacity !== undefined) {
        setRasterOpacity(raster.id, raster.opacity);
      }
    }
    for (const [key, value] of Object.entries(ui.overlayOpacity)) {
      setOverlayOpacity(key, value);
    }
    setTagFilter(ui.tagFilter);
    setMapTagFilter(ui.tagFilter);
    reloadEntrances();
    reloadSurfaceFeatures();
  };

  return (
    <Group orientation="horizontal" className="map-workspace">
      {/* Panel sizes: bare numbers mean pixels in react-resizable-panels v4 — use percent strings. */}
      <Panel
        panelRef={leftPanelRef}
        collapsible
        collapsedSize="0%"
        defaultSize="16%"
        minSize="10%"
        className="map-workspace-panel"
        onResize={() => setLeftCollapsed(leftPanelRef.current?.isCollapsed() ?? false)}
      >
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
          centerlinesVisible={centerlinesVisible}
          onCenterlinesVisibleChange={setCenterlinesVisible}
          geofiles={importedGeofiles}
          visibleGeofileIds={visibleGeofileIds}
          onGeofileVisibleChange={setGeofileVisible}
          rasters={readyRasters}
          visibleRasterIds={visibleRasterIds}
          onRasterVisibleChange={setRasterVisible}
          rasterOpacity={rasterOpacity}
          onRasterOpacityChange={setRasterOpacity}
          overlayOpacity={overlayOpacity}
          onOverlayOpacityChange={setOverlayOpacity}
          tagFilter={tagFilter}
          onTagFilterChange={(slug) => {
            setTagFilter(slug);
            setMapTagFilter(slug);
            reloadEntrances();
            reloadSurfaceFeatures();
          }}
          footer={<ViewsPanel onCapture={captureCurrentView} onApply={applyView} />}
        />
      </Panel>
      <Separator className="map-workspace-handle" />
      <Panel minSize="30%">
        <div className="map-canvas-wrap">
          <div ref={mapTarget} className="map-canvas" data-testid="map-canvas" />
          <div className="map-search-overlay">
            <MapSearch />
          </div>
          <Tooltip title={leftCollapsed ? t('map.showPanel') : t('map.hidePanel')} placement="right">
            <Button
              className="map-dock-toggle map-dock-toggle-left"
              size="small"
              aria-label={leftCollapsed ? t('map.showPanel') : t('map.hidePanel')}
              icon={leftCollapsed ? <RightOutlined /> : <LeftOutlined />}
              onClick={() =>
                leftCollapsed ? leftPanelRef.current?.expand() : leftPanelRef.current?.collapse()
              }
            />
          </Tooltip>
          <Tooltip title={rightCollapsed ? t('map.showPanel') : t('map.hidePanel')} placement="left">
            <Button
              className="map-dock-toggle map-dock-toggle-right"
              size="small"
              aria-label={rightCollapsed ? t('map.showPanel') : t('map.hidePanel')}
              icon={rightCollapsed ? <LeftOutlined /> : <RightOutlined />}
              onClick={() =>
                rightCollapsed ? rightPanelRef.current?.expand() : rightPanelRef.current?.collapse()
              }
            />
          </Tooltip>
          <div className="map-popout-overlay">
            <BaseLayerSwitcher
              layers={layers ?? []}
              activeBaseId={activeBaseId}
              onChange={(id) => {
                setActiveBaseId(id);
                setActiveBaseLayer(getWorkspaceMap(), id);
              }}
            />
            <Tooltip title={t('panel.popOut')}>
              <Button
                size="small"
                icon={<ExportOutlined />}
                onClick={() => window.open('/panel/registry', 'silexgis-registry', 'popup,width=900,height=700')}
              />
            </Tooltip>
            <Tooltip title={t('panel.popOut3d')}>
              <Button
                size="small"
                icon={<CodeSandboxOutlined />}
                onClick={() => window.open('/panel/viewer3d', 'silexgis-viewer3d', 'popup,width=1000,height=750')}
              />
            </Tooltip>
          </div>
          {canEdit && editController && (
            <div className="map-edit-overlay">
              <EditToolbar controller={editController} />
            </div>
          )}
        </div>
      </Panel>
      <Separator className="map-workspace-handle" />
      <Panel
        panelRef={rightPanelRef}
        collapsible
        collapsedSize="0%"
        defaultSize="22%"
        minSize="12%"
        className="map-workspace-panel"
        onResize={() => setRightCollapsed(rightPanelRef.current?.isCollapsed() ?? false)}
      >
        <SelectionPanel />
      </Panel>
    </Group>
  );
}
