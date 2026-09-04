// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
import BackgroundLayerChooser from '@terrestris/react-geo/dist/BackgroundLayerChooser/BackgroundLayerChooser';
import GeoLocationButton from '@terrestris/react-geo/dist/Button/GeoLocationButton/GeoLocationButton';
import ScaleCombo from '@terrestris/react-geo/dist/Field/ScaleCombo/ScaleCombo';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import { App, Button, Drawer, Flex, Spin, Tabs, Tooltip, Typography } from 'antd';
import {
  AimOutlined,
  BorderVerticleOutlined,
  CloseOutlined,
  CodeSandboxOutlined,
  ExpandOutlined,
  ProfileOutlined,
  ExportOutlined,
  EyeInvisibleOutlined,
  EyeOutlined,
  GlobalOutlined,
  LeftOutlined,
  RightOutlined,
  SelectOutlined,
} from '@ant-design/icons';
import type { EventsKey } from 'ol/events';
import type BaseLayer from 'ol/layer/Base';
import type TileLayer from 'ol/layer/Tile';
import { unByKey } from 'ol/Observable';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Group, Panel, Separator, usePanelRef } from 'react-resizable-panels';
import { useSearchParams } from 'react-router-dom';
import {
  useCan,
  useFeatureTypes,
  useGeofiles,
  useMapConfig,
  useMapLayers,
  useMapViews,
  usePhotoLibraries,
  useRasterMaps,
  useSurveyModel,
  useWorkAreas,
  type SurveyModelInfo,
} from '../api/hooks.ts';
import { transformExtent } from 'ol/proj';
import { extentOf } from '../workareas/tree.ts';
import { useIsMobile } from '../hooks/useIsMobile.ts';
import EditToolbar from '../components/map/EditToolbar.tsx';
import FeatureListPanel from '../components/map/FeatureListPanel.tsx';
import { drawShapeForType } from '../components/map/featureTypeGroups.ts';
import LayerPanel from '../components/map/LayerPanel.tsx';
import MapContextMenu from '../components/map/MapContextMenu.tsx';
import ViewsPanel from '../components/map/ViewsPanel.tsx';
import MapObjectSelector from '../components/map/MapObjectSelector.tsx';
import MapSearch from '../components/map/MapSearch.tsx';
import SelectionPanel from '../components/map/SelectionPanel.tsx';
import {
  getBaseLayerId,
  getBaseLayers,
  setActiveBaseLayer,
  syncBaseLayers,
  syncTileOverlays,
} from '../map/baseLayers.ts';
import { setMapDeclutter } from '../map/declutter.ts';
import { attachGeofilePopup } from '../map/geofilePopup.ts';
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
  setFeatureTypeSymbols,
  setSelectedSurfaceFeature,
} from '../map/featureLayer.ts';
import {
  CLOSEST_APPROACH_LAYER_ID,
  attachClosestApproachLine,
  createClosestApproachLayer,
} from '../map/closestApproachLayer.ts';
import { ENTRANCE_HEATMAP_LAYER_ID, createEntranceHeatmapLayer } from '../map/heatmapLayer.ts';
import { GEOFILE_LAYER_PREFIX, attachGeofileLoader, syncGeofileLayers } from '../map/geofileLayers.ts';
import { PHOTO_LAYER_ID, attachPhotoLoader, createPhotoLayer, setPhotosEnabled } from '../map/photoLayer.ts';
import { readTripListFilter } from './trips/tripListFilter.ts';
import {
  TRIP_LAYER_ID,
  attachTripLoader,
  createTripLayer,
  setTripLayerFilter,
  setTripsEnabled,
  type TripLayerFilter,
} from '../map/tripLayer.ts';
import { attachPhotoPopup } from '../map/photoPopup.ts';
import {
  attachLibraryPhotoLoader,
  createLibraryPhotoLayer,
  libraryPhotoLayerId,
  libraryPhotoSourceOf,
  setLibraryPhotosEnabled,
} from '../map/libraryPhotoLayer.ts';
import {
  attachLibraryPhotoPopup,
  setLibraryPhotoFeatureHandler,
  type LibraryPhotoFeatureTarget,
} from '../map/libraryPhotoPopup.ts';
import { getMapTagFilter, setMapTagFilter } from '../map/mapFilters.ts';
import { applyViewConfig, captureViewConfig } from '../map/viewConfig.ts';
import { attachViewSync2d, type ViewSync2dHandle } from '../map/viewSync2d.ts';
import { openModelWindow } from '../caveview/openModelWindow.ts';
import { viewerFileName } from '../caveview/viewerFileName.ts';
import { hasScene3dHash } from '../scene3d/urlHash3d.ts';
import { surfaceFeaturesChanged } from '../workspace/surfaceFeatureRefresh.ts';
import { applyViewCamera3d, setActiveViewCamera } from '../workspace/viewCamera.ts';
import { subscribe } from '../workspace/workspaceBus.ts';
import { RASTER_LAYER_PREFIX, syncRasterLayers } from '../map/rasterLayers.ts';
import { setRasterSwipeActive, setRasterSwipeFraction } from '../map/rasterSwipe.ts';
import { attachHoverTooltip } from '../map/hoverTooltip.ts';
import { attachUrlHash, hasMapHash } from '../map/urlHash.ts';
import {
  applyPendingOverlayOrder,
  findOverlayLayer,
  APPROXIMATE_MAX_ZOOM,
  fitGeoJsonGeometry,
  flyTo,
  getOverlayGroup,
  getWorkspaceMap,
  setDesiredOverlayOrder,
  setLayerOpacity,
} from '../map/mapContext.ts';
import { attachContextMenu, type MapContextMenuTarget } from '../map/contextMenu.ts';
import { MapEditController, type DrawShape } from '../map/mapEdit.ts';
import { attachSelection } from '../map/selection.ts';
import { useShortcuts } from '../hooks/useShortcuts.ts';
import { geometryFor, isGeographic } from '../viewlinks/geoTargets.ts';
import { useViewControl } from '../viewlinks/useViewControl.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import './MapPage.css';

