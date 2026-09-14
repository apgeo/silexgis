// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  CAVEVIEW_HOME,
  focusNamedNothing,
  loadCaveView,
  makeCrsLookup,
  type CaveViewStationMediaSource,
  type CaveViewToolbar,
  type CaveViewToolbarOptions,
  type CaveViewUi,
  type CaveViewer,
} from '../../caveview/loadCaveView.ts';
import {
  focusForRef,
  partFromLeg,
  partFromStation,
  type PickedModelPart,
} from '../../caveview/modelParts.ts';
import { mediaForStation } from '../../caveview/stationMedia.ts';
import { caveViewToolbarButtons } from '../../caveview/toolbarButtons.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import type { ResourceRef } from '../../viewlinks/resourceRef.ts';
import { useViewControl } from '../../viewlinks/useViewControl.ts';
import CaveViewTrackingOverlay, { type TrackedPlace } from './CaveViewTrackingOverlay.tsx';
import './CaveViewPanel.css';

/**
 * A part of the model to fly to, asked for by whoever mounted the panel.
 *
 * Separate from the link bus this panel already answers, and needed because that bus is a
 * workspace: it is joined by every view in the window, it addresses controls by a roster somebody
 * chooses from, and none of that exists on a page with no workspace around it. The published
 * embed is one viewer in an iframe being driven by the article it sits in, so it hands the panel
 * the place directly.
 *
 * <b>A new object is what asks.</b> The effect that answers watches this by identity, so a caller
 * that wants the camera moved to where it already is — a reader pressing the same link twice —
 * passes a fresh object; one that re-renders for its own reasons passes the same one and the
 * camera stays where the reader left it.
 */
export interface CaveViewFocusRequest {
  kind: 'station' | 'survey';
  /** The dotted path the viewer itself uses, as authored. */
  ref: string;
  /** Told whether the loaded model turned out to hold it. */
  onSettled?: (found: boolean) => void;
}

export interface CaveViewPanelProps {
  /** Delivery URL of the survey file (carries its own access token, no auth header). */
  fileUrl: string;
  /**
   * File name ending in the format extension (.lox or .3d) — CaveView selects its parser
   * from the name, not the URL, so signed URLs without extensions work fine.
   */
  fileName: string;
  /** CSS height of the viewer surface. */
  height?: number | string;
  /** Fired with the survey's entrance label when one is clicked in the 3D scene. */
  onEntrancePick?: (displayName: string) => void;
  /**
   * Fired when a station or a leg is clicked and can be named — the caller then decides what to
   * offer. Absent means clicks do only what the viewer does with them.
   *
   * A pick that cannot be named produces nothing rather than an empty offer: a splay's far end
   * was never a station, and an anchor naming one end and inventing the other would read as
   * exact while pointing at nothing.
   */
  onPartPick?: (part: PickedModelPart) => void;
  /**
   * Which survey model this panel is showing, when the caller knows.
   *
   * Supplied only so the panel can answer links: a passage that names a station of *this* model
   * is something it can show, and a passage that names another cave's model is not. Without it
   * the panel still works and simply never volunteers to answer one, which is the right default
   * — a viewer that accepted every link would jump to a station name that happens to exist in
   * whatever cave it has open.
   */
  surveyModelId?: string;
  /**
   * Who is underground and where they were last reported, already resolved against this model.
   *
   * Resolved by the caller rather than read here: the watch carries caver ids and nothing on it
   * knows what anybody is called — the trip's roster does — and the panel is mounted by three
   * pages, only one of which has a trip at all. Absent means no markers and no chrome for them.
   */
  trackedCavers?: readonly TrackedCaver[];
  /**
   * Opts into the viewer's own row of controls, placed over the model.
   *
   * `true` takes the default buttons, which are chosen by how much room there is across *and* by
   * what is pointing at them: the full set does not fit on a phone at all, and it does not fit on
   * any screen once every control in it has been grown to the size a finger can land on. A set that
   * does not fit is not a row with its end off the screen — the bar wraps — it is a second row of
   * controls over the model. A caller that has to take one control out asks
   * {@link caveViewToolbarButtons} for the same set and subtracts from it.
   */
  toolbar?: boolean | CaveViewToolbarOptions;
  /**
   * Pictures to show over the model for the station the pointer rests on — and, where there is no
   * pointer that can rest on anything, for the station a finger taps.
   *
   * May arrive late: the setting that makes the viewer follow the pointer over stations is applied
   * to the loaded viewer rather than asked for when it is built, so a caller still fetching its
   * pictures may pass nothing and pass them when they arrive.
   */
  stationMedia?: CaveViewStationMediaSource;
  /**
   * Where the viewer resolves a survey's coordinate system, when the default will not do.
   *
   * A Survex file names its coordinate system by code and the viewer resolves that code by asking
   * for a definition; left alone, this panel points it at this installation's own registry, which
   * is an authenticated route. That is right for every signed-in mount and wrong for exactly one:
   * a page somebody without an account is reading, where the route answers 401 and the survey
   * loads unreferenced. Such a page is handed the definition in its own response and supplies a
   * lookup that answers from it — which is why the option exists on the viewer at all.
   *
   * Read once, when the viewer is built. A caller that changes it afterwards changes nothing until
   * the model is loaded again, which is the same contract the `home` option has.
   */
  crsLookup?: (code: string) => Promise<string | null>;
  /**
   * A place to fly the camera to, asked for from outside. See {@link CaveViewFocusRequest}.
   */
  focusRequest?: CaveViewFocusRequest;
}

