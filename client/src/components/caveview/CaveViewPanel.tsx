// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { Alert, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  CAVEVIEW_HOME,
  focusNamedNothing,
  loadCaveView,
  makeCrsLookup,
  type CaveViewMediaEntry,
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
  pathOf,
  type PickedModelPart,
} from '../../caveview/modelParts.ts';
import { noStationsMissing, stationsNotOnModel } from '../../caveview/placedOnModel.ts';
import { mediaForStation } from '../../caveview/stationMedia.ts';
import { caveViewToolbarButtons } from '../../caveview/toolbarButtons.ts';
import {
  sharedTeamTitle,
  undergroundFirst,
  type TrackedCaver,
} from '../../caveview/trackedCavers.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import { markerLine } from '../../caveview/markerLine.ts';
import type { ResourceRef } from '../../viewlinks/resourceRef.ts';
import { useViewControl } from '../../viewlinks/useViewControl.ts';
import Lightbox from '../gallery/Lightbox.tsx';
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
   * Told which stations the loaded model turns out to hold no node for.
   *
   * <b>Only this panel can answer it, and the surfaces that most need the answer are not in
   * it.</b> Whether a station somebody named exists in the drawing is a fact about the parsed file
   * and is known to the viewer alone; the coordinator's table sits above this panel and a
   * published page's list of people sits beside it, and both of them state a station as a place
   * somebody is. So the answer is handed back out rather than kept to the list drawn over the
   * model.
   *
   * <b>Stations rather than the people standing at them, which is what makes the answer usable
   * outside this panel.</b> What the viewer knows is a property of the survey it parsed, true
   * whoever is drawn on it and true of nobody at all; the party this panel is drawing is not
   * necessarily the party the caller is listing — a replay hands this panel the watch as it stood
   * an hour ago while the table above it goes on showing the watch as it stands. Keyed by person
   * the answer would be about the wrong party the moment those two differ.
   *
   * Answered again whenever it changes, with an empty set when nothing is claimed — including as
   * this panel goes off the screen, because a claim about the drawing is only true while there is
   * one. A caller that keeps the last answer after closing the model would be marking a table
   * against a drawing nobody is looking at.
   */
  onUnplacedStationsChange?: (stations: ReadonlySet<string>) => void;
  /**
   * Told every station of the drawing once it is parsed — the index a station typeahead
   * offers, in the viewer's own spelling, the same one every anchor stores.
   *
   * Answered from the loaded survey when it arrives, and with an empty list as a new
   * load begins: like the unplaced-stations answer above, a claim about a drawing is
   * only true while that drawing is the one on screen. An empty answer is also what a
   * vendored viewer build without station enumeration produces — the caller's list is
   * simply empty, and picking in the 3D pane still works.
   */
  onStationsLoaded?: (stations: readonly string[]) => void;
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
 * The pictures a click on a thumbnail opens, and which of them was clicked.
 *
 * <b>The station's whole set, not the one thumbnail.</b> The strip is bounded by the model it is
 * drawn over — two thumbnails across on a phone, and the rows past what fits are cut off at its edge
 * — so the pictures a station carries beyond the first few can be reached in exactly one way:
 * opening one of the ones that are drawn and moving on from it. The set is also not an arbitrary
 * one; it is what somebody linked to this station, which is as much of a sequence as a run of
 * photographs anywhere in this application has.
 */
interface OpenedPictures {
  entries: readonly CaveViewMediaEntry[];
  index: number;
}

/**
 * What a clicked thumbnail opens, read back out of the same source the strip was built from.
 *
 * The clicked entry is the object this panel handed over in the first place, so it is normally
 * found in the set by identity; the URL comparison is what answers a source that rebuilt its
 * entries between the strip being drawn and the thumbnail being pressed. A station that cannot be
 * named, or a set the entry is not in, opens the one picture that was actually clicked rather than
 * nothing — a thumbnail that answers with an empty viewer is worse than one that answers narrowly.
 */
function openedFrom(
  source: CaveViewStationMediaSource | undefined,
  station: unknown,
  entry: CaveViewMediaEntry,
): OpenedPictures {
  if (source === undefined) {
    return { entries: [entry], index: 0 };
  }
  const entries = mediaForStation(source, pathOf(station) ?? '', station);
  const index = entries.findIndex(
    (candidate) => candidate === entry || candidate.url === entry.url,
  );
  return index < 0 ? { entries: [entry], index: 0 } : { entries, index };
}

