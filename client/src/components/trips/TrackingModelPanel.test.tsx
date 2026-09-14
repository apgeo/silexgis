// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SurveyModelInfo, TrackingEvent, TrackingState, TripParticipant } from '../../api/hooks.ts';
import type { PickedModelPart } from '../../caveview/modelParts.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';

const ANA = 'caver-ana';
const BOGDAN = 'caver-bogdan';
const MODEL = 'model-1';

let held: SurveyModelInfo | undefined;
let askedFor: string | undefined;
/** The whole log the replay reads, and whether anything has asked for it yet. */
let log: TrackingEvent[] = [];
let logAskedFor: { tripLogId: string | undefined; enabled: boolean } | undefined;

/** The one call that writes a report, whichever surface filled it in. */
const recordEvents = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  TRACKING_EVENT_KINDS: ['entered', 'atStation', 'atDepth', 'note', 'exited'],
  useRecordTrackingEvents: () => ({ mutateAsync: recordEvents, isPending: false }),
  surveyModelReadableByViewer: (m: { format: string }) => m.format === 'lox' || m.format === 'survex3d',
  useSurveyModel: (id: string | undefined) => {
    askedFor = id;
    return { data: id === undefined ? undefined : held, isPending: false };
  },
  useTripTrackingEventLog: (tripLogId: string | undefined, enabled: boolean) => {
    logAskedFor = { tripLogId, enabled };
    return { data: enabled ? log : undefined, isPending: !enabled, error: null };
  },
}));

// The viewer itself is a three.js bundle holding a drawing context. What it is handed is the
// point: which model, who is drawn on it, and how much of the screen it may take.
interface GivenProps {
  fileName?: string;
  surveyModelId?: string;
  trackedCavers?: readonly TrackedCaver[];
  height?: number | string;
  onPartPick?: (part: PickedModelPart) => void;
  /** Which of the viewer's own controls this panel asks for — see the test that reads it. */
  toolbar?: boolean | { buttons?: readonly string[] };
}
let given: GivenProps | undefined;
vi.mock('../caveview/CaveViewPanel.tsx', () => ({
  default: (props: GivenProps) => {
    given = props;
    return <div data-testid="viewer" />;
  },
}));

// The two axes this panel chooses on, both mocked rather than driven by media queries, as the rest
// of this application tests its phone layouts. Both false by default: a desk screen with a mouse,
// which is what every other test in this file is being read on.
let narrow = false;
let coarse = false;
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => narrow }));
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

const { default: TrackingModelPanel } = await import('./TrackingModelPanel.tsx');

function model(overrides: Partial<SurveyModelInfo> = {}): SurveyModelInfo {
  return {
    id: MODEL,
    caveId: 'cave-1',
    name: 'Pestera de test',
    format: 'lox',
    status: 'ready',
    modelUrl: 'https://files.local/model-1',
    ...overrides,
  } as unknown as SurveyModelInfo;
}

function tracking(overrides: Partial<TrackingState> = {}): TrackingState {
  return {
    state: 'armed',
    surveyModelId: MODEL,
    referenceStationName: null,
    depthFilter: [],
    armedAt: '2026-09-12T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    teams: [{ id: 'team-1', title: 'Team A' }],
    participants: [
      {
        caverId: ANA,
        teamId: 'team-1',
        lastKind: 'atStation',
        lastRecordedAt: '2026-09-12T07:00:00Z',
        stationName: 'p.g.7',
        depthM: null,
        out: false,
      },
    ],
    ...overrides,
  } as TrackingState;
}

const roster: TripParticipant[] = [
  { caverId: ANA, name: 'Ana Popescu' },
  { caverId: BOGDAN, name: 'Bogdan Ilie' },
] as unknown as TripParticipant[];

const entered = (caverId: string, recordedAt: string): TrackingEvent =>
  ({ id: `e-${caverId}`, caverId, kind: 'entered', recordedAt }) as unknown as TrackingEvent;

