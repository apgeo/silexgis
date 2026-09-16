// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { CAVEVIEW_HOME, type Cv2Namespace } from '../../caveview/loadCaveView.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';
import { shortNameOf } from '../../caveview/modelParts.ts';
import { trackedCaverPalette } from '../../map/markerPalette.ts';
import { reveal, resetViewControlsForTests } from '../../viewlinks/viewTargets.ts';
import CaveViewPanel from './CaveViewPanel.tsx';

// How much room there is across, which is what decides how many of the viewer's own controls the
// toolbar is asked for. Hoisted rather than a plain `let` because the module under test is
// imported statically here, so the mock's factory runs before any declaration in this file would.
// Never driven through a media query: jsdom lays nothing out and answers every one of them false.
const axes = vi.hoisted(() => ({ narrow: false, coarse: false }));
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => axes.narrow }));
// The other axis, and the reason it is a second one: a control's size is a question about what is
// pointing at it, and a phone held sideways is wide and coarse at once — so a set chosen on width
// alone hands that device the mouse set at finger sizes.
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => axes.coarse }));

// A fake CV2 global stands in for the vendored bundle: it records event
// listeners so tests can replay the viewer's events against the wrapper,
// the construction config so tests can pin what the panel wires up, and
// every call the panel makes on the viewer, which is the whole of the
// contract this wrapper has with it.
type Listener = (event: unknown) => void;
const listeners = new Map<string, Listener[]>();
let lastViewerConfig: Record<string, unknown> | undefined;
/** The view setting the station pictures depend on, as the panel leaves it on the viewer. */
let stationLabelOver = false;

const loadCave = vi.fn();
const focusStation = vi.fn<(ref: unknown, options?: unknown) => Promise<unknown>>();
const focusSurvey = vi.fn<(ref: unknown) => Promise<void>>();
const highlightStation = vi.fn();
const clearHighlight = vi.fn();
/**
 * The stations this fake model does not hold, which is the whole of what makes a marker unresolved.
 *
 * <b>Modelled rather than stubbed, because the thing under test is whether the panel asks.</b> The
 * vendored viewer keeps a marker whose reference names no station of the loaded survey and reports
 * it back as `resolved: false` — it does not refuse it, and it raises nothing. A fake that simply
 * returned a fixed list would let a panel that never asked pass, so this one answers from what it
 * was told and from what the panel actually did to it.
 */
const missingStations = new Set<string>();
/** What the fake viewer is holding, keyed as the real one keys markers. */
const heldMarkers = new Map<string, { id: string; ref: unknown; resolved: boolean }>();
const held = (id: string, ref: unknown) => ({
  id,
  ref,
  resolved: !missingStations.has(String(ref)),
});

const addLiveMarker = vi.fn((id: string, ref: unknown, _options?: unknown) => {
  const marker = held(id, ref);
  heldMarkers.set(id, marker);
  return marker;
});
const moveLiveMarker = vi.fn((id: string, ref: unknown, _options?: unknown) => {
  if (!heldMarkers.has(id)) {
    return null;
  }
  const marker = held(id, ref);
  heldMarkers.set(id, marker);
  return marker;
});
const removeLiveMarker = vi.fn((id: string) => heldMarkers.delete(id));
const getLiveMarkers = vi.fn(() => [...heldMarkers.values()]);
const setLiveMarkerClusterLabel = vi.fn();
const setStationMedia = vi.fn();
const clearStationMedia = vi.fn();
const toolbarDispose = vi.fn();
let lastToolbar: { container: unknown; options: unknown } | undefined;

/**
 * Every viewer built, newest last.
 *
 * Kept per instance rather than as one recorded value, because whether the markers are labelled is
 * a property of a viewer that the real one neither saves nor restores — so the question a test has
 * to be able to ask is what the panel left on *this* viewer, the one built for the survey now on
 * screen, and a single module-level flag would answer it with the last write to any of them.
 */
const viewers: FakeViewer[] = [];

class FakeViewer {
  // The real viewer exposes this as a settable property, and what the panel does with it is the
  // whole point of the test — so it is recorded rather than stored.
  get stationLabelOver() {
    return stationLabelOver;
  }
  set stationLabelOver(value: boolean) {
    stationLabelOver = value;
  }
  /** As the real viewer starts: labels on, and nothing restores what the last viewer was left at. */
  liveMarkerLabels = true;
  constructor(_containerId: string, config: Record<string, unknown>) {
    lastViewerConfig = config;
    viewers.push(this);
  }
  addEventListener(type: string, listener: Listener) {
    listeners.set(type, [...(listeners.get(type) ?? []), listener]);
  }
  removeEventListener() {}
  focusStation = focusStation;
  focusSurvey = focusSurvey;
  highlightStation = highlightStation;
  clearHighlight = clearHighlight;
  addLiveMarker = addLiveMarker;
  moveLiveMarker = moveLiveMarker;
  removeLiveMarker = removeLiveMarker;
  getLiveMarkers = getLiveMarkers;
  setLiveMarkerClusterLabel = setLiveMarkerClusterLabel;
  setStationMedia = setStationMedia;
  clearStationMedia = clearStationMedia;
}

class FakeUi {
  loadCave = loadCave;
  dispose() {}
}

class FakeToolbar {
  constructor(_viewer: unknown, container: unknown, options: unknown) {
    lastToolbar = { container, options };
  }
  dispose = toolbarDispose;
}

function emit(type: string, event: unknown) {
  for (const listener of listeners.get(type) ?? []) {
    listener(event);
  }
}

/**
 * What the viewer would draw in place of the markers of one station, asked exactly as it asks.
 *
 * The viewer holds the function and calls it again whenever what it draws is settled, handing over
 * a marker object for each marker it collapsed — so this is the whole of the contract, and calling
 * the registered function with ids is the only way to read back what a group says.
 */
function clusterLabel(...ids: string[]): string[] | null {
  const label = setLiveMarkerClusterLabel.mock.calls.at(-1)?.[0] as
    | ((markers: readonly { id: string }[]) => string[] | null)
    | undefined;
  if (label === undefined) {
    throw new Error('the panel registered no cluster label');
  }
  return label(ids.map((id) => ({ id })));
}

const MODEL = 'model-1';

/** A station of this model, as a link names one. */
const stationRef = (station: string) => ({
  targetType: 'surveyModel',
  targetId: MODEL,
  anchorKind: 'modelStation',
  anchor: { station },
});

/** One station of this model with a picture on it, as the viewer takes them. */
const media = () =>
  new Map([[
    'p.g.7',
    [{ url: 'http://files.local/photo?size=1200', thumbnailUrl: 'http://files.local/photo?size=160' }],
  ]]);

const caver = (overrides: Partial<TrackedCaver> = {}): TrackedCaver => ({
  caverId: 'caver-1',
  name: 'Ana',
  teamId: 'team-a',
  teamTitle: 'Team A',
  position: { kind: 'station', station: 'p.g.7' },
  lastRecordedAt: '2026-09-12T09:00:00Z',
  enteredAt: '2026-09-12T08:00:00Z',
  out: false,
  ...overrides,
  // Follows the latest report unless a test separates them, which is the ordinary case.
  positionAt:
    'positionAt' in overrides ? (overrides.positionAt ?? null) : (overrides.lastRecordedAt ?? '2026-09-12T09:00:00Z'),
});

/** Renders a panel and takes it to the state where the model is loaded. */
async function renderReady(props: Partial<Parameters<typeof CaveViewPanel>[0]> = {}) {
  // Counted from what is already attached rather than from zero: a test that mounts a second
  // panel would otherwise replay the model's arrival before the new viewer had asked for it.
  const attached = listeners.get('newCave')?.length ?? 0;
  const view = render(
    <CaveViewPanel
      fileUrl="http://files.local/survey"
      fileName="demo.lox"
      surveyModelId={MODEL}
      {...props}
    />,
  );
  await waitFor(() => expect(listeners.get('newCave')?.length ?? 0).toBeGreaterThan(attached));
  act(() => emit('newCave', {}));
  await waitFor(() => expect(screen.queryByTestId('caveview-loading')).not.toBeInTheDocument());
  return view;
}