/**
 * Where the picture viewer has to be drawn to be seen at all, or null for its place in the panel.
 *
 * <b>An element covering the screen paints over the whole document, whatever anything else's
 * stacking order says.</b> The viewer's own fullscreen button puts the drawing surface — not this
 * panel — into the browser's top layer, and nothing outside that element is drawn while it is
 * there. The picture viewer is a fixed sheet with a z-index, and a sibling of the surface: opened
 * from a thumbnail while the model is fullscreen it would be painted behind the model, having also
 * suppressed the viewer's own in-model popup, so the thumbnail would answer with nothing at all —
 * and the Escape that ought to close the picture would leave fullscreen instead, taking the unseen
 * sheet with it. Below 768px the same thing happens without the top layer, because the surface is
 * then pinned over the screen by the class the viewer adds, at a z-index above this sheet's.
 *
 * So while the surface covers the screen the sheet is rendered inside it, where it is part of the
 * same stacking context and paints over the model. Both ways of covering the screen are asked
 * about, because the second raises no `fullscreenchange` and happens exactly where the first is
 * refused — inside an embedded frame, which is how this viewer is read on somebody else's page.
 */
function screenCoveringSurface(surface: HTMLElement | null): HTMLElement | null {
  if (surface === null) {
    return null;
  }
  const covering =
    document.fullscreenElement === surface || surface.classList.contains('toggle-fullscreen');
  return covering ? surface : null;
}

/**
 * What one marker is currently drawn as, so the next answer can be turned into the moves it needs.
 *
 * <b>A marker's own label is one line and stays a string.</b> Several lines are a thing a *group*
 * of markers at one station says, and that label is answered by a function the viewer asks rather
 * than carried in a marker's options — so the comparison below stays a string comparison. An array
 * here would be a new value on every render and would slide every marker on every re-read of the
 * watch, which is precisely the rebuild this diff exists to avoid.
 *
 * <b>Nothing is carried on the hover line any more, and the switch is why.</b> The last-report time
 * used to go there, revealed by a pointer resting on the marker. A collapsed marker has no such
 * line — the viewer says so outright — so a group's times had to be drawn on the label instead, and
 * one switch therefore meant two different things: hover-only for a caver standing alone, permanent
 * for the same caver a minute later once somebody joined them at their station. Both read off the
 * label now. The case that needed a marker to be added again rather than moved — a sublabel cannot
 * be taken off by a move, because a move replaces only the options it is given — goes with it.
 */
interface DrawnMarker {
  station: string;
  label: string;
  color: string;
}

