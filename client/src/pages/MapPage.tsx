// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import MapContext from '@terrestris/react-util/dist/Context/MapContext/MapContext';
import { Drawer, Tabs } from 'antd';
import { useSelectionInUrl } from '../components/map/useSelectionInUrl.ts';
import { useFeatureRights } from '../api/hooks/objectRights.ts';
import type BaseLayer from 'ol/layer/Base';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Group, usePanelRef } from 'react-resizable-panels';
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
  useTerrainDerivatives,
  useSurveyModel,
  type SurveyModelInfo,
} from '../api/hooks.ts';
import { useIsMobile } from '../hooks/useIsMobile.ts';
import { useEditScope } from '../components/map/useEditScope.ts';
import { useOverviewMap } from '../components/map/useOverviewMap.ts';
import FeatureListPanel from '../components/map/FeatureListPanel.tsx';
import LayerPanel from '../components/map/LayerPanel.tsx';
import ViewsPanel from '../components/map/ViewsPanel.tsx';
import SelectionPanel from '../components/map/SelectionPanel.tsx';
import { setActiveBaseLayer } from '../map/baseLayers.ts';
import { CENTERLINE_LAYER_ID } from '../map/centerlineLayer.ts';
import { ENTRANCE_LAYER_ID } from '../map/entranceLayer.ts';
import { setEntranceSymbols } from '../map/entranceSymbols.ts';
import {
  SURFACE_FEATURE_LAYER_ID,
  setFeatureTypeSymbols,
  setSelectedSurfaceFeature,
} from '../map/featureLayer.ts';
import { ENTRANCE_HEATMAP_LAYER_ID } from '../map/heatmapLayer.ts';
import {
  KARST_DENSITY_LAYER_ID,
  type KarstDensityBands,
  type KarstDensityMode,
} from '../map/karstDensityLayer.ts';
import { KARST_INDEX_LAYER_ID, setKarstIndexSelected } from '../map/karstIndexLayer.ts';
import { GEOFILE_LAYER_PREFIX } from '../map/geofileLayers.ts';
import { PHOTO_LAYER_ID } from '../map/photoLayer.ts';
import { TRIP_LAYER_ID, type TripLayerFilter } from '../map/tripLayer.ts';
import { libraryPhotoSourceOf } from '../map/libraryPhotoLayer.ts';
import { setLibraryPhotoFeatureHandler, type LibraryPhotoFeatureTarget } from '../map/libraryPhotoPopup.ts';
import { tagChipFilter, tagOfMapFilter } from '../map/mapFilters.ts';
import { applyViewConfig, captureViewConfig } from '../map/viewConfig.ts';
import {
  addFilterLayer,
  filterLayerCandidates,
  filterLayerSavedIdOf,
  removeFilterLayer,
  updateFilterLayer,
} from '../map/filterLayerList.ts';
import { useCompareOffer } from '../caveview/useCompareOffer.ts';
import { hasScene3dHash } from '../scene3d/urlHash3d.ts';
import { applyViewCamera3d } from '../workspace/viewCamera.ts';
import { subscribe } from '../workspace/workspaceBus.ts';
import { RASTER_LAYER_PREFIX } from '../map/rasterLayers.ts';
import { hasMapHash } from '../map/urlHash.ts';
import {
  APPROXIMATE_MAX_ZOOM,
  fitGeoJsonGeometry,
  flyTo,
  getWorkspaceMap,
  setDesiredOverlayOrder,
} from '../map/mapContext.ts';
import ShortcutHelpDialog from '../components/ShortcutHelpDialog.tsx';
import { geometryFor, isGeographic } from '../viewlinks/geoTargets.ts';
import { useViewControl } from '../viewlinks/useViewControl.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { useWorkspacePanesStore } from '../stores/workspacePanesStore.ts';
import { DEFAULT_ARRANGEMENT } from '../stores/workspacePrefs.ts';
import { useUiDefaults } from '../api/hooks/me.ts';
import { usePaneElement } from '../components/map/workspace/paneElement.ts';
import { applyWorkspaceSnapshot } from '../components/map/workspace/workspaceActions.ts';
import { installationPresetOf } from '../components/map/workspace/workspacePresets.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import { useMapMount } from '../components/map/useMapMount.ts';
import { useOverlayLayerSync } from '../components/map/useOverlayLayerSync.ts';
import { useOverlayVisibility } from '../components/map/useOverlayVisibility.ts';
import { useBaseLayers } from '../components/map/useBaseLayers.ts';
import { usePhotoLibraryOverlays } from '../components/map/usePhotoLibraryOverlays.ts';
import { useRasterSwipe } from '../components/map/useRasterSwipe.ts';
import { useMapFilterState } from '../components/map/useMapFilterState.ts';
import { useFilterLayers } from '../components/map/useFilterLayers.ts';
import { useWorkspaceDocks } from '../components/map/workspace/useWorkspaceDocks.tsx';
import { useWorkspaceRow } from '../components/map/workspace/useWorkspaceRow.tsx';
import { useMapUrlRequests } from '../components/map/useMapUrlRequests.ts';
import { useMapShortcuts } from '../components/map/useMapShortcuts.ts';
import './MapPage.css';

