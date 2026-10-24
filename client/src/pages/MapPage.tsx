// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, useEffect, useMemo, useRef, useState, type ReactElement } from 'react';
import { createPortal } from 'react-dom';
import GeoLocationButton from '@terrestris/react-geo/dist/Button/GeoLocationButton/GeoLocationButton';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import { App, Drawer, Spin, Tabs, Tooltip } from 'antd';
import Button from '../components/Button.tsx';
import BaseLayerChooser from '../components/map/BaseLayerChooser.tsx';
import ScaleBox from '../components/map/ScaleBox.tsx';
import { useSelectionInUrl } from '../components/map/useSelectionInUrl.ts';
import { useFeatureRights } from '../api/hooks/objectRights.ts';
import {
  AimOutlined,
  BorderVerticleOutlined,
  CodeSandboxOutlined,
  ExpandOutlined,
  ProfileOutlined,
  ExportOutlined,
  EyeInvisibleOutlined,
  EyeOutlined,
  FullscreenExitOutlined,
  FullscreenOutlined,
  GlobalOutlined,
  LeftOutlined,
  QuestionCircleOutlined,
  RightOutlined,
  SelectOutlined,
} from '@ant-design/icons';
import type { EventsKey } from 'ol/events';
import type BaseLayer from 'ol/layer/Base';
import { unByKey } from 'ol/Observable';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Group, Panel, Separator, usePanelRef, type PanelProps } from 'react-resizable-panels';
import { useSearchParams } from 'react-router-dom';
import {
  isGenericFeatureType,
  useCan,
  useFeatureTypes,
  useGeofiles,
  useMapConfig,
  useMapLayers,
  useMapViews,
  usePhotoLibraries,
  useRasterMaps,
  useTerrainDerivatives,
  useSurveyModel,
  useWorkAreas,
  type SurveyModelInfo,
} from '../api/hooks.ts';
import { transformExtent } from 'ol/proj';
import { extentOf } from '../workareas/tree.ts';
import { useIsMobile } from '../hooks/useIsMobile.ts';
import EditToolbar from '../components/map/EditToolbar.tsx';
import { useEditScope } from '../components/map/useEditScope.ts';
import { useOverviewMap } from '../components/map/useOverviewMap.ts';
import FeatureListPanel from '../components/map/FeatureListPanel.tsx';
import { drawShapeForType } from '../components/map/featureTypeGroups.ts';
import LayerPanel from '../components/map/LayerPanel.tsx';
import MapContextMenu from '../components/map/MapContextMenu.tsx';
import ViewsPanel from '../components/map/ViewsPanel.tsx';
import MapFilterNotice from '../components/map/MapFilterNotice.tsx';
import CenterlineWithheldNotice from '../components/map/CenterlineWithheldNotice.tsx';
import { MAP_CANVAS_ID } from '../components/mapCanvasId.ts';
import { focusCenterlineStatus } from '../components/map/centerlineStatus.ts';
import QuickFilterChips from '../components/filters/QuickFilterChips.tsx';
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
import {
  OVERBURDEN_HIGHLIGHT_LAYER_ID,
  attachOverburdenHighlight,
  createOverburdenHighlightLayer,
} from '../map/overburdenHighlightLayer.ts';
import {
  attachRowHighlight,
  createRowHighlightLayer,
  ROW_HIGHLIGHT_LAYER_ID,
} from '../map/rowHighlightLayer.ts';
import MapListInViewButton from '../components/map/MapListInViewButton.tsx';
import { ENTRANCE_HEATMAP_LAYER_ID, createEntranceHeatmapLayer } from '../map/heatmapLayer.ts';
import {
  KARST_DENSITY_LAYER_ID,
  attachKarstDensityLoader,
  createKarstDensityLayer,
  setKarstDensityBands,
  setKarstDensityEnabled,
  setKarstDensityFixedBounds,
  setKarstDensityMode,
  type KarstDensityBands,
  type KarstDensityMode,
} from '../map/karstDensityLayer.ts';
import {
  KARST_INDEX_LAYER_ID,
  attachKarstIndexLoader,
  createKarstIndexLayer,
  setKarstIndexEnabled,
  setKarstIndexSelected,
} from '../map/karstIndexLayer.ts';
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
import {
  getMapFilter,
  mapFilterAddressKey,
  onMapFilterRefusal,
  readMapFilterAddress,
  sameMapFilter,
  setMapFilter,
  tagChipFilter,
  tagOfMapFilter,
  writeMapFilterAddress,
  type MapFilter,
} from '../map/mapFilters.ts';
import { applyViewConfig, captureViewConfig } from '../map/viewConfig.ts';
import {
  addFilterLayer,
  nextFreeFilterLayerColor,
  nextFreeFilterLayerSymbol,
  filterLayerCandidates,
  filterLayerSavedIdOf,
  filterLayerStatus,
  removeFilterLayer,
  updateFilterLayer,
} from '../map/filterLayerList.ts';
import { detachFilterLayers, onFilterLayerRefusal, syncFilterLayers } from '../map/filterLayers.ts';
import { useSavedFilters } from '../api/hooks/savedFilters.ts';
import { useSelectionSetMax } from '../api/hooks/workingSet.ts';
import type { FilterLayerRowInfo } from '../components/map/FilterLayerRow.tsx';
import { attachViewSync2d, type ViewSync2dHandle } from '../map/viewSync2d.ts';
import { openModelWindow } from '../caveview/openModelWindow.ts';
import { useCompareOffer } from '../caveview/useCompareOffer.ts';
import { viewerFileName } from '../caveview/viewerFileName.ts';
import { hasScene3dHash } from '../scene3d/urlHash3d.ts';
import { surfaceFeaturesChanged } from '../workspace/surfaceFeatureRefresh.ts';
import { applyViewCamera3d, setActiveViewCamera } from '../workspace/viewCamera.ts';
import { subscribe } from '../workspace/workspaceBus.ts';
import { RASTER_LAYER_PREFIX, syncRasterLayers } from '../map/rasterLayers.ts';
import { syncTerrainDerivativeLayers, terrainDerivativeIdOf } from '../map/terrainDerivativeLayers.ts';
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
import { MAP_WORKSPACE_SHORTCUTS } from '../hooks/shortcutRegistry.ts';
import ShortcutHelpDialog from '../components/ShortcutHelpDialog.tsx';
import { geometryFor, isGeographic } from '../viewlinks/geoTargets.ts';
import { useViewControl } from '../viewlinks/useViewControl.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { useWorkspacePanesStore, type WorkspacePaneId } from '../stores/workspacePanesStore.ts';
import {
  BOTTOM_SIZE_MAX,
  BOTTOM_SIZE_MIN,
  DEFAULT_ARRANGEMENT,
  type PanePlace,
} from '../stores/workspacePrefs.ts';
import { useUiDefaults } from '../api/hooks/me.ts';
import BottomTable from '../components/map/workspace/BottomTable.tsx';
import PaneHeader, { type PaneDestination } from '../components/map/workspace/PaneHeader.tsx';
import PaneSlot from '../components/map/workspace/PaneSlot.tsx';
import { usePaneElement } from '../components/map/workspace/paneElement.ts';
import SurveyModelChooser from '../components/map/workspace/SurveyModelChooser.tsx';
import WorkspaceMenu from '../components/map/workspace/WorkspaceMenu.tsx';
import { applyWorkspaceSnapshot, openScene3dWindow } from '../components/map/workspace/workspaceActions.ts';
import { installationPresetOf } from '../components/map/workspace/workspacePresets.ts';
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
  const { t, i18n } = useTranslation();
  // A vector background's labels follow the language the interface is read in.
  const language = (i18n.resolvedLanguage ?? i18n.language ?? 'ro').slice(0, 2);
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const mapTarget = useRef<HTMLDivElement>(null);
  const layersPanelRef = usePanelRef();
  const detailsPanelRef = usePanelRef();
  // The docks' collapsed state is the person's, remembered with their account: somebody who works
  // with the layer panel folded away finds it folded away the next time, on any browser. Kept by
  // what the dock holds, not by the side it is on: swapping the docks moves a folded panel folded.
  const docks = useUiPrefsStore((s) => s.docks);
  const setDocks = useUiPrefsStore((s) => s.setDocks);
  const layersCollapsed = docks.leftHidden;
  const detailsCollapsed = docks.rightHidden;
  const setLayersCollapsed = (hidden: boolean) => setDocks({ leftHidden: hidden });
  const setDetailsCollapsed = (hidden: boolean) => setDocks({ rightHidden: hidden });
  // Where the panes around the map go, the account's like the docks.
  const arrangement = useUiPrefsStore((s) => s.workspace) ?? DEFAULT_ARRANGEMENT;
  const setWorkspace = useUiPrefsStore((s) => s.setWorkspace);
  const workspaceGroupRef = useRef<HTMLDivElement>(null);
  // The set of panels last laid out, and whether the layout library is still laying out a new one —
  // see where the row is built.
  const panelSetRef = useRef('');
  const relayingOutRef = useRef(false);
  // Each pane that must outlive a move is drawn into an element of its own, which the slot showing
  // it takes in — see PaneSlot for why.
  const scenePaneElement = usePaneElement('scene3d');
  const caveViewPaneElement = usePaneElement('caveview');
  const tablePaneElement = usePaneElement('table');
  // The stored state can also change under a mounted dock: the account's own arrangement arrives
  // a moment after sign-in, over what this browser had. The panel is folded or unfolded to match,
  // so the toggle's label and the dock never disagree. A fold the panel made itself has already
  // reached the store by then, and asks for nothing here.
  useEffect(() => {
    const panel = layersPanelRef.current;
    if (isMobile || !panel) {
      return;
    }
    if (docks.leftHidden !== panel.isCollapsed()) {
      if (docks.leftHidden) {
        panel.collapse();
      } else {
        panel.expand();
      }
    }
  }, [docks.leftHidden, isMobile, layersPanelRef]);
  useEffect(() => {
    const panel = detailsPanelRef.current;
    if (isMobile || !panel) {
      return;
    }
    if (docks.rightHidden !== panel.isCollapsed()) {
      if (docks.rightHidden) {
        panel.collapse();
      } else {
        panel.expand();
      }
    }
  }, [docks.rightHidden, isMobile, detailsPanelRef]);
  const [shortcutHelpOpen, setShortcutHelpOpen] = useState(false);
  // Below md the docks are drawers over the map instead of resizable panes; the same
  // dock-toggle buttons drive whichever one is mounted.
  const [leftDrawerOpen, setLeftDrawerOpen] = useState(false);
  const [rightDrawerOpen, setRightDrawerOpen] = useState(false);
  // The 3D scene as a pane beside the map, off until asked for. Off is the honest default: it is
  // a second renderer with a graphics context of its own, and most visits to the map do not want
  // one. Opening it is what makes the two-way sync visible — the point of having both on screen.
  const scene3dOpen = useWorkspacePanesStore((s) => s.scene3dOpen);
  const setScene3dOpen = useWorkspacePanesStore((s) => s.setScene3dOpen);
  // Which survey model is open beside the map, if any, and whether its pane is up without one.
  const caveViewModelId = useWorkspacePanesStore((s) => s.caveViewModelId);
  const setCaveViewModelId = useWorkspacePanesStore((s) => s.setCaveViewModelId);
  const caveViewPaneOpen = useWorkspacePanesStore((s) => s.caveViewPaneOpen);
  const setCaveViewPaneOpen = useWorkspacePanesStore((s) => s.setCaveViewPaneOpen);
  const maximised = useWorkspacePanesStore((s) => s.maximised);
  const setMaximised = useWorkspacePanesStore((s) => s.setMaximised);
  // The panes are this visit's: leaving the map shuts them, as it did while they were the page's
  // own state, so coming back to the map does not quietly rebuild a scene nobody asked for again.
  useEffect(
    () => () => {
      const panes = useWorkspacePanesStore.getState();
      panes.setScene3dOpen(false);
      panes.setCaveViewModelId(null);
      panes.setCaveViewPaneOpen(false);
      panes.setMaximised(null);
    },
    [],
  );
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
  const [surfaceFeaturesVisible, setSurfaceFeaturesVisible] = useState(true);
  // Off by default: it is the heaviest overlay by a wide margin, and a cave's splay work is
  // invisible at the zooms most sessions spend their time at.
  const [centerlinesVisible, setCenterlinesVisible] = useState(false);
  const [heatmapVisible, setHeatmapVisible] = useState(false);
  const [karstDensityVisible, setKarstDensityVisible] = useState(false);
  const [karstDensityMode, setKarstDensityModeState] = useState<KarstDensityMode>('density');
  const [karstDensityBands, setKarstDensityBandsState] = useState<KarstDensityBands>('relative');
  const [karstIndexVisible, setKarstIndexVisible] = useState(false);
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
  const visibleTerrainDerivativeIds = useWorkspaceStore((s) => s.visibleTerrainDerivativeIds);
  const setTerrainDerivativeVisible = useWorkspaceStore((s) => s.setTerrainDerivativeVisible);
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
  // Every computed picture of the ground, finished or not: the panel lists the unfinished ones
  // too, because a picture somebody asked for and cannot see anywhere reads as a lost request.
  //
  // Not asked for at all without the terrain right. The server refuses the same request either
  // way, but a query that fails every eight minutes for the whole life of a session is a refusal
  // nobody reads, arriving forever, and it fills the browser's own error trail with something no
  // reader can act on.
  const mayReadTerrain = useCan('terrain', 'read');
  const { data: terrainDerivatives } = useTerrainDerivatives(mayReadTerrain);
  const derivativeLayers = useMemo(() => terrainDerivatives ?? [], [terrainDerivatives]);

  // The toolbar mixes drawing new features with modifying existing ones, so either
  // domain-level right shows it — and so does the right to change the one feature selected, which
  // a member can hold on that feature alone. Its reshaping tools follow the selected feature's own
  // answer while one is selected; the server re-decides every save.
  const mayWriteFeatures = useCan('features', 'write');
  const mayCreateFeatures = useCan('features', 'create');
  const selectedFeatureId = selection?.kind === 'feature' ? selection.featureId : undefined;
  const selectedRights = useFeatureRights(selectedFeatureId, { enabled: selectedFeatureId !== undefined });
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

  const { canEdit, toolbarShown, mayReshapeSaved, rightsPending } = useEditScope({
    controller: editController,
    selectedFeatureId,
    selectedRights,
    mayWriteFeatures,
    mayCreateFeatures,
    unsavedEdits: editDirty,
  });

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
      // Under everything, the entrance wash included: a cell is a statement about a tract of
      // ground, and every other overlay is a statement about something on it.
      [KARST_DENSITY_LAYER_ID, createKarstDensityLayer],
      // Area fills above the density cells, so an area's outline still reads over a dense tract.
      [KARST_INDEX_LAYER_ID, createKarstIndexLayer],
      [ENTRANCE_HEATMAP_LAYER_ID, createEntranceHeatmapLayer],
      [SURFACE_FEATURE_LAYER_ID, createSurfaceFeatureLayer],
      [ENTRANCE_LAYER_ID, createEntranceLayer],
      [CENTERLINE_LAYER_ID, createCenterlineLayer],
      [PHOTO_LAYER_ID, createPhotoLayer],
    [TRIP_LAYER_ID, createTripLayer],
      // On top of the data it is drawn over: it is one short line answering a question somebody
      // asked, and it is of no use at all under the surveys it joins.
      [CLOSEST_APPROACH_LAYER_ID, createClosestApproachLayer],
      // Topmost of all: it is a single mark saying "the reading you pressed came from here", and
      // one that anything at all could cover is a mark that failed to answer the question.
      [OVERBURDEN_HIGHLIGHT_LAYER_ID, createOverburdenHighlightLayer],
      // The row a list in another window is pointing at, ringed over whatever this map drew for it.
      [ROW_HIGHLIGHT_LAYER_ID, createRowHighlightLayer],
    ] as const) {
      if (!findOverlayLayer(id)) {
        getOverlayGroup().getLayers().push(create());
      }
    }

    // Nothing camera-driven about it: it draws what was last measured, wherever that is, and
    // stays until a different pair is measured or the panel clears it.
    const detachApproach = attachClosestApproachLine();
    // Same arrangement, same reason: it marks whichever reading was pressed last on an overburden
    // curve, wherever in the world that is, and stays until another is pressed or the panel clears
    // it.
    const detachOverburdenHighlight = attachOverburdenHighlight();
    const detachRowHighlight = attachRowHighlight();
    const detachLoader = attachEntranceLoader(map);
    const detachFeatureLoader = attachSurfaceFeatureLoader(map);
    const detachCenterlineLoader = attachCenterlineLoader(map);
    const detachGeofileLoader = attachGeofileLoader(map);
    const detachPhotoLoader = attachPhotoLoader(map);
    const detachTripLoader = attachTripLoader(map);
    const detachKarstDensityLoader = attachKarstDensityLoader(map);
    const detachKarstIndexLoader = attachKarstIndexLoader(map);
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
      detachOverburdenHighlight();
      detachRowHighlight();
      detachLoader();
      detachFeatureLoader();
      detachCenterlineLoader();
      detachGeofileLoader();
      detachPhotoLoader();
      detachTripLoader();
      detachKarstDensityLoader();
      detachKarstIndexLoader();
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

  // The filter the map's layers draw through. Unlike the one-shot parameters around it, it is a
  // description of the page and stays in the address — `filter` for a document, `saved` for a
  // saved filter's id — so a link to a filtered map opens filtered, as the cave registry's does.
  // The loaders read the module's copy; this state is what renders, and the two change together.
  const [mapFilter, setMapFilterState] = useState<MapFilter | null>(() => {
    const initial = readMapFilterAddress(searchParams).filter;
    setMapFilter(initial);
    return initial;
  });
  const [unreadableFilterLink, setUnreadableFilterLink] = useState(
    () => readMapFilterAddress(searchParams).unreadableLink,
  );
  const [filterRefused, setFilterRefused] = useState(false);
  const filterAddressKey = mapFilterAddressKey(searchParams);
  // Whether the filter in force came in by the address — a link opened or followed — rather than
  // from a chip, the tag chooser or a view picked on this page. A link's filter the recipient
  // cannot run (a clubmate's private saved filter, one from another world) is a link that cannot
  // be read: the map opens with nothing filtered and says so, rather than empty under a refusal.
  const filterFromAddress = useRef(readMapFilterAddress(searchParams).filter !== null);

  const adoptMapFilter = (next: MapFilter | null, fromAddress = false) => {
    filterFromAddress.current = fromAddress && next !== null;
    setMapFilter(next);
    setMapFilterState(next);
    setFilterRefused(false);
    reloadEntrances();
    surfaceFeaturesChanged();
  };

  useEffect(
    () =>
      onMapFilterRefusal(() => {
        // Several layers ask under one filter, so the refusal can come more than once; once the
        // filter has been dropped the later ones are about a filter no longer in force.
        if (getMapFilter() === null) {
          return;
        }
        if (filterFromAddress.current) {
          adoptMapFilter(null);
          setUnreadableFilterLink(true);
        } else {
          setFilterRefused(true);
        }
      }),
    [],
  );

  // The address changed under the page — a link followed inside the app, back or forward. Keyed
  // on the two values alone, so the one-shot parameters being consumed never re-reads it.
  useEffect(() => {
    const reading = readMapFilterAddress(searchParams);
    if (reading.unreadableLink) {
      setUnreadableFilterLink(true);
    } else if (!sameMapFilter(reading.filter, getMapFilter())) {
      adoptMapFilter(reading.filter, true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- keyed on the filter's own two values; adoptMapFilter only sets state
  }, [filterAddressKey]);

  // The filter changed on the page — the tag chooser, a saved view — and is written into the
  // address after the render that changed it. Written then rather than at the change because a
  // view applied on arrival consumes `?view=` in the same breath, and two writes from one render
  // both start from the same parameters: the second would put the first one's filter back out.
  // An unreadable token is taken out too; the notice it raised stays until dismissed.
  useEffect(() => {
    const wanted = writeMapFilterAddress(searchParams, mapFilter);
    if (mapFilterAddressKey(wanted) !== mapFilterAddressKey(searchParams)) {
      setSearchParams((params) => writeMapFilterAddress(params, mapFilter), { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- runs when the filter changes, not on every address change
  }, [mapFilter]);

  // Layers drawn from saved filters, each beside the main layers rather than instead of them. The
  // list is this page's state and a saved view's; the OpenLayers layers follow it. Whether a
  // layer's filter can still be run is decided from the saved-filter list and from the server's
  // own refusals, and a layer whose filter cannot is kept as a row that says so and draws nothing.
  const filterLayers = useWorkspaceStore((s) => s.filterLayers);
  const setFilterLayers = useWorkspaceStore((s) => s.setFilterLayers);
  const [refusedFilterLayers, setRefusedFilterLayers] = useState<ReadonlySet<string>>(() => new Set());
  const { data: savedFilters } = useSavedFilters();
  // The installation's ceiling on the working set, so a modifier-click stops where the server's
  // summary request does.
  useSelectionSetMax();
  useEffect(
    () =>
      onFilterLayerRefusal((savedFilterId) =>
        setRefusedFilterLayers((current) => new Set([...current, savedFilterId.toLowerCase()])),
      ),
    [],
  );
  const filterLayerRows = useMemo<FilterLayerRowInfo[]>(
    () =>
      filterLayers.map((spec) => ({
        spec,
        name:
          savedFilters?.find((filter) => filter.id.toLowerCase() === spec.savedFilterId.toLowerCase())?.name ?? null,
        status: filterLayerStatus(spec, savedFilters, refusedFilterLayers),
        nextColor: nextFreeFilterLayerColor(filterLayers, spec),
        nextSymbol: nextFreeFilterLayerSymbol(filterLayers, spec),
      })),
    [filterLayers, savedFilters, refusedFilterLayers],
  );
  const unreadableFilterLayerKey = filterLayerRows
    .filter((row) => row.status === 'unreadable')
    .map((row) => row.spec.savedFilterId.toLowerCase())
    .join(',');
  useEffect(() => () => detachFilterLayers(), []);
  // The 3D scene beside the map draws the same layers, and draws nothing for the same unreadable ones.
  useEffect(() => {
    useWorkspaceStore
      .getState()
      .setUnreadableFilterLayers(unreadableFilterLayerKey === '' ? [] : unreadableFilterLayerKey.split(','));
  }, [unreadableFilterLayerKey]);

  // The saved-filter layers follow their list, their filters' readability and their transparency.
  useEffect(() => {
    syncFilterLayers(
      getWorkspaceMap(),
      filterLayers,
      new Set(unreadableFilterLayerKey === '' ? [] : unreadableFilterLayerKey.split(',')),
      new globalThis.Map(Object.entries(overlayOpacity)),
    );
    // A just-applied saved view may prescribe a stacking that includes these layers.
    applyPendingOverlayOrder();
  }, [filterLayers, unreadableFilterLayerKey, overlayOpacity]);

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
  }, [requestedModelId, setSearchParams, setCaveViewModelId]);

  // A model the reader may not see answers 404 rather than an empty result — a cave's models are
  // its location — so there is nothing to report and nothing to draw, and the pane simply does not
  // open. The query does not retry, so a withheld model costs one request.
  const { data: caveViewModel } = useSurveyModel(caveViewModelId ?? undefined);
  // The same cave's other line plots, for looking at this one together with another.
  const caveViewCompare = useCompareOffer(caveViewModel);

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
    setKarstIndexSelected(selection?.kind === 'feature' ? selection.featureId : null);
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
      syncBaseLayers(getWorkspaceMap(), layers, activeBaseId, language);
      setBaseLayersVersion((v) => v + 1);
    }
  }, [layers, activeBaseId, language]);
  const baseOlLayers = useMemo(
    () => (baseLayersVersion > 0 ? getBaseLayers(getWorkspaceMap()) : []),
    [baseLayersVersion],
  );

  // The overview inset, drawn from whichever base layer the map shows. Not on a phone, whose
  // screen it would cover a good part of; open or shut as the account's preference says.
  useOverviewMap(getWorkspaceMap(), baseOlLayers, !isMobile, t('map.overviewMap'));

  // What is selected travels in the address, and an address can say what to select.
  useSelectionInUrl();

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
  // The libraries this account may see. A caller outside the audience is told it may read nothing
  // and given an empty list, which is the same answer to this question as an installation that has
  // been given no library: either way there is nothing to offer, and neither is told which
  // products the installation runs.
  //
  // A library this installation has stopped using is left out, so no overlay is made for it and
  // nothing asks it for a viewport. That is the whole of what the switch has to do here: an
  // overlay left in place with its requests refused would draw an empty layer somebody would read
  // as an empty library.
  const photoLibraries = useMemo(
    () =>
      libraryStatus?.mayRead
        ? (libraryStatus.providers ?? []).filter((library) => !library.suspended)
        : [],
    [libraryStatus],
  );

  // The ones this installation has and is not using. No overlay and no layer row — a layer that
  // can only ever draw nothing is not a layer — but they are named in the panel all the same,
  // because a library that was there yesterday and is gone today is a question, and the panel is
  // where somebody looks for the answer.
  const suspendedPhotoLibraries = useMemo(
    () =>
      libraryStatus?.mayRead
        ? (libraryStatus.providers ?? []).filter((library) => library.suspended)
        : [],
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
    } else if (id === KARST_DENSITY_LAYER_ID) {
      setKarstDensityVisible(visible);
    } else if (id === KARST_INDEX_LAYER_ID) {
      setKarstIndexVisible(visible);
    } else if (id === PHOTO_LAYER_ID) {
      setPhotosVisible(visible);
    } else if (id === TRIP_LAYER_ID) {
      setTripsVisible(visible);
    } else if (libraryPhotoSourceOf(id)) {
      const source = libraryPhotoSourceOf(id)!;
      setLibraryPhotoSources((current) =>
        visible ? [...new Set([...current, source])] : current.filter((s) => s !== source),
      );
    } else if (filterLayerSavedIdOf(id)) {
      const savedFilterId = filterLayerSavedIdOf(id)!;
      setFilterLayers((current) => updateFilterLayer(current, savedFilterId, { visible }));
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
      karstDensityVisible,
      karstDensityMode,
      karstDensityBands,
      karstIndexVisible,
      photosVisible,
      tripsVisible,
      tripsFrom: tripFilter.from,
      tripsTo: tripFilter.to,
      libraryPhotoSources,
      geofileIds: visibleGeofileIds,
      rasters: visibleRasterIds.map((id) => ({ id, opacity: rasterOpacity[id] })),
      mapFilter,
      filterLayers,
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
    setKarstDensityVisible(ui.karstDensityVisible);
    setKarstDensityModeState(ui.karstDensityMode);
    setKarstDensityBandsState(ui.karstDensityBands);
    setKarstIndexVisible(ui.karstIndexVisible);
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
    // A view saved from a session that had the scene open remembers where its camera stood. It
    // goes to whichever view is on screen, which is nothing at all when no scene is mounted — the
    // camera then stays in the document for the next time one is, rather than being staged for a
    // scene the viewer has not opened.
    applyViewCamera3d(ui.camera3d);
    // A refusal remembered from before is about this session's filters, not the view's; each layer
    // the view brings asks the server afresh.
    setRefusedFilterLayers(new Set());
    setFilterLayers(ui.filterLayers);
    // Reloads both data layers, which the view's visibility toggles above need as well.
    adoptMapFilter(ui.mapFilter);
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
      terrainDerivatives={derivativeLayers}
      visibleTerrainDerivativeIds={visibleTerrainDerivativeIds}
      onTerrainDerivativeVisibleChange={setTerrainDerivativeVisible}
      onOverlayVisibilityChanged={onOverlayVisibilityChanged}
      photoLibraries={photoLibraries}
      suspendedPhotoLibraries={suspendedPhotoLibraries}
      unconfiguredPhotoLibraries={unconfiguredPhotoLibraries}
      visibleLibraryPhotoSources={libraryPhotoSources}
      treeNonce={treeNonce}
      tagFilter={tagOfMapFilter(mapFilter)}
      onTagFilterChange={(slug) => adoptMapFilter(tagChipFilter(slug))}
      filterLayers={filterLayerRows}
      filterLayerCandidates={filterLayerCandidates(savedFilters, filterLayers).map((filter) => ({
        id: filter.id,
        name: filter.name,
      }))}
      onAddFilterLayer={(savedFilterId) => {
        setRefusedFilterLayers((current) => {
          const next = new Set(current);
          next.delete(savedFilterId.toLowerCase());
          return next;
        });
        setFilterLayers((current) => addFilterLayer(current, savedFilterId));
      }}
      onRemoveFilterLayer={(savedFilterId) =>
        setFilterLayers((current) => removeFilterLayer(current, savedFilterId))}
      onFilterLayerStyleChange={(savedFilterId, patch) =>
        setFilterLayers((current) => updateFilterLayer(current, savedFilterId, patch))}
      centerlinesVisible={centerlinesVisible}
      tripsVisible={tripsVisible}
      karstDensityVisible={karstDensityVisible}
      karstDensityMode={karstDensityMode}
      onKarstDensityModeChange={setKarstDensityModeState}
      karstDensityBands={karstDensityBands}
      onKarstDensityBandsChange={setKarstDensityBandsState}
      karstIndexVisible={karstIndexVisible}
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

  // The "In view" list moves to the table under the map while that is shown, rather than being
  // drawn twice: each copy marks the rows it has ticked on the map's own features, and two copies
  // would undo each other's marks. On a phone there is no table, and the list stays here.
  const inViewInTable = !isMobile && arrangement.table;
  const rightDock = (
    <Tabs
      className="map-right-tabs"
      activeKey={inViewInTable ? 'selection' : rightTab}
      onChange={setRightTab}
      items={[
        { key: 'selection', label: t('map.tabSelection'), children: <SelectionPanel /> },
        ...(inViewInTable ? [] : [{ key: 'inview', label: t('map.tabInView'), children: <FeatureListPanel /> }]),
      ]}
    />
  );

  // Which dock is on which side. Swapping is a desktop arrangement only: on a phone the docks are
  // drawers opened from the edge buttons, and the layer drawer stays the one on the left.
  const swapped = !isMobile && arrangement.docksSwapped;
  const dockOnSide = (side: 'left' | 'right'): DockContent =>
    (side === 'left') !== swapped ? 'layers' : 'details';
  const sideOfDock = (dock: DockContent): 'left' | 'right' =>
    dockOnSide('left') === dock ? 'left' : 'right';

  // One notion of "is this dock showing" across both layouts, so the toggle buttons keep
  // their icon and label truthful whichever host is mounted.
  const dockHidden = (dock: DockContent) =>
    isMobile
      ? !(dock === 'layers' ? leftDrawerOpen : rightDrawerOpen)
      : dock === 'layers'
        ? layersCollapsed
        : detailsCollapsed;
  const leftHidden = dockHidden(dockOnSide('left'));
  const rightHidden = dockHidden(dockOnSide('right'));
  const layersHidden = dockHidden('layers');

  const toggleDock = (dock: DockContent) => {
    if (isMobile) {
      if (dock === 'layers') {
        setLeftDrawerOpen((open) => !open);
      } else {
        setRightDrawerOpen((open) => !open);
      }
      return;
    }
    const panel = dock === 'layers' ? layersPanelRef.current : detailsPanelRef.current;
    const collapsed = dock === 'layers' ? layersCollapsed : detailsCollapsed;
    if (!panel) {
      // The details panel floating over the map rather than docked: its drawer follows the flag.
      if (dock === 'details') {
        setDetailsCollapsed(!detailsCollapsed);
      }
      return;
    }
    if (collapsed) {
      panel.expand();
    } else {
      panel.collapse();
    }
  };
  // The bracket keys and the edge buttons are about sides, which is what a person sees: after a
  // swap, `[` folds whatever is on the left.
  const toggleLeftDock = () => toggleDock(dockOnSide('left'));
  const toggleRightDock = () => toggleDock(dockOnSide('right'));

  // Opens (never closes) the layer panel and takes focus to its survey-line block, where the
  // withheld-lines alert and the line budget are.
  const openLayerPanelAtCenterlines = () => {
    if (isMobile) {
      setLeftDrawerOpen(true);
    } else {
      layersPanelRef.current?.expand();
    }
    focusCenterlineStatus();
  };

  // The survey-model pane is up while it holds a model, or while a preset has put it up empty.
  const caveViewShown = !isMobile && (caveViewModel !== undefined || caveViewPaneOpen);
  const tableShown = !isMobile && arrangement.table;
  const sceneShown = !isMobile && scene3dOpen;

  // A pane that went away cannot stay filling the workspace, and a phone has no panes to fill it.
  useEffect(() => {
    const gone =
      (maximised === 'scene3d' && !sceneShown) ||
      (maximised === 'caveview' && !caveViewShown) ||
      (maximised === 'table' && !tableShown) ||
      (maximised === 'details' && !rightPinned && detailsCollapsed);
    if (maximised !== null && (isMobile || gone)) {
      setMaximised(null);
    }
  }, [maximised, isMobile, sceneShown, caveViewShown, tableShown, rightPinned, detailsCollapsed, setMaximised]);

  // A pane filling the workspace is drawn over the others at the workspace's own box, which is
  // followed while it is filled: the window can be resized, or the navigation rail opened, under it.
  useEffect(() => {
    const group = workspaceGroupRef.current;
    if (maximised === null || !group) {
      return;
    }
    const place = () => {
      const box = group.getBoundingClientRect();
      group.style.setProperty('--workspace-top', `${box.top}px`);
      group.style.setProperty('--workspace-left', `${box.left}px`);
      group.style.setProperty('--workspace-width', `${box.width}px`);
      group.style.setProperty('--workspace-height', `${box.height}px`);
    };
    place();
    const observer = new ResizeObserver(place);
    observer.observe(group);
    window.addEventListener('resize', place);
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', place);
    };
  }, [maximised]);

  const toggleMaximised = (pane: WorkspacePaneId) => setMaximised(maximised === pane ? null : pane);
  const mapMaximiseLabel =
    maximised === 'map'
      ? t('workspace.restore', { name: t('workspace.mapName') })
      : t('workspace.maximise', { name: t('workspace.mapName') });
  // What a pane's root carries: its name, for the maximise key to find which pane focus is in, and
  // — while another pane fills the workspace — `inert`, so the panes drawn over cannot be reached
  // by Tab or a screen reader while they cannot be seen.
  const paneRoot = (pane: WorkspacePaneId, className: string) => ({
    className: [className, maximised === pane ? 'workspace-pane-maximised' : ''].filter(Boolean).join(' '),
    'data-workspace-pane': pane,
    inert: maximised !== null && maximised !== pane ? true : undefined,
  });
  // The docks have no strip of their own to put a restore button in, so one floats in the corner.
  const restoreFloat = (pane: WorkspacePaneId, name: string) =>
    maximised === pane ? (
      <Button
        className="workspace-restore-float"
        size="small"
        icon={<FullscreenExitOutlined />}
        aria-label={t('workspace.restore', { name })}
        aria-keyshortcuts="M"
        onClick={() => setMaximised(null)}
        data-testid={`workspace-${pane}-restore`}
      >
        {t('workspace.restoreShort')}
      </Button>
    ) : null;

  const moveScene3d = (destination: PaneDestination) => {
    setMaximised(null);
    if (destination === 'window') {
      // One window holds one scene: the pane shuts as the window opens.
      setScene3dOpen(false);
      openScene3dWindow();
    } else {
      setWorkspace({ ...arrangement, scene3dPlace: destination });
    }
  };
  const moveCaveView = (destination: PaneDestination) => {
    setMaximised(null);
    if (destination === 'window') {
      if (caveViewModel) {
        openModelWindow(caveViewModel.id);
      }
      setCaveViewModelId(null);
      setCaveViewPaneOpen(false);
    } else {
      setWorkspace({ ...arrangement, caveViewPlace: destination });
    }
  };
  const closeCaveView = () => {
    setCaveViewModelId(null);
    setCaveViewPaneOpen(false);
  };

  // An installation can start a new account on one of the presets. "New" is an account that has
  // arranged nothing yet, and it is decided only once the account's own stored preferences have
  // been read — before that, an account that did arrange its workspace would look as if it had not.
  // Applying it stores the arrangement, so it happens once and is the person's own from then on.
  const { data: uiDefaults } = useUiDefaults();
  const accountPrefsRead = useUiPrefsStore((s) => s.accountPrefsBaseline !== undefined);
  const workspaceChosen = useUiPrefsStore((s) => s.workspace !== undefined);
  useEffect(() => {
    if (isMobile || !accountPrefsRead || workspaceChosen || !uiDefaults) {
      return;
    }
    const preset = installationPresetOf(uiDefaults.panel);
    if (preset) {
      applyWorkspaceSnapshot(preset.snapshot, false);
    }
  }, [isMobile, accountPrefsRead, workspaceChosen, uiDefaults]);

  useShortcuts(
    useMemo(
      () => [
        { ...MAP_WORKSPACE_SHORTCUTS.help, run: () => setShortcutHelpOpen(true) },
        // Escape clears the selection — but only when nothing modal is open, which the hook
        // decides rather than each caller guessing.
        {
          ...MAP_WORKSPACE_SHORTCUTS.clearSelection,
          run: () => {
            setSelection(null);
            useWorkspaceStore.getState().clearSelectionSet();
          },
        },
        { ...MAP_WORKSPACE_SHORTCUTS.toggleLeftDock, run: () => toggleLeftDock() },
        { ...MAP_WORKSPACE_SHORTCUTS.toggleRightDock, run: () => toggleRightDock() },
        { ...MAP_WORKSPACE_SHORTCUTS.selectionBack, run: () => useWorkspaceStore.getState().goBackSelection() },
        {
          ...MAP_WORKSPACE_SHORTCUTS.selectionForward,
          run: () => useWorkspaceStore.getState().goForwardSelection(),
        },
        // The pane focus is in fills the workspace — the map when focus is in none of them — and
        // the same key gives every pane its own size back. Not on a phone, which has one pane.
        ...(isMobile
          ? []
          : [
              {
                ...MAP_WORKSPACE_SHORTCUTS.maximise,
                run: () => {
                  const panes = useWorkspacePanesStore.getState();
                  if (panes.maximised !== null) {
                    panes.setMaximised(null);
                    return;
                  }
                  const focused = document.activeElement?.closest('[data-workspace-pane]');
                  panes.setMaximised(
                    (focused?.getAttribute('data-workspace-pane') as WorkspacePaneId | null) ?? 'map',
                  );
                },
              },
            ]),
      ],
      // The dock toggles read state that changes with the layout; rebuilding the list when it
      // does is what keeps a shortcut from acting on a stale idea of which host is mounted.
      // eslint-disable-next-line react-hooks/exhaustive-deps
      [isMobile, layersCollapsed, detailsCollapsed, leftDrawerOpen, rightDrawerOpen, swapped, rightPinned],
    ),
  );

  // The details panel's separator: a double-click snaps between the width somebody dragged to and
  // the one the panel ships at, which is the gesture every resizable pane has and the fastest way
  // to get the map back whole without losing the width you chose.
  const snapDetailsWidth = () => {
    const shipped = 22;
    // The pane reports both units; the stored width is a percentage, so compare in one.
    const size = detailsPanelRef.current?.getSize();
    const current = Math.round(typeof size === 'number' ? size : (size?.asPercentage ?? shipped));
    const next = current === shipped ? (rightWidth === shipped ? 34 : rightWidth) : shipped;
    detailsPanelRef.current?.resize(`${next}%`);
    setPanelPrefs('main', { width: next });
  };

  const layersDockPanel = (
    <StablePanel
      key={`dock-layers-${sideOfDock('layers')}`}
      id="ws-dock-layers"
      panelRef={layersPanelRef}
      collapsible
      collapsedSize="0%"
      defaultSize={docks.leftHidden ? '0%' : '16%'}
      minSize="10%"
      className="map-workspace-panel"
      onResize={() => {
        if (!relayingOutRef.current) {
          setLayersCollapsed(layersPanelRef.current?.isCollapsed() ?? false);
        }
      }}
    >
      <div {...paneRoot('layers', 'workspace-dock')}>
        {layerDock}
        {restoreFloat('layers', t('map.layersTitle'))}
      </div>
    </StablePanel>
  );

  // Pinned pushes the map aside; unpinned floats over it. A preference rather than a breakpoint,
  // because which one is right depends on the screen *and* on what somebody is doing — a wide
  // monitor still wants the map whole while tracing a passage.
  const detailsDockPanel = rightPinned ? (
    <StablePanel
      key={`dock-details-${sideOfDock('details')}`}
      id="ws-dock-details"
      panelRef={detailsPanelRef}
      collapsible
      collapsedSize="0%"
      defaultSize={docks.rightHidden ? '0%' : `${rightWidth}%`}
      minSize="12%"
      className="map-workspace-panel"
      onResize={(size) => {
        if (relayingOutRef.current) {
          return;
        }
        setDetailsCollapsed(detailsPanelRef.current?.isCollapsed() ?? false);
        // Remembered as the person drags, so the width survives a reload. Rounded: a stored
        // 21.7318% is noise that makes every save look like a change.
        if (typeof size === 'number' && size > 0) {
          setPanelPrefs('main', { width: Math.round(size) });
        }
      }}
    >
      <div {...paneRoot('details', 'workspace-dock')}>
        {rightDock}
        {restoreFloat('details', t('map.detailsTitle'))}
      </div>
    </StablePanel>
  ) : null;

  const dockPanelOn = (side: 'left' | 'right') =>
    dockOnSide(side) === 'layers' ? layersDockPanel : detailsDockPanel;

  // A pane beside the map or in the strip under it is a slot its content's element is shown in.
  // Keyed by place, so a move mounts a new slot — a new panel the layout library sizes afresh —
  // while what is drawn in it is never touched.
  const scene3dSlot = (place: PanePlace) => (
    <StablePanel
      key={`scene3d-${place}`}
      id={`ws-scene3d-${place}`}
      defaultSize={place === 'bottom' ? undefined : '34%'}
      minSize="20%"
      className="map-workspace-panel"
    >
      <PaneSlot element={scenePaneElement} />
    </StablePanel>
  );
  const caveViewSlot = (place: PanePlace) => (
    <StablePanel
      key={`caveview-${place}`}
      id={`ws-caveview-${place}`}
      defaultSize={place === 'bottom' ? undefined : '30%'}
      minSize="20%"
      className="map-workspace-panel"
    >
      <PaneSlot element={caveViewPaneElement} />
    </StablePanel>
  );

  // Panels with a separator between each pair, keyed by the pair, so a separator is made afresh
  // whenever what it stands between changes and the layout library never resizes a stale pair.
  const withSeparators = (panels: ReactElement[], separatorClass: string, label: string): ReactElement[] =>
    panels.flatMap((panel, index) => {
      if (index === 0) {
        return [panel];
      }
      const before = panels[index - 1];
      const nextToDetails = before.key?.startsWith('dock-details') || panel.key?.startsWith('dock-details');
      return [
        <Separator
          key={`sep:${before.key}|${panel.key}`}
          className={separatorClass}
          aria-label={label}
          inert={maximised !== null ? true : undefined}
          onDoubleClick={nextToDetails ? snapDetailsWidth : undefined}
        />,
        panel,
      ];
    });

  // Under the map: the table, then whichever of the two drawing panes was sent there.
  const bottomPanels: ReactElement[] = [
    ...(tableShown
      ? [
          <StablePanel key="table" id="ws-table" minSize="20%" className="map-workspace-panel">
            <PaneSlot element={tablePaneElement} />
          </StablePanel>,
        ]
      : []),
    ...(sceneShown && arrangement.scene3dPlace === 'bottom' ? [scene3dSlot('bottom')] : []),
    ...(caveViewShown && arrangement.caveViewPlace === 'bottom' ? [caveViewSlot('bottom')] : []),
  ];
  const bottomShown = bottomPanels.length > 0;

  const mapColumn = (
    <Panel key="center" id="ws-center" minSize="30%">
      <Group orientation="vertical" className="map-workspace-column">
        <Panel id="ws-map" minSize="30%">
          <div
            {...paneRoot(
              'map',
              [
                'map-canvas-wrap',
                mapChromeHidden ? 'map-chrome-hidden' : '',
                isMobile ? 'map-canvas-wrap-mobile' : '',
              ]
                .filter(Boolean)
                .join(' '),
            )}
          >
            {/* Focusable, because OpenLayers listens for its keyboard pan and zoom on this element
                and nowhere else: without a place in the tab order the map could only be moved with
                a pointer. An application to assistive technology, so that a screen reader hands the
                arrow keys to the map instead of using them to read the page. */}
            <div
              ref={mapTarget}
              id={MAP_CANVAS_ID}
              className="map-canvas"
              data-testid="map-canvas"
              tabIndex={0}
              role="application"
              aria-label={t('a11y.map')}
            />
            {/* Search is the primary action on a phone: it gets the width the pop-out
                buttons no longer need. The selector sits beside it — search asks what mentions
                these words, the selector asks which of these things you mean — and stacks under
                it on a phone, where they cannot both have the strip. */}
            <div className={`map-search-overlay map-chrome${isMobile ? ' map-search-overlay-mobile' : ''}`}>
              <MapSearch fullWidth={isMobile} />
              <MapObjectSelector fullWidth={isMobile} />
              {/* A row of its own under search: on the first row the button ran on past the
                  selector, under the pop-out buttons pinned at the top right, which then took
                  every press meant for it. */}
              <div className="map-filter-row">
                <MapListInViewButton filter={mapFilter} />
                {/* The account's pinned saved filters: one click narrows the main entrance and
                    feature layers to one of them, a second click lifts it again. */}
                <QuickFilterChips
                  className="map-quick-filters"
                  activeId={mapFilter?.kind === 'saved' ? mapFilter.savedFilterId : null}
                  onApply={(filter) => adoptMapFilter({ kind: 'saved', savedFilterId: filter.id })}
                  onRelease={() => adoptMapFilter(null)}
                  data-testid="map-quick-filters"
                />
              </div>
              <MapFilterNotice
                filter={mapFilter}
                unreadableLink={unreadableFilterLink}
                onDismissUnreadableLink={() => setUnreadableFilterLink(false)}
                refused={filterRefused}
                onClear={() => adoptMapFilter(null)}
              />
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
            {/* The bottom-left corner holds a column: the survey-line notice over the scale box,
                over OpenLayers' own scale line. One column rather than two boxes each placed by a
                guess at the other's height, so the notice moves up when the scale box grows — it
                does, by a line, when a typed scale is refused — and never lies over it; its width
                stops short of the background chooser and the attribution in the opposite corner.
                The notice comes after the map in the tab order, beside the dock toggles it is a
                shortcut for, so it never lengthens the way from the page start to the map. */}
            <div className={`map-corner-stack${isMobile ? ' map-corner-stack-mobile' : ''}`}>
              <CenterlineWithheldNotice
                centerlinesVisible={centerlinesVisible}
                panelHidden={layersHidden}
                chromeHidden={mapChromeHidden}
                onOpenPanel={openLayerPanelAtCenterlines}
              />
              {/* Jump-to-scale is niche clutter at phone size; OL's own scale line stays. */}
              {!isMobile && (
                <div className="map-scale-overlay map-chrome">
                  <ScaleBox map={getWorkspaceMap()} />
                </div>
              )}
            </div>
            {baseOlLayers.length > 0 && (
              <BaseLayerChooser
                layers={baseOlLayers}
                backgroundLayerFilter={(l) => getBaseLayerId(l) !== undefined}
                buttonTooltip={t('map.changeBaseLayer')}
              />
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
                    aria-label={t('map.swipeCompare')}
                    aria-pressed={swipeActive}
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
                aria-label={t('map.locateMe')}
                showMarker
                follow
                enableTracking
                onError={() => message.error(t('map.geolocationFailed'))}
              />
              {/* Pop-out windows are meaningless on a phone; the 3D viewer stays reachable
                  from the cave detail page. */}
              {!isMobile && (
                <>
                  <WorkspaceMenu />
                  <Tooltip title={`${mapMaximiseLabel} (M)`}>
                    <Button
                      size="small"
                      type={maximised === 'map' ? 'primary' : 'default'}
                      icon={maximised === 'map' ? <FullscreenExitOutlined /> : <FullscreenOutlined />}
                      aria-label={mapMaximiseLabel}
                      aria-pressed={maximised === 'map'}
                      aria-keyshortcuts="M"
                      onClick={() => toggleMaximised('map')}
                      data-testid="map-maximise"
                    />
                  </Tooltip>
                  <Tooltip title={scene3dOpen ? t('map.hideScene3d') : t('map.showScene3d')}>
                    <Button
                      size="small"
                      type={scene3dOpen ? 'primary' : 'default'}
                      icon={<GlobalOutlined />}
                      aria-label={scene3dOpen ? t('map.hideScene3d') : t('map.showScene3d')}
                      aria-pressed={scene3dOpen}
                      onClick={() => setScene3dOpen(!scene3dOpen)}
                      data-testid="map-scene3d-toggle"
                    />
                  </Tooltip>
                  <Tooltip title={t('panel.popOut')}>
                    <Button
                      size="small"
                      icon={<ExportOutlined />}
                      aria-label={t('panel.popOut')}
                      onClick={() => window.open('/panel/registry', 'silexgis-registry', 'popup,width=900,height=700')}
                      data-testid="map-popout-registry"
                    />
                  </Tooltip>
                  <Tooltip title={t('panel.popOutScene3d')}>
                    <Button
                      size="small"
                      icon={<SelectOutlined />}
                      aria-label={t('panel.popOutScene3d')}
                      onClick={() => window.open('/panel/scene3d', 'silexgis-scene3d', 'popup,width=1100,height=800')}
                      data-testid="map-popout-scene3d"
                    />
                  </Tooltip>
                  <Tooltip title={t('panel.popOutSelection')}>
                    <Button
                      size="small"
                      icon={<ProfileOutlined />}
                      aria-label={t('panel.popOutSelection')}
                      onClick={() =>
                        window.open('/panel/selection', 'silexgis-selection', 'popup,width=520,height=900')}
                      data-testid="map-popout-selection"
                    />
                  </Tooltip>
                  <Tooltip title={t('panel.popOut3d')}>
                    <Button
                      size="small"
                      icon={<CodeSandboxOutlined />}
                      aria-label={t('panel.popOut3d')}
                      onClick={() => window.open('/panel/viewer3d', 'silexgis-viewer3d', 'popup,width=1000,height=750')}
                      data-testid="map-popout-viewer3d"
                    />
                  </Tooltip>
                  <Tooltip title={t('shortcuts.open')}>
                    <Button
                      size="small"
                      icon={<QuestionCircleOutlined />}
                      aria-label={t('shortcuts.title')}
                      onClick={() => setShortcutHelpOpen(true)}
                      data-testid="map-shortcut-help"
                    />
                  </Tooltip>
                </>
              )}
            </div>
            {toolbarShown && editController && (
              <div className={`map-edit-overlay map-chrome${isMobile ? ' map-edit-overlay-mobile' : ''}`}>
                <EditToolbar controller={editController} mayModify={mayReshapeSaved} rightsPending={rightsPending} />
              </div>
            )}
            <MapContextMenu
              target={contextTarget}
              featureTypes={(featureTypes ?? []).filter(isGenericFeatureType)}
              canEdit={canEdit}
              onClose={() => {
                // A menu the keyboard opened hands focus back to the map it was opened from; a
                // dialog an item opens takes it from there, and gives it back to the map in turn.
                if (contextTarget?.viaKeyboard) {
                  getWorkspaceMap().getTargetElement()?.focus();
                }
                setContextTarget(null);
              }}
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
        {bottomShown && (
          <Separator
            className="map-workspace-handle map-workspace-handle-row"
            aria-label={t('workspace.resizeBottom')}
            inert={maximised !== null ? true : undefined}
          />
        )}
        {bottomShown && (
          <StablePanel
            id="ws-bottom"
            defaultSize={`${arrangement.bottomSize ?? 35}%`}
            minSize={`${BOTTOM_SIZE_MIN}%`}
            maxSize={`${BOTTOM_SIZE_MAX}%`}
            className="map-workspace-panel"
            onResize={(size) => {
              // Remembered as it is dragged, rounded for the same reason the details width is.
              const percent = Math.round(size.asPercentage);
              const now = useUiPrefsStore.getState().workspace ?? DEFAULT_ARRANGEMENT;
              if (percent >= BOTTOM_SIZE_MIN && percent <= BOTTOM_SIZE_MAX && percent !== now.bottomSize) {
                setWorkspace({ ...now, bottomSize: percent });
              }
            }}
          >
            <Group orientation="horizontal" className="map-workspace-bottom">
              {withSeparators(bottomPanels, 'map-workspace-handle', t('workspace.resizePanes'))}
            </Group>
          </StablePanel>
        )}
      </Group>
    </Panel>
  );

  // The workspace's row, left to right: a dock, the panes sent left (the 3D scene nearest the
  // map), the map's column, the panes sent right (the 3D scene nearest the map again), a dock.
  const row: ReactElement[] = isMobile
    ? [mapColumn]
    : [
        ...[dockPanelOn('left')].filter((panel): panel is ReactElement => panel !== null),
        ...(caveViewShown && arrangement.caveViewPlace === 'left' ? [caveViewSlot('left')] : []),
        ...(sceneShown && arrangement.scene3dPlace === 'left' ? [scene3dSlot('left')] : []),
        mapColumn,
        ...(sceneShown && arrangement.scene3dPlace === 'right' ? [scene3dSlot('right')] : []),
        ...(caveViewShown && arrangement.caveViewPlace === 'right' ? [caveViewSlot('right')] : []),
        ...[dockPanelOn('right')].filter((panel): panel is ReactElement => panel !== null),
      ];

  // Which panels the row and the strip hold. When that changes the layout library lays the row out
  // afresh — from the sizes it last had for this same set of panels, or from their starting sizes —
  // and reports each dock's new size as though somebody had dragged it, so a folded dock would come
  // back unfolded and its being folded would be forgotten. For the frames that takes, a dock's report
  // is not taken as the person's choice; afterwards each dock is put back as the stored state says.
  const panelSetKey = [...row, ...bottomPanels].map((panel) => panel.key).join('|');
  if (panelSetRef.current !== panelSetKey) {
    panelSetRef.current = panelSetKey;
    relayingOutRef.current = true;
  }
  useEffect(() => {
    let second = 0;
    const first = requestAnimationFrame(() => {
      second = requestAnimationFrame(() => {
        relayingOutRef.current = false;
        const stored = useUiPrefsStore.getState().docks;
        for (const [panel, hidden] of [
          [layersPanelRef.current, stored.leftHidden],
          [detailsPanelRef.current, stored.rightHidden],
        ] as const) {
          if (panel && panel.isCollapsed() !== hidden) {
            if (hidden) {
              panel.collapse();
            } else {
              panel.expand();
            }
          }
        }
      });
    });
    return () => {
      cancelAnimationFrame(first);
      cancelAnimationFrame(second);
    };
  }, [panelSetKey, layersPanelRef, detailsPanelRef]);

  const scenePane = (
    <div {...paneRoot('scene3d', 'workspace-pane')}>
      <PaneHeader
        title={t('workspace.scene3dName')}
        name={t('workspace.scene3dName')}
        testId="workspace-scene3d"
        place={arrangement.scene3dPlace}
        onMove={moveScene3d}
        maximised={maximised === 'scene3d'}
        onToggleMaximised={() => toggleMaximised('scene3d')}
        onClose={() => setScene3dOpen(false)}
      />
      <div className="workspace-pane-body">
        <Suspense fallback={<Spin style={{ margin: 48 }} />}>
          <Scene3DView />
        </Suspense>
      </div>
    </div>
  );

  const caveViewPane = (
    <div {...paneRoot('caveview', 'workspace-pane map-caveview-pane')}>
      <PaneHeader
        title={caveViewModel?.name ?? t('workspace.surveyName')}
        name={t('workspace.surveyName')}
        testId="map-caveview"
        place={arrangement.caveViewPlace}
        onMove={moveCaveView}
        mayPopOut={caveViewModel !== undefined}
        maximised={maximised === 'caveview'}
        onToggleMaximised={() => toggleMaximised('caveview')}
        onClose={closeCaveView}
        extra={
          caveViewModel && (
            <>
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
            </>
          )
        }
      />
      <div className="map-caveview-body">
        {caveViewModel ? (
          <Suspense fallback={<Spin style={{ margin: 48 }} />}>
            <CaveViewPanel
              fileUrl={caveViewModel.modelUrl}
              fileName={viewerFileName(caveViewModel)}
              height="100%"
              surveyModelId={caveViewModel.id}
              compare={caveViewCompare}
            />
          </Suspense>
        ) : (
          <SurveyModelChooser onOpen={setCaveViewModelId} />
        )}
      </div>
    </div>
  );

  const tablePane = (
    <div {...paneRoot('table', 'workspace-pane')}>
      <BottomTable
        maximised={maximised === 'table'}
        onToggleMaximised={() => toggleMaximised('table')}
        onClose={() => setWorkspace({ ...arrangement, table: false })}
      />
    </div>
  );

  return (
    <MapContext.Provider value={getWorkspaceMap()}>
    {/* The map's column is keyed and never moves out of this row, so React keeps its DOM — and
        with it the OL target the map was attached to — across a viewport crossing the breakpoint
        and every pane coming, going or moving around it. Panel sizes: bare numbers mean pixels
        in react-resizable-panels v4 — use percent strings. */}
    <Group orientation="horizontal" className="map-workspace" elementRef={workspaceGroupRef}>
      {withSeparators(row, 'map-workspace-handle', t('workspace.resizePanes'))}
    </Group>
    {/* The panes that must survive a move, drawn here — in one place in the tree whatever slot is
        showing them — and only while they are up, so a drawing context exists exactly as long as
        its pane does: a collapsed pane would keep one alive at zero width. */}
    {sceneShown && createPortal(scenePane, scenePaneElement)}
    {caveViewShown && createPortal(caveViewPane, caveViewPaneElement)}
    {tableShown && createPortal(tablePane, tablePaneElement)}
    {/* The pane's model over the whole window. Mounted here rather than reached for on the cave
        page, because a reader who arrived at the map from a link has no cave page open. */}
    <ShortcutHelpDialog open={shortcutHelpOpen} onClose={() => setShortcutHelpOpen(false)} />
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
        placement={sideOfDock('details')}
        open={!detailsCollapsed}
        onClose={() => setDetailsCollapsed(true)}
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

/** Which of the two docks a side holds. */
type DockContent = 'layers' | 'details';

/**
 * A panel whose starting size is taken once, when it mounts. The layout library re-registers a
 * panel whose `defaultSize` changes, which re-lays the whole row; a starting size read from stored
 * preferences changes whenever the person drags, so it is frozen here at the value it had when the
 * panel appeared — afterwards the panel itself is what is sized, and reports back.
 */
function StablePanel({ defaultSize, ...props }: PanelProps) {
  const [initial] = useState(defaultSize);
  return <Panel {...props} defaultSize={initial} />;
}
