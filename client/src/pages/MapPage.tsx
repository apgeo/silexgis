// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import BackgroundLayerChooser from '@terrestris/react-geo/dist/BackgroundLayerChooser/BackgroundLayerChooser';
import GeoLocationButton from '@terrestris/react-geo/dist/Button/GeoLocationButton/GeoLocationButton';
import ScaleCombo from '@terrestris/react-geo/dist/Field/ScaleCombo/ScaleCombo';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import { App, Button, Drawer, Tabs, Tooltip } from 'antd';
import {
  AimOutlined,
  BorderVerticleOutlined,
  CodeSandboxOutlined,
  ExportOutlined,
  EyeInvisibleOutlined,
  EyeOutlined,
  LeftOutlined,
  RightOutlined,
} from '@ant-design/icons';
import type { EventsKey } from 'ol/events';
import type BaseLayer from 'ol/layer/Base';
import type TileLayer from 'ol/layer/Tile';
import { unByKey } from 'ol/Observable';
import { useTranslation } from 'react-i18next';
import { Group, Panel, Separator, usePanelRef } from 'react-resizable-panels';
import { useSearchParams } from 'react-router-dom';
import { useCan, useFeatureTypes, useGeofiles, useMapConfig, useMapLayers, useMapViews, useRasterMaps } from '../api/hooks.ts';
import { useIsMobile } from '../hooks/useIsMobile.ts';
import EditToolbar from '../components/map/EditToolbar.tsx';
import FeatureListPanel from '../components/map/FeatureListPanel.tsx';
import { drawShapeForType } from '../components/map/featureTypeGroups.ts';
import LayerPanel from '../components/map/LayerPanel.tsx';
import MapContextMenu from '../components/map/MapContextMenu.tsx';
import ViewsPanel from '../components/map/ViewsPanel.tsx';
import MapSearch from '../components/map/MapSearch.tsx';
import SelectionPanel from '../components/map/SelectionPanel.tsx';
import { getBaseLayerId, getBaseLayers, setActiveBaseLayer, syncBaseLayers } from '../map/baseLayers.ts';
import {
  CENTERLINE_LAYER_ID,
  attachCenterlineLoader,
  createCenterlineLayer,
  setCenterlineLimits,
  setCenterlineOverrides,
  setCenterlinesEnabled,
} from '../map/centerlineLayer.ts';
import { ENTRANCE_LAYER_ID, attachEntranceLoader, createEntranceLayer, reloadEntrances } from '../map/entranceLayer.ts';
import {
  SURFACE_FEATURE_LAYER_ID,
  attachSurfaceFeatureLoader,
  createSurfaceFeatureLayer,
  reloadSurfaceFeatures,
  setFeatureTypeSymbols,
  setSelectedSurfaceFeature,
} from '../map/featureLayer.ts';
import { ENTRANCE_HEATMAP_LAYER_ID, createEntranceHeatmapLayer } from '../map/heatmapLayer.ts';
import { GEOFILE_LAYER_PREFIX, attachGeofileLoader, syncGeofileLayers } from '../map/geofileLayers.ts';
import { PHOTO_LAYER_ID, attachPhotoLoader, createPhotoLayer, setPhotosEnabled } from '../map/photoLayer.ts';
import { attachPhotoPopup } from '../map/photoPopup.ts';
import { getMapTagFilter, setMapTagFilter } from '../map/mapFilters.ts';
import { applyViewConfig, captureViewConfig } from '../map/viewConfig.ts';
import { subscribe } from '../workspace/workspaceBus.ts';
import { RASTER_LAYER_PREFIX, syncRasterLayers } from '../map/rasterLayers.ts';
import { setRasterSwipeActive, setRasterSwipeFraction } from '../map/rasterSwipe.ts';
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
import { attachContextMenu, type MapContextMenuTarget } from '../map/contextMenu.ts';
import { MapEditController, type DrawShape } from '../map/mapEdit.ts';
import { attachSelection } from '../map/selection.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import './MapPage.css';