// Loaded on demand: it is a survey-viewer bundle that most visits to the map never open.
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
  const isMobile = useIsMobile();
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
  const { data: layers } = useMapLayers();
  const { data: mapConfig } = useMapConfig();
  // The installation's entrance symbols arrive with the map's configuration; the layer restyles
  // itself when they change.
  useEffect(() => {
    if (mapConfig) {
      setEntranceSymbols(mapConfig);
    }
  }, [mapConfig]);
  // Asked of the photo-library slice itself rather than of the map config: most of that
  // slice's routes are not map routes, and keeping the question out of the map endpoints is
  // what keeps the whole feature removable by deleting its directories.
  const { data: libraryStatus } = usePhotoLibraries();
  const { data: featureTypes } = useFeatureTypes();
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
  const { mapTarget, syncRef, editController, contextTarget, setContextTarget } = useMapMount();
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

  useOverlayLayerSync({
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
  });

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

  const { mapFilter, adoptMapFilter, unreadableFilterLink, setUnreadableFilterLink, filterRefused } =
    useMapFilterState(searchParams, setSearchParams);

  const { filterLayers, setFilterLayers, setRefusedFilterLayers, savedFilters, filterLayerRows } =
    useFilterLayers(overlayOpacity);

  useMapUrlRequests({
    searchParams, setSearchParams, requestedTrips, requestedModelId, requestedAreaId, setTripFilter,
    setUnappliedTripFilters, setTripsVisible, setCaveViewModelId,
  });

  // A model the reader may not see answers 404 rather than an empty result — a cave's models are
  // its location — so there is nothing to report and nothing to draw, and the pane simply does not
  // open. The query does not retry, so a withheld model costs one request.
  const { data: caveViewModel } = useSurveyModel(caveViewModelId ?? undefined);
  // The same cave's other line plots, for looking at this one together with another.
  const caveViewCompare = useCompareOffer(caveViewModel);

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

  const { activeBaseId, setActiveBaseId, baseOlLayers } = useBaseLayers(layers, language, baseOpacity);

  // The overview inset, drawn from whichever base layer the map shows. Not on a phone, whose
  // screen it would cover a good part of; open or shut as the account's preference says.
  useOverviewMap(getWorkspaceMap(), baseOlLayers, !isMobile, t('map.overviewMap'));

  // What is selected travels in the address, and an address can say what to select.
  useSelectionInUrl();


  useOverlayVisibility({
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
  });
  const { photoLibraries, suspendedPhotoLibraries, unconfiguredPhotoLibraries } = usePhotoLibraryOverlays(
    libraryStatus,
    libraryPhotoSources,
  );

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

  const { swipeActive, setSwipeActive, swipePos, onSwipeHandleDown } = useRasterSwipe(visibleRasterIds);

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

  const dockApi = useWorkspaceDocks({
    t, isMobile, arrangement, layersCollapsed, detailsCollapsed, setDetailsCollapsed, leftDrawerOpen,
    rightDrawerOpen, setLeftDrawerOpen, setRightDrawerOpen, layersPanelRef, detailsPanelRef,
    workspaceGroupRef, caveViewModel, caveViewPaneOpen, scene3dOpen, maximised, setMaximised,
    rightPinned, setWorkspace, setScene3dOpen, setCaveViewModelId, setCaveViewPaneOpen,
  });
  const {
    swapped, sideOfDock, leftHidden, rightHidden, layersHidden, toggleLeftDock, toggleRightDock,
    openLayerPanelAtCenterlines, caveViewShown, tableShown, sceneShown, toggleMaximised, paneRoot,
  } = dockApi;

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

  useMapShortcuts({
    isMobile, layersCollapsed, detailsCollapsed, leftDrawerOpen, rightDrawerOpen, swapped, rightPinned,
    setShortcutHelpOpen, setSelection, toggleLeftDock, toggleRightDock,
  });

  const { row, withSeparators, scenePane, caveViewPane, tablePane } = useWorkspaceRow({
    t, dockApi, isMobile, arrangement, maximised, docks, rightPinned, rightWidth, setPanelPrefs,
    setWorkspace, setScene3dOpen, setLayersCollapsed, setDetailsCollapsed, layersPanelRef,
    detailsPanelRef, relayingOutRef, panelSetRef, layerDock, rightDock, scenePaneElement,
    caveViewPaneElement, tablePaneElement, caveViewModel, caveViewCompare, setCaveViewOverlay,
    setCaveViewModelId,
    mapCanvasProps: {
      paneRoot,
      toggleMaximised,
      isMobile,
      mapTarget,
      mapFilter,
      adoptMapFilter,
      unreadableFilterLink,
      onDismissUnreadableLink: () => setUnreadableFilterLink(false),
      filterRefused,
      editDirty,
      leftHidden,
      rightHidden,
      layersHidden,
      toggleLeftDock,
      toggleRightDock,
      centerlinesVisible,
      openLayerPanelAtCenterlines,
      baseOlLayers,
      swipeActive,
      swipePos,
      onSwipeHandleDown,
      onToggleSwipe: () => setSwipeActive((v) => !v),
      hasVisibleRasters: visibleRasterIds.length > 0,
      onOpenShortcutHelp: () => setShortcutHelpOpen(true),
      mayReadTerrain,
      toolbarShown,
      editController,
      mayReshapeSaved,
      rightsPending,
      contextTarget,
      onContextTargetChange: setContextTarget,
      featureTypes,
      canEdit,
    },
  });

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
