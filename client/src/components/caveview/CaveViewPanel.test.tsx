// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { CAVEVIEW_HOME, type Cv2Namespace } from '../../caveview/loadCaveView.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';
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
const addLiveMarker = vi.fn();
const moveLiveMarker = vi.fn();
const removeLiveMarker = vi.fn();
const setStationMedia = vi.fn();
const clearStationMedia = vi.fn();
const toolbarDispose = vi.fn();
let lastToolbar: { container: unknown; options: unknown } | undefined;

class FakeViewer {
  // The real viewer exposes this as a settable property, and what the panel does with it is the
  // whole point of the test — so it is recorded rather than stored.
  get stationLabelOver() {
    return stationLabelOver;
  }
  set stationLabelOver(value: boolean) {
    stationLabelOver = value;
  }
  constructor(_containerId: string, config: Record<string, unknown>) {
    lastViewerConfig = config;
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

beforeEach(() => {
  listeners.clear();
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
  window.CV2 = undefined;
});

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
        sublabel: undefined,
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

    it('adds the marker again when the time is taken off it, because a move cannot clear one', async () => {
      await renderReady({ trackedCavers: [caver()] });

      fireEvent.click(screen.getByTestId('caveview-tracking-times'));
      expect(moveLiveMarker).toHaveBeenCalledWith(
        'caver-1',
        'p.g.7',
        expect.objectContaining({ sublabel: expect.any(String) }),
      );

      fireEvent.click(screen.getByTestId('caveview-tracking-times'));
      // A move leaves out what it is not given, so the sublabel would survive being switched
      // off. Adding replaces the marker whole, which is the only call that takes it away.
      expect(addLiveMarker).toHaveBeenCalledTimes(2);
      expect(addLiveMarker).toHaveBeenLastCalledWith(
        'caver-1',
        'p.g.7',
        expect.objectContaining({ sublabel: undefined }),
      );
    });

    it('opens a caver’s card when the pointer rests on their marker', async () => {
      await renderReady({ trackedCavers: [caver()] });

      await waitFor(() => expect(listeners.get('liveMarkerHover')?.length ?? 0).toBeGreaterThan(0));
      act(() => emit('liveMarkerHover', { id: 'caver-1' }));

      expect(await screen.findByTestId('caveview-caver-card')).toHaveTextContent('Team A');
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
   * The strip of pictures is opened by a pointer coming to rest on a station, and a finger cannot
   * do that: a stationary tap moves no pointer, so on a touch screen the strip is unreachable
   * however large its thumbnails are. The tap that picks the station opens it instead.
   */
  describe('station pictures where no pointer can hover', () => {
    /** The station object the viewer hands over with a click — it names itself with `name()`. */
    const station = { name: () => 'p.g.7' };
    const tap = (pointerType: string, node: unknown = station) =>
      act(() => emit('station', { node, mouseEvent: { pointerType } }));

    it('opens a station’s pictures from the tap that picked it', async () => {
      await renderReady({ stationMedia: media() });

      tap('touch');

      // Focusing the station is what shows the strip without a pointer, and it centres the
      // station — which is also what keeps the strip on screen, since it is drawn only while its
      // station is in view.
      expect(focusStation).toHaveBeenCalledWith('p.g.7', { popup: true });
    });

    it('takes them off again on a second tap, the only dismissal a finger has', async () => {
      await renderReady({ stationMedia: media() });
      setStationMedia.mockClear();

      tap('touch');
      tap('touch');

      // Clearing the source and putting it straight back is what removes the strip itself; the
      // source returns so the next tap still has pictures to find.
      expect(clearStationMedia).toHaveBeenCalledTimes(1);
      expect(setStationMedia).toHaveBeenCalledTimes(1);
      expect(focusStation).toHaveBeenCalledTimes(1);
    });

    it('leaves a mouse click meaning what it meant', async () => {
      // A mouse opens the strip by resting on the station. Flying the camera on every click as
      // well would take over the ordinary way of looking around a model.
      await renderReady({ stationMedia: media() });

      tap('mouse');

      expect(focusStation).not.toHaveBeenCalled();
      expect(clearStationMedia).not.toHaveBeenCalled();
    });

    it('does not move the camera for a station that has no pictures', async () => {
      await renderReady({ stationMedia: media() });

      tap('touch', { name: () => 'p.g.9' });

      expect(focusStation).not.toHaveBeenCalled();
    });

    it('does nothing at all when the panel was given no pictures', async () => {
      await renderReady();

      tap('touch');

      expect(focusStation).not.toHaveBeenCalled();
    });
  });
});