/** The same panel with another watch on it — the element a poll's answer re-renders it as. */
const watching = (trackedCavers: readonly TrackedCaver[]) => (
  <CaveViewPanel
    fileUrl="http://files.local/survey"
    fileName="demo.lox"
    surveyModelId={MODEL}
    trackedCavers={trackedCavers}
  />
);

/** Every call that changes what is on the model, which is the cost a poll is measured in. */
const markerOperations = () =>
  addLiveMarker.mock.calls.length
  + moveLiveMarker.mock.calls.length
  + removeLiveMarker.mock.calls.length;

beforeEach(() => {
  listeners.clear();
  viewers.length = 0;
  missingStations.clear();
  heldMarkers.clear();
  lastViewerConfig = undefined;
  stationLabelOver = false;
  lastToolbar = undefined;
  axes.narrow = false;
  axes.coarse = false;
  vi.clearAllMocks();
  focusStation.mockResolvedValue({});
  focusSurvey.mockResolvedValue(undefined);
  resetViewControlsForTests();
  window.CV2 = {
    CaveViewer: FakeViewer,
    CaveViewUI: FakeUi,
    CaveViewToolbar: FakeToolbar,
  } as unknown as Cv2Namespace;
  vi.stubGlobal('fetch', vi.fn(async () => new Response(new Blob(['survey']))));
});

afterEach(() => {
  cleanup();
  resetViewControlsForTests();
  vi.unstubAllGlobals();
  // Set by the tests that assert what a marker prints: what day it is decides whether the label
  // says one, so those run against a fixed clock rather than against whichever day this suite is
  // run on. Put back here so nothing else in the file inherits it.
  vi.useRealTimers();
  window.CV2 = undefined;
});

/**
 * The day the fixtures below were reported on, as the reader's clock would read it.
 *
 * Every watch in this file is dated 2026-09-12, so a test about a same-day label has to be run on
 * that day and one about an older label on a later one. Left to the real clock both assertions
 * would mean whatever today happened to be, and the pair that matters most — a time from last
 * night against the same time today — could not be written at all.
 */
const REPORTED_DAY = '2026-09-12T12:00:00Z';

/**
 * The clock a marker prints for a moment from the day it is being read on.
 *
 * Composed by the same formatter the label is, so what these tests pin is *which moment* was
 * printed rather than how the reader's locale spells one — the labels are asserted in whatever
 * language this suite happens to be initialised in.
 */
const clockOn = (iso: string) =>
  new Date(iso).toLocaleTimeString(i18n.language, { hour: '2-digit', minute: '2-digit' });

/** A moment in September 2026 as the reader's own clock reads it, wherever this suite is run. */
const localMoment = (day: number, hour: number, minute: number) =>
  new Date(2026, 8, day, hour, minute);

/**
 * The same caver as read from a server that never wrote the position's moment — the property
 * absent rather than null, which no type in this client admits is possible and every read from
 * such a server produces.
 */
const withoutPositionAt = (base: TrackedCaver): TrackedCaver => {
  const { positionAt: _absent, ...rest } = base;
  return rest as TrackedCaver;
};