// The label spelling itself lives in `caveview/markerLine.ts` now: the scanned map sheets
// draw the same party, and one person must read the same on both drawings.

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
  onUnplacedStationsChange,
  onStationsLoaded,
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
  /**
   * The day it is where this is being read, which is what decides whether a moment on a marker
   * needs a date said with it.
   *
   * <b>A day and not a clock, and that is the whole reason this is cheap.</b> Read at render, so
   * it follows the reader's own midnight rather than a fixed point captured when the panel opened —
   * but its *value* changes only once a day, so the two dependency lists that carry it re-register
   * the labels at midnight and are untouched by the thirty-second poll in between. Keying the same
   * question on a live clock would rebuild every collapsed marker on the model twice a minute, for
   * a label that had not changed.
   */
  const today = new Date().toLocaleDateString(i18n.language);
  /**
   * Whether the markers say who they are, rather than only where somebody is.
   *
   * <b>On, and remembered nowhere.</b> A reader who has never asked for anything is the one this
   * is drawn for — a name beside a dot is the answer to "who is that", and a model of anonymous
   * dots makes them ask the list for every one of them. Turning it off is for a screen a party at
   * one station has crowded, which is a thing about this view at this moment rather than a
   * preference: it lasts as long as the panel is mounted and a fresh page opens with names again,
   * exactly like the last-update switch beside it.
   */
  const [showMarkerLabels, setShowMarkerLabels] = useState(true);
  const [openCaverId, setOpenCaverId] = useState<string | null>(null);
  /**
   * Which stations the loaded model could not place a marker at — asked of the viewer, never
   * inferred.
   *
   * <b>A set of station paths in React state, which is not the rule this panel keeps for viewer
   * objects.</b> Nothing of the viewer's is held here: what is kept is the answer to a question,
   * as plain strings, because three surfaces have to draw it and a ref draws nothing.
   *
   * It only ever grows while one model stays loaded, and it is emptied when another is. What the
   * viewer says about a station is a fact about the file it parsed, so it does not stop being true
   * when the marker that revealed it is taken off the model — which is what happens to the whole
   * party every time a coordinator engages the replay under this panel.
   */
  const [unplacedStations, setUnplacedStations] = useState<ReadonlySet<string>>(noStationsMissing);
  /** Which row of the watch the camera was last sent to, and whose station carries the mark. */
  const [shownPlace, setShownPlace] = useState<TrackedPlace | null>(null);
  /**
   * The pictures a reader opened from a station's strip, or null while none is open.
   *
   * The entries themselves, which are plain data the viewer was handed in the first place — a URL,
   * a caption and which document each is. The `station` the event also carries is a viewer object
   * and is deliberately not kept: what a reader opened is pictures, and holding the model's own
   * node in React state is how a scene outlives the viewer that owns it.
   */
  const [openedPictures, setOpenedPictures] = useState<OpenedPictures | null>(null);
  /**
   * The element the picture viewer is drawn inside, or null for its ordinary place in the panel.
   *
   * Settled when a thumbnail is clicked and re-settled while the picture is open, because leaving
   * fullscreen underneath an open picture has to bring it back out of the surface — a sheet left
   * inside a container that is no longer covering the screen is a sheet inside a card.
   */
  const [pictureHost, setPictureHost] = useState<HTMLElement | null>(null);

  // The viewer that holds the loaded survey, kept so links, markers, pictures and the toolbar can
  // reach it after the load. The survey file itself is no longer kept: showing a named part of it
  // is a camera move now, not a second parse of the same bytes.
  const viewerRef = useRef<{ viewer: CaveViewer; ui: CaveViewUi } | null>(null);
  /** What is drawn for each caver right now — the thing the next answer is compared against. */
  const drawnMarkersRef = useRef(new Map<string, DrawnMarker>());

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
  const onStationsLoadedRef = useRef(onStationsLoaded);
  onStationsLoadedRef.current = onStationsLoaded;
  // The listener that answers a tap is attached once with the viewer, and what it has to show can
  // arrive long afterwards.
  const stationMediaRef = useRef(stationMedia);
  stationMediaRef.current = stationMedia;
  // Read when a viewer is built, never as a reason to build one: naming it in the effect's
  // dependencies would re-download and re-parse the survey every time a caller re-rendered with a
  // fresh closure, which is what the callbacks above already ride a ref to avoid.
  const crsLookupRef = useRef(crsLookup);
  crsLookupRef.current = crsLookup;

  /**
   * What the one marker drawn in place of a party standing together says.
   *
   * <b>A count is not an answer to the question this surface exists for.</b> Three dots at one
   * station collapse to a single marker reading "3", and who those three are is the whole of what
   * somebody watching a trip wants from the model — so the group is named: its team, where the
   * people in it are one team, and then each of them on a line of their own.
   *
   * <b>Only the markers the viewer collapsed are named.</b> The lines are built from the ids it
   * hands over and never from a team's roster, so somebody whose position was withheld — who has
   * no marker, deliberately — cannot appear under a heading that would place them at a station
   * nobody said they were at. That rule is the reason this reads ids rather than teams.
   *
   * <b>Ordered by the watch, not by the viewer.</b> Markers arrive in the order they were added,
   * and a marker re-added moves to the end of that order — so a label built in it would silently
   * re-sort itself while nothing about the party had changed. The watch's order is the trip's
   * roster; the one rearrangement made of it is that whoever has come out is set below whoever
   * has not, which is a fact about the people rather than about the drawing.
   *
   * <b>The heading is read from everybody collapsed, including whoever is out.</b> It is the claim
   * that the names under it are one team, so it is answered by the whole set the marker stands for:
   * a caver of another team who has come out at this station is still a second team at it, and
   * heading the block with the first team's name because the mixture only shows below the fold
   * would be the same false claim the shared-title rule exists to refuse.
   *
   * Answering null where none of them is on the watch any more leaves the viewer's own count,
   * which is the honest thing to draw when the panel has nothing to say about them.
   */
  const clusterLabelRef = useRef<(ids: readonly string[]) => string[] | null>(() => null);
  clusterLabelRef.current = (ids) => {
    const here = new Set(ids);
    const members = (trackedCavers ?? []).filter((caver) => here.has(caver.caverId));
    if (members.length === 0) {
      return null;
    }
    const title = sharedTeamTitle(members);
    const line = { t, language: i18n.language, showTimes: showMarkerTimes, today };
    return [
      ...(title === null ? [] : [title]),
      ...undergroundFirst(members).map((member) => markerLine(member, line)),
    ];
  };

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
    // And nothing is known yet about what the next model can place. Left standing, the answer
    // about the survey just taken off the screen would be drawn against the one arriving — on a
    // panel that is mid-load and showing no model at all, which is where a stale claim is least
    // visible and most wrong. This is also the one moment the answer may shrink: a station name is
    // missing from a *drawing*, so what is learned about one survey is worth nothing about another.
    setUnplacedStations(noStationsMissing);
    // The station index is a claim about the survey being taken off the screen; the one
    // arriving answers for itself when it is parsed.
    onStationsLoadedRef.current?.([]);
    // A picture opened from the model on screen belongs to that model. A second survey file loaded
    // under it would leave a photograph of another cave's pitch head sitting over the new one.
    setOpenedPictures(null);

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
        if (disposed) return;
        setStatus('ready');
        // The parsed survey is the only thing that knows its stations; asked here, once,
        // the moment it exists. A build that cannot enumerate them (or a survey that
        // refuses) costs an empty index, never a broken viewer.
        if (onStationsLoadedRef.current !== undefined) {
          const stations: string[] = [];
          try {
            viewer.forEachStation?.((station) => {
              const path = pathOf(station);
              if (path !== null) {
                stations.push(path);
              }
            });
          } catch {
            stations.length = 0;
          }
          onStationsLoadedRef.current(stations);
        }
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
      //
      // <b>A tap is not answered with a camera move, and used to be.</b> The reasoning for that was
      // that the strip is a hover and a finger cannot hover, so the only way to it was focusing the
      // station — which flies and recentres the camera. It is not: the viewer reports a station
      // tapped as well as hovered, and shows the strip for it where it stands. Driven against the
      // vendored bundle on a 286px surface, a stationary tap on a station dispatched `station` and
      // then `stationHover` with `pointerType` touch and drew the strip at the station, with
      // nothing having called `focusStation`; a tap on empty space took it off again, which is the
      // dismissal a finger otherwise has to be given. So the move bought nothing, and it cost the
      // most on the one surface where a tap means something else — the tracking tab, where somebody
      // taps the station the party is at and then reaches for "Record here" while the model slides
      // out from under their finger.
      viewer.addEventListener('station', (event) => {
        if (disposed) return;
        const part = partFromStation((event as { node?: unknown }).node);
        if (part === null) return;
        onPartPickRef.current?.(part);
      });
      // A thumbnail of the strip was clicked. Taken over from the viewer's own popup, which draws
      // the picture into the model at the station: that is the right fallback for a host with
      // nowhere better to put it, but this application has a picture viewer, and a photograph of a
      // pitch head is worth more than a small square floating in a line drawing — it can be
      // zoomed, panned, read with its caption and dismissed with Escape.
      //
      // `handled` is set only once there is something to show. A malformed entry leaves it alone
      // so the viewer still does whatever it would have done, rather than the click doing nothing
      // at all because this listener claimed it and then declined.
      viewer.addEventListener('mediaOpen', (event) => {
        if (disposed) return;
        const opened = event as { entry?: CaveViewMediaEntry; station?: unknown; handled?: boolean };
        const entry = opened.entry;
        if (entry == null || typeof entry.url !== 'string' || entry.url.length === 0) {
          return;
        }
        opened.handled = true;
        // Where it has to be drawn is a question about this moment — whether the model is covering
        // the screen right now — so it is asked now, with the element the viewer would have put
        // into the top layer in hand.
        setPictureHost(screenCoveringSurface(document.getElementById(containerIdRef.current!)));
        setOpenedPictures(openedFrom(stationMediaRef.current, opened.station, entry));
      });
      viewer.addEventListener('leg', (event) => {
        const part = partFromLeg((event as { leg?: unknown }).leg);
        if (!disposed && part !== null) {
          onPartPickRef.current?.(part);
        }
      });
      // Resting on a caver's marker opens that caver's card. `handled` is left alone here too,
      // and now decides nothing: what it suppresses is the marker's own hover line, and no marker
      // this panel draws is given one — everything the switches ask for is on the label, where a
      // collapsed marker can say it too. Left unset rather than set, so a hover line added here
      // later is not swallowed by a flag nobody would think to look for.
      viewer.addEventListener('liveMarkerHover', (event) => {
        const id = (event as { id?: unknown }).id;
        if (!disposed && typeof id === 'string') {
          setOpenCaverId(id);
        }
      });
      // A caver standing with others is not the same event: the viewer collapses them and
      // reports the group, so listening only for the one above leaves a party of three
      // answering nothing — which is most of them, because a party moves together. The card
      // is per person, so the group's first line is opened; the list beside the model is how
      // a reader reaches the rest, and it is already showing them together.
      viewer.addEventListener('liveMarkerCluster', (event) => {
        const markers = (event as { markers?: readonly { id?: unknown }[] }).markers;
        const first = markers?.find((marker) => typeof marker.id === 'string')?.id;
        if (!disposed && typeof first === 'string') {
          setOpenCaverId(first);
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

  // ---- Re-asking the viewer what a group says ----
  //
  // <b>The viewer asks, but only when something it draws has moved.</b> It calls the cluster-label
  // function as markers are added, slid and removed, and at no other moment — so a poll that
  // renamed a team, or moved one caver of a standing party onto another team, changed what that
  // function would answer while nothing asked it again. Seen live: the table and the list beside
  // the model both followed a rename and the model went on drawing the old name, two surfaces on
  // one screen disagreeing about one team. The membership case is worse than untidy — a heading is
  // the claim that the names under it are one team, and it went on asserting that over a set that
  // had become a mixture, which is the one claim the shared-title rule exists to refuse. A party
  // standing at one station stands there for an hour, so "it corrects itself when somebody moves"
  // is not a correction.
  //
  // Setting the function again is what re-asks it: the viewer rebuilds the collapsed markers
  // already displayed, and rebuilds only those whose text has actually changed — a marker drawn on
  // its own is not touched at all. So the whole of the fix is to set it again whenever the answer
  // could differ, which is what the key below decides. That key is read from exactly what the label
  // is built out of: who is on the watch, what each of them is called, whose team they are on,
  // whether they have come out, and the switch, the clock and the language that shape the lines.
  // An unchanged poll composes the same string, re-registers nothing, and leaves the per-marker
  // diffing below to do what it already did — which is what keeps this from becoming a rebuild of
  // every collapsed marker every thirty seconds.
  //
  // Declared *before* the marker effect so that on the commit a model becomes ready the label is in
  // place before the first marker is added: a party already standing together is then named as it
  // is drawn, rather than drawn as a count and relabelled a moment later.
  const clusterLabelKey = JSON.stringify([
    showMarkerTimes,
    i18n.language,
    // The other thing a printed moment is composed against: a position from before midnight stops
    // being from today while nothing about the watch changes, and the label has to gain its date
    // at that moment rather than at the next report. Changes once a day and so costs one rebuild.
    today,
    (trackedCavers ?? []).map((caver) => [
      caver.caverId,
      caver.name,
      caver.teamId,
      caver.teamTitle,
      caver.out,
      // The moment the label actually prints — see the line the labels are composed by. Keyed on
      // the same field, so a poll that moves somebody's last word without moving their position
      // re-registers nothing and rebuilds no collapsed marker.
      showMarkerTimes ? caver.positionAt : null,
    ]),
  ]);

  useEffect(() => {
    const viewer = viewerRef.current?.viewer;
    if (viewer === undefined || status !== 'ready') {
      return;
    }
    // The answer itself still rides a ref, so what is registered is one shape of function and the
    // watch it reads is always the current one. `status` is a dependency because a panel pointed at
    // a second survey file builds a second viewer, which has been told none of this.
    viewer.setLiveMarkerClusterLabel((markers) =>
      clusterLabelRef.current(markers.map((marker) => marker.id)),
    );
  }, [clusterLabelKey, status]);

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
    // Built here rather than shared with the cluster label above, so that everything this effect
    // composes a label from is also something it is re-run for.
    const line = { t, language: i18n.language, showTimes: showMarkerTimes, today };
    for (const caver of trackedCavers ?? []) {
      if (caver.position.kind !== 'station') {
        // No marker is invented for a position nobody reported or one that was withheld: there
        // is no station to put it at, and a marker placed anyway would be this application
        // claiming to know something it was deliberately not told. Those cavers are listed.
        continue;
      }
      wanted.set(caver.caverId, {
        // The same line a group's label gives this person, so that the party gathering at one
        // station and separating again does not add and drop facts about them as it goes.
        station: caver.position.station,
        label: markerLine(caver, line),
        // Somebody reported out is drawn in the muted colour as well: their marker is where they
        // were last seen, not where they are, and a party half of which is above ground has to
        // read as that rather than as everybody still being underground. The colour is the part
        // of that which collapsing throws away, which is why the label says it too.
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
      const options = { label: marker.label, color: marker.color };
      // A move replaces only the options it is given, which is no longer a trap: every option
      // these markers carry is given on every call, so none of them can be left behind by one.
      if (before === undefined) {
        viewer.addLiveMarker(id, marker.station, options);
      } else if (
        before.station !== marker.station
        || before.label !== marker.label
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

    // ---- And then ask the viewer what actually went onto the model ----
    //
    // <b>The viewer has always known, and this is where it is finally asked.</b> Every call above
    // answers with the marker as the viewer now holds it, and a marker naming a station the loaded
    // model has no node for is held <em>unresolved</em> — kept, in case a model containing it is
    // loaded later, and drawn nowhere at all. The answers used to be thrown away, so a watch whose
    // stations the model does not hold drew nobody while the list beside it went on naming each
    // caver and the station they were at. Nothing was logged. Nothing was said. A coordinator reads
    // that as "we know where they are" over an empty cave.
    //
    // <b>The whole list is asked for rather than the answers collected one by one</b>, and the
    // difference is not tidiness: an add and a move each answer about the marker they acted on,
    // while a poll that changed nobody's station calls neither — and a marker already standing can
    // stop being drawable without this panel touching it, because the viewer re-resolves all of
    // them whenever a survey is loaded. One read covers the markers this pass moved and the ones
    // it did not.
    //
    // <b>What is learned is kept, and it is kept about stations rather than about people.</b> A
    // marker taken off the model takes its evidence with it, so an answer rebuilt from the markers
    // standing at this instant would forget a station the moment nobody was at it. That is not a
    // corner: the coordinator's panel replaces this panel's whole party whenever its replay is
    // engaged, so the answer would empty itself while the table above went on printing those same
    // stations as places somebody is — the very sentence this exists to stop. A station the drawing
    // has no node for stays one for as long as that drawing is loaded, and the load above is where
    // it is unlearned.
    //
    // <b>The moment is safe, which is the thing that would otherwise make this lie.</b> A marker
    // added before the survey is parsed is unresolved and would be reported as unplaceable; the
    // vendored viewer dispatches its survey-loaded event before the one this panel takes as ready,
    // and nothing here adds a marker until then. So by the time anything above has run, the model
    // is parsed and the answer is the final one.
    //
    // This cannot feed itself: the same set is answered when nothing was learned, and it is named
    // in no dependency list of this effect.
    setUnplacedStations((known) => stationsNotOnModel(known, viewer.getLiveMarkers()));
  }, [trackedCavers, showMarkerTimes, status, t, i18n.language, today]);

  // ---- Handing that answer to the surfaces outside this panel ----
  //
  // The table above the model and a published page's list of people each state a station as where
  // somebody is, and neither of them can ask the viewer anything. Told on every change rather than
  // polled, and told again — empty — when this panel goes off the screen, because "the drawing does
  // not hold this station" is a claim about a drawing that is on it.
  const onUnplacedRef = useRef(onUnplacedStationsChange);
  onUnplacedRef.current = onUnplacedStationsChange;
  useEffect(() => {
    onUnplacedRef.current?.(unplacedStations);
  }, [unplacedStations]);
  useEffect(() => () => onUnplacedRef.current?.(noStationsMissing), []);

  // ---- Whether the markers say who they are ----
  //
  // <b>Applied whenever a model is ready, not only when the switch is moved.</b> The setting
  // belongs to the markers rather than to the view, so the viewer neither saves it nor restores
  // it — and this panel builds a *new* viewer for every survey file it is pointed at, each of
  // which starts with its labels on. Without the `status` dependency, opening a second cave would
  // quietly bring back the labels somebody had just taken off, with the switch still reading off.
  //
  // Nothing is added or removed here: the markers stay drawn and stay pointable with their labels
  // off, so a party that crowds the screen is read by pressing it rather than by squinting at it,
  // and the cards the list and the marker hover open are unaffected.
  useEffect(() => {
    const viewer = viewerRef.current?.viewer;
    if (viewer === undefined || status !== 'ready') {
      return;
    }
    viewer.liveMarkerLabels = showMarkerLabels;
  }, [showMarkerLabels, status]);

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
      // Measured zero is not measured — the rule the image canvas's measure already
      // applies. A pane hidden behind another tab reports 0×0, and announcing it would
      // have the viewer resize its drawing buffers to nothing; staying silent leaves the
      // last real size on record, so reappearing at that same size dispatches nothing
      // (nothing changed) and reappearing at a new one dispatches once.
      if (box.width === 0 || box.height === 0) {
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
    return () => {
      if (viewerRef.current?.viewer === viewer) {
        viewer.clearStationMedia();
        // The panel asked for the label and takes it back with the pictures it was for. Where the
        // pictures are merely being replaced, the run that follows this one asks again.
        viewer.stationLabelOver = false;
      }
    };
  }, [stationMedia, status]);

  // ---- Following the screen under an open picture ----
  //
  // Which element the picture viewer belongs in is settled when a thumbnail is clicked, and one
  // thing can change it underneath: leaving fullscreen while the picture is open, by the browser's
  // own way out rather than by a control this panel drew. A sheet left inside a surface that is no
  // longer covering the screen is a full-screen viewer squeezed into a card, so it is moved back.
  const picturesOpen = openedPictures !== null;
  useEffect(() => {
    if (!picturesOpen) {
      return;
    }
    const settle = () =>
      setPictureHost(screenCoveringSurface(document.getElementById(containerIdRef.current!)));
    document.addEventListener('fullscreenchange', settle);
    return () => document.removeEventListener('fullscreenchange', settle);
  }, [picturesOpen]);

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
          // Which stations the model on screen has no node for, as the viewer answered it.
          unplacedStations={unplacedStations}
          showTimes={showMarkerTimes}
          onShowTimesChange={setShowMarkerTimes}
          showLabels={showMarkerLabels}
          onShowLabelsChange={setShowMarkerLabels}
          openCaverId={openCaverId}
          onOpenCaver={setOpenCaverId}
          shown={shownPlace}
          onShow={setShownPlace}
          raised={toolbarWanted && toolbarPlacement === 'bottom'}
        />
      )}
      {openedPictures !== null
        && inside(
          pictureHost,
          /*
           * The station's pictures, opened at the one that was clicked. Paging through them is how
           * the pictures the strip could not fit are seen at all — the strip is held inside the
           * model surface, which on a phone is two thumbnails across — and the set is exactly what
           * somebody linked to this one station, so moving through it is moving through the
           * photographs of one place rather than through an invented sequence.
           *
           * <b>Only the renderings are offered, and the download refuses itself.</b> No `contentUrl`
           * and no `mayDownloadOriginal` is passed because the panel holds neither — the strip's
           * entries are derived from a published thumbnail URL, at the sizes the reader is entitled
           * to, precisely so that a reader who may not have a photograph's stored bytes is never
           * handed them by a screen that is only displaying it. The viewer's download control reads
           * that absence as "withheld" and says so, which is the honest answer here.
           */
          <Lightbox
            photos={openedPictures.entries.map((entry) => ({
              documentId: entry.documentId ?? entry.url,
              // Shown only where the picture carries no caption of its own; the viewer prefers the
              // caption wherever there is one.
              title: t('caveview.stationPicture'),
              previewUrl: entry.url,
              caption: entry.caption,
            }))}
            index={openedPictures.index}
            // What the facts panel opens into, for the same reason the sheet itself is placed: an
            // overlay the component library puts at the end of the document is not drawn at all
            // while the model is covering the screen.
            overlayContainer={pictureHost}
            onClose={() => setOpenedPictures(null)}
            onIndexChange={(index) => setOpenedPictures({ ...openedPictures, index })}
          />,
        )}
    </div>
  );
}

/** Draws something inside an element that is covering the screen, or where it stands. */
function inside(host: HTMLElement | null, node: ReactNode): ReactNode {
  return host === null ? node : createPortal(node, host);
}