/** Map workspace v1: fixed resizable panes on desktop, drawers on phones. */
export default function MapPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const mapTarget = useRef<HTMLDivElement>(null);
  const leftPanelRef = usePanelRef();
  const rightPanelRef = usePanelRef();
  const [leftCollapsed, setLeftCollapsed] = useState(false);
  const [rightCollapsed, setRightCollapsed] = useState(false);
  // Below md the docks are drawers over the map instead of resizable panes; the same
  // dock-toggle buttons drive whichever one is mounted.
  const [leftDrawerOpen, setLeftDrawerOpen] = useState(false);
  const [rightDrawerOpen, setRightDrawerOpen] = useState(false);
  const { data: layers } = useMapLayers();
  const { data: mapConfig } = useMapConfig();
  const { data: featureTypes } = useFeatureTypes();
  const [activeBaseId, setActiveBaseId] = useState<number>();
  const [entrancesVisible, setEntrancesVisible] = useState(true);
  const [tagFilter, setTagFilter] = useState<string | null>(getMapTagFilter());
  const [surfaceFeaturesVisible, setSurfaceFeaturesVisible] = useState(true);
  // Off by default: it is the heaviest overlay by a wide margin, and a cave's splay work is
  // invisible at the zooms most sessions spend their time at.
  const [centerlinesVisible, setCenterlinesVisible] = useState(false);
  const [heatmapVisible, setHeatmapVisible] = useState(false);
  const [photosVisible, setPhotosVisible] = useState(false);
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
  const baseOpacity = useWorkspaceStore((s) => s.baseOpacity);
  const setBaseOpacity = useWorkspaceStore((s) => s.setBaseOpacity);
  const resetBaseOpacity = useWorkspaceStore((s) => s.resetBaseOpacity);
  const { data: rasterPage } = useRasterMaps({ pageSize: 100 });
  const readyRasters = useMemo(
    () => (rasterPage?.items ?? []).filter((r) => r.status === 'ready'),
    [rasterPage],
  );
  // The toolbar mixes drawing new features with modifying existing ones, so either
  // domain-level right shows it; per-feature answers stay with the server.
  const mayWriteFeatures = useCan('features', 'write');
  const mayCreateFeatures = useCan('features', 'create');
  const canEdit = mayWriteFeatures || mayCreateFeatures;
  const mapChromeHidden = useUiPrefsStore((s) => s.mapChromeHidden);
  const setMapChromeHidden = useUiPrefsStore((s) => s.setMapChromeHidden);
  const centerlineDetailZoom = useUiPrefsStore((s) => s.centerlineDetailZoom);
  const centerlineMaxPaths = useUiPrefsStore((s) => s.centerlineMaxPaths);
  const setCenterlinePrefs = useUiPrefsStore((s) => s.setCenterlineLimits);

  // Unsaved-edit count mirrored out of the edit controller so the dirty guard pill
  // can warn even while the edit toolbar is hidden with the rest of the chrome.
  const [editDirty, setEditDirty] = useState(0);
  useEffect(() => editController?.subscribe((s) => setEditDirty(s.dirty)), [editController]);

  // Right-click (long-press on touch) context menu over the canvas.
  const [contextTarget, setContextTarget] = useState<MapContextMenuTarget | null>(null);

  useEffect(() => {
    const map = getWorkspaceMap();
    map.setTarget(mapTarget.current ?? undefined);

    // Built-in overlays live in the shared overlay group; bottom→top order here
    // is the default stacking (users can re-drag it in the composer tree). The
    // entrance heatmap sits at the bottom as a density wash beneath the points.
    for (const [id, create] of [
      [ENTRANCE_HEATMAP_LAYER_ID, createEntranceHeatmapLayer],
      [SURFACE_FEATURE_LAYER_ID, createSurfaceFeatureLayer],
      [ENTRANCE_LAYER_ID, createEntranceLayer],
      [CENTERLINE_LAYER_ID, createCenterlineLayer],
      [PHOTO_LAYER_ID, createPhotoLayer],
    ] as const) {
      if (!findOverlayLayer(id)) {
        getOverlayGroup().getLayers().push(create());
      }
    }

    const detachLoader = attachEntranceLoader(map);
    const detachFeatureLoader = attachSurfaceFeatureLoader(map);
    const detachCenterlineLoader = attachCenterlineLoader(map);
    const detachGeofileLoader = attachGeofileLoader(map);
    const detachPhotoLoader = attachPhotoLoader(map);
    const detachPhotoPopup = attachPhotoPopup(map);
    const detachSelection = attachSelection(map, setSelection);
    const detachHover = attachHoverTooltip(map);
    const detachUrlHash = attachUrlHash(map);
    const detachContextMenu = attachContextMenu(map, setContextTarget);
    // Panning/zooming away invalidates the menu's anchor point.
    const moveKey = map.on('movestart', () => setContextTarget(null));
    const controller = new MapEditController(map);
    setEditController(controller);
    return () => {
      controller.dispose();
      setEditController(null);
      detachLoader();
      detachFeatureLoader();
      detachCenterlineLoader();
      detachGeofileLoader();
      detachPhotoLoader();
      detachPhotoPopup();
      detachSelection();
      detachHover();
      detachUrlHash();
      detachContextMenu();
      unByKey(moveKey);
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

  // Built-in vector overlays (entrances, surface features, centerlines, heatmap) follow their opacity.
  useEffect(() => {
    for (const id of [ENTRANCE_LAYER_ID, SURFACE_FEATURE_LAYER_ID, CENTERLINE_LAYER_ID, ENTRANCE_HEATMAP_LAYER_ID]) {
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

  const { data: savedViews } = useMapViews();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedViewId = searchParams.get('view');

  // A view picked elsewhere (?view=<id>, e.g. from the dashboard) is applied on arrival, then
  // the param is consumed. It is a one-shot instruction, not a description of the URL: the
  // camera position is synced into the hash as the user pans, so leaving ?view= behind would
  // let a reload of a panned-and-shared link re-apply the view over the position it was
  // shared for. Consuming it also makes the effect self-guarding — a views refetch hands back
  // a new array reference, which would otherwise re-apply the view and stomp the user's panning.
  useEffect(() => {
    if (!savedViews || !requestedViewId) {
      return;
    }
    const requested = savedViews.find((v) => v.id === requestedViewId);
    if (requested) {
      applyView(requested);
    }
    setSearchParams(
      (params) => {
        params.delete('view');
        return params;
      },
      { replace: true },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps -- applyView is stable for this use
  }, [savedViews, requestedViewId, setSearchParams]);

  // The home view opens the workspace once per session. The flag is claimed on the first load
  // of the views whichever path runs, so that a requested view or a shared position can never
  // be overwritten by the home view later in the session.
  useEffect(() => {
    if (!savedViews || sessionStorage.getItem('silexgis.homeApplied')) {
      return;
    }
    sessionStorage.setItem('silexgis.homeApplied', '1');
    // An explicit view request and a shareable position in the URL both outrank the home view.
    if (requestedViewId || hasMapHash()) {
      return;
    }
    const home = savedViews.find((v) => v.isHome);
    if (home) {
      applyView(home);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- one-shot on first data
  }, [savedViews, requestedViewId]);

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

  // Base tile layers carry per-catalog-id opacity. Only the active base is visible,
  // but each keeps its own value so switching restores it; re-applied when the base
  // layers are (re)created (version bump) or a saved view changes the opacities.
  useEffect(() => {
    for (const layer of baseOlLayers) {
      const id = getBaseLayerId(layer);
      if (id !== undefined) {
        layer.setOpacity(baseOpacity[id] ?? 1);
      }
    }
  }, [baseOlLayers, baseOpacity]);

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
    setCenterlinesEnabled(centerlinesVisible); // gate the bbox loader so hidden = no fetches
  }, [centerlinesVisible]);

  // Rendering limits are the installation's, with this viewer's overrides on top.
  useEffect(() => {
    if (mapConfig) {
      setCenterlineLimits(mapConfig);
    }
  }, [mapConfig]);

  useEffect(() => {
    setCenterlineOverrides({ detailZoom: centerlineDetailZoom, maxPaths: centerlineMaxPaths });
  }, [centerlineDetailZoom, centerlineMaxPaths]);

  useEffect(() => {
    findOverlayLayer(ENTRANCE_HEATMAP_LAYER_ID)?.setVisible(heatmapVisible);
  }, [heatmapVisible]);

  useEffect(() => {
    findOverlayLayer(PHOTO_LAYER_ID)?.setVisible(photosVisible);
    setPhotosEnabled(photosVisible); // gate the bbox loader so hidden = no fetches
  }, [photosVisible]);

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
    } else if (id === ENTRANCE_HEATMAP_LAYER_ID) {
      setHeatmapVisible(visible);
    } else if (id === PHOTO_LAYER_ID) {
      setPhotosVisible(visible);
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
      heatmapVisible,
      photosVisible,
      geofileIds: visibleGeofileIds,
      rasters: visibleRasterIds.map((id) => ({ id, opacity: rasterOpacity[id] })),
      tagFilter,
      overlayOpacity,
      baseOpacity,
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
      // Bringing the tab forward means nothing on a phone, where the dock is a closed
      // drawer: open it, or a pick appears to do nothing. Keyed on the selection alone —
      // a viewport crossing the breakpoint must not re-open the drawer over the map.
      if (isMobile) {
        setRightDrawerOpen(true);
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- isMobile is read here, not a trigger
  }, [selection]);

  // Raster swipe-compare: ephemeral, auto-disarms when the last raster goes away.
  const [swipeActive, setSwipeActive] = useState(false);
  const [swipePos, setSwipePos] = useState(0.5);
  useEffect(() => {
    if (visibleRasterIds.length === 0) {
      setSwipeActive(false);
    }
  }, [visibleRasterIds]);
  useEffect(() => {
    setRasterSwipeActive(swipeActive);
    return () => setRasterSwipeActive(false);
  }, [swipeActive]);

  const onSwipeHandleDown = (event: React.PointerEvent<HTMLDivElement>) => {
    const wrap = event.currentTarget.parentElement!.getBoundingClientRect();
    const handle = event.currentTarget;
    handle.setPointerCapture(event.pointerId);
    const move = (ev: PointerEvent) => {
      const f = Math.min(1, Math.max(0, (ev.clientX - wrap.left) / wrap.width));
      setSwipePos(f);
      setRasterSwipeFraction(f);
    };
    const up = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', up);
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', up);
  };

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
    setHeatmapVisible(ui.heatmapVisible);
    setPhotosVisible(ui.photosVisible);
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
    // Restore base opacity as a full replacement, not a merge: a base the view does not
    // mention reverts to opaque (its `?? 1` default), so applying a view can't leave an
    // earlier manual dim in place. Keys are catalog ids (JSON-serialized as strings), which
    // the numeric-id consumers coerce back on lookup.
    resetBaseOpacity(ui.baseOpacity);
    setTagFilter(ui.tagFilter);
    setMapTagFilter(ui.tagFilter);
    reloadEntrances();
    reloadSurfaceFeatures();
  };

  // Dock contents, hosted either by a resizable pane (desktop) or a drawer (phone).
  const layerDock = (
    <LayerPanel
      layers={layers ?? []}
      activeBaseId={activeBaseId}
      onBaseChange={(id) => {
        setActiveBaseId(id);
        setActiveBaseLayer(getWorkspaceMap(), id);
      }}
      baseOpacity={baseOpacity}
      onBaseOpacityChange={setBaseOpacity}
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
      centerlinesVisible={centerlinesVisible}
      mapConfig={mapConfig}
      centerlineDetailZoom={centerlineDetailZoom}
      centerlineMaxPaths={centerlineMaxPaths}
      onCenterlineLimitsChange={setCenterlinePrefs}
      footer={<ViewsPanel onCapture={captureCurrentView} onApply={applyView} />}
    />
  );

  const rightDock = (
    <Tabs
      className="map-right-tabs"
      activeKey={rightTab}
      onChange={setRightTab}
      items={[
        { key: 'selection', label: t('map.tabSelection'), children: <SelectionPanel /> },
        { key: 'inview', label: t('map.tabInView'), children: <FeatureListPanel /> },
      ]}
    />
  );

  // One notion of "is this dock showing" across both layouts, so the toggle buttons keep
  // their icon and label truthful whichever host is mounted.
  const leftHidden = isMobile ? !leftDrawerOpen : leftCollapsed;
  const rightHidden = isMobile ? !rightDrawerOpen : rightCollapsed;

  const toggleLeftDock = () => {
    if (isMobile) {
      setLeftDrawerOpen((open) => !open);
    } else if (leftCollapsed) {
      leftPanelRef.current?.expand();
    } else {
      leftPanelRef.current?.collapse();
    }
  };

  const toggleRightDock = () => {
    if (isMobile) {
      setRightDrawerOpen((open) => !open);
    } else if (rightCollapsed) {
      rightPanelRef.current?.expand();
    } else {
      rightPanelRef.current?.collapse();
    }
  };

  return (
    <MapContext.Provider value={getWorkspaceMap()}>
    {/* The side panes render conditionally, but the map's slot in this list never moves:
        React keeps its DOM (and with it the OL target the map was attached to) across a
        viewport crossing the breakpoint. */}
    <Group orientation="horizontal" className="map-workspace">
      {/* Panel sizes: bare numbers mean pixels in react-resizable-panels v4 — use percent strings. */}
      {!isMobile && (
        <Panel
          panelRef={leftPanelRef}
          collapsible
          collapsedSize="0%"
          defaultSize="16%"
          minSize="10%"
          className="map-workspace-panel"
          onResize={() => setLeftCollapsed(leftPanelRef.current?.isCollapsed() ?? false)}
        >
          {layerDock}
        </Panel>
      )}
      {!isMobile && <Separator className="map-workspace-handle" />}
      <Panel minSize="30%">
        <div className={`map-canvas-wrap${mapChromeHidden ? ' map-chrome-hidden' : ''}`}>
          <div ref={mapTarget} className="map-canvas" data-testid="map-canvas" />
          {/* Search is the primary action on a phone: it gets the width the pop-out
              buttons no longer need. */}
          <div className={`map-search-overlay map-chrome${isMobile ? ' map-search-overlay-mobile' : ''}`}>
            <MapSearch fullWidth={isMobile} />
          </div>
          <Tooltip title={mapChromeHidden ? t('map.showChrome') : t('map.hideChrome')} placement="left">
            <Button
              className="map-chrome-toggle"
              size="small"
              aria-label={mapChromeHidden ? t('map.showChrome') : t('map.hideChrome')}
              icon={mapChromeHidden ? <EyeOutlined /> : <EyeInvisibleOutlined />}
              onClick={() => setMapChromeHidden(!mapChromeHidden)}
              data-testid="map-chrome-toggle"
            />
          </Tooltip>
          {mapChromeHidden && editDirty > 0 && (
            <Button
              className="map-dirty-pill"
              size="small"
              type="primary"
              onClick={() => setMapChromeHidden(false)}
              data-testid="map-dirty-pill"
            >
              {t('map.unsavedEdits', { count: editDirty })}
            </Button>
          )}
          <Tooltip title={leftHidden ? t('map.showPanel') : t('map.hidePanel')} placement="right">
            <Button
              className="map-dock-toggle map-dock-toggle-left"
              size="small"
              aria-label={leftHidden ? t('map.showPanel') : t('map.hidePanel')}
              icon={leftHidden ? <RightOutlined /> : <LeftOutlined />}
              onClick={toggleLeftDock}
              data-testid="map-dock-toggle-left"
            />
          </Tooltip>
          <Tooltip title={rightHidden ? t('map.showPanel') : t('map.hidePanel')} placement="left">
            <Button
              className="map-dock-toggle map-dock-toggle-right"
              size="small"
              aria-label={rightHidden ? t('map.showPanel') : t('map.hidePanel')}
              icon={rightHidden ? <LeftOutlined /> : <RightOutlined />}
              onClick={toggleRightDock}
              data-testid="map-dock-toggle-right"
            />
          </Tooltip>
          {baseOlLayers.length > 0 && (
            <BackgroundLayerChooser
              layers={baseOlLayers}
              backgroundLayerFilter={(l) => getBaseLayerId(l as TileLayer) !== undefined}
              buttonTooltip={t('map.changeBaseLayer')}
            />
          )}
          {/* Jump-to-scale is niche clutter at phone size; OL's own scale line stays. */}
          {!isMobile && (
            <div className="map-scale-overlay map-chrome">
              <ScaleCombo syncWithMap size="small" style={{ width: 128 }} />
            </div>
          )}
          {swipeActive && (
            <div
              className="map-swipe-handle"
              style={{ left: `calc(${(swipePos * 100).toFixed(2)}% - 2px)` }}
              onPointerDown={onSwipeHandleDown}
              data-testid="raster-swipe-handle"
            />
          )}
          <div className="map-popout-overlay map-chrome">
            {visibleRasterIds.length > 0 && (
              <Tooltip title={t('map.swipeCompare')}>
                <Button
                  size="small"
                  type={swipeActive ? 'primary' : 'default'}
                  icon={<BorderVerticleOutlined />}
                  onClick={() => setSwipeActive((v) => !v)}
                  data-testid="raster-swipe-toggle"
                />
              </Tooltip>
            )}
            <GeoLocationButton
              size="small"
              icon={<AimOutlined />}
              pressedIcon={<AimOutlined />}
              tooltip={t('map.locateMe')}
              showMarker
              follow
              enableTracking
              onError={() => message.error(t('map.geolocationFailed'))}
            />
            {/* Pop-out windows are meaningless on a phone; the 3D viewer stays reachable
                from the cave detail page. */}
            {!isMobile && (
              <>
                <Tooltip title={t('panel.popOut')}>
                  <Button
                    size="small"
                    icon={<ExportOutlined />}
                    onClick={() => window.open('/panel/registry', 'silexgis-registry', 'popup,width=900,height=700')}
                    data-testid="map-popout-registry"
                  />
                </Tooltip>
                <Tooltip title={t('panel.popOut3d')}>
                  <Button
                    size="small"
                    icon={<CodeSandboxOutlined />}
                    onClick={() => window.open('/panel/viewer3d', 'silexgis-viewer3d', 'popup,width=1000,height=750')}
                    data-testid="map-popout-viewer3d"
                  />
                </Tooltip>
              </>
            )}
          </div>
          {canEdit && editController && (
            <div className={`map-edit-overlay map-chrome${isMobile ? ' map-edit-overlay-mobile' : ''}`}>
              <EditToolbar controller={editController} />
            </div>
          )}
          <MapContextMenu
            target={contextTarget}
            featureTypes={featureTypes ?? []}
            canEdit={canEdit}
            onClose={() => setContextTarget(null)}
            onAddFeature={(typeId, lonLat) => {
              const picked = featureTypes?.find((ft) => Number(ft.id) === typeId);
              const shape: DrawShape = drawShapeForType(picked) ?? 'Point';
              if (shape === 'LineString' || shape === 'Polygon') {
                // Multi-click shapes start at the user's next clicks; just arm the tool.
                editController?.setMode('draw', shape, typeId);
              } else {
                // Point types land exactly where the menu was opened.
                editController?.placePointAt(lonLat, typeId);
              }
            }}
            onPlace={(mode, lonLat) => editController?.requestPlacement(mode, lonLat)}
          />
        </div>
      </Panel>
      {!isMobile && <Separator className="map-workspace-handle" />}
      {!isMobile && (
        <Panel
          panelRef={rightPanelRef}
          collapsible
          collapsedSize="0%"
          defaultSize="22%"
          minSize="12%"
          className="map-workspace-panel"
          onResize={() => setRightCollapsed(rightPanelRef.current?.isCollapsed() ?? false)}
        >
          {rightDock}
        </Panel>
      )}
    </Group>
    {isMobile && (
      <>
        <Drawer
          placement="left"
          open={leftDrawerOpen}
          onClose={() => setLeftDrawerOpen(false)}
          size="min(320px, 85vw)"
          title={t('map.layersTitle')}
          styles={{ body: { padding: 0 } }}
          rootClassName="map-dock-drawer"
          data-testid="map-left-drawer"
        >
          {layerDock}
        </Drawer>
        <Drawer
          placement="right"
          open={rightDrawerOpen}
          onClose={() => setRightDrawerOpen(false)}
          size="min(320px, 85vw)"
          title={t('map.detailsTitle')}
          styles={{ body: { padding: 0 } }}
          rootClassName="map-dock-drawer"
          data-testid="map-right-drawer"
        >
          {rightDock}
        </Drawer>
      </>
    )}
    </MapContext.Provider>
  );
}
