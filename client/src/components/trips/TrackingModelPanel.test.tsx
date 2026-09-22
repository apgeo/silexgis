// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { useEffect } from 'react';
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
/**
 * The model's links — where a station's pictures come from — and what was asked for them.
 *
 * Recorded rather than merely answered, because *whether* this was asked at all is the property
 * under test: this tab is opened to record that a party went in, and the pictures must cost
 * nothing until somebody opens the model.
 */
let links: unknown[] = [];
let linksAskedFor: { targetType: string; targetId: string; enabled: boolean } | undefined;

/** The trip's links — where a picture hung on one of its moments lives — and what was asked. */
let momentLinks: unknown[] = [];
let momentLinksAskedFor: { tripLogId: string | undefined; enabled: boolean } | undefined;

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
  useResLinksForTarget: (
    targetType: string,
    targetId: string,
    _params: unknown,
    enabled: boolean,
  ) => {
    linksAskedFor = { targetType, targetId, enabled };
    return { data: enabled ? { items: links } : undefined, isPending: !enabled, error: null };
  },
  /**
   * The trip's own links, which is where a picture hung on a moment lives. Recorded the same way
   * the model's are: whether it is asked for at all before the model is opened is the property,
   * for the same reason — this tab is opened to record that a party went in.
   */
  useTripMomentPictureLinks: (tripLogId: string | undefined, enabled: boolean) => {
    momentLinksAskedFor = { tripLogId, enabled };
    return { data: enabled ? { items: momentLinks } : undefined, isPending: !enabled, error: null };
  },
  usePhotos: () => ({ data: { items: [] }, isPending: false }),
  useAttachTrackingPictures: () => ({ mutate: vi.fn(), isPending: false }),
}));

// The viewer itself is a three.js bundle holding a drawing context. What it is handed is the
// point: which model, who is drawn on it, and how much of the screen it may take.
interface GivenProps {
  fileName?: string;
  surveyModelId?: string;
  trackedCavers?: readonly TrackedCaver[];
  height?: number | string;
  onPartPick?: (part: PickedModelPart) => void;
  /**
   * Where the viewer answers which stations the drawing turns out not to hold.
   *
   * Carried up rather than kept, because the table above this panel says those same stations in
   * words — see the test that engages a replay and watches it keep flowing.
   */
  onUnplacedStationsChange?: (stations: ReadonlySet<string>) => void;
  /** Which of the viewer's own controls this panel asks for — see the test that reads it. */
  toolbar?: boolean | { buttons?: readonly string[] };
  /**
   * Where the viewer reads a station's pictures from. A function rather than a map, because this
   * panel's answer moves as a replay is scrubbed and the viewer must not be handed a new source
   * five times a second — see the test that holds its identity still.
   */
  stationMedia?: (station: unknown) => readonly { url: string }[] | null;
}
let given: GivenProps | undefined;

/**
 * What the viewer would draw at one station, asked the way the viewer asks: with a station object,
 * not a path. The viewer hands over its own node and the source reads the path off it, so a test
 * that looked the path up itself would be exercising a lookup nothing performs.
 */
function mediaAt(path: string): readonly { url: string }[] {
  return given!.stationMedia?.({ name: () => path }) ?? [];
}
/**
 * Whether the fake viewer was ever torn down. The strip of map tabs beside it exists on
 * the promise that it never is: the real panel re-fetches and re-parses the whole survey
 * on a remount and burns a WebGL context doing it, so the counter is the assertion.
 */
const viewerLifecycle = { mounts: 0, unmounts: 0 };
vi.mock('../caveview/CaveViewPanel.tsx', () => ({
  default: function FakeCaveViewPanel(props: GivenProps) {
    given = props;
    useEffect(() => {
      viewerLifecycle.mounts += 1;
      return () => {
        viewerLifecycle.unmounts += 1;
      };
    }, []);
    return <div data-testid="viewer" />;
  },
}));

/** The model's raster-map links — the drawing strip's source — and what was asked of them. */
let rasterLinks: unknown[] = [];
let rasterLinksAskedFor: { surveyModelId: string | undefined; enabled: boolean } | undefined;
vi.mock('../../rastermap/useRasterMapLinks.ts', () => ({
  useRasterMapLinks: (surveyModelId: string | undefined, enabled: boolean) => {
    rasterLinksAskedFor = { surveyModelId, enabled };
    return { data: enabled ? rasterLinks : undefined };
  },
}));