const atStation = (caverId: string, recordedAt: string, stationName: string): TrackingEvent =>
  ({
    id: `s-${caverId}-${recordedAt}`,
    caverId,
    teamId: null,
    kind: 'atStation',
    surveyModelId: MODEL,
    stationName,
    depthEnteredM: null,
    note: null,
    recordedAt,
  }) as unknown as TrackingEvent;

/** Told when a report has landed, so the table's selection can be let go. */
const onRecorded = vi.fn();

function show(
  state = tracking(),
  events: TrackingEvent[] = [],
  props: { canEdit?: boolean; selectedCaverIds?: string[] } = {},
) {
  return render(
    // The dialog inside this panel words its own answers, and that needs the library's message
    // context — the same wrapper every other surface here is drawn inside.
    <App>
      <TrackingModelPanel
        tripLogId="trip-1"
        tracking={state}
        participants={roster}
        events={events}
        canEdit={props.canEdit ?? true}
        selectedCaverIds={props.selectedCaverIds ?? [ANA]}
        onRecorded={onRecorded}
      />
    </App>,
  );
}

/** Opens the model and presses a station in it, the way the viewer reports one. */
function pressStation(station = 'p.g.7') {
  fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
  act(() =>
    given!.onPartPick?.({
      anchorKind: 'modelStation',
      anchor: { station },
      label: station,
    } as PickedModelPart),
  );
}

beforeEach(() => {
  held = model();
  askedFor = undefined;
  given = undefined;
  log = [];
  logAskedFor = undefined;
  narrow = false;
  coarse = false;
  onRecorded.mockReset();
  recordEvents.mockReset().mockResolvedValue([{}]);
});

afterEach(cleanup);

