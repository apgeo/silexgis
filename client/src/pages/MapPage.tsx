// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import BackgroundLayerChooser from '@terrestris/react-geo/dist/BackgroundLayerChooser/BackgroundLayerChooser';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import { Button, Tabs, Tooltip } from 'antd';
import { CodeSandboxOutlined, ExportOutlined, LeftOutlined, RightOutlined } from '@ant-design/icons';
import type { EventsKey } from 'ol/events';
import type BaseLayer from 'ol/layer/Base';
import type TileLayer from 'ol/layer/Tile';
import { unByKey } from 'ol/Observable';
import { useTranslation } from 'react-i18next';
import { Group, Panel, Separator, usePanelRef } from 'react-resizable-panels';
import { useFeatureTypes, useGeofiles, useMapLayers, useMapViews, useMe, useRasterMaps } from '../api/hooks.ts';
import EditToolbar from '../components/map/EditToolbar.tsx';
import FeatureListPanel from '../components/map/FeatureListPanel.tsx';
import LayerPanel from '../components/map/LayerPanel.tsx';
import ViewsPanel from '../components/map/ViewsPanel.tsx';
import MapSearch from '../components/map/MapSearch.tsx';
import SelectionPanel from '../components/map/SelectionPanel.tsx';
import { getBaseLayerId, getBaseLayers, setActiveBaseLayer, syncBaseLayers } from '../map/baseLayers.ts';
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
import { GEOFILE_LAYER_PREFIX, attachGeofileLoader, syncGeofileLayers } from '../map/geofileLayers.ts';
import { getMapTagFilter, setMapTagFilter } from '../map/mapFilters.ts';
import { applyViewConfig, captureViewConfig } from '../map/viewConfig.ts';
import { subscribe } from '../workspace/workspaceBus.ts';
import { RASTER_LAYER_PREFIX, syncRasterLayers } from '../map/rasterLayers.ts';
import { attachHoverTooltip } from '../map/hoverTooltip.ts';
import { attachUrlHash, hasMapHash } from '../map/urlHash.ts';
import {
  applyPendingOverlayOrder,
  findOverlayLayer,
  flyTo,
  getOverlayGroup,
  getWorkspaceMap,
  setDesiredOverlayOrder,
  setLayerOpacity,
} from '../map/mapContext.ts';
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

    // Built-in overlays live in the shared overlay group; bottom→top order here
    // is the default stacking (users can re-drag it in the composer tree).
    for (const [id, create] of [
      [SURFACE_FEATURE_LAYER_ID, createSurfaceFeatureLayer],
      [ENTRANCE_LAYER_ID, createEntranceLayer],
      [CENTERLINE_LAYER_ID, createCenterlineLayer],
    ] as const) {
      if (!findOverlayLayer(id)) {
        getOverlayGroup().getLayers().push(create());
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
    // A just-applied saved view may prescribe a stacking that includes these layers.
    applyPendingOverlayOrder();
  }, [importedGeofiles, visibleGeofileIds, overlayOpacity]);

  // Built-in vector overlays (entrances, surface features, centerlines) follow their opacity.
  useEffect(() => {
    for (const id of [ENTRANCE_LAYER_ID, SURFACE_FEATURE_LAYER_ID, CENTERLINE_LAYER_ID]) {
      setLayerOpacity(id, overlayOpacity[id] ?? 1);
    }
  }, [overlayOpacity]);

  // Raster overlays likewise, with per-map opacity.
  useEffect(() => {
    syncRasterLayers(readyRasters, new Set(visibleRasterIds), new globalThis.Map(Object.entries(rasterOpacity)));
    applyPendingOverlayOrder();
  }, [readyRasters, visibleRasterIds, rasterOpacity]);

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
        const key = id.startsWith(GEOFILE_LAYER_PREFIX) ? id.slice(GEOFILE_LAYER_PREFIX.length) : id;
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

  // Base layers are created lazily from the catalog; the version bump tells the
  // on-canvas chooser (which needs the OL layer instances) that they exist now.
  const [baseLayersVersion, setBaseLayersVersion] = useState(0);
  useEffect(() => {
    if (layers && activeBaseId !== undefined) {
      syncBaseLayers(getWorkspaceMap(), layers, activeBaseId);
      setBaseLayersVersion((v) => v + 1);
    }
  }, [layers, activeBaseId]);
  const baseOlLayers = useMemo(
    () => (baseLayersVersion > 0 ? getBaseLayers(getWorkspaceMap()) : []),
    [baseLayersVersion],
  );

  // The on-canvas chooser switches base layers by flipping OL visibility itself;
  // follow it so the radio, saved views and the URL state stay truthful.
  useEffect(() => {
    const keys = baseOlLayers.map((layer) =>
      layer.on('change:visible', () => {
        if (layer.getVisible()) {
          const id = getBaseLayerId(layer);
          if (id !== undefined) {
            setActiveBaseId(id);
          }
        }
      }),
    );
    return () => unByKey(keys);
  }, [baseOlLayers]);

  useEffect(() => {
    findOverlayLayer(ENTRANCE_LAYER_ID)?.setVisible(entrancesVisible);
  }, [entrancesVisible]);

  useEffect(() => {
    findOverlayLayer(SURFACE_FEATURE_LAYER_ID)?.setVisible(surfaceFeaturesVisible);
  }, [surfaceFeaturesVisible]);

  useEffect(() => {
    findOverlayLayer(CENTERLINE_LAYER_ID)?.setVisible(centerlinesVisible);
  }, [centerlinesVisible]);

  // Checkbox toggles coming from the composer tree. Built-ins hide/show and are
  // reflected into page state (for saved views); geofile/raster overlays are
  // deactivated entirely — their layer is removed and the catalog checkbox clears.
  const onOverlayVisibilityChanged = (layer: BaseLayer, visible: boolean) => {
    const id = layer.get('id') as string | undefined;
    if (id === ENTRANCE_LAYER_ID) {
      setEntrancesVisible(visible);
    } else if (id === SURFACE_FEATURE_LAYER_ID) {
      setSurfaceFeaturesVisible(visible);
    } else if (id === CENTERLINE_LAYER_ID) {
      setCenterlinesVisible(visible);
    } else if (id?.startsWith(GEOFILE_LAYER_PREFIX)) {
      setGeofileVisible(id.slice(GEOFILE_LAYER_PREFIX.length), visible);
    } else if (id?.startsWith(RASTER_LAYER_PREFIX)) {
      setRasterVisible(id.slice(RASTER_LAYER_PREFIX.length), visible);
    }
  };

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

  // Applying a view remounts the composer's transparency sliders (they are
  // uncontrolled and read layer opacity on mount) via this nonce.
  const [treeNonce, setTreeNonce] = useState(0);

  // Right dock: selection details / live "in view" index. Picking something
  // (map click or list row) brings the selection tab forward.
  const [rightTab, setRightTab] = useState('selection');
  useEffect(() => {
    if (selection) {
      setRightTab('selection');
    }
  }, [selection]);

  const applyView = (view: { config: unknown }) => {
    const ui = applyViewConfig(view.config);
    if (!ui) {
      return;
    }
    setTreeNonce((n) => n + 1);
    if (ui.overlayOrder.length > 0) {
      // Geofile/raster layers may not exist yet; the sync effects re-apply this.
      setDesiredOverlayOrder(ui.overlayOrder);
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
    <MapContext.Provider value={getWorkspaceMap()}>
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
          geofiles={importedGeofiles}
          visibleGeofileIds={visibleGeofileIds}
          onGeofileVisibleChange={setGeofileVisible}
          rasters={readyRasters}
          visibleRasterIds={visibleRasterIds}
          onRasterVisibleChange={setRasterVisible}
          onOverlayVisibilityChanged={onOverlayVisibilityChanged}
          treeNonce={treeNonce}
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
          {baseOlLayers.length > 0 && (
            <BackgroundLayerChooser
              layers={baseOlLayers}
              backgroundLayerFilter={(l) => getBaseLayerId(l as TileLayer) !== undefined}
              buttonTooltip={t('map.changeBaseLayer')}
            />
          )}
          <div className="map-popout-overlay">
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
        <Tabs
          className="map-right-tabs"
          activeKey={rightTab}
          onChange={setRightTab}
          items={[
            { key: 'selection', label: t('map.tabSelection'), children: <SelectionPanel /> },
            { key: 'inview', label: t('map.tabInView'), children: <FeatureListPanel /> },
          ]}
        />
      </Panel>
    </Group>
    </MapContext.Provider>
  );
}