/** What the panel hands one map sheet: the party, its activation, and the press channel. */
interface FakeSheetPaneProps {
  declaration: { linkId: string };
  active: boolean;
  cavers: readonly TrackedCaver[];
  onPickStation?: (station: string) => void;
}
/** The latest props each mounted sheet pane was given, by declaration link id. */
const sheetPanes = new Map<string, FakeSheetPaneProps>();
vi.mock('../../rastermap/RasterMapTrackingPane.tsx', () => ({
  default: (props: FakeSheetPaneProps) => {
    sheetPanes.set(props.declaration.linkId, props);
    return (
      <div
        data-testid={`fake-sheet-${props.declaration.linkId}`}
        data-active={String(props.active)}
      >
        <button
          data-testid={`fake-sheet-press-${props.declaration.linkId}`}
          onClick={() => props.onPickStation?.('p.g.7')}
        />
      </div>
    );
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

/**
 * An ordinary armed watch: one person, reported at a station of the very model this panel shows.
 *
 * <b>Written out in full and deliberately not cast.</b> It used to end `as TrackingState`, and the
 * cast is what let it go on compiling after the watch started carrying the survey each position
 * was measured against. With that field missing, every comparison against the model on screen was
 * a comparison against `undefined` — so this fixture, which is the plainest possible watch, folded
 * its whole party onto "reported on another survey". A fixture that cannot be told apart from the
 * answer a real server gives is the only kind worth asserting against.
 */
function tracking(overrides: Partial<TrackingState> = {}): TrackingState {
  return {
    state: 'armed',
    surveyModelId: MODEL,
    surveyModelMissing: false,
    referenceStationName: null,
    depthFilter: [],
    armedAt: '2026-09-12T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    publishesRealNames: false,
    // Not published. The pair travels together — "published" and "until when" are one answer —
    // and a test about a published trip says both.
    publishedAt: null,
    publishedUntil: null,
    teams: [{ id: 'team-1', title: 'Team A' }],
    participants: [
      {
        caverId: ANA,
        teamId: 'team-1',
        lastKind: 'atStation',
        lastRecordedAt: '2026-09-12T07:00:00Z',
        // The station report is itself the latest one here, so the two moments agree.
        positionRecordedAt: '2026-09-12T07:00:00Z',
        stationName: 'p.g.7',
        depthM: null,
        // Measured in the model the panel is showing, which is what makes this the ordinary case.
        positionSurveyModelId: MODEL,
        label: null,
        in: true,
        out: false,
        publishedAs: null,
      },
    ],
    ...overrides,
  };
}

const roster: TripParticipant[] = [
  { caverId: ANA, name: 'Ana Popescu' },
  { caverId: BOGDAN, name: 'Bogdan Ilie' },
] as unknown as TripParticipant[];

/** A declared map of the watch's model, shaped as the real fold reads it off the links. */
function mapLink(linkId: string, title: string, code = 'map-plan-of') {
  return {
    id: linkId,
    relationType: { id: 1, code, name: code, directed: true, inverseName: 'x' },
    description: null,
    createdAt: '2026-09-01T10:00:00Z',
    mayEdit: true,
    members: [
      {
        id: `${linkId}-doc`,
        targetType: 'document',
        targetId: `${linkId}-document`,
        isMain: true,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
        display: { title, subtitle: null, route: null, thumbnailUrl: null, mediaType: 'image/png' },
      },
      {
        id: `${linkId}-model`,
        targetType: 'surveyModel',
        targetId: MODEL,
        isMain: false,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
        display: null,
      },
    ],
  };
}

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
  props: {
    canEdit?: boolean;
    selectedCaverIds?: string[];
    onUnplacedStationsChange?: (stations: ReadonlySet<string>) => void;
  } = {},
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
        onUnplacedStationsChange={props.onUnplacedStationsChange}
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
  links = [];
  linksAskedFor = undefined;
  momentLinks = [];
  momentLinksAskedFor = undefined;
  rasterLinks = [];
  rasterLinksAskedFor = undefined;
  sheetPanes.clear();
  viewerLifecycle.mounts = 0;
  viewerLifecycle.unmounts = 0;
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
            // A withheld position carries no moment either — the read sends neither.
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: true,
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
   * The pictures a station carries, on the surface a watch is actually kept from.
   *
   * <b>What is being protected here is the closed tab.</b> This panel sits on a tab somebody opens
   * to record that the party went in, on a phone, on a hillside — so the cost of the pictures has
   * to be nothing at all until the model is opened, and that is a property of a request that is
   * never made rather than of one that returns quickly.
   */
  describe('station pictures', () => {
    const linkWithPhoto = (station: string, photo = 'photo-1') => ({
      id: `link-${station}`,
      members: [
        {
          id: `station-${station}`,
          targetType: 'surveyModel',
          targetId: MODEL,
          anchorKind: 'modelStation',
          anchor: { station },
          display: null,
        },
        {
          id: photo,
          targetType: 'document',
          targetId: photo,
          anchorKind: 'whole',
          anchor: null,
          display: {
            title: 'Sala mare',
            thumbnailUrl: `http://files.local/${photo}/thumb?token=abc`,
            mediaType: 'image/jpeg',
          },
        },
      ],
    });

    it('asks for nothing until the model is opened', () => {
      links = [linkWithPhoto('p.g.7')];
      show(tracking(), []);

      // The tab is drawn, the model is not. Asking here would spend a request on a hillside for
      // pictures that have nowhere to be drawn — and that holds for both readings of "picture on
      // the model": the ones anchored to a station, and the ones hung on a moment of this trip.
      expect(linksAskedFor).toMatchObject({ enabled: false });
      expect(momentLinksAskedFor).toMatchObject({ enabled: false });

      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      // Opened: now they are worth having, and they are asked for about the model rather than
      // about the cave, which is what anchors them to a station at all.
      expect(linksAskedFor).toEqual({
        targetType: 'surveyModel',
        targetId: MODEL,
        enabled: true,
      });
      // The moment pictures are asked about the trip, not about the model — the whole point of
      // hanging them there is that they outlive the survey the party was placed in.
      expect(momentLinksAskedFor).toEqual({ tripLogId: 'trip-1', enabled: true });
    });

    it('stops asking again when the model is closed', () => {
      links = [linkWithPhoto('p.g.7')];
      show(tracking(), []);
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      expect(linksAskedFor).toMatchObject({ enabled: true });

      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      expect(linksAskedFor).toMatchObject({ enabled: false });
    });

    it('hands the viewer the pictures at the station they are linked to', () => {
      links = [linkWithPhoto('p.g.7')];
      show(tracking(), []);
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      const shown = mediaAt('p.g.7');
      expect(shown).toHaveLength(1);
      // Derived from the published thumbnail URL, token and all — never the stored bytes. The
      // same rule the survey viewer's strip is built by, because it is the same derivation.
      expect(shown[0].url).toContain('/thumb');
      expect(shown[0].url).not.toContain('/content');
    });

    it('shows no strip at a station nothing is linked to', () => {
      links = [linkWithPhoto('p.g.7')];
      show(tracking(), []);
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      // Nothing at a station nothing is linked to, which is what the viewer reads as "draw no
      // strip here" — its positive twin is the assertion above.
      expect(mediaAt('p.g.9')).toHaveLength(0);
    });

    it('still shows the model when nothing is linked to any station', () => {
      links = [];
      show(tracking(), []);
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

      // A source, not a missing prop: its presence is what says this surface shows pictures at
      // all, and a cave nobody has photographed yet is not a cave whose model should be withheld.
      expect(given!.stationMedia).toBeTypeOf('function');
      expect(mediaAt('p.g.7')).toHaveLength(0);
      expect(screen.getByTestId('viewer')).toBeTruthy();
    });

    /**
     * The source is a function whose identity never changes, and that is load-bearing rather than
     * a style choice: a replay re-derives which pictures stand at which station on every tick of
     * its clock, and the viewer drops its hover listeners and closes an open strip whenever it is
     * handed a different source. A reader who had tapped a station would watch the photographs
     * vanish under their thumb having touched nothing.
     */
    it('never hands the viewer a different picture source once it has one', async () => {
      links = [linkWithPhoto('p.g.7')];
      log = [
        {
          id: 'e1',
          caverId: ANA,
          teamId: null,
          kind: 'atStation',
          surveyModelId: MODEL,
          stationName: 'p.g.7',
          depthEnteredM: null,
          note: null,
          recordedAt: '2026-09-12T09:00:00Z',
        } as TrackingEvent,
      ];
      show(tracking(), []);
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      const first = given!.stationMedia;

      await act(async () => {
        fireEvent.click(screen.getByTestId('trip-tracking-replay-open'));
      });
      await waitFor(() => expect(screen.getByTestId('trip-tracking-replay')).toBeTruthy());

      expect(given!.stationMedia).toBe(first);
    });

    /**
     * A picture hung on a moment of the trip, as the write actually stores it: the trip anchored
     * to the instant as the main member, the caver it is about, and the photograph.
     */
    const momentLink = (at: string, caverId: string | null, photo = 'photo-moment') => ({
      id: `moment-${at}`,
      members: [
        {
          id: `trip-${at}`,
          targetType: 'tripLog',
          targetId: 'trip-1',
          isMain: true,
          anchorKind: 'tripMoment',
          anchor: { at },
          display: null,
        },
        ...(caverId === null
          ? []
          : [
              {
                id: `caver-${at}`,
                targetType: 'caver',
                targetId: caverId,
                anchorKind: 'whole',
                anchor: null,
                display: null,
              },
            ]),
        {
          id: photo,
          targetType: 'document',
          targetId: photo,
          anchorKind: 'whole',
          anchor: null,
          display: {
            title: 'La capul puțului',
            thumbnailUrl: `http://files.local/${photo}/thumb?token=abc`,
            mediaType: 'image/jpeg',
          },
        },
      ],
    });

    /**
     * <b>A position this reader was not told cannot acquire one by having a photograph.</b> The
     * server withholds the station from a caller who may not place the cave — a station report
     * arrives with no station at all — and the picture hung on that moment still travels, exactly
     * as the note on the same report does. What must not happen is the picture being drawn under a
     * station anyway, which would hand back the very thing the withholding kept.
     *
     * Both halves are driven here, because a negative that passes because nothing was ever placed
     * proves nothing at all.
     */
    it('draws a moment picture at a reported station and never at a withheld one', async () => {
      const armed = '2026-09-12T06:00:00Z';
      momentLinks = [momentLink(armed, ANA)];

      // Positive half: the place was reported, so the photograph hangs under it.
      log = [atStation(ANA, armed, 'p.g.7')];
      show(tracking({ armedAt: armed }), []);
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      await act(async () => {
        fireEvent.click(screen.getByTestId('trip-tracking-replay-open'));
      });
      await waitFor(() => expect(mediaAt('p.g.7')).toHaveLength(1));

      cleanup();

      // Negative half: the same picture, the same instant, the same caver — read by somebody the
      // station was kept from, which is a station report arriving with no station and no model.
      log = [
        {
          ...atStation(ANA, armed, 'p.g.7'),
          stationName: null,
          surveyModelId: null,
        } as TrackingEvent,
      ];
      show(
        tracking({
          armedAt: armed,
          positionsWithheld: true,
          participants: [
            {
              ...tracking().participants[0],
              stationName: null,
              positionSurveyModelId: null,
            },
          ],
        }),
        [],
      );
      fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
      await act(async () => {
        fireEvent.click(screen.getByTestId('trip-tracking-replay-open'));
      });
      await waitFor(() => expect(screen.getByTestId('trip-tracking-replay')).toBeTruthy());

      expect(mediaAt('p.g.7')).toHaveLength(0);
    });
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
   * The drawing's own answer keeps reaching the table above, replay or no replay.
   *
   * <b>The two halves of this surface show different parties, and only one of them is in here.</b>
   * Engaging the replay hands the viewer the watch as it stood at some past moment; the table above
   * this panel goes on listing the watch as it stands, and has no idea a replay is running. What
   * comes back from the viewer names stations of the drawing — the one thing a scrubbed moment
   * cannot change — so it is forwarded exactly as it arrives. Held back or reworded into people
   * while a replay ran, it would drop every mark from that table the moment the scrubber moved, and
   * leave a station reading as a place somebody is over a model drawing nobody.
   */
  it('goes on carrying the drawing’s answer up while a replay is engaged', () => {
    log = [
      atStation(ANA, '2026-09-12T06:40:00Z', 'p.g.3'),
      entered(ANA, '2026-09-12T06:10:00Z'),
    ];
    const answers: ReadonlySet<string>[] = [];
    show(tracking(), [entered(ANA, '2026-09-12T06:10:00Z')], {
      onUnplacedStationsChange: (stations) => answers.push(stations),
    });
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    const channel = given!.onUnplacedStationsChange;
    expect(channel).toBeTypeOf('function');

    fireEvent.click(screen.getByTestId('trip-tracking-replay-open'));

    // The party drawn is now the party of an earlier moment — and the channel is the same one.
    expect(given!.trackedCavers![0].position).toEqual({ kind: 'unreported' });
    expect(given!.onUnplacedStationsChange).toBe(channel);

    act(() => given!.onUnplacedStationsChange?.(new Set(['p.g.7'])));

    expect([...(answers.at(-1) ?? [])]).toEqual(['p.g.7']);
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
      expect(screen.queryByTestId('trip-tracking-dialog-place')).toBeNull();
    });

    it('sends the report through the same call the card under the watch sends', async () => {
      show(tracking(), [], { selectedCaverIds: [ANA] });
      pressStation('p.g.42');

      fireEvent.click(screen.getByTestId('trip-tracking-record-here-open'));
      // The station arrives named by the press rather than asked for — that is the whole of what
      // the press buys, and the dialog states it rather than offering it as something to type.
      expect(screen.getByTestId('trip-tracking-dialog-place')).toHaveTextContent('p.g.42');

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

  /**
   * A reading that failed is not the same absence as a reading still running, and used to be drawn
   * as though it were — this panel simply was not there.
   *
   * The difference is that waiting fixes one and never fixes the other. A failed reading holds no
   * stations and never will, so the watch stays on, goes on accepting every report relayed to it,
   * and places nobody — while the surface that should be showing where the party is shows nothing
   * and gives no reason. That is the one absence on this panel worth spending a box on.
   */
  it('says a failed survey cannot place anybody rather than drawing nothing', () => {
    held = model({ status: 'failed' });
    show();

    expect(screen.getByTestId('trip-tracking-model-unreadable')).toHaveTextContent(
      /holds no stations/i,
    );
  });

  /**
   * And says it in its own words. The setup card at the top of this tab states the same fault at
   * length, because it owns the chooser that answers it — and this panel is mounted on the same
   * tab, so a box repeating that headline, that body and that remedy word for word puts the
   * identical red alert on one screen twice. Two copies of a warning are read as one piece of
   * noise and skipped together, which is the failure this whole surface is meant to avoid.
   *
   * So: what is wrong *here* — there is nothing to draw — and a pointer to where the remedy lives,
   * rather than the remedy repeated.
   */
  it('leaves the remedy to the setup card rather than printing it a second time', () => {
    held = model({ status: 'failed' });
    show();

    const said = screen.getByTestId('trip-tracking-model-unreadable');
    expect(said).toHaveTextContent(/no drawing to show/i);
    expect(said).toHaveTextContent(/tracking setup at the top of this tab/i);
    expect(said).not.toHaveTextContent(/import that survey again/i);
    expect(said).not.toHaveTextContent(/choose another survey above/i);
  });

  // The twin, and the reason the case above had to be split out rather than widened: a reading
  // still running does finish, so it is left to finish in silence exactly as before.
  it('says nothing of the kind about a survey still being read', () => {
    held = model({ status: 'processing' });
    show();

    expect(screen.queryByTestId('trip-tracking-model-unreadable')).toBeNull();
  });

  // And the positive twin of both: a survey read right through draws the model, with no box at all.
  it('draws the model, and says none of it, when the survey was read through', () => {
    held = model();
    show();

    expect(screen.getByTestId('trip-tracking-model-panel')).toBeInTheDocument();
    expect(screen.queryByTestId('trip-tracking-model-unreadable')).toBeNull();
  });
});

/**
 * The map sheets beside the 3D scene: one tab per declared map, fed the very party the
 * viewer draws, under the one replay bar and the one record-here flow that stay outside
 * the strip. The sheet pane itself is faked — its drawing has its own tests — so what is
 * proved here is the panel's side of the bargain: what it fetches and when, what it hands
 * each pane, and that the viewer survives every switch.
 */
describe('the map sheets beside the 3D scene', () => {
  it('asks for the map links only while the model is open, like everything else it draws', () => {
    show();

    // Closed, the read is priced at nothing: this tab is opened routinely by somebody who
    // only wants to record that the party went in.
    expect(rasterLinksAskedFor).toEqual({ surveyModelId: MODEL, enabled: false });

    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    expect(rasterLinksAskedFor).toEqual({ surveyModelId: MODEL, enabled: true });

    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    expect(rasterLinksAskedFor).toEqual({ surveyModelId: MODEL, enabled: false });
  });

  it('shows no strip while the model declares no maps, and one tab per map when it does', () => {
    show();
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

    // No maps: the viewer is simply there, with no bar of one tab restating it. The
    // strip has nothing else to offer here — this surface authors no maps.
    expect(screen.getByTestId('viewer')).toBeInTheDocument();
    expect(screen.queryAllByRole('tab')).toHaveLength(0);

    cleanup();
    rasterLinks = [mapLink('link-a', 'Plan sheet'), mapLink('link-b', 'Profile sheet', 'map-profile-of')];
    show();
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

    expect(screen.getByRole('tab', { name: '3D' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Plan sheet/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Profile sheet/ })).toBeInTheDocument();
    // A sheet nobody has visited is not even mounted — the pane pays nothing until asked.
    expect(sheetPanes.size).toBe(0);
  });

  it('never remounts the viewer across a switch to a sheet and back', () => {
    rasterLinks = [mapLink('link-a', 'Plan sheet')];
    show();
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    expect(viewerLifecycle.mounts).toBe(1);

    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));
    expect(sheetPanes.get('link-a')?.active).toBe(true);
    // Hidden, not destroyed: the drawing keeps its parsed model and its context.
    expect(screen.getByTestId('viewer')).toBeInTheDocument();
    expect(viewerLifecycle.unmounts).toBe(0);

    fireEvent.click(screen.getByRole('tab', { name: '3D' }));
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));
    fireEvent.click(screen.getByRole('tab', { name: '3D' }));

    expect(viewerLifecycle.mounts).toBe(1);
    expect(viewerLifecycle.unmounts).toBe(0);
    // The sheet also stayed mounted, told it is off screen rather than taken down.
    expect(sheetPanes.get('link-a')?.active).toBe(false);
  });

  it('hands the sheet the very party the viewer draws — the replay’s fold included', () => {
    rasterLinks = [mapLink('link-a', 'Plan sheet')];
    log = [
      atStation(ANA, '2026-09-12T06:40:00Z', 'p.g.3'),
      entered(ANA, '2026-09-12T06:10:00Z'),
    ];
    show(tracking(), [entered(ANA, '2026-09-12T06:10:00Z')]);
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));

    // The same array, not an equal one: the sheet consumes the panel's one fold of who
    // is where, so the two drawings cannot even in principle disagree about the party.
    expect(sheetPanes.get('link-a')?.cavers).toBe(given!.trackedCavers);

    // Engage the replay and wind it to the end: the party under BOTH drawings is now the
    // log's answer for that moment — through the same single fold.
    fireEvent.click(screen.getByTestId('trip-tracking-replay-open'));
    fireEvent.keyDown(screen.getByRole('slider'), { key: 'End', keyCode: 35 });

    expect(given!.trackedCavers![0].position).toEqual({ kind: 'station', station: 'p.g.3' });
    expect(sheetPanes.get('link-a')?.cavers).toBe(given!.trackedCavers);
  });

  it('reopens on the 3D scene after the model was hidden while a sheet was up', () => {
    rasterLinks = [mapLink('link-a', 'Plan sheet')];
    show();
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));
    expect(screen.getByRole('tab', { name: /Plan sheet/ })).toHaveAttribute(
      'aria-selected',
      'true',
    );

    // Hide and reopen: which sheet was up belonged to the model that was on screen,
    // exactly like the replay, the size and the standing offer.
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));

    expect(screen.getByRole('tab', { name: '3D' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: /Plan sheet/ })).toHaveAttribute(
      'aria-selected',
      'false',
    );
  });

  it('raises the very record-here offer from a pinned station pressed on a sheet', async () => {
    rasterLinks = [mapLink('link-a', 'Plan sheet')];
    show();
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));

    fireEvent.click(screen.getByTestId('fake-sheet-press-link-a'));

    // The same alert a 3D press raises, outside the strip so it stands over whichever
    // pane is showing — and the same dialog behind its button.
    expect(screen.getByTestId('trip-tracking-picked-station')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('trip-tracking-record-here-open'));
    await screen.findByTestId('trip-tracking-record-here');
    // Named with the very station the sheet press handed up.
    expect(screen.getByTestId('trip-tracking-dialog-station-name')).toHaveTextContent('p.g.7');
  });

  it('offers no press channel to a reader who cannot record', () => {
    rasterLinks = [mapLink('link-a', 'Plan sheet')];
    show(tracking(), [], { canEdit: false });
    fireEvent.click(screen.getByTestId('trip-tracking-model-toggle'));
    fireEvent.click(screen.getByRole('tab', { name: /Plan sheet/ }));

    // The absence twin of the offer above: the channel itself is withheld, so the sheet
    // never dresses a pin up as a control whose press would be refused.
    expect(sheetPanes.get('link-a')?.onPickStation).toBeUndefined();
  });
});