// CaveView addresses its container by element id; keep ids unique across remounts and
// multiple simultaneous panels (main window + pop-outs).
let panelSequence = 0;

/**
 * A station's pictures, opened by the tap that picked it.
 *
 * <b>A finger cannot hover, and the strip is a hover.</b> The viewer follows the pointer to decide
 * which station's pictures to show, and a stationary tap moves no pointer — so on a touch screen
 * that strip is unreachable, and would stay unreachable however large its thumbnails were made.
 * What a tap does produce is the pick this panel already listens for, and focusing a station is the
 * documented way to show its strip without a pointer: it centres the station, which is also what
 * keeps the strip on screen, since the strip is only drawn while its station is in view.
 *
 * Three things are deliberate. <b>Only a pointer that cannot hover takes this path</b>, so a mouse
 * click keeps meaning exactly what it meant. <b>A station with no pictures is left alone</b> rather
 * than focused for nothing, because the camera move is the cost of asking. And <b>tapping the same
 * station again takes the strip off</b>, which is the only way to dismiss one where no pointer will
 * ever leave the station: clearing the source and setting it again is what removes the strip
 * itself, and the source is put straight back so the next tap still has pictures to find.
 */
function showPicturesForTap(
  viewer: CaveViewer,
  source: CaveViewStationMediaSource | undefined,
  shown: { current: string | null },
  station: { node: unknown; path: string; pointerType: string | undefined },
): void {
  const byFinger = station.pointerType === 'touch' || station.pointerType === 'pen';
  if (source === undefined || !byFinger) {
    return;
  }
  if (mediaForStation(source, station.path, station.node).length === 0) {
    return;
  }
  if (shown.current === station.path) {
    shown.current = null;
    viewer.clearStationMedia();
    viewer.setStationMedia(source);
    return;
  }

  shown.current = station.path;
  void viewer.focusStation(station.path, { popup: true }).catch(() => {
    // Nothing was shown, so the next tap on this station has to ask again rather than try to take
    // away a strip that is not there. Silent by design: the station came from a click on the model
    // in front of the reader, so a rejection here is an abandoned camera move, not a lost place.
    if (shown.current === station.path) {
      shown.current = null;
    }
  });
}

