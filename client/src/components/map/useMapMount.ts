// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { unByKey } from 'ol/Observable';
import { attachHoverTooltip } from '../../map/hoverTooltip.ts';
import { attachUrlHash } from '../../map/urlHash.ts';
import { attachGeofilePopup } from '../../map/geofilePopup.ts';
import {
  CENTERLINE_LAYER_ID,
  attachCenterlineLoader,
  createCenterlineLayer,
} from '../../map/centerlineLayer.ts';
import { ENTRANCE_LAYER_ID, attachEntranceLoader, createEntranceLayer } from '../../map/entranceLayer.ts';
import {
  SURFACE_FEATURE_LAYER_ID,
  attachSurfaceFeatureLoader,
  createSurfaceFeatureLayer,
} from '../../map/featureLayer.ts';
import {
  CLOSEST_APPROACH_LAYER_ID,
  attachClosestApproachLine,
  createClosestApproachLayer,
} from '../../map/closestApproachLayer.ts';
import {
  OVERBURDEN_HIGHLIGHT_LAYER_ID,
  attachOverburdenHighlight,
  createOverburdenHighlightLayer,
} from '../../map/overburdenHighlightLayer.ts';
import {
  attachRowHighlight,
  createRowHighlightLayer,
  ROW_HIGHLIGHT_LAYER_ID,
} from '../../map/rowHighlightLayer.ts';
import { attachTemporaryMarker } from '../../map/temporaryMarker.ts';
import { ENTRANCE_HEATMAP_LAYER_ID, createEntranceHeatmapLayer } from '../../map/heatmapLayer.ts';
import {
  KARST_DENSITY_LAYER_ID,
  attachKarstDensityLoader,
  createKarstDensityLayer,
} from '../../map/karstDensityLayer.ts';
import {
  KARST_INDEX_LAYER_ID,
  attachKarstIndexLoader,
  createKarstIndexLayer,
} from '../../map/karstIndexLayer.ts';
import { attachGeofileLoader } from '../../map/geofileLayers.ts';
import { PHOTO_LAYER_ID, attachPhotoLoader, createPhotoLayer } from '../../map/photoLayer.ts';
import { TRIP_LAYER_ID, attachTripLoader, createTripLayer } from '../../map/tripLayer.ts';
import { attachPhotoPopup } from '../../map/photoPopup.ts';
import { attachLibraryPhotoLoader } from '../../map/libraryPhotoLayer.ts';
import { attachLibraryPhotoPopup } from '../../map/libraryPhotoPopup.ts';
import { attachViewSync2d, type ViewSync2dHandle } from '../../map/viewSync2d.ts';
import { setActiveViewCamera } from '../../workspace/viewCamera.ts';
import {
  findOverlayLayer,
  fitGeoJsonGeometry,
  flyTo,
  getOverlayGroup,
  getWorkspaceMap,
} from '../../map/mapContext.ts';
import { attachContextMenu, type MapContextMenuTarget } from '../../map/contextMenu.ts';
import { MapEditController } from '../../map/mapEdit.ts';
import { attachSelection } from '../../map/selection.ts';
import { useWorkspacePanesStore } from '../../stores/workspacePanesStore.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

/**
 * Mounts the shared workspace map onto the canvas element this returns a ref for: creates the
 * built-in overlays in their default stacking, attaches every loader, popup and interaction, and
 * undoes all of it when the page goes away. The state the attachments report into (the edit
 * controller, the context-menu target and the 2D end of the 3D sync) lives here, so the effect's
 * dependency list is only the selection setter.
 */
export function useMapMount() {
  const mapTarget = useRef<HTMLDivElement>(null);
  const syncRef = useRef<ViewSync2dHandle | null>(null);
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const scene3dOpen = useWorkspacePanesStore((s) => s.scene3dOpen);
  const [editController, setEditController] = useState<MapEditController | null>(null);
  // Right-click (long-press on touch) context menu over the canvas.
  const [contextTarget, setContextTarget] = useState<MapContextMenuTarget | null>(null);

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
    // A link from the coordinate converter asks for one throwaway mark on a place.
    const detachTemporaryMarker = attachTemporaryMarker(map);
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
      detachTemporaryMarker();
      detachContextMenu();
      unByKey(moveKey);
      map.setTarget(undefined);
    };
  }, [setSelection]);

  // A scene opening beside the map starts on the ground the map is showing rather than on its own
  // default view of the Carpathians. It is said once, when the pane opens, rather than being
  // republished on a timer: the scene subscribes as it starts, and the sync remembers the last
  // thing said so a view that arrives afterwards still hears it.
  useEffect(() => {
    if (scene3dOpen) {
      syncRef.current?.announce();
    }
  }, [scene3dOpen]);

  return { mapTarget, syncRef, editController, contextTarget, setContextTarget };
}