describe('TrackingModelPanel', () => {
  it('offers the watch on the model it is resolved against, and opens it when asked', () => {
    show(tracking(), [entered(ANA, '2026-09-12T06:10:00Z')]);

    expect(askedFor).toBe(MODEL);
    // Not mounted until somebody asks: a survey viewer downloads a model and takes one of the
    // few drawing contexts a browser keeps alive, and this tab is opened routinely by somebody
    // who only wants to record that the party went in.
    expect(screen.queryByTestId('viewer')).toBeNull();

    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

    expect(screen.getByTestId('viewer')).toBeTruthy();
    expect(given).toMatchObject({ fileName: 'Pestera de test.lox', surveyModelId: MODEL });
    // The watch carries caver ids and nothing that knows what anybody is called; the trip's
    // roster is what knows, and the moment somebody went in is only on the log.
    expect(given!.trackedCavers).toEqual([
      {
        caverId: ANA,
        name: 'Ana Popescu',
        teamId: 'team-1',
        teamTitle: 'Team A',
        position: { kind: 'station', station: 'p.g.7' },
        lastRecordedAt: '2026-09-12T07:00:00Z',
        // The same instant here because the latest report is itself the station report. The two
        // part company after a note or a "come out", which is what the watch's fold makes possible
        // and what anything comparing two people's positions has to know about.
        positionAt: '2026-09-12T07:00:00Z',
        enteredAt: '2026-09-12T06:10:00Z',
        out: false,
      },
    ]);
  });

  /**
   * The rule this surface may not soften. A station name is location data and is kept from a
   * reader who may not place the cave; it arrives as an absence. Nobody is drawn at a position
   * that was withheld — there is nowhere to put a marker — but they are still on the watch handed
   * to the panel, and marked as withheld, because leaving them out would turn a withholding into
   * nobody knowing where they are.
   */
  it('hands over a caver whose position was withheld, said as withheld', () => {
    show(
      tracking({
        positionsWithheld: true,
        participants: [
          {
            caverId: ANA,
            teamId: null,
            lastKind: 'atStation',
            lastRecordedAt: '2026-09-12T07:00:00Z',
            stationName: null,
            depthM: null,
            out: false,
          },
        ],
      } as Partial<TrackingState>),
    );
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

    expect(given!.trackedCavers).toHaveLength(1);
    expect(given!.trackedCavers![0].position).toEqual({ kind: 'withheld', certain: true });
  });

  /**
   * The replay hands the viewer the same shape the live watch produces, so nothing downstream of it
   * knows the difference — and the one thing that must survive it is the way back. A replay that
   * left the model showing a moment hours old would be a tracking panel quietly lying about where
   * the party is, which is the single thing this surface exists not to do.
   */
  it('replays the trip over its log and hands the live watch back on leaving', () => {
    log = [
      atStation(ANA, '2026-09-12T06:40:00Z', 'p.g.3'),
      entered(ANA, '2026-09-12T06:10:00Z'),
    ];
    show(tracking(), [entered(ANA, '2026-09-12T06:10:00Z')]);
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

    const live = given!.trackedCavers;
    expect(live![0].position).toEqual({ kind: 'station', station: 'p.g.7' });
    // The whole log is several requests on a long trip, and this tab is opened routinely by
    // somebody who only wants to record that the party went in — so nothing is read until
    // somebody asks for a replay.
    expect(logAskedFor).toEqual({ tripLogId: 'trip-1', enabled: false });

    fireEvent.click(screen.getByTestId('trip-tracking-replay-open'));

    expect(logAskedFor).toEqual({ tripLogId: 'trip-1', enabled: true });
    // A replay opens where the watch was armed, which is before anybody had said anything.
    expect(given!.trackedCavers![0]).toMatchObject({
      caverId: ANA,
      position: { kind: 'unreported' },
      lastRecordedAt: null,
    });

    // Wound to the end of the window, the same person stands where the log says they were — which
    // is not where the live watch says they are, so the two are demonstrably different answers.
    // The slider reads the legacy key code rather than the key name, so both are sent.
    fireEvent.keyDown(screen.getByRole('slider'), { key: 'End', keyCode: 35 });
    expect(given!.trackedCavers![0].position).toEqual({ kind: 'station', station: 'p.g.3' });

    fireEvent.click(screen.getByTestId('trip-tracking-replay-leave'));

    expect(given!.trackedCavers).toEqual(live);
  });

  /**
   * The three axes this panel is laid out on, and the one that used to be missing.
   *
   * Width and pointer were already read. Height was not, and it is the one a phone held sideways
   * fails on: it is wide enough to be handed the desk layout and short enough that the desk
   * layout's model does not fit on it.
   */
  describe('on a screen with a shape', () => {
    it('never lets the model take more of the screen than it can spare', () => {
      // A desk screen keeps the size it always had. `min` is what makes that true and keeps it
      // true on a short window: the number below is a ceiling, not a measurement.
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      expect(given!.height).toBe('min(460px, 60dvh)');
    });

    it('asks for less of a narrow screen, and still no more than a share of it', () => {
      narrow = true;
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      expect(given!.height).toBe('min(320px, 60dvh)');
    });

    it('caps against the viewport on the branch a phone held sideways actually takes', () => {
      // The case all of this is for, and the reason the cap cannot live in the narrow branch. A
      // phone in landscape reports a desk's width, so `useIsMobile` is false and the panel takes
      // the wide branch — on a viewport 360px tall, which cannot hold 460px of anything. So the
      // wide branch is asserted to carry a limit that is a share of the screen rather than a
      // number of pixels, because that is the only half of it that knows the screen is short.
      narrow = false;
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      const height = String(given!.height);
      expect(height).toMatch(/^min\(/);
      // A fraction of the viewport's own height, not of its width and not a constant: those are
      // the two answers that leave a short screen with a model taller than it is.
      expect(height).toMatch(/\b\d+dvh\b/);
      expect(height).not.toMatch(/vw/);
    });

    /**
     * Asking the model for more of the screen, without giving it all of it.
     *
     * The defect the share exists to prevent is not about a number of pixels: the viewer owns
     * every touch that begins inside it, so a model as tall as the viewport leaves nowhere at all
     * to put a finger to get past it — measured at zero pixels of page movement from a swipe
     * anywhere on the screen. So the larger size is still a share, and the assertions below are
     * about the *shape* of the answer rather than about the numbers in it: a fraction of the
     * viewport's own height, and one that leaves a real strip of ordinary page behind.
     */
    it('gives a bigger model more of the screen and still never all of it', () => {
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      expect(given!.height).toBe('min(460px, 60dvh)');

      fireEvent.click(screen.getByTestId('trip-tracking-model-size'));
      const height = String(given!.height);
      expect(height).toMatch(/^min\(/);
      const share = Number(/(\d+)dvh/.exec(height)?.[1]);
      expect(share).toBeGreaterThan(60);
      // The number that must never be written here: at a full viewport the model is the only
      // thing under a thumb, which is exactly the state the fraction was introduced to end.
      expect(share).toBeLessThanOrEqual(80);
    });

    it('asks a narrow screen for less, on both sizes', () => {
      narrow = true;
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      expect(given!.height).toBe('min(320px, 60dvh)');

      fireEvent.click(screen.getByTestId('trip-tracking-model-size'));
      expect(String(given!.height)).toMatch(/^min\(5\d\dpx, \d+dvh\)$/);
    });

    it('opens the next model at the size a model opens at', () => {
      // The size belongs to a model on screen. Kept, it would decide how much of the screen a
      // model somebody opened to glance at takes, without their having asked for that one.
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      fireEvent.click(screen.getByTestId('trip-tracking-model-size'));
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      expect(given!.height).toBe('min(460px, 60dvh)');
    });

    /**
     * The one viewer control this panel does not offer.
     *
     * <b>The viewer's fullscreen button puts its drawing surface in the browser's top layer, and
     * the top layer paints over the whole document.</b> Everything this panel draws over the model
     * is a sibling of that surface rather than a child of it — the list of who is where, the notice
     * that a link named a station this model does not hold, and the offer a station press raises.
     * Measured on the live instance at 1440x900 with the surface fullscreen: the press still fires
     * and still raises the offer at 117,541, and `document.elementFromPoint` at the centre of its
     * "Record here" button answers `CANVAS`; the same probe at the centre of the party list answers
     * nothing at all. So the whole click-to-record path looks like it is working and is not.
     *
     * Asserted on every shape of screen, because the button is in every set — and because on a
     * phone it is one of only five controls, which is where a reader is most likely to press it.
     */
    it('leaves the viewer’s own fullscreen out, on every shape of screen', () => {
      for (const shape of [
        { narrow: false, coarse: false },
        { narrow: false, coarse: true },
        { narrow: true, coarse: true },
      ]) {
        cleanup();
        narrow = shape.narrow;
        coarse = shape.coarse;
        show();
        fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

        const buttons = (given!.toolbar as { buttons: readonly string[] }).buttons;
        expect(buttons, JSON.stringify(shape)).not.toContain('fullscreen');
        // Subtracted from the set the viewer wrapper chooses rather than listed here, so this panel
        // never becomes a second opinion about what fits a screen. Everything else survives.
        expect(buttons.length, JSON.stringify(shape)).toBeGreaterThan(3);
        expect(buttons, JSON.stringify(shape)).toContain('shadingMode');
      }
    });

    it('spells the size control out where there is room and names it where there is not', () => {
      // Whether the word fits is a question about room, so it is answered by the width; the
      // answer is moved to the accessible name rather than to a tooltip, because a device with
      // no hovering pointer never opens one.
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      expect(screen.getByTestId('trip-tracking-model-size')).toHaveTextContent('Bigger model');

      cleanup();
      narrow = true;
      show();
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      const control = screen.getByTestId('trip-tracking-model-size');
      expect(control).toHaveTextContent('');
      expect(control).toHaveAttribute('aria-label', 'Bigger model');
    });
  });

  /**
   * Recording a position by pressing the place on the model.
   *
   * The act being tested is not the dialog: it is that the dialog reaches the same call the card
   * under the watch reaches, with the same body. Two ways to one act, and the moment they become
   * two calls is the moment a report recorded one way stops meaning what a report recorded the
   * other way means.
   */
  describe('pressing a station', () => {
    it('offers to record there rather than opening a form on every press', () => {
      // Looking around a model means pressing things. A form that appeared on every press would
      // make the model unusable as a model.
      show();
      pressStation();

      expect(screen.getByTestId('trip-tracking-picked-station')).toHaveTextContent('p.g.7');
      expect(screen.queryByTestId('trip-tracking-dialog-station')).toBeNull();
    });

    it('sends the report through the same call the card under the watch sends', async () => {
      show(tracking(), [], { selectedCaverIds: [ANA] });
      pressStation('p.g.42');

      fireEvent.click(screen.getByTestId('trip-tracking-record-here-open'));
      // The station arrives filled in from the press — that is the whole of what the press buys.
      expect(screen.getByTestId('trip-tracking-dialog-station')).toHaveValue('p.g.42');

      fireEvent.click(screen.getByRole('button', { name: /Record for/ }));

      await waitFor(() => expect(recordEvents).toHaveBeenCalledTimes(1));
      expect(recordEvents).toHaveBeenCalledWith({
        tripLogId: 'trip-1',
        caverIds: [ANA],
        kind: 'atStation',
        stationName: 'p.g.42',
        depthM: null,
        teamId: null,
        note: null,
        recordedAt: null,
      });
      // And the selection that produced it is let go, exactly as the card's own report does.
      await waitFor(() => expect(onRecorded).toHaveBeenCalled());
    });

    it('offers nothing to a reader who cannot write to the log', () => {
      // A press that produced an offer that produced a refusal is three acts spent learning
      // something the page already knew.
      show(tracking(), [], { canEdit: false });
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      expect(given!.onPartPick).toBeUndefined();
    });

    it('offers nothing while the watch is not armed', () => {
      show(tracking({ state: 'closed' }));
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      expect(given!.onPartPick).toBeUndefined();
    });

    it('takes the offer down when the press names no single place', () => {
      // A leg or a splay is two places or none. Leaving the last station standing under it would
      // put a station name on screen that the reader's last press did not mean.
      show();
      pressStation();
      act(() =>
        given!.onPartPick?.({
          anchorKind: 'modelStationRange',
          anchor: { fromStation: 'p.g.7', toStation: 'p.g.8' },
          label: '7 → 8',
        } as PickedModelPart),
      );

      expect(screen.queryByTestId('trip-tracking-picked-station')).toBeNull();
    });
  });

  describe('drawn for a finger', () => {
    it('sizes the button that opens the model for one', () => {
      coarse = true;
      show();

      expect(screen.getByTestId('trip-tracking-model-toggle')).toHaveClass('ant-btn-lg');
    });

    it('keeps the dense chrome where there is a mouse', () => {
      // The button sits in a card header beside a title; on a desk the room it takes is worth
      // more than a hit tolerance nothing there needs.
      show();

      expect(screen.getByTestId('trip-tracking-model-toggle')).toHaveClass('ant-btn-sm');
    });
  });

  it('answers to a name of its own rather than sharing the survey chooser\'s', () => {
    // Two elements under one name is a locator that matches both and picks neither, which is a
    // defect a phone test would meet before anybody else did.
    show();

    expect(screen.getByTestId('trip-tracking-model-panel')).toBeTruthy();
    expect(screen.queryByTestId('trip-tracking-model')).toBeNull();
  });

  it('offers nothing when the watch names no model', () => {
    show(tracking({ surveyModelId: null }));

    expect(askedFor).toBeUndefined();
    expect(screen.queryByTestId('trip-tracking-model-panel')).toBeNull();
  });

  it('offers nothing for a model this viewer cannot read, or one that is not ready yet', () => {
    // A wall mesh has no stations to place anybody at, and a model still being processed has
    // nothing to draw. Offering either is a button whose every press fails.
    held = model({ format: 'stl' });
    const walls = show();
    expect(screen.queryByTestId('trip-tracking-model-panel')).toBeNull();
    walls.unmount();

    held = model({ status: 'processing' });
    show();
    expect(screen.queryByTestId('trip-tracking-model-panel')).toBeNull();
  });
});