// Loaded only when a viewer opens the 3D pane, the same way the 3D route loads it: the scene
// module and its runtime assets are about a megabyte, and a session that never opens the pane must
// not pay for it.
const Scene3DView = lazy(() => import('../components/scene3d/Scene3DView.tsx'));
// Loaded on demand for the same reason as the scene above: it is a survey-viewer bundle that
// most visits to the map never open.
const CaveViewPanel = lazy(() => import('../components/caveview/CaveViewPanel.tsx'));
const SurveyModelViewerModal = lazy(
  () => import('../components/caveview/SurveyModelViewerModal.tsx'),
);
// On demand as well: it is only ever opened from a balloon over a photo-library overlay, which
// most installations do not run and most visits never switch on.
const LibraryPhotoFeatureModal = lazy(
  () => import('../components/map/LibraryPhotoFeatureModal.tsx'),
);

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
  // The 3D scene as a pane beside the map, off until asked for. Off is the honest default: it is
  // a second renderer with a graphics context of its own, and most visits to the map do not want
  // one. Opening it is what makes the two-way sync visible — the point of having both on screen.
  const [scene3dOpen, setScene3dOpen] = useState(false);
  // Which survey model is open beside the map, if any. Held as an id rather than as the model,
  // so the pane keeps asking for it: the answer carries a signed URL with a ten-minute life, and
  // a pane left open outlives it.
  const [caveViewModelId, setCaveViewModelId] = useState<string | null>(null);
  /** The same model over the whole window, escalated from the pane. */
  const [caveViewOverlay, setCaveViewOverlay] = useState<SurveyModelInfo | null>(null);
  const syncRef = useRef<ViewSync2dHandle | null>(null);
  const { data: layers } = useMapLayers();
  const { data: mapConfig } = useMapConfig();
  // Asked of the photo-library slice itself rather than of the map config: most of that
  // slice's routes are not map routes, and keeping the question out of the map endpoints is
  // what keeps the whole feature removable by deleting its directories.
  const { data: libraryStatus } = usePhotoLibraries();
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
  const [tripsVisible, setTripsVisible] = useState(false);
  // What the trip overlay is asking for. Held here rather than only in the layer module so a
  // saved view can restore it and the panel can show what is currently being asked.
  const [tripFilter, setTripFilter] = useState<TripLayerFilter>({});
  // How many of a carried listing filter's narrowings this overlay cannot ask about.
  const [unappliedTripFilters, setUnappliedTripFilters] = useState(0);
  // Which neighbouring photo libraries are switched on, by the name the server gives each. A
  // list rather than a flag per product, so pointing this installation at a different library
  // needs no new state here and no new field in a saved view.
  const [libraryPhotoSources, setLibraryPhotoSources] = useState<string[]>([]);
  const [editController, setEditController] = useState<MapEditController | null>(null);
  const selection = useWorkspaceStore((s) => s.selection);
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const queryClient = useQueryClient();

  // Somewhere a hyperlink in a text panel can be sent. Registered for as long as this page is
  // mounted, and separately from the camera registration above it: that one answers "the view
  // the reader is looking at", and there is exactly one; this one answers "every view that can
  // show this", and there are as many as there are windows open.
  useViewControl({
    id: 'map2d',
    kind: 'map2d',
    labelKey: 'viewLinks.controls.map2d',
    canReveal: isGeographic,
    reveal: (ref) => {
      void geometryFor(queryClient, ref).then((target) => {
        // Protection has two outcomes and this used to answer only one. A shape whose position is
        // withheld comes back with nothing, and not moving is the honest answer. A point comes
        // back SNAPPED — present, and wrong by up to the grid — so the comment that stood here,
        // saying such a feature "comes back without one", described a case that does not arise for
        // the caves and entrances most links point at: the guard never fired and the map closed in
        // on the fuzzed point as though it were surveyed. Framing it loosely is what says how well
        // it is known.
        if (target !== null) {
          fitGeoJsonGeometry(target.geometry, target.approximate ? APPROXIMATE_MAX_ZOOM : undefined);
        }
      });
    },
  });
  const visibleGeofileIds = useWorkspaceStore((s) => s.visibleGeofileIds);
  const setGeofileVisible = useWorkspaceStore((s) => s.setGeofileVisible);
  const { data: geofilePage } = useGeofiles({ pageSize: 100 });
  const importedGeofiles = useMemo(
    () => (geofilePage?.items ?? []).filter((g) => g.importStatus === 'imported'),
    [geofilePage],
  );
  const visibleTileOverlayIds = useWorkspaceStore((s) => s.visibleTileOverlayIds);
  const setTileOverlayVisible = useWorkspaceStore((s) => s.setTileOverlayVisible);
  const tileOverlayOpacity = useWorkspaceStore((s) => s.tileOverlayOpacity);
  const setTileOverlayOpacity = useWorkspaceStore((s) => s.setTileOverlayOpacity);
  const declutterLabels = useWorkspaceStore((s) => s.declutterLabels);
  const setDeclutterLabels = useWorkspaceStore((s) => s.setDeclutterLabels);
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
  const setPanelPrefs = useUiPrefsStore((s) => s.setPanelPrefs);
  const rightPinned = useUiPrefsStore((s) => s.panels.main?.pinned ?? true);
  const rightWidth = useUiPrefsStore((s) => s.panels.main?.width ?? 22);
  const centerlineDetailZoom = useUiPrefsStore((s) => s.centerlineDetailZoom);
  const centerlineMaxPaths = useUiPrefsStore((s) => s.centerlineMaxPaths);
  const setCenterlinePrefs = useUiPrefsStore((s) => s.setCenterlineLimits);

  // Unsaved-edit count mirrored out of the edit controller so the dirty guard pill
  // can warn even while the edit toolbar is hidden with the rest of the chrome.
  const [editDirty, setEditDirty] = useState(0);
  useEffect(() => editController?.subscribe((s) => setEditDirty(s.dirty)), [editController]);

  // Right-click (long-press on touch) context menu over the canvas.
  const [contextTarget, setContextTarget] = useState<MapContextMenuTarget | null>(null);

  // A photograph in a neighbouring library, offered up by its balloon to become an object here.
  const [libraryPhotoTarget, setLibraryPhotoTarget] = useState<LibraryPhotoFeatureTarget | null>(
    null,
  );

  // The balloon offers the button only when it has somewhere to send it, so withholding the
  // handler is what withholds the button from an account that may not create features. The server
  // decides in any case; this only keeps a button that would be refused off the screen. Set apart
  // from the map's mount effect because the answer arrives after it and can change, and
  // re-attaching the balloon to carry a new callback would tear one down mid-read.
  useEffect(() => {
    setLibraryPhotoFeatureHandler(mayCreateFeatures ? setLibraryPhotoTarget : undefined);
    return () => setLibraryPhotoFeatureHandler(undefined);
  }, [mayCreateFeatures]);

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
    [TRIP_LAYER_ID, createTripLayer],
      // On top of the data it is drawn over: it is one short line answering a question somebody
      // asked, and it is of no use at all under the surveys it joins.
      [CLOSEST_APPROACH_LAYER_ID, createClosestApproachLayer],
    ] as const) {
      if (!findOverlayLayer(id)) {
        getOverlayGroup().getLayers().push(create());
      }
    }

    // Nothing camera-driven about it: it draws what was last measured, wherever that is, and
    // stays until a different pair is measured or the panel clears it.
    const detachApproach = attachClosestApproachLine();
    const detachLoader = attachEntranceLoader(map);
    const detachFeatureLoader = attachSurfaceFeatureLoader(map);
    const detachCenterlineLoader = attachCenterlineLoader(map);
    const detachGeofileLoader = attachGeofileLoader(map);
    const detachPhotoLoader = attachPhotoLoader(map);
    const detachTripLoader = attachTripLoader(map);
    const detachPhotoPopup = attachPhotoPopup(map);
    // The foreign libraries' end of the same two things. Attached unconditionally: both are
    // gated on a layer being switched on, and no library configured means no layer to switch on.
    const detachLibraryPhotoLoader = attachLibraryPhotoLoader(map);
    const detachLibraryPhotoPopup = attachLibraryPhotoPopup(map);
    // Clicking a point of an imported file opens what the file recorded beside it. Attached here,
    // beside the photo popup, because the two are the same kind of thing and share the rule that
    // a click landing on neither dismisses whichever is open.
    const detachGeofilePopup = attachGeofilePopup(map);
    // The map's end of the two-way sync with the 3D scene: it announces the ground it is showing
    // and follows the ground the scene reports, in degrees rather than in cameras. The selection
    // is read back out of the store as well as written into it, because the panels beside this map
    // change it without announcing anything and the protocol has to compare an arriving pick
    // against what this window is really showing.
    const sync = attachViewSync2d(map, {
      current: () => useWorkspaceStore.getState().selection,
      set: setSelection,
    });
    syncRef.current = sync;
    const detachSelection = attachSelection(
      map,
      (picked) => {
        setSelection(picked);
        // Announced as well as stored: a popped-out window has a store of its own that this one
        // cannot reach, and the scene beside it may be in that window rather than this one.
        sync.publishSelection(picked);
      },
      // A modifier-click gathers rather than replaces. Not announced on the bus: a multi-pick is
      // this window's working set, and another window's panel showing half of it would be worse
      // than it showing none.
      (ref) => useWorkspaceStore.getState().toggleInSelectionSet(ref),
    );
    const detachHover = attachHoverTooltip(map);
    const detachUrlHash = attachUrlHash(map);
    const detachContextMenu = attachContextMenu(map, setContextTarget);
    // Panning/zooming away invalidates the menu's anchor point.
    const moveKey = map.on('movestart', () => setContextTarget(null));
    const controller = new MapEditController(map);
    setEditController(controller);
    // While this page is on screen it owns the camera the shared detail panel drives. The panel is
    // also mounted beside the 3D scene, which has a camera of its own, so it asks for whichever
    // view is showing rather than reaching for this map directly.
    const detachCamera = setActiveViewCamera({ flyTo, fitGeometry: fitGeoJsonGeometry });
    return () => {
      sync.detach();
      syncRef.current = null;
      detachCamera();
      controller.dispose();
      setEditController(null);
      detachApproach();
      detachLoader();
      detachFeatureLoader();
      detachCenterlineLoader();
      detachGeofileLoader();
      detachPhotoLoader();
      detachTripLoader();
      detachPhotoPopup();
      detachLibraryPhotoLoader();
      detachLibraryPhotoPopup();
      detachGeofilePopup();
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

  // Tile overlays from the catalogue: several at once, above the basemap and below the data.
  useEffect(() => {
    if (layers) {
      syncTileOverlays(getWorkspaceMap(), layers, new Set(visibleTileOverlayIds), tileOverlayOpacity);
    }
  }, [layers, visibleTileOverlayIds, tileOverlayOpacity]);

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

  // Pop-out windows ask the main map to go somewhere. Selection arrives through the view sync
  // above instead, which drops the map's own echo and compares by value, so a pick made here does
  // not come back as a change.
  useEffect(() => subscribe((event) => {
    if (event.kind === 'fly-to') {
      flyTo(event.lon, event.lat, event.zoom ?? 15);
    }
  }), []);

  const { data: savedViews } = useMapViews();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedViewId = searchParams.get('view');
  const requestedAreaId = searchParams.get('area');
  const requestedModelId = searchParams.get('model');
  const requestedTrips = searchParams.get('trips');

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

  /**
   * The trip listing's "show on map" button (?trips=1, plus the narrowings it was showing).
   *
   * Consumed on arrival like the params above — it is an instruction and not a description of the
   * URL — but the layer is left on afterwards, because the overlay somebody asked for is a place
   * they stay rather than a camera move that finishes.
   *
   * Only the narrowings this overlay can actually answer are adopted. The listing can also cut by
   * who was on the trip, which areas it named, one cave and one camp, and none of those are
   * questions the map layer asks; carrying them silently would draw an answer to a question
   * nobody asked. They are counted instead, and the panel says how many were left behind.
   */
  useEffect(() => {
    if (!requestedTrips) {
      return;
    }
    const carried = readTripListFilter(searchParams);
    setTripFilter({
      from: carried.from,
      to: carried.to,
      types: carried.types,
      states: carried.states,
      visibilities: carried.visibilities,
      hadIncident: carried.hadIncident,
    });
    setUnappliedTripFilters(
      [
        carried.search !== '',
        carried.participantIds.length > 0,
        carried.areaIds.length > 0,
        carried.caveId !== undefined,
        carried.expeditionId !== undefined,
      ].filter(Boolean).length,
    );
    setTripsVisible(true);
    setSearchParams(
      (params) => {
        for (const key of ['trips', 'q', 'from', 'to', 'types', 'states', 'visibilities',
          'hadIncident', 'participantIds', 'areaIds', 'caveId', 'expeditionId', 'page', 'sort']) {
          params.delete(key);
        }
        return params;
      },
      { replace: true },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a one-shot instruction, read once
  }, [requestedTrips]);

  // A survey model picked elsewhere (?model=<id>, from a cave's model list) opens the survey
  // viewer beside the map. Consumed on arrival like ?view= and ?area= above, and for the same
  // reason — it is an instruction, not a description of the URL — but unlike those two it is
  // remembered here afterwards, because the pane it opens is a place the reader stays rather than
  // a camera move that finishes.
  useEffect(() => {
    if (!requestedModelId) {
      return;
    }
    setCaveViewModelId(requestedModelId);
    setSearchParams(
      (params) => {
        params.delete('model');
        return params;
      },
      { replace: true },
    );
  }, [requestedModelId, setSearchParams]);

  // A model the reader may not see answers 404 rather than an empty result — a cave's models are
  // its location — so there is nothing to report and nothing to draw, and the pane simply does not
  // open. The query does not retry, so a withheld model costs one request.
  const { data: caveViewModel } = useSurveyModel(caveViewModelId ?? undefined);

  // An area picked elsewhere (?area=<id>, from the work-area overview or the dashboard board) is
  // framed on arrival, then the param is consumed — a one-shot instruction, exactly like ?view=
  // above and for the same reason: the camera is synced into the hash as the reader pans, so
  // leaving it behind would re-frame the area over a position somebody had panned to and shared.
  //
  // Fitted to the shape rather than centred on it at a guessed zoom: an area is a stretch of
  // country, and the only honest answer to "show me this massif" is one that has all of it on
  // screen. A guessed zoom would frame a valley and a mountain range identically.
  const { data: workAreas } = useWorkAreas(requestedAreaId !== null);
  useEffect(() => {
    if (!requestedAreaId || !workAreas) {
      return;
    }
    const extent = extentOf(workAreas.items.find((a) => a.id === requestedAreaId)?.geometry ?? null);
    if (extent) {
      getWorkspaceMap().getView().fit(transformExtent(extent, 'EPSG:4326', 'EPSG:3857'), {
        padding: [48, 48, 48, 48],
        duration: 250,
        maxZoom: 15,
      });
    }
    // Consumed whether or not it framed anything: an area with no boundary drawn yet, or one this
    // reader may not see, must not leave the map trying again on every refetch.
    setSearchParams(
      (params) => {
        params.delete('area');
        return params;
      },
      { replace: true },
    );
  }, [requestedAreaId, workAreas, setSearchParams]);

  // The home view opens the workspace once per session. The flag is claimed on the first load
  // of the views whichever path runs, so that a requested view or a shared position can never
  // be overwritten by the home view later in the session.
  useEffect(() => {
    if (!savedViews || sessionStorage.getItem('silexgis.homeApplied')) {
      return;
    }
    sessionStorage.setItem('silexgis.homeApplied', '1');
    // An explicit view request and a shareable position in the URL both outrank the home view.
    // A 3D position counts: arriving on a shared 3D link and then opening the map must not have
    // the home view quietly take the position the link was sent for.
    if (requestedViewId || requestedAreaId || hasMapHash() || hasScene3dHash()) {
      return;
    }
    const home = savedViews.find((v) => v.isHome);
    if (home) {
      applyView(home);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- one-shot on first data
  }, [savedViews, requestedViewId, requestedAreaId]);

  // A scene opening beside the map starts on the ground the map is showing rather than on its own
  // default view of the Carpathians. It is said once, when the pane opens, rather than being
  // republished on a timer: the scene subscribes as it starts, and the sync remembers the last
  // thing said so a view that arrives afterwards still hears it.
  useEffect(() => {
    if (scene3dOpen) {
      syncRef.current?.announce();
    }
  }, [scene3dOpen]);

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

  useEffect(() => {
    findOverlayLayer(TRIP_LAYER_ID)?.setVisible(tripsVisible);
    setTripsEnabled(tripsVisible); // gate the bbox loader so hidden = no fetches
  }, [tripsVisible]);

  // The narrowings reach the layer module, which is what the loader reads on every fetch — a
  // panned map must ask the same question the panel last set. Setting them re-asks immediately.
  useEffect(() => {
    setTripLayerFilter(tripFilter);
  }, [tripFilter]);
  // The libraries this account may see. A caller outside the audience is told it may read nothing
  // and given an empty list, which is the same answer to this question as an installation that has
  // been given no library: either way there is nothing to offer, and neither is told which
  // products the installation runs.
  const photoLibraries = useMemo(
    () => (libraryStatus?.mayRead ? (libraryStatus.providers ?? []) : []),
    [libraryStatus],
  );

  // The products this build can read that nobody supplied an address for. The server answers this
  // for a full administrator and with an empty list for everybody else, so nothing here decides
  // who is told; what it earns is the one thing an empty layer panel cannot say for itself —
  // whether there is nothing to look at because nothing was connected. No overlay is made for
  // these: a layer that can only ever draw nothing is not a layer.
  const unconfiguredPhotoLibraries = useMemo(
    () => (libraryStatus?.mayRead ? (libraryStatus.unconfigured ?? []) : []),
    [libraryStatus],
  );

  // Overlays for those libraries. Not registered with the built-ins on mount, because their
  // existence is a server answer that arrives after it — the same way imported files and
  // georeferenced rasters are registered — and followed by the pending-order pass, because a saved
  // view can name a layer that did not exist when the view was applied.
  useEffect(() => {
    const wanted = new Set(photoLibraries.map((library) => libraryPhotoLayerId(library.source)));
    for (const library of photoLibraries) {
      if (!findOverlayLayer(libraryPhotoLayerId(library.source))) {
        getOverlayGroup().getLayers().push(createLibraryPhotoLayer(library.source));
      }
    }
    // A library disconnected while somebody was looking at the map: the row goes away rather than
    // staying as a layer that can only ever fail.
    for (const layer of getOverlayGroup().getLayers().getArray().slice()) {
      const id = layer.get('id') as string | undefined;
      if (id && libraryPhotoSourceOf(id) && !wanted.has(id)) {
        getOverlayGroup().getLayers().remove(layer);
      }
    }
    applyPendingOverlayOrder();
  }, [photoLibraries]);

  useEffect(() => {
    for (const library of photoLibraries) {
      const on = libraryPhotoSources.includes(library.source);
      findOverlayLayer(libraryPhotoLayerId(library.source))?.setVisible(on);
      setLibraryPhotosEnabled(library.source, on); // gate the bbox loader so hidden = no fetches
    }
  }, [photoLibraries, libraryPhotoSources]);

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
    } else if (id === TRIP_LAYER_ID) {
      setTripsVisible(visible);
    } else if (libraryPhotoSourceOf(id)) {
      const source = libraryPhotoSourceOf(id)!;
      setLibraryPhotoSources((current) =>
        visible ? [...new Set([...current, source])] : current.filter((s) => s !== source),
      );
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
      tripsVisible,
      tripsFrom: tripFilter.from,
      tripsTo: tripFilter.to,
      libraryPhotoSources,
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
    // A view that remembers a 3D camera places both views itself, so this map's restored position
    // is not news for anybody: announced, it would send the scene off to frame this map's box and
    // undo the camera the same document restores a few lines below. A view that remembers no
    // camera — every view saved before the scene existed — still announces, because then the map
    // is the only thing the document knows about and the scene has nothing better to go on.
    if (ui.camera3d) {
      syncRef.current?.muteUntilSettled();
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
    setTripsVisible(ui.tripsVisible);
    // Only the window is restored, and deliberately not the facet narrowings: those are carried
    // from a listing somebody was reading at the time, and a view reopened months later would
    // otherwise silently answer for a filter whose reason nobody remembers.
    setTripFilter((current) => ({ ...current, from: ui.tripsFrom, to: ui.tripsTo }));
    setLibraryPhotoSources(ui.libraryPhotoSources);
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
    // A view saved from a session that had the scene open remembers where its camera stood. It
    // goes to whichever view is on screen, which is nothing at all when no scene is mounted — the
    // camera then stays in the document for the next time one is, rather than being staged for a
    // scene the viewer has not opened.
    applyViewCamera3d(ui.camera3d);
    reloadEntrances();
    surfaceFeaturesChanged();
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
      visibleTileOverlayIds={visibleTileOverlayIds}
      onTileOverlayVisibleChange={setTileOverlayVisible}
      tileOverlayOpacity={tileOverlayOpacity}
      onTileOverlayOpacityChange={setTileOverlayOpacity}
      declutterLabels={declutterLabels}
      onDeclutterLabelsChange={setDeclutterLabels}
      rasters={readyRasters}
      visibleRasterIds={visibleRasterIds}
      onRasterVisibleChange={setRasterVisible}
      onOverlayVisibilityChanged={onOverlayVisibilityChanged}
      photoLibraries={photoLibraries}
      unconfiguredPhotoLibraries={unconfiguredPhotoLibraries}
      visibleLibraryPhotoSources={libraryPhotoSources}
      treeNonce={treeNonce}
      tagFilter={tagFilter}
      onTagFilterChange={(slug) => {
        setTagFilter(slug);
        setMapTagFilter(slug);
        reloadEntrances();
        surfaceFeaturesChanged();
      }}
      centerlinesVisible={centerlinesVisible}
      tripsVisible={tripsVisible}
      tripFilter={tripFilter}
      onTripFilterChange={setTripFilter}
      unappliedTripFilters={unappliedTripFilters}
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

  useShortcuts(
    useMemo(
      () => [
        // Escape clears the selection — but only when nothing modal is open, which the hook
        // decides rather than each caller guessing.
        {
          key: 'Escape',
          run: () => {
            setSelection(null);
            useWorkspaceStore.getState().clearSelectionSet();
          },
        },
        { key: '[', run: () => toggleLeftDock() },
        { key: ']', run: () => toggleRightDock() },
        { key: 'ArrowLeft', alt: true, run: () => useWorkspaceStore.getState().goBackSelection() },
        { key: 'ArrowRight', alt: true, run: () => useWorkspaceStore.getState().goForwardSelection() },
      ],
      // The dock toggles read state that changes with the layout; rebuilding the list when it
      // does is what keeps a shortcut from acting on a stale idea of which host is mounted.
      // eslint-disable-next-line react-hooks/exhaustive-deps
      [isMobile, leftCollapsed, rightCollapsed, leftDrawerOpen, rightDrawerOpen],
    ),
  );

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
              buttons no longer need. The selector sits beside it — search asks what mentions
              these words, the selector asks which of these things you mean — and stacks under
              it on a phone, where they cannot both have the strip. */}
          <div className={`map-search-overlay map-chrome${isMobile ? ' map-search-overlay-mobile' : ''}`}>
            <MapSearch fullWidth={isMobile} />
            <MapObjectSelector fullWidth={isMobile} />
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
                <Tooltip title={scene3dOpen ? t('map.hideScene3d') : t('map.showScene3d')}>
                  <Button
                    size="small"
                    type={scene3dOpen ? 'primary' : 'default'}
                    icon={<GlobalOutlined />}
                    aria-label={scene3dOpen ? t('map.hideScene3d') : t('map.showScene3d')}
                    aria-pressed={scene3dOpen}
                    onClick={() => setScene3dOpen((open) => !open)}
                    data-testid="map-scene3d-toggle"
                  />
                </Tooltip>
                <Tooltip title={t('panel.popOut')}>
                  <Button
                    size="small"
                    icon={<ExportOutlined />}
                    onClick={() => window.open('/panel/registry', 'silexgis-registry', 'popup,width=900,height=700')}
                    data-testid="map-popout-registry"
                  />
                </Tooltip>
                <Tooltip title={t('panel.popOutScene3d')}>
                  <Button
                    size="small"
                    icon={<SelectOutlined />}
                    onClick={() => window.open('/panel/scene3d', 'silexgis-scene3d', 'popup,width=1100,height=800')}
                    data-testid="map-popout-scene3d"
                  />
                </Tooltip>
                <Tooltip title={t('panel.popOutSelection')}>
                  <Button
                    size="small"
                    icon={<ProfileOutlined />}
                    onClick={() =>
                      window.open('/panel/selection', 'silexgis-selection', 'popup,width=520,height=900')}
                    data-testid="map-popout-selection"
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
      {/* The 3D scene beside the map rather than instead of it. Rendered only while it is open,
          so the graphics context exists exactly as long as the pane does: a collapsed pane keeps
          its children mounted, which would leave a drawing surface alive at zero width. */}
      {!isMobile && scene3dOpen && <Separator className="map-workspace-handle" />}
      {!isMobile && scene3dOpen && (
        <Panel defaultSize="34%" minSize="20%" className="map-workspace-panel">
          <Suspense fallback={<Spin style={{ margin: 48 }} />}>
            <Scene3DView />
          </Suspense>
        </Panel>
      )}
      {/* A cave's survey model beside the map, opened from the cave's model list. Rendered only
          while it holds a model, for the same reason the 3D scene above is: a survey viewer keeps
          a drawing context, and a collapsed pane would keep one alive at zero width. */}
      {!isMobile && caveViewModel && <Separator className="map-workspace-handle" />}
      {!isMobile && caveViewModel && (
        <Panel defaultSize="30%" minSize="20%" className="map-workspace-panel">
          <div className="map-caveview-pane">
            <Flex align="center" justify="space-between" className="map-caveview-header" gap={8}>
              <Typography.Text ellipsis strong title={caveViewModel.name}>
                {caveViewModel.name}
              </Typography.Text>
              <Flex gap={4}>
                <Tooltip title={t('surveyModels.openOverlay')}>
                  <Button
                    size="small"
                    type="text"
                    icon={<ExpandOutlined />}
                    aria-label={t('surveyModels.openOverlay')}
                    onClick={() => setCaveViewOverlay(caveViewModel)}
                    data-testid="map-caveview-overlay"
                  />
                </Tooltip>
                <Tooltip title={t('surveyModels.openInWindow')}>
                  <Button
                    size="small"
                    type="text"
                    icon={<ExportOutlined />}
                    aria-label={t('surveyModels.openInWindow')}
                    onClick={() => openModelWindow(caveViewModel.id)}
                    data-testid="map-caveview-popout"
                  />
                </Tooltip>
                <Tooltip title={t('common.close')}>
                  <Button
                    size="small"
                    type="text"
                    icon={<CloseOutlined />}
                    aria-label={t('common.close')}
                    onClick={() => setCaveViewModelId(null)}
                    data-testid="map-caveview-close"
                  />
                </Tooltip>
              </Flex>
            </Flex>
            <div className="map-caveview-body">
              <Suspense fallback={<Spin style={{ margin: 48 }} />}>
                <CaveViewPanel
                  fileUrl={caveViewModel.modelUrl}
                  fileName={viewerFileName(caveViewModel)}
                  height="100%"
                  surveyModelId={caveViewModel.id}
                />
              </Suspense>
            </div>
          </div>
        </Panel>
      )}
      {!isMobile && (
        <Separator
          className="map-workspace-handle"
          // Double-click snaps between the width somebody dragged to and the one the panel
          // ships at, which is the gesture every resizable pane has and the fastest way to get
          // the map back whole without losing the width you chose.
          onDoubleClick={() => {
            const shipped = 22;
            // The pane reports both units; the stored width is a percentage, so compare in one.
            const size = rightPanelRef.current?.getSize();
            const current = Math.round(
              typeof size === 'number' ? size : (size?.asPercentage ?? shipped),
            );
            const next = current === shipped ? (rightWidth === shipped ? 34 : rightWidth) : shipped;
            rightPanelRef.current?.resize(`${next}%`);
            setPanelPrefs('main', { width: next });
          }}
        />
      )}
      {/* Pinned pushes the map aside; unpinned floats over it. A preference rather than a
          breakpoint, because which one is right depends on the screen *and* on what somebody is
          doing — a wide monitor still wants the map whole while tracing a passage. */}
      {!isMobile && rightPinned && (
        <Panel
          panelRef={rightPanelRef}
          collapsible
          collapsedSize="0%"
          defaultSize={`${rightWidth}%`}
          minSize="12%"
          className="map-workspace-panel"
          onResize={(size) => {
            setRightCollapsed(rightPanelRef.current?.isCollapsed() ?? false);
            // Remembered as the person drags, so the width survives a reload. Rounded: a stored
            // 21.7318% is noise that makes every save look like a change.
            if (typeof size === 'number' && size > 0) {
              setPanelPrefs('main', { width: Math.round(size) });
            }
          }}
        >
          {rightDock}
        </Panel>
      )}
    </Group>
    {/* The pane's model over the whole window. Mounted here rather than reached for on the cave
        page, because a reader who arrived at the map from a link has no cave page open. */}
    <Suspense fallback={null}>
      <SurveyModelViewerModal model={caveViewOverlay} onClose={() => setCaveViewOverlay(null)} />
    </Suspense>
    <Suspense fallback={null}>
      <LibraryPhotoFeatureModal
        target={libraryPhotoTarget}
        onClose={() => setLibraryPhotoTarget(null)}
      />
    </Suspense>
    {!isMobile && !rightPinned && (
      <Drawer
        placement="right"
        open={!rightCollapsed}
        onClose={() => setRightCollapsed(true)}
        // No mask: floating over the map is only useful if the map underneath still works.
        mask={false}
        size={`${rightWidth}%` as never}
        title={t('map.detailsTitle')}
        styles={{ body: { padding: 0 } }}
        rootClassName="map-dock-drawer"
        data-testid="map-right-overlay"
      >
        {rightDock}
      </Drawer>
    )}
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