/** What one marker is currently drawn as, so the next answer can be turned into the moves it needs. */
interface DrawnMarker {
  station: string;
  label: string;
  sublabel: string | undefined;
  color: string;
}

/**
 * Isolated wrapper around the vendored CaveView.js viewer: fetches the survey file,
 * hands it to CaveView as a named File (parser choice), and tears the viewer down on
 * unmount. CaveView owns everything inside its container div — React never touches it.
 */
export default function CaveViewPanel({
  fileUrl,
  fileName,
  height = 480,
  onEntrancePick,
  onPartPick,
  surveyModelId,
  trackedCavers,
  toolbar = false,
  stationMedia,
  crsLookup,
  focusRequest,
}: CaveViewPanelProps) {
  const { t, i18n } = useTranslation();
  const narrow = useIsMobile();
  // The other axis the toolbar's set is chosen on. Every control in the bar is grown for a finger
  // by this panel's stylesheet, so how many of them fit depends on this as much as on the width.
  const coarse = useCoarsePointer();
  const containerIdRef = useRef<string>(null);
  containerIdRef.current ??= `caveview-panel-${panelSequence++}`;

  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [errorDetail, setErrorDetail] = useState<string>();
  /** Set when a link named a part of the survey this model turned out not to hold. */
  const [missingPart, setMissingPart] = useState(false);
  const [showMarkerTimes, setShowMarkerTimes] = useState(false);
  const [openCaverId, setOpenCaverId] = useState<string | null>(null);
  /** Which row of the watch the camera was last sent to, and whose station carries the mark. */
  const [shownPlace, setShownPlace] = useState<TrackedPlace | null>(null);

  // The viewer that holds the loaded survey, kept so links, markers, pictures and the toolbar can
  // reach it after the load. The survey file itself is no longer kept: showing a named part of it
  // is a camera move now, not a second parse of the same bytes.
  const viewerRef = useRef<{ viewer: CaveViewer; ui: CaveViewUi } | null>(null);
  /** What is drawn for each caver right now — the thing the next answer is compared against. */
  const drawnMarkersRef = useRef(new Map<string, DrawnMarker>());
  /** The station whose pictures a tap put on screen, so a second tap on it takes them off again. */
  const strippedStationRef = useRef<string | null>(null);

  const focusOf = (ref: ResourceRef) => focusForRef(ref, surveyModelId);

  useViewControl({
    id: `caveview-${containerIdRef.current}`,
    kind: 'caveview',
    labelKey: 'viewLinks.controls.caveview',
    enabled: status === 'ready' && surveyModelId !== undefined,
    canReveal: (ref) => focusOf(ref) !== null,
    reveal: (ref) => {
      const focus = focusOf(ref);
      const viewer = viewerRef.current?.viewer;
      if (focus === null || viewer === undefined) {
        return;
      }
      setMissingPart(false);
      const moved =
        focus.call === 'survey' ? viewer.focusSurvey(focus.ref) : viewer.focusStation(focus.ref);
      void moved.catch((error: unknown) => {
        // Only the model not holding what the link named is said — a move abandoned because a
        // second link was followed, or because somebody selected something while the camera flew,
        // is ordinary use and passes in silence. Which is which is decided in one place, next to
        // the rest of what is known about the viewer's own words.
        //
        // Said rather than thrown: a reader who followed a link is owed an answer, and the control
        // that delivered it cannot carry one back.
        if (focusNamedNothing(error)) {
          setMissingPart(true);
        }
      });
    },
  });

  // The callbacks ride refs so a new identity doesn't reload the whole viewer. Naming either of
  // them in the effect's dependencies is how a parent that re-renders per keystroke ends up
  // re-fetching and re-parsing a survey file on every one.
  const onEntrancePickRef = useRef(onEntrancePick);
  onEntrancePickRef.current = onEntrancePick;
  const onPartPickRef = useRef(onPartPick);
  onPartPickRef.current = onPartPick;
  // The listener that answers a tap is attached once with the viewer, and what it has to show can
  // arrive long afterwards.
  const stationMediaRef = useRef(stationMedia);
  stationMediaRef.current = stationMedia;
  // Read when a viewer is built, never as a reason to build one: naming it in the effect's
  // dependencies would re-download and re-parse the survey every time a caller re-rendered with a
  // fresh closure, which is what the callbacks above already ride a ref to avoid.
  const crsLookupRef = useRef(crsLookup);
  crsLookupRef.current = crsLookup;

  useEffect(() => {
    let disposed = false;
    let ui: CaveViewUi | null = null;
    setStatus('loading');
    setMissingPart(false);
    setOpenCaverId(null);
    // A mark belongs to the model it was put on, and a new model has none. Left standing it would
    // also be a list row lit up against a cave whose stations are not the ones it names.
    setShownPlace(null);
    // A new viewer draws none of the old one's markers, so nothing is drawn until they are added
    // again — which the marker effect does as soon as this one reports the model loaded.
    drawnMarkersRef.current = new Map();
    strippedStationRef.current = null;

    (async () => {
      const cv2 = await loadCaveView();
      const response = await fetch(fileUrl);
      if (!response.ok) throw new Error(`survey file request failed (${response.status})`);
      const blob = await response.blob();
      if (disposed) return;

      // `crsLookup` points the viewer's coordinate-system resolution at this installation's
      // own registry instead of epsg.io — see loadCaveView.ts for why that matters. A caller with
      // no account cannot reach that registry and supplies its own; see the prop.
      const viewer = new cv2.CaveViewer(containerIdRef.current!, {
        home: CAVEVIEW_HOME,
        crsLookup: crsLookupRef.current ?? makeCrsLookup(),
      });
      viewer.addEventListener('newCave', () => {
        if (!disposed) setStatus('ready');
      });
      viewer.addEventListener('entrance', (event) => {
        const name = (event as { displayName?: unknown }).displayName;
        if (!disposed && typeof name === 'string' && name) {
          onEntrancePickRef.current?.(name);
        }
      });

      // Both are watched unconditionally and answer only when somebody is listening, because the
      // listeners are attached once with the viewer and the caller's interest can change without
      // the survey being reloaded.
      //
      // `handled` is deliberately left alone. The bundle reads it back after dispatching and
      // treats a true as "the application dealt with this click", which would stop the viewer
      // selecting and highlighting what was clicked — so offering to link a station would take
      // away the ability to simply look at one.
      viewer.addEventListener('station', (event) => {
        if (disposed) return;
        const picked = event as { node?: unknown; mouseEvent?: { pointerType?: string } };
        const part = partFromStation(picked.node);
        if (part === null) return;
        onPartPickRef.current?.(part);
        showPicturesForTap(viewer, stationMediaRef.current, strippedStationRef, {
          node: picked.node,
          path: part.anchor.station,
          pointerType: picked.mouseEvent?.pointerType,
        });
      });
      viewer.addEventListener('leg', (event) => {
        const part = partFromLeg((event as { leg?: unknown }).leg);
        if (!disposed && part !== null) {
          onPartPickRef.current?.(part);
        }
      });
      // Resting on a caver's marker opens that caver's card. `handled` is left alone here too,
      // for a different reason: on this event it suppresses only the marker's own second line,
      // which is the last-report time somebody asked for with the switch. Claiming the event
      // would quietly turn that switch off.
      viewer.addEventListener('liveMarkerHover', (event) => {
        const id = (event as { id?: unknown }).id;
        if (!disposed && typeof id === 'string') {
          setOpenCaverId(id);
        }
      });

      ui = new cv2.CaveViewUI(viewer);
      viewerRef.current = { viewer, ui };
      ui.loadCave(new File([blob], fileName));
    })().catch((error: unknown) => {
      if (disposed) return;
      setStatus('error');
      setErrorDetail(error instanceof Error ? error.message : String(error));
    });

    return () => {
      disposed = true;
      viewerRef.current = null;
      ui?.dispose();
      ui = null;
    };
  }, [fileUrl, fileName]);

  // ---- Live markers ----
  //
  // Added, slid and taken off the loaded model as the watch is re-read, which happens every half
  // minute while a party is underground. Reloading the survey to redraw them would re-parse the
  // whole model on every one of those, and would take whoever is watching back to the view the
  // model opens at each time.
  useEffect(() => {
    const viewer = viewerRef.current?.viewer;
    if (viewer === undefined || status !== 'ready') {
      return;
    }

    const drawn = drawnMarkersRef.current;
    const wanted = new Map<string, DrawnMarker>();
    for (const caver of trackedCavers ?? []) {
      if (caver.position.kind !== 'station') {
        // No marker is invented for a position nobody reported or one that was withheld: there
        // is no station to put it at, and a marker placed anyway would be this application
        // claiming to know something it was deliberately not told. Those cavers are listed.
        continue;
      }
      wanted.set(caver.caverId, {
        station: caver.position.station,
        label: caver.name,
        sublabel:
          showMarkerTimes && caver.lastRecordedAt !== null
            ? t('caveview.tracking.markerSublabel', {
                when: new Date(caver.lastRecordedAt).toLocaleTimeString(i18n.language),
              })
            : undefined,
        // Somebody reported out is drawn in the muted colour: their marker is where they were
        // last seen, not where they are, and a party half of which is above ground has to read
        // as that rather than as everybody still being underground.
        //
        // Both colours are stated rather than taken from the interface theme, for two reasons the
        // palette spells out: the scene behind them is the viewer's own, not the page's, and a
        // theme token carrying its muting in an alpha channel arrives at the viewer as solid
        // black or solid white — which would draw whoever is out louder than whoever is not.
        color: caver.out ? trackedCaverPalette.out : trackedCaverPalette.underground,
      });
    }

    for (const [id, marker] of wanted) {
      const before = drawn.get(id);
      const options = { label: marker.label, sublabel: marker.sublabel, color: marker.color };
      // A move only replaces the options it is given, so an option that has gone away cannot be
      // taken off a marker by moving it — turning the time off would leave every marker showing
      // the time it had when it was turned off. Adding replaces the marker whole, which is what
      // that case needs; it costs the slide, and nothing there is sliding anyway.
      const clearsSublabel = before !== undefined && before.sublabel !== undefined && marker.sublabel === undefined;
      if (before === undefined || clearsSublabel) {
        viewer.addLiveMarker(id, marker.station, options);
      } else if (
        before.station !== marker.station
        || before.label !== marker.label
        || before.sublabel !== marker.sublabel
        || before.color !== marker.color
      ) {
        viewer.moveLiveMarker(id, marker.station, options);
      }
    }
    for (const id of drawn.keys()) {
      if (!wanted.has(id)) {
        viewer.removeLiveMarker(id);
      }
    }

    drawnMarkersRef.current = wanted;
  }, [trackedCavers, showMarkerTimes, status, t, i18n.language]);

  // ---- A place asked for from outside ----
  //
  // The same move the link bus makes, and deliberately the same failure handling: only the model
  // not holding what was named is said, because an abandoned move — superseded by a second link
  // followed a moment later, or cancelled by somebody selecting something while the camera flew —
  // leaves the camera exactly where whoever was driving it wanted it.
  useEffect(() => {
    const viewer = viewerRef.current?.viewer;
    if (focusRequest === undefined || viewer === undefined || status !== 'ready') {
      return;
    }
    let abandoned = false;
    setMissingPart(false);
    const moved =
      focusRequest.kind === 'survey'
        ? viewer.focusSurvey(focusRequest.ref)
        : viewer.focusStation(focusRequest.ref);
    void moved.then(
      () => {
        if (!abandoned) focusRequest.onSettled?.(true);
      },
      (error: unknown) => {
        if (abandoned) return;
        if (focusNamedNothing(error)) {
          setMissingPart(true);
          focusRequest.onSettled?.(false);
        }
      },
    );
    return () => {
      abandoned = true;
    };
  }, [focusRequest, status]);

  // ---- A place asked for from the list of who is where ----
  //
  // The same move a link makes, with the mark left on: a reader who pressed a name in the list is
  // asking "which of these is that", and a camera that arrives somewhere with nothing marked has
  // answered "somewhere around here". The mark is the viewer's own selection highlight, so it
  // survives being turned and zoomed and is taken off by exactly one call.
  //
  // Rejections are triaged the way every other move here is, and for the same reason: pressing a
  // second name while the camera is still flying to the first is ordinary use, rejects, and must
  // pass in silence — while a station the model does not hold is a real thing to say, and here it
  // means a watch resolved against a survey that has since been re-exported under other names.
  useEffect(() => {
    const viewer = viewerRef.current?.viewer;
    if (viewer === undefined || status !== 'ready') {
      return;
    }
    if (shownPlace === null) {
      viewer.clearHighlight();
      return;
    }
    let abandoned = false;
    setMissingPart(false);
    void viewer.focusStation(shownPlace.station, { highlight: true }).catch((error: unknown) => {
      if (!abandoned && focusNamedNothing(error)) {
        setMissingPart(true);
      }
    });
    return () => {
      abandoned = true;
    };
  }, [shownPlace, status]);

  // ---- Telling the viewer its container changed size ----
  //
  // <b>The viewer watches the window and nothing else.</b> It installs exactly one size listener,
  // on `window`, whose handler reads its container's width and height and resizes the drawing
  // surface to match; it has no observer of the container itself. That is enough for a page whose
  // viewer fills the window, and wrong for every panel here, because a panel's height is decided by
  // the card around it and can change while the window does not.
  //
  // What that looks like is measured: growing the tracking panel from 320px to 560px on a phone
  // left the canvas at 338x320 with a drawing buffer of 887x840 — the scene drawn at the old size
  // inside a box 240px taller, so the model sat in the top half with an empty band under it, and
  // the viewer went on hit-testing against a rectangle that was no longer the one on screen.
  //
  // A window `resize` event is the one way in, because it is the only thing the viewer listens for
  // — and it is honest rather than a trick: the layout really did change, and this is the event a
  // reader dragging their window would have produced. It is sent only when the surface's own size
  // actually changed, so nothing here can start a loop with anything else that answers the event.
  const dispatchedSizeRef = useRef('');
  useEffect(() => {
    const surface = document.getElementById(containerIdRef.current!);
    if (surface === null || status !== 'ready' || typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect;
      if (box === undefined) {
        return;
      }
      const size = `${Math.round(box.width)}x${Math.round(box.height)}`;
      if (size === dispatchedSizeRef.current) {
        return;
      }
      dispatchedSizeRef.current = size;
      window.dispatchEvent(new Event('resize'));
    });
    observer.observe(surface);
    return () => observer.disconnect();
  }, [status]);

  // ---- The viewer's own toolbar ----
  const toolbarOptions = toolbar === true ? {} : toolbar === false ? null : toolbar;
  const toolbarWanted = toolbarOptions !== null;
  const toolbarPlacement = toolbarOptions?.placement ?? 'top';
  const toolbarButtons = toolbarOptions?.buttons ?? caveViewToolbarButtons({ narrow, coarse });
  // The list is a new array on every render, so what the toolbar is rebuilt for is its contents;
  // the list itself is read off a ref at the moment one is built.
  const toolbarButtonKey = toolbarButtons.join(',');
  const toolbarButtonsRef = useRef(toolbarButtons);
  toolbarButtonsRef.current = toolbarButtons;

  useEffect(() => {
    const viewer = viewerRef.current?.viewer;
    if (!toolbarWanted || viewer === undefined || status !== 'ready') {
      return;
    }
    let disposed = false;
    let bar: CaveViewToolbar | null = null;
    // The bundle is already loaded — this viewer came out of it — so this resolves immediately;
    // it is awaited rather than read off the global so there is one way to reach the namespace.
    void loadCaveView().then((cv2) => {
      if (disposed || viewerRef.current?.viewer !== viewer) {
        return;
      }
      // The container is the viewer's own element, which is positioned — so the toolbar is drawn
      // over the model, and goes fullscreen with it.
      bar = new cv2.CaveViewToolbar(viewer, containerIdRef.current!, {
        placement: toolbarPlacement,
        buttons: [...toolbarButtonsRef.current],
      });
    });
    return () => {
      disposed = true;
      // Removed unconditionally, including where the viewer has already gone and taken the
      // toolbar with it — the bundle's own teardown is written to be run twice, and the cleanup
      // of the effect that owns the viewer runs before this one, so a check for a live viewer
      // here would simply never remove a toolbar the panel is keeping.
      bar?.dispose();
      bar = null;
    };
  }, [status, toolbarWanted, toolbarPlacement, toolbarButtonKey]);

  // ---- Station pictures ----
  //
  // The strip is drawn for the station the viewer is tracking, and it only tracks one while it is
  // labelling it — so a panel showing pictures has to ask for that label. It is asked for *here*,
  // on the loaded viewer, rather than in the construction config: the viewer assembles its view
  // settings as its own defaults, then the config, then whatever its "save as default" button last
  // stored in this browser, and re-applies that assembly on every load. One press of that button
  // with the label off would otherwise turn the station pictures off for every cave and every panel
  // in this browser, for good, with nothing anywhere to say why.
  useEffect(() => {
    const viewer = viewerRef.current?.viewer;
    if (stationMedia === undefined || viewer === undefined || status !== 'ready') {
      return;
    }
    viewer.setStationMedia(stationMedia);
    viewer.stationLabelOver = true;
    strippedStationRef.current = null;
    return () => {
      if (viewerRef.current?.viewer === viewer) {
        viewer.clearStationMedia();
        // The panel asked for the label and takes it back with the pictures it was for. Where the
        // pictures are merely being replaced, the run that follows this one asks again.
        viewer.stationLabelOver = false;
        strippedStationRef.current = null;
      }
    };
  }, [stationMedia, status]);

  return (
    <div className="caveview-panel" style={{ height }}>
      {status === 'loading' && (
        <Spin style={{ position: 'absolute', inset: 0, marginTop: 48 }} data-testid="caveview-loading" />
      )}
      {status === 'error' && (
        <Alert type="error" showIcon message={t('caveview.loadError')} description={errorDetail} />
      )}
      {missingPart && (
        <Alert
          className="caveview-panel-notice"
          type="warning"
          showIcon
          closable
          onClose={() => setMissingPart(false)}
          message={t('caveview.notInThisModel')}
          data-testid="caveview-missing-part"
        />
      )}
      <div
        id={containerIdRef.current}
        className="caveview-panel-surface"
        data-testid="caveview-container"
        style={{ width: '100%', height: '100%', display: status === 'error' ? 'none' : undefined }}
      />
      {trackedCavers !== undefined && trackedCavers.length > 0 && status !== 'error' && (
        <CaveViewTrackingOverlay
          cavers={trackedCavers}
          showTimes={showMarkerTimes}
          onShowTimesChange={setShowMarkerTimes}
          openCaverId={openCaverId}
          onOpenCaver={setOpenCaverId}
          shown={shownPlace}
          onShow={setShownPlace}
          raised={toolbarWanted && toolbarPlacement === 'bottom'}
        />
      )}
    </div>
  );
}