describe('CaveViewPanel', () => {
  it('constructs the viewer with the versioned home and a CRS lookup function', async () => {
    render(<CaveViewPanel fileUrl="http://files.local/survey" fileName="demo.lox" />);

    await waitFor(() => expect(lastViewerConfig).toBeDefined());
    // The versioned home is what makes an upgraded viewer reach the browser at all, and the
    // crsLookup entry is the only thing keeping the bundle's built-in epsg.io fallback
    // unreachable — dropping either would leave every other test green.
    expect(lastViewerConfig!.home).toBe(CAVEVIEW_HOME);
    expect(typeof lastViewerConfig!.crsLookup).toBe('function');
  });

  it('reports entrance label clicks through onEntrancePick', async () => {
    const onPick = vi.fn();
    render(<CaveViewPanel fileUrl="http://files.local/survey" fileName="demo.lox" onEntrancePick={onPick} />);

    await waitFor(() => expect(listeners.get('entrance')?.length ?? 0).toBeGreaterThan(0));
    emit('entrance', { type: 'entrance', displayName: 'Main entrance' });

    expect(onPick).toHaveBeenCalledWith('Main entrance');
  });

  it('ignores entrance events without a display name and leaves loading on newCave', async () => {
    const onPick = vi.fn();
    render(<CaveViewPanel fileUrl="http://files.local/survey" fileName="demo.lox" onEntrancePick={onPick} />);

    await waitFor(() => expect(listeners.get('entrance')?.length ?? 0).toBeGreaterThan(0));
    emit('entrance', { type: 'entrance' });
    expect(onPick).not.toHaveBeenCalled();

    expect(screen.getByTestId('caveview-loading')).toBeInTheDocument();
    emit('newCave', {});
    await waitFor(() => expect(screen.queryByTestId('caveview-loading')).not.toBeInTheDocument());
  });

  describe('answering a link', () => {
    it('moves the camera to a station instead of loading the survey again', async () => {
      await renderReady();
      loadCave.mockClear();

      act(() => reveal(stationRef('p.g.7')));

      expect(focusStation).toHaveBeenCalledWith('p.g.7');
      // The whole point of the change: following a link no longer re-parses the model, which
      // is what made every link cost a full load and threw away the view somebody was at.
      expect(loadCave).not.toHaveBeenCalled();
    });

    it('frames a survey rather than reducing it to a station', async () => {
      // The case that did not work while a link could only be answered by reloading: a run of
      // surveys has no station to name, so nothing moved.
      await renderReady();

      act(() =>
        reveal({
          targetType: 'surveyModel',
          targetId: MODEL,
          anchorKind: 'modelSurvey',
          anchor: { survey: 'p.g' },
        }),
      );

      expect(focusSurvey).toHaveBeenCalledWith('p.g');
      expect(focusStation).not.toHaveBeenCalled();
    });

    it('says so when the model does not hold the point a link names', async () => {
      // A real state rather than a failure: a survey re-exported with its sections renamed
      // leaves every anchor written against the old names pointing at nothing.
      focusStation.mockRejectedValue(new Error('No station [p.g.7] in the loaded survey'));
      await renderReady();

      act(() => reveal(stationRef('p.g.7')));

      expect(await screen.findByTestId('caveview-missing-part')).toHaveTextContent(
        'That point is not in this model',
      );
    });

    it('stays quiet when a second link supersedes the first', async () => {
      // Following two links in quick succession is the ordinary case, and the abandoned move
      // rejects. Saying anything about it would report normal use as a fault.
      focusStation.mockRejectedValue(new Error('superseded'));
      await renderReady();

      act(() => reveal(stationRef('p.g.7')));

      await waitFor(() => expect(focusStation).toHaveBeenCalled());
      expect(screen.queryByTestId('caveview-missing-part')).not.toBeInTheDocument();
    });

    it('stays quiet about an abandonment it has never seen worded that way', async () => {
      // The one that decides which way the triage fails. Both the failures and the abandonments
      // are told apart by a message the vendored viewer writes, so a later build wording one
      // differently is a real prospect — and the direction it must fail in is silence. Treating
      // an unrecognised rejection as "this model does not hold that point" would answer a link
      // that worked perfectly by telling its reader it points at nothing.
      focusStation.mockRejectedValue(new Error('camera move abandoned'));
      await renderReady();

      act(() => reveal(stationRef('p.g.7')));

      await waitFor(() => expect(focusStation).toHaveBeenCalled());
      expect(screen.queryByTestId('caveview-missing-part')).not.toBeInTheDocument();
    });

    it('says so when a survey section a link names is not in this model either', async () => {
      focusSurvey.mockRejectedValue(new Error('No survey section [p.g] in the loaded survey'));
      await renderReady();

      act(() =>
        reveal({
          targetType: 'surveyModel',
          targetId: MODEL,
          anchorKind: 'modelSurvey',
          anchor: { survey: 'p.g' },
        }),
      );

      expect(await screen.findByTestId('caveview-missing-part')).toBeInTheDocument();
    });

    it('refuses a link to another cave’s model', async () => {
      await renderReady();

      act(() =>
        reveal({
          targetType: 'surveyModel',
          targetId: 'another-model',
          anchorKind: 'modelStation',
          anchor: { station: 'p.g.7' },
        }),
      );

      expect(focusStation).not.toHaveBeenCalled();
    });
  });

  describe('live markers', () => {
    it('adds, slides and removes markers as the watch is re-read', async () => {
      const { rerender } = await renderReady({ trackedCavers: [caver()] });

      expect(addLiveMarker).toHaveBeenCalledWith('caver-1', 'p.g.7', {
        label: 'Ana',
        color: expect.any(String),
      });

      // A new position slides rather than being added again: the marker moving is what tells
      // somebody watching that a report came in.
      rerender(
        <CaveViewPanel
          fileUrl="http://files.local/survey"
          fileName="demo.lox"
          surveyModelId={MODEL}
          trackedCavers={[caver({ position: { kind: 'station', station: 'p.g.9' } })]}
        />,
      );
      expect(moveLiveMarker).toHaveBeenCalledWith('caver-1', 'p.g.9', expect.any(Object));
      expect(addLiveMarker).toHaveBeenCalledTimes(1);

      rerender(
        <CaveViewPanel
          fileUrl="http://files.local/survey"
          fileName="demo.lox"
          surveyModelId={MODEL}
          trackedCavers={[]}
        />,
      );
      expect(removeLiveMarker).toHaveBeenCalledWith('caver-1');
    });

    it('draws whoever has come out in a quieter colour, and in one the viewer can keep', async () => {
      // The safety-relevant distinction on this surface, and one that cannot be carried in an
      // alpha channel: the viewer hands a marker's colour to a point material whose parser keeps
      // the three colour channels of an `rgba()` string and discards the transparency. A muted
      // interface token — 25% black in the light theme, 25% white in the dark — therefore arrives
      // as solid black or solid white, the second of which outshines everybody still underground.
      const { rerender } = await renderReady({ trackedCavers: [caver()] });

      expect(addLiveMarker).toHaveBeenLastCalledWith(
        'caver-1',
        'p.g.7',
        expect.objectContaining({ color: trackedCaverPalette.underground }),
      );

      rerender(
        <CaveViewPanel
          fileUrl="http://files.local/survey"
          fileName="demo.lox"
          surveyModelId={MODEL}
          trackedCavers={[caver({ out: true })]}
        />,
      );
      expect(moveLiveMarker).toHaveBeenLastCalledWith(
        'caver-1',
        'p.g.7',
        expect.objectContaining({ color: trackedCaverPalette.out }),
      );

      for (const color of [trackedCaverPalette.underground, trackedCaverPalette.out]) {
        expect(color).toMatch(/^#[0-9a-f]{6}$/i);
      }
    });

    it('draws no marker for a position that was withheld, and says it was withheld', async () => {
      // The rule that must not soften: no marker is invented for a position this reader was not
      // told, and the person is still on the list saying exactly that.
      await renderReady({
        trackedCavers: [caver({ position: { kind: 'withheld', certain: true } })],
      });

      expect(addLiveMarker).not.toHaveBeenCalled();
      expect(screen.getByTestId('caveview-position-withheld')).toBeInTheDocument();
    });

    it('says so when the model turns out to hold no station of the name a report gave', async () => {
      // The defect this pair exists for. The viewer accepts a marker for a station the loaded
      // survey has no node for, keeps it, draws it nowhere and reports it back unresolved — and
      // the list beside the model went on naming the caver and the station, over a model showing
      // nobody. Nothing was logged and nothing was said.
      missingStations.add('p.g.7');

      await renderReady({ trackedCavers: [caver()] });

      expect(addLiveMarker).toHaveBeenCalledWith('caver-1', 'p.g.7', expect.any(Object));
      const row = screen.getByTestId('caveview-caver-caver-1');
      expect(within(row).getByTestId('caveview-position-not-on-model')).toBeInTheDocument();
      // And the heading over them, which names a station of its own drawn from the same members:
      // a team read as standing at a place the drawing cannot show is the same false sentence one
      // line up.
      const team = screen.getByTestId('caveview-team-team-a');
      expect(within(team).getByTestId('caveview-position-not-on-model')).toBeInTheDocument();
    });

    it('says nothing of the kind about a station the model does hold', async () => {
      // The twin, and the one that makes the test above mean something: the same watch, the same
      // station, a model that holds it — and the row reads as the place it is.
      await renderReady({ trackedCavers: [caver()] });

      const row = screen.getByTestId('caveview-caver-caver-1');
      expect(within(row).queryByTestId('caveview-position-not-on-model')).not.toBeInTheDocument();
      expect(row).toHaveTextContent(shortNameOf('p.g.7'));
    });

    it('offers no flight to a station the drawing does not hold, and still opens the card', async () => {
      // Pressing a row is how a reader asks "which of these is that". A station the model has no
      // node for answers that with a rejected move and a notice about a link nobody followed, so
      // the row stops offering the flight — while staying the tap path to the person's own card,
      // which is the only one a phone has.
      missingStations.add('p.g.7');
      await renderReady({ trackedCavers: [caver()] });

      fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));

      expect(focusStation).not.toHaveBeenCalled();
      expect(screen.getByTestId('caveview-caver-card')).toBeInTheDocument();
      expect(screen.getByTestId('caveview-caver-card-not-on-model')).toBeInTheDocument();
    });

    it('still flies to a station the drawing does hold', async () => {
      // The twin. Same press, same row, a model that holds the station.
      await renderReady({ trackedCavers: [caver()] });

      fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));

      await waitFor(() =>
        expect(focusStation).toHaveBeenCalledWith('p.g.7', { highlight: true }),
      );
      expect(screen.queryByTestId('caveview-caver-card-not-on-model')).not.toBeInTheDocument();
    });

    it('tells whoever mounted it which stations the model could not place a marker at', async () => {
      // The table above this panel and a published page's list of people say the same stations in
      // words, and neither of them can ask a viewer anything. So the answer leaves the panel.
      missingStations.add('p.g.7');
      const answers: ReadonlySet<string>[] = [];

      await renderReady({
        trackedCavers: [caver(), caver({ caverId: 'caver-2', name: 'Radu', position: { kind: 'station', station: 'p.g.9' } })],
        onUnplacedStationsChange: (stations: ReadonlySet<string>) => answers.push(stations),
      });

      // The station, not the person standing at it: what the viewer knows is a property of the
      // file it parsed, and the surfaces reading this answer are not always listing this party.
      expect([...(answers.at(-1) ?? [])]).toEqual(['p.g.7']);
    });

    it('goes on saying it when the party being drawn is replaced', async () => {
      // <b>What engaging the replay under the coordinator's panel does.</b> The viewer is handed
      // the watch as it stood at a past moment, so every live marker comes off the model — and an
      // answer rebuilt from the markers standing at that instant would empty itself, while the
      // table above went on printing those same stations with a freshness age under them. The
      // drawing has not changed and neither has what it holds.
      missingStations.add('p.g.7');
      const answers: ReadonlySet<string>[] = [];
      const listen = (stations: ReadonlySet<string>) => answers.push(stations);
      const view = await renderReady({
        trackedCavers: [caver()],
        onUnplacedStationsChange: listen,
      });
      expect([...(answers.at(-1) ?? [])]).toEqual(['p.g.7']);

      // The party of an earlier moment: nobody had been reported yet, so there is nothing to draw.
      view.rerender(
        <CaveViewPanel
          fileUrl="http://files.local/survey"
          fileName="demo.lox"
          surveyModelId={MODEL}
          trackedCavers={[]}
          onUnplacedStationsChange={listen}
        />,
      );

      expect(removeLiveMarker).toHaveBeenCalledWith('caver-1');
      expect([...(answers.at(-1) ?? [])]).toEqual(['p.g.7']);
    });

    it('never says it of a station the drawing turned out to hold', async () => {
      // The twin of the pair above, driven the same way: a party replaced by another party teaches
      // nothing about the stations of either, and a station the model placed is never named.
      missingStations.add('p.g.7');
      const answers: ReadonlySet<string>[] = [];
      const listen = (stations: ReadonlySet<string>) => answers.push(stations);
      const view = await renderReady({
        trackedCavers: [caver()],
        onUnplacedStationsChange: listen,
      });

      view.rerender(
        <CaveViewPanel
          fileUrl="http://files.local/survey"
          fileName="demo.lox"
          surveyModelId={MODEL}
          trackedCavers={[caver({ caverId: 'caver-2', name: 'Radu', position: { kind: 'station', station: 'p.g.9' } })]}
          onUnplacedStationsChange={listen}
        />,
      );

      expect([...(answers.at(-1) ?? [])]).toEqual(['p.g.7']);
      expect(answers.at(-1)?.has('p.g.9')).toBe(false);
    });

    it('claims nothing about a drawing that is no longer on the screen', async () => {
      // A panel taken off the screen has stopped answering the question, and the answer it gave
      // last belongs to a model nobody is looking at. Left standing it would mark a table against
      // a drawing that has been closed.
      missingStations.add('p.g.7');
      const answers: ReadonlySet<string>[] = [];
      const view = await renderReady({
        trackedCavers: [caver()],
        onUnplacedStationsChange: (stations: ReadonlySet<string>) => answers.push(stations),
      });
      expect([...(answers.at(-1) ?? [])]).toEqual(['p.g.7']);

      view.unmount();

      expect([...(answers.at(-1) ?? [])]).toEqual([]);
    });

    it('puts a lone caver’s time on their label, where the switch can also take it off', async () => {
      // One switch, one meaning. The time used to go on the hover line of a marker drawn alone
      // and onto the label of a marker collapsed with others — so "show last update" showed
      // nothing at all until the pointer rested on somebody, unless that somebody happened to
      // have company at their station, which is a difference the reader neither asked for nor
      // can see. Both read off the label now, and taking it off is a move rather than the
      // add-again dance a sublabel needed, because every option is given on every call.
      //
      // Run on the day the fixture was reported, so what is asserted is that a time appears at all
      // rather than which spelling of one a label from another day would take.
      vi.setSystemTime(new Date(REPORTED_DAY));
      await renderReady({ trackedCavers: [caver()] });

      fireEvent.click(screen.getByTestId('caveview-tracking-times'));
      expect(moveLiveMarker).toHaveBeenLastCalledWith(
        'caver-1',
        'p.g.7',
        expect.objectContaining({ label: expect.stringMatching(/^Ana · \d/) }),
      );
      expect(moveLiveMarker.mock.calls.at(-1)![2]).not.toHaveProperty('sublabel');

      fireEvent.click(screen.getByTestId('caveview-tracking-times'));
      expect(moveLiveMarker).toHaveBeenLastCalledWith(
        'caver-1',
        'p.g.7',
        expect.objectContaining({ label: 'Ana' }),
      );
      expect(addLiveMarker).toHaveBeenCalledTimes(1);
    });

    /**
     * <b>The time on a marker is the moment the position was reported, and it has to be.</b> A
     * marker is a station: a clock printed against it is read as when that person was there. Ana
     * was placed at p.g.7 at nine and radioed "all fine" at five to twelve — the label used to
     * carry the second of those, so a station three hours old was drawn under a five-minute-old
     * time on the surface somebody reads while deciding whether a team is overdue.
     */
    it('puts the time the position was reported on the label, not the last word', async () => {
      vi.setSystemTime(new Date(REPORTED_DAY));
      await renderReady({
        trackedCavers: [
          caver({
            lastRecordedAt: '2026-09-12T11:55:00Z',
            positionAt: '2026-09-12T09:00:00Z',
          }),
        ],
      });

      fireEvent.click(screen.getByTestId('caveview-tracking-times'));
      const label = moveLiveMarker.mock.calls.at(-1)![2] as { label: string };

      // Compared against the clock the label is composed in, so this pins which moment was
      // printed rather than anything about the reader's locale.
      expect(label.label).toBe(`Ana · ${clockOn('2026-09-12T09:00:00Z')}`);
      expect(label.label).not.toContain(clockOn('2026-09-12T11:55:00Z'));
    });

    /**
     * <b>A bare clock beside a name used to be unambiguous and is not any more, and the change is
     * what made it so.</b> The moment printed here was the last word about somebody — refreshed by
     * every radio check, so in practice always from the last few minutes, and "22:10" could only
     * mean tonight. It is now the position's own moment, which the rest of this change exists
     * because it is routinely hours older and on an overnight trip is from yesterday. A party
     * placed at ten past ten at night and heard from through the night would draw at seven the next
     * morning as "Ana · 22:10": the right number and the wrong day, on the surface somebody reads
     * while deciding whether that team is overdue.
     *
     * The pair is the whole test. The same clock time, reported on two different days, must not
     * produce the same label — which is precisely what it did before, and is a thing no assertion
     * about one label alone can catch.
     */
    it('says which day a position is from once it is not from today', async () => {
      // Built from local components rather than from UTC text, because what "today" means here is
      // the reader's own midnight: 22:10 UTC is already tomorrow in some of the time zones this
      // suite runs in, and a fixture written as UTC would be testing the machine's offset.
      const lastNightAt = localMoment(12, 22, 10);
      const tonightAt = localMoment(13, 22, 10);

      vi.setSystemTime(localMoment(13, 7, 0));
      await renderReady({ trackedCavers: [caver({ positionAt: lastNightAt.toISOString() })] });
      fireEvent.click(screen.getByTestId('caveview-tracking-times'));
      const lastNight = (moveLiveMarker.mock.calls.at(-1)![2] as { label: string }).label;

      // The same clock time, reported tonight instead: these are the two readings a bare clock
      // could not tell apart, and they were the same string before this change.
      cleanup();
      vi.setSystemTime(localMoment(13, 23, 30));
      await renderReady({ trackedCavers: [caver({ positionAt: tonightAt.toISOString() })] });
      fireEvent.click(screen.getByTestId('caveview-tracking-times'));
      const tonight = (moveLiveMarker.mock.calls.at(-1)![2] as { label: string }).label;

      expect(lastNight).not.toBe(tonight);
      // And the positive halves, so this cannot pass by the label having become unreadable: the
      // older one carries the day it was reported on, and today's is still the bare clock a reader
      // glancing at a live model wants.
      expect(lastNight).toContain(
        lastNightAt.toLocaleString(i18n.language, {
          month: 'short',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
        }),
      );
      expect(tonight).toBe(`Ana · ${clockOn(tonightAt.toISOString())}`);
    });

    /**
     * <b>The three ways a moment goes missing, on the surface that says the least about it.</b> A
     * marker whose moment is absent, never written by the server, or unparsable reaches
     * `toLocaleTimeString`, which does not refuse any of them — it returns the words "Invalid
     * Date", and the model draws them beside somebody's name as though that were a report. Only
     * the first of the three was guarded, and it is the only one of the three that a type error
     * would ever have caught.
     */
    it('prints no time at all for a moment it cannot read, rather than the words Invalid Date', async () => {
      vi.setSystemTime(new Date(REPORTED_DAY));
      await renderReady({
        trackedCavers: [
          caver({ caverId: 'a', name: 'Ana', positionAt: null }),
          // What a read answered by anything that never wrote the field actually produces: not a
          // null, but no property at all. Built by taking the key off rather than by setting it to
          // undefined, because the factory above — like any code written against the generated
          // type, which declares this field required — folds an undefined back into null and would
          // quietly test the case that was already guarded.
          withoutPositionAt(caver({ caverId: 'b', name: 'Bogdan' })),
          caver({ caverId: 'c', name: 'Cora', positionAt: 'not-a-date' }),
          caver({ caverId: 'd', name: 'Dana', positionAt: '2026-09-12T09:00:00Z' }),
        ],
      });

      fireEvent.click(screen.getByTestId('caveview-tracking-times'));

      // What each marker says *now*, which is after the switch was turned on — the add calls that
      // drew them before that carry a label with no time in it and would answer this question with
      // a pass whatever the code did.
      const drawn = new Map<string, string>();
      for (const call of [...addLiveMarker.mock.calls, ...moveLiveMarker.mock.calls]) {
        drawn.set(call[0] as string, (call[2] as { label: string }).label);
      }

      // Named one at a time so the three ways a moment goes missing are three assertions: an
      // absent one, one a server never wrote, and one that will not parse.
      expect(drawn.get('a')).toBe('Ana');
      expect(drawn.get('b')).toBe('Bogdan');
      expect(drawn.get('c')).toBe('Cora');
      // The twin: a moment that reads is still printed, so the three above are silent about a
      // missing moment rather than about the switch having stopped working.
      expect(drawn.get('d')).toBe(`Dana · ${clockOn('2026-09-12T09:00:00Z')}`);
    });

    it('opens a caver’s card when the pointer rests on their marker', async () => {
      await renderReady({ trackedCavers: [caver()] });

      await waitFor(() => expect(listeners.get('liveMarkerHover')?.length ?? 0).toBeGreaterThan(0));
      act(() => emit('liveMarkerHover', { id: 'caver-1' }));

      expect(await screen.findByTestId('caveview-caver-card')).toHaveTextContent('Team A');
    });
  });

  /**
   * What the markers say, and the switch that takes it off.
   *
   * <b>A party at one station is drawn as one marker, and the viewer knows only how many they
   * are.</b> So the question this surface exists to answer — who is where — was answered with "3"
   * for exactly the case where it matters most, a team standing together underground. The label is
   * the application's to write, and what is asserted here is what it writes: the team's name and
   * then its members, one to a line.
   */
  describe('names on the model', () => {
    it('names a party standing together with its team and every one of its members', async () => {
      await renderReady({
        trackedCavers: [caver({ caverId: 'a', name: 'Ana' }), caver({ caverId: 'b', name: 'Bogdan' })],
      });

      expect(clusterLabel('a', 'b')).toEqual(['Team A', 'Ana', 'Bogdan']);
    });

    it('leaves a caver drawn on their own labelled with their own name', async () => {
      // Including one who is on a team. A heading over a single name would be a claim that the
      // team is at that station, which is true of a party standing together and is not true of one
      // of its members — and the name is already the whole answer to "who is that".
      await renderReady({ trackedCavers: [caver({ caverId: 'a', name: 'Ana' })] });

      expect(addLiveMarker).toHaveBeenCalledWith(
        'a',
        'p.g.7',
        expect.objectContaining({ label: 'Ana' }),
      );
    });

    it('heads no group whose members are not one team', async () => {
      // Two teams that happen to have met at one station are not a team, and everybody on no team
      // has no name to be called by: in both cases the names are the whole of what is known, and a
      // word over them would say more than anybody reported.
      await renderReady({
        trackedCavers: [
          caver({ caverId: 'a', name: 'Ana' }),
          caver({ caverId: 'c', name: 'Cora', teamId: 'team-b', teamTitle: 'Team B' }),
        ],
      });
      expect(clusterLabel('a', 'c')).toEqual(['Ana', 'Cora']);

      cleanup();
      await renderReady({
        trackedCavers: [
          caver({ caverId: 'a', name: 'Ana', teamId: null, teamTitle: null }),
          caver({ caverId: 'b', name: 'Bogdan', teamId: null, teamTitle: null }),
        ],
      });
      expect(clusterLabel('a', 'b')).toEqual(['Ana', 'Bogdan']);
    });

    it('names only the markers collapsed, never the team standing behind them', async () => {
      // The rule this whole surface is built around, in the one place a multi-line label could
      // quietly break it: a label built from a team's roster would name somebody whose position
      // this reader may not be told, and would name them *at a station* — an assertion about where
      // a caver is, made out of a withholding. So the lines come from the ids the viewer collapsed.
      await renderReady({
        trackedCavers: [
          caver({ caverId: 'a', name: 'Ana' }),
          caver({ caverId: 'b', name: 'Bogdan', position: { kind: 'withheld', certain: true } }),
          caver({ caverId: 'c', name: 'Cora' }),
        ],
      });

      expect(addLiveMarker).not.toHaveBeenCalledWith('b', expect.anything(), expect.anything());
      const label = clusterLabel('a', 'c');
      expect(label).toEqual(['Team A', 'Ana', 'Cora']);
      expect(label).not.toContain('Bogdan');
    });

    it('puts the time each position was reported beside each name, where that was asked for', async () => {
      // The switch beside this one, honoured for a group, and honoured the same way for somebody
      // standing alone: a collapsed marker has no hover line to put a time on, so inline is the
      // only spelling that can be the same on both sides of a party gathering at one station.
      //
      // Bogdan's two moments disagree — placed at half past ten, heard from at five to twelve —
      // so the line that names him also says which of them a collapsed label prints.
      vi.setSystemTime(new Date(REPORTED_DAY));
      await renderReady({
        trackedCavers: [
          caver({ caverId: 'a', name: 'Ana' }),
          caver({
            caverId: 'b',
            name: 'Bogdan',
            lastRecordedAt: '2026-09-12T11:55:00Z',
            positionAt: '2026-09-12T10:30:00Z',
          }),
        ],
      });

      fireEvent.click(screen.getByTestId('caveview-tracking-times'));

      const label = clusterLabel('a', 'b')!;
      expect(label[0]).toBe('Team A');
      expect(label[1]).toMatch(/^Ana · \d/);
      expect(label[2]).toBe(`Bogdan · ${clockOn('2026-09-12T10:30:00Z')}`);
      // And not the moment of his last word, which is the pair this label used to print.
      expect(label[2]).not.toContain(clockOn('2026-09-12T11:55:00Z'));
    });

    /**
     * Whether what the label says reaches the model at all.
     *
     * <b>The viewer asks the label function, and only when a marker is added, slid or removed.</b>
     * So a poll that changed an input of the label without moving anybody used to change nothing
     * on screen: the table and the list beside the model followed a rename and the model went on
     * drawing the old name. Setting the function again is what re-asks it, which is why these
     * tests assert on the registration and not only on what the registered function answers — the
     * function reads the watch through a ref and would answer correctly either way.
     */
    describe('reaching the model when nobody has moved', () => {
      const party = (teamTitle: string) => [
        caver({ caverId: 'a', name: 'Ana', teamTitle }),
        caver({ caverId: 'b', name: 'Bogdan', teamTitle }),
      ];

      it('follows a team being renamed, with no marker moving', async () => {
        const { rerender } = await renderReady({ trackedCavers: party('Team A') });
        expect(clusterLabel('a', 'b')).toEqual(['Team A', 'Ana', 'Bogdan']);
        const asked = setLiveMarkerClusterLabel.mock.calls.length;
        const operations = markerOperations();

        rerender(watching(party('The far team')));

        // Two surfaces on one screen disagreeing about one team is what this stops: the list
        // follows the rename by re-rendering, and the model follows it only by being re-asked.
        expect(setLiveMarkerClusterLabel.mock.calls.length).toBeGreaterThan(asked);
        expect(clusterLabel('a', 'b')).toEqual(['The far team', 'Ana', 'Bogdan']);
        // And the party is exactly where it was, which is the half of this that must not be
        // bought by redrawing anybody.
        expect(markerOperations()).toBe(operations);
      });

      it('stops heading a group the moment one of them is on another team', async () => {
        // The worse half of the same defect. A heading is the claim that the names under it are
        // one team, so a stale one goes on asserting a single team over a set that has become a
        // mixture — which is the one claim the shared-title rule exists to refuse.
        const { rerender } = await renderReady({ trackedCavers: party('Team A') });
        expect(clusterLabel('a', 'b')).toEqual(['Team A', 'Ana', 'Bogdan']);
        const asked = setLiveMarkerClusterLabel.mock.calls.length;
        const operations = markerOperations();

        rerender(
          watching([
            caver({ caverId: 'a', name: 'Ana' }),
            caver({ caverId: 'b', name: 'Bogdan', teamId: 'team-b', teamTitle: 'Team B' }),
          ]),
        );

        expect(setLiveMarkerClusterLabel.mock.calls.length).toBeGreaterThan(asked);
        expect(clusterLabel('a', 'b')).toEqual(['Ana', 'Bogdan']);
        expect(markerOperations()).toBe(operations);
      });

      it('asks the viewer nothing again for a poll that changed nothing', async () => {
        // The other half of the fix, and the one that is easy to lose: re-registering on every
        // re-read would rebuild every collapsed marker twice a minute for a party that has not
        // moved. A poll arrives as a fresh array of fresh objects, so identity says nothing and
        // the comparison has to be over what the label is actually read from.
        const { rerender } = await renderReady({ trackedCavers: party('Team A') });
        const asked = setLiveMarkerClusterLabel.mock.calls.length;
        const operations = markerOperations();

        rerender(watching(party('Team A')));

        expect(setLiveMarkerClusterLabel.mock.calls.length).toBe(asked);
        expect(markerOperations()).toBe(operations);
      });
    });

    /**
     * What a group says about somebody who has come out.
     *
     * A marker drawn alone says it in the muted colour. Collapsed, that is gone — one dot in one
     * colour over a list of names — and the label was making the strongest claim on this surface,
     * that these people are at this station, about people who had left it. Nobody is dropped from
     * the label: the marker stands for their marker too, and the station really is the last place
     * they were reported. What changes is that no line of it reads as somebody present.
     */
    describe('who has come out', () => {
      it('does not read as all present when some of a group have come out', async () => {
        await renderReady({
          trackedCavers: [
            caver({ caverId: 'a', name: 'Ana' }),
            caver({ caverId: 'b', name: 'Bogdan' }),
            caver({ caverId: 'c', name: 'Cora', out: true }),
            caver({ caverId: 'd', name: 'Dan', out: true }),
            caver({ caverId: 'e', name: 'Elena' }),
          ],
        });

        const label = clusterLabel('a', 'b', 'c', 'd', 'e')!;
        // Five names at one station, two of whom are above ground. Whoever is still underground
        // is the block at the top, so the answer to "is this party still down there" is the shape
        // of the label rather than five lines to be read one at a time.
        expect(label).toEqual(['Team A', 'Ana', 'Bogdan', 'Elena', 'Cora (out)', 'Dan (out)']);
        expect(label).not.toContain('Cora');
        expect(label).not.toContain('Dan');
      });

      it('says it of a caver drawn on their own too, rather than only in the colour', async () => {
        // The same person one report later can be alone at their station or collapsed with the
        // rest of their team, and which of the two is the viewer's decision — so a fact carried
        // one way and not the other appears and disappears under a reader doing nothing. The
        // colour stays; it is simply not the only thing saying this.
        await renderReady({ trackedCavers: [caver({ caverId: 'a', name: 'Ana', out: true })] });

        expect(addLiveMarker).toHaveBeenCalledWith(
          'a',
          'p.g.7',
          expect.objectContaining({ label: 'Ana (out)', color: trackedCaverPalette.out }),
        );
      });

      it('reads the heading from everybody collapsed, whoever has come out included', async () => {
        // Taking the title from the people still underground would head this block with one
        // team's name and leave the other team below the fold — the same false claim, told by
        // the fix for a different one.
        await renderReady({
          trackedCavers: [
            caver({ caverId: 'a', name: 'Ana' }),
            caver({ caverId: 'c', name: 'Cora', teamId: 'team-b', teamTitle: 'Team B', out: true }),
          ],
        });

        expect(clusterLabel('a', 'c')).toEqual(['Ana', 'Cora (out)']);
      });
    });

    it('leaves the viewer its own count when the watch holds none of them', async () => {
      // Answering null is what leaves the count drawn. A group the panel can say nothing about is
      // better drawn as "2" than as a label built out of whatever is left of a stale watch.
      await renderReady({ trackedCavers: [caver({ caverId: 'a' })] });

      expect(clusterLabel('gone-1', 'gone-2')).toBeNull();
    });

    it('takes the labels off the viewer and leaves every marker on the model', async () => {
      await renderReady({ trackedCavers: [caver({ caverId: 'a' })] });
      expect(viewers.at(-1)!.liveMarkerLabels).toBe(true);

      fireEvent.click(screen.getByTestId('caveview-tracking-labels'));

      expect(viewers.at(-1)!.liveMarkerLabels).toBe(false);
      // The point of the property over simply not drawing the markers: they stay on the model and
      // stay pointable, so a crowded screen is cleared without anybody disappearing from it.
      expect(removeLiveMarker).not.toHaveBeenCalled();

      fireEvent.click(screen.getByTestId('caveview-tracking-labels'));
      expect(viewers.at(-1)!.liveMarkerLabels).toBe(true);
    });

    it('asks again for each survey loaded, because a new viewer starts with its labels on', async () => {
      // Whether the markers are labelled is deliberately none of the view state the viewer saves,
      // and this panel builds a *new* viewer for every survey file it is pointed at. So nothing
      // but this panel will put the setting back, and without that the second cave somebody opens
      // brings back the labels they had just taken off — with the switch still reading off.
      const { rerender } = await renderReady({ trackedCavers: [caver({ caverId: 'a' })] });
      fireEvent.click(screen.getByTestId('caveview-tracking-labels'));
      expect(viewers.at(-1)!.liveMarkerLabels).toBe(false);

      rerender(
        <CaveViewPanel
          fileUrl="http://files.local/another-survey"
          fileName="another.lox"
          surveyModelId={MODEL}
          trackedCavers={[caver({ caverId: 'a' })]}
        />,
      );
      await waitFor(() => expect(viewers).toHaveLength(2));
      act(() => emit('newCave', {}));

      await waitFor(() => expect(viewers.at(-1)!.liveMarkerLabels).toBe(false));
      expect(screen.getByTestId('caveview-tracking-labels')).not.toBeChecked();
    });
  });

  /**
   * Pressing a name in the list and having the camera go there.
   *
   * The list is drawn by this panel and the viewer is held by it, so the move is made here — the
   * list has no way to reach a viewer and is not given one. What is asserted is the call and the
   * mark: a camera that arrives somewhere with nothing marked has answered "somewhere around
   * here", which on a model of two hundred stations is not an answer.
   */
  describe('showing the place a list row names', () => {
    it('flies to the station and marks it, and unmarks it when the row is pressed again', async () => {
      await renderReady({ trackedCavers: [caver()] });
      focusStation.mockClear();

      fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
      await waitFor(() =>
        expect(focusStation).toHaveBeenCalledWith('p.g.7', { highlight: true }),
      );

      fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));
      // One call, and the viewer's own selection highlight is what it takes off — the mark
      // survives the model being turned and zoomed, so nothing else could remove it.
      await waitFor(() => expect(clearHighlight).toHaveBeenCalled());
    });

    it('says so when the watch names a station this model turns out not to hold', async () => {
      // A real state rather than a failure: a survey re-exported with its sections renamed leaves
      // every station name the watch was resolved against pointing at nothing.
      focusStation.mockRejectedValue(new Error('No station [p.g.7] in the loaded survey'));
      await renderReady({ trackedCavers: [caver()] });

      fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));

      expect(await screen.findByTestId('caveview-missing-part')).toBeInTheDocument();
    });

    it('stays quiet when a second row supersedes the first', async () => {
      // Pressing two names in quick succession is ordinary use, and the abandoned move rejects.
      focusStation.mockRejectedValue(new Error('superseded'));
      await renderReady({ trackedCavers: [caver()] });

      fireEvent.click(screen.getByTestId('caveview-caver-caver-1'));

      await waitFor(() => expect(focusStation).toHaveBeenCalled());
      expect(screen.queryByTestId('caveview-missing-part')).not.toBeInTheDocument();
    });
  });

  describe('the viewer’s own chrome', () => {
    it('mounts a toolbar over the model only when asked, and takes it down with the panel', async () => {
      const plain = await renderReady();
      expect(lastToolbar).toBeUndefined();
      plain.unmount();

      const { unmount } = await renderReady({ toolbar: { placement: 'bottom' } });
      await waitFor(() => expect(lastToolbar).toBeDefined());
      expect(lastToolbar!.container).toBe(screen.getByTestId('caveview-container').id);
      expect(lastToolbar!.options).toMatchObject({ placement: 'bottom' });
      expect((lastToolbar!.options as { buttons: string[] }).buttons.length).toBeGreaterThan(0);

      unmount();
      expect(toolbarDispose).toHaveBeenCalled();
    });

    /**
     * The controls that decide which way the cave is looked at.
     *
     * <b>They exist in the toolbar and were left out of the set this application asks for</b>, on
     * the reasoning that both were "already in the side panel" — the vendored viewer's own drawer,
     * which none of the four surfaces that draw a model here mounts. So a reader had a plan view
     * and no way at all to look at a cave from the side, which is how depth is read. Asserted as
     * the set handed to the toolbar rather than as pixels, because what the viewer then draws is
     * the viewer's business and the choice is this panel's.
     */
    it('offers the elevations and both projections where there is room across', async () => {
      await renderReady({ toolbar: true });
      await waitFor(() => expect(lastToolbar).toBeDefined());

      const buttons = (lastToolbar!.options as { buttons: string[] }).buttons;
      for (const control of [
        'viewPlan',
        'viewNorth',
        'viewEast',
        'viewSouth',
        'viewWest',
        'cameraPerspective',
        'cameraOrthographic',
      ]) {
        expect(buttons, control).toContain(control);
      }
    });

    /**
     * A screen that is wide and driven by a finger at the same time.
     *
     * <b>The set was chosen on width alone, and a phone held sideways is 863px wide.</b> So it took
     * the mouse branch while every control in the bar was being drawn at the forty pixels a finger
     * needs, and the bar wrapped: measured on the live instance in that profile, 709x98 in two rows
     * over a model surface of 709x216 — 45% of the model spent on chrome, on the one profile this
     * feature is written for. Thirteen controls fit there at 638px, and twelve is what also fits at
     * 768px, the narrowest width this branch can be reached at, where the surface is 614px and
     * twelve come to 594px.
     *
     * Asserted as the set rather than as pixels: what a bar of forty-pixel controls then measures is
     * the viewer's business, and the count is the constraint this panel owns.
     */
    it('cuts the set for a finger on a screen too wide to be called narrow', async () => {
      axes.coarse = true;
      await renderReady({ toolbar: true });
      await waitFor(() => expect(lastToolbar).toBeDefined());

      const buttons = (lastToolbar!.options as { buttons: string[] }).buttons;
      // The compass survives whole — four elevations are how depth is read, and half a compass is
      // not a control — along with the plan that comes back from wherever a gesture has turned to.
      for (const kept of ['viewPlan', 'viewNorth', 'viewEast', 'viewSouth', 'viewWest']) {
        expect(buttons, kept).toContain(kept);
      }
      // What goes: a close reading done at a desk, and two controls that change how the same view
      // is drawn rather than what is shown.
      for (const cut of ['splays', 'cameraPerspective', 'cameraOrthographic']) {
        expect(buttons, cut).not.toContain(cut);
      }
      expect(buttons).toHaveLength(12);
    });

    it('spends a narrow screen’s one row on the plan and not on a compass', async () => {
      // The toolbar wraps rather than overflowing, so a set that does not fit takes a second row —
      // measured on a 412px phone, five controls are one row of 54px and six are two rows of 98,
      // over a model the same phone gives 320px to. So the count is held and the plan displaces
      // the splay toggle: the plan is the only view control that can be offered alone and is the
      // way back from wherever a touchscreen has dragged the model to, while splays are drawn off
      // by default and turning them on is a close reading done at a desk.
      // Both axes, because a phone is both: narrow across and coarse under the finger. The width is
      // what decides this one — below the breakpoint there is no pointer for which more is safe,
      // since the narrowest screen this is designed at is 360px.
      axes.narrow = true;
      axes.coarse = true;
      await renderReady({ toolbar: true });
      await waitFor(() => expect(lastToolbar).toBeDefined());

      const buttons = (lastToolbar!.options as { buttons: string[] }).buttons;
      expect(buttons).toContain('viewPlan');
      expect(buttons).toContain('fullscreen');
      expect(buttons).not.toContain('splays');
      for (const left of ['viewNorth', 'viewEast', 'viewSouth', 'viewWest', 'cameraOrthographic']) {
        expect(buttons, left).not.toContain(left);
      }
      // The count itself is the constraint, so it is the thing asserted: anything added here has
      // to displace something, and a test that only listed names would not say so.
      expect(buttons).toHaveLength(5);
    });

    /**
     * Telling the viewer its container changed size.
     *
     * <b>The viewer watches the window and nothing else.</b> It installs one size listener, on
     * `window`, and has no observer of its own container — so a panel whose height is decided by
     * the card around it can change size without the viewer ever hearing about it. Measured on a
     * phone when the model panel was grown from 320px to 560px: the canvas stayed 338x320 with a
     * drawing buffer of 887x840, leaving the scene drawn at the old size in a box 240px taller and
     * the viewer hit-testing against a rectangle no longer on screen.
     *
     * The window event is the only way in, so what is asserted is that it is sent — and that it is
     * sent only when the surface's size really changed, since anything else answering that event
     * could otherwise be driven in a loop.
     */
    it('tells the viewer when its own container changes size, and only then', async () => {
      const observers: ResizeObserverCallback[] = [];
      vi.stubGlobal(
        'ResizeObserver',
        class {
          constructor(callback: ResizeObserverCallback) {
            observers.push(callback);
          }
          observe() {}
          unobserve() {}
          disconnect() {}
        },
      );
      const resized = vi.fn();
      window.addEventListener('resize', resized);
      try {
        await renderReady();
        await waitFor(() => expect(observers.length).toBeGreaterThan(0));
        const report = (width: number, height: number) =>
          act(() =>
            observers[0](
              [{ contentRect: { width, height } } as ResizeObserverEntry],
              {} as ResizeObserver,
            ),
          );

        report(400, 320);
        expect(resized).toHaveBeenCalledTimes(1);

        // The same size again is not a resize. Answering it would put this panel and anything else
        // listening to the window into a conversation neither of them started.
        report(400, 320);
        expect(resized).toHaveBeenCalledTimes(1);

        report(400, 560);
        expect(resized).toHaveBeenCalledTimes(2);
      } finally {
        window.removeEventListener('resize', resized);
      }
    });

    it('hands the viewer its station pictures, and asks the loaded viewer for the label that shows them', async () => {
      await renderReady({ stationMedia: media() });

      expect(setStationMedia).toHaveBeenCalled();
      // Asked of the loaded viewer, never of its construction config: the viewer re-applies the
      // settings its own "save as default" button stored over anything the config said, so one
      // press of that button with the label off would otherwise turn the pictures off for this
      // browser for good.
      expect(stationLabelOver).toBe(true);
      expect(lastViewerConfig!.view).toBeUndefined();
    });

    it('leaves the pointer alone when no pictures are coming', async () => {
      await renderReady();

      expect(setStationMedia).not.toHaveBeenCalled();
      expect(stationLabelOver).toBe(false);
      expect(lastViewerConfig!.view).toBeUndefined();
    });
  });

  /**
   * A tap on a station is answered by the pick and by nothing else.
   *
   * <b>It used to fly the camera, and the reasoning for that turned out to be wrong.</b> The
   * argument was that the strip is a hover, that a finger cannot hover, and that focusing the
   * station — which centres the camera on it — was therefore the only way to reach it. Driven
   * against the vendored bundle on a 286px surface: a stationary tap on a station dispatched
   * `station` and then `stationHover` with `pointerType` touch, and the strip was drawn at the
   * station where it stood, with nothing having called `focusStation`; a tap on empty space took it
   * off again. So the move bought a strip that was already there, and it cost the most on the one
   * surface where a tap means something else — the tracking tab, where somebody taps the station
   * the party is at and then reaches for "Record here" while the model slides out from under them.
   */
  describe('a tap on a station', () => {
    /** The station object the viewer hands over with a click — it names itself with `name()`. */
    const station = { name: () => 'p.g.7' };
    const tap = (pointerType: string, node: unknown = station) =>
      act(() => emit('station', { node, mouseEvent: { pointerType } }));

    it('leaves the camera where the reader left it, pictures or no pictures', async () => {
      await renderReady({ stationMedia: media() });
      setStationMedia.mockClear();

      tap('touch');
      tap('touch');

      expect(focusStation).not.toHaveBeenCalled();
      // Nor is the source taken away and handed back, which is how a second tap used to dismiss a
      // strip this panel had opened. The viewer takes its own off when a finger lands elsewhere.
      expect(clearStationMedia).not.toHaveBeenCalled();
      expect(setStationMedia).not.toHaveBeenCalled();
    });

    it('still names what was picked, which is what a report is recorded against', async () => {
      const onPartPick = vi.fn();
      await renderReady({ stationMedia: media(), onPartPick });

      tap('touch');

      // The offer the tracking tab raises from a press, unchanged: what went is the camera move
      // that used to happen beside it.
      expect(onPartPick).toHaveBeenCalledWith(
        expect.objectContaining({ anchorKind: 'modelStation', anchor: { station: 'p.g.7' } }),
      );
      expect(focusStation).not.toHaveBeenCalled();
    });

    it('leaves a mouse click meaning what it meant', async () => {
      await renderReady({ stationMedia: media() });

      tap('mouse');

      expect(focusStation).not.toHaveBeenCalled();
      expect(clearStationMedia).not.toHaveBeenCalled();
    });
  });

  /**
   * Clicking a thumbnail opens the application's own picture viewer rather than the viewer's.
   *
   * The viewer's fallback draws the picture into the model at the station, which is the right
   * answer for a host with nowhere better to put it. This application has somewhere better: a
   * photograph of a pitch head is worth zooming, panning and reading a caption on, and none of
   * that is possible in a small square floating inside a line drawing.
   */
  describe('opening a picture from the strip', () => {
    const entry = {
      url: 'http://files.local/photo?size=1200',
      thumbnailUrl: 'http://files.local/photo?size=160',
      caption: 'Sala mare',
      documentId: 'photo-1',
    };

    it('opens the picture viewer on the thumbnail that was clicked, and takes the click', async () => {
      await renderReady({ stationMedia: media() });

      const event: { entry: typeof entry; handled?: boolean } = { entry };
      act(() => emit('mediaOpen', event));

      // Claimed, so the viewer does not also draw its own popup into the model behind this one.
      expect(event.handled).toBe(true);
      const shown = await screen.findByAltText('Sala mare');
      expect(shown.getAttribute('src')).toBe(entry.url);
    });

    it('offers no address for the stored bytes, because it was never handed one', async () => {
      const { container } = await renderReady({ stationMedia: media() });

      act(() => emit('mediaOpen', { entry }));
      await screen.findByAltText('Sala mare');

      // The protection rule where a reader could actually act on it. The strip's entries are
      // renderings derived from a published thumbnail URL precisely so that a reader who may not
      // be told where a photograph was taken never receives the file that says so — so nothing
      // this opens may carry an address for the original, and the download control has to refuse
      // itself rather than quietly reach for one.
      for (const element of Array.from(container.querySelectorAll('[href], [src]'))) {
        const url = element.getAttribute('href') ?? element.getAttribute('src') ?? '';
        expect(url).not.toContain('/content');
      }
    });

    it('leaves a malformed entry to the viewer rather than claiming the click and dropping it', async () => {
      await renderReady({ stationMedia: media() });

      const event: { entry: { url: string }; handled?: boolean } = { entry: { url: '' } };
      act(() => emit('mediaOpen', event));

      // Unclaimed: the viewer still does whatever it would have done. Claiming a click and then
      // declining to show anything is a thumbnail that silently does nothing.
      expect(event.handled).toBeUndefined();
    });

    /**
     * A station's other pictures, and where the picture viewer is drawn.
     *
     * Both are the same measurement in the end: the strip is held inside the model surface, so on a
     * phone it shows two thumbnails across and cuts off what follows — and the viewer that opens
     * from it is a sheet that a model covering the screen paints straight over.
     */
    describe('a station with several pictures', () => {
      const station = { name: () => 'p.g.7' };
      const strip = [
        { url: 'http://files.local/one?size=1200', caption: 'One', documentId: 'photo-1' },
        { url: 'http://files.local/two?size=1200', caption: 'Two', documentId: 'photo-2' },
        { url: 'http://files.local/three?size=1200', caption: 'Three', documentId: 'photo-3' },
      ];
      const stripMedia = () => new Map([['p.g.7', strip]]);

      it('opens the whole set at the picture that was clicked', async () => {
        await renderReady({ stationMedia: stripMedia() });

        act(() => emit('mediaOpen', { entry: strip[1], station }));

        expect((await screen.findByAltText('Two')).getAttribute('src')).toBe(strip[1].url);
        // Moving on is how the pictures the strip had no room to draw are seen at all: it is
        // bounded by the model it is drawn over, and on a phone that is two thumbnails across.
        fireEvent.click(screen.getByLabelText('Next'));
        expect((await screen.findByAltText('Three')).getAttribute('src')).toBe(strip[2].url);
      });

      it('opens only what was clicked when the station cannot be named', async () => {
        await renderReady({ stationMedia: stripMedia() });

        // No station on the event: a viewer that named nothing still opens the picture that was
        // pressed, rather than an empty sheet.
        act(() => emit('mediaOpen', { entry: strip[1] }));

        expect(await screen.findByAltText('Two')).toBeInTheDocument();
        fireEvent.click(screen.getByLabelText('Next'));
        // One picture wraps to itself rather than arrowing into somebody else's station.
        expect(await screen.findByAltText('Two')).toBeInTheDocument();
      });

      it('draws the picture inside the model while the model is covering the screen', async () => {
        await renderReady({ stationMedia: stripMedia() });
        const surface = screen.getByTestId('caveview-container');
        // The viewer's fullscreen button puts *its own container* into the browser's top layer,
        // which paints over the whole document: a sheet that is a sibling of it is not drawn at
        // all, and the thumbnail would answer with nothing — having also suppressed the viewer's
        // own popup on the way.
        Object.defineProperty(document, 'fullscreenElement', {
          configurable: true,
          get: () => surface,
        });
        try {
          act(() => emit('mediaOpen', { entry: strip[0], station }));

          expect(surface.contains(await screen.findByTestId('lightbox'))).toBe(true);
        } finally {
          Object.defineProperty(document, 'fullscreenElement', {
            configurable: true,
            get: () => null,
          });
        }
      });

      it('does the same where the request was refused and only the class covers the screen', async () => {
        await renderReady({ stationMedia: stripMedia() });
        const surface = screen.getByTestId('caveview-container');
        // What happens inside an embedded frame, which is how this viewer is read on somebody
        // else's page: the fullscreen request is refused, no `fullscreenchange` is raised, and the
        // surface is pinned over the screen by the class the viewer adds — above this sheet.
        surface.classList.add('toggle-fullscreen');

        act(() => emit('mediaOpen', { entry: strip[0], station }));

        expect(surface.contains(await screen.findByTestId('lightbox'))).toBe(true);
      });

      it('leaves it in the panel while the model is one card among others', async () => {
        await renderReady({ stationMedia: stripMedia() });
        const surface = screen.getByTestId('caveview-container');

        act(() => emit('mediaOpen', { entry: strip[0], station }));

        // Inside the surface it would be clipped to the card and scrolled away with it; outside
        // it, the fixed sheet covers the window, which is what a picture viewer is.
        expect(surface.contains(await screen.findByTestId('lightbox'))).toBe(false);
      });
    });
  });
});
