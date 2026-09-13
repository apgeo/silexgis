// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { SurveyModelInfo, TrackingEvent, TrackingState, TripParticipant } from '../../api/hooks.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';

const ANA = 'caver-ana';
const BOGDAN = 'caver-bogdan';
const MODEL = 'model-1';

let held: SurveyModelInfo | undefined;
let askedFor: string | undefined;

vi.mock('../../api/hooks.ts', () => ({
  surveyModelReadableByViewer: (m: { format: string }) => m.format === 'lox' || m.format === 'survex3d',
  useSurveyModel: (id: string | undefined) => {
    askedFor = id;
    return { data: id === undefined ? undefined : held, isPending: false };
  },
}));

// The viewer itself is a three.js bundle holding a drawing context. What it is handed is the
// point: which model, and who is drawn on it.
let given: { fileName?: string; surveyModelId?: string; trackedCavers?: readonly TrackedCaver[] } | undefined;
vi.mock('../caveview/CaveViewPanel.tsx', () => ({
  default: (props: { fileName: string; surveyModelId?: string; trackedCavers?: readonly TrackedCaver[] }) => {
    given = props;
    return <div data-testid="viewer" />;
  },
}));

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

function show(state = tracking(), events: TrackingEvent[] = []) {
  return render(<TrackingModelPanel tracking={state} participants={roster} events={events} />);
}

beforeEach(() => {
  held = model();
  askedFor = undefined;
  given = undefined;
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
        teamTitle: 'Team A',
        position: { kind: 'station', station: 'p.g.7' },
        lastRecordedAt: '2026-09-12T07:00:00Z',
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

  it('offers nothing when the watch names no model', () => {
    show(tracking({ surveyModelId: null }));

    expect(askedFor).toBeUndefined();
    expect(screen.queryByTestId('trip-tracking-model')).toBeNull();
  });

  it('offers nothing for a model this viewer cannot read, or one that is not ready yet', () => {
    // A wall mesh has no stations to place anybody at, and a model still being processed has
    // nothing to draw. Offering either is a button whose every press fails.
    held = model({ format: 'stl' });
    const walls = show();
    expect(screen.queryByTestId('trip-tracking-model')).toBeNull();
    walls.unmount();

    held = model({ status: 'processing' });
    show();
    expect(screen.queryByTestId('trip-tracking-model')).toBeNull();
  });
});
