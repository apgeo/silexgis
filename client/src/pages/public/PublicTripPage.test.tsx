// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PublicTripEnvelope, PublicTripParticipant } from '../../api/hooks.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';

const TEAM_A = '11111111-1111-1111-1111-111111111111';
const TEAM_B = '22222222-2222-2222-2222-222222222222';

let answer: { data?: PublicTripEnvelope; isPending: boolean; error: unknown } = {
  data: undefined,
  isPending: true,
  error: null,
};
let askedFor: string | undefined;

vi.mock('../../api/hooks.ts', () => ({
  usePublicTrip: (token: string | undefined) => {
    askedFor = token;
    return answer;
  },
}));

vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: 'follow-token' }),
}));

// The viewer is a three.js bundle holding a drawing context; what it is handed is the point. The
// delivery URL in particular, because the rule about it is the whole reason this page pins one.
let given:
  | {
      fileUrl?: string;
      fileName?: string;
      height?: number | string;
      trackedCavers?: readonly TrackedCaver[];
      crsLookup?: (code: string) => Promise<string | null>;
    }
  | undefined;
let mounts = 0;
vi.mock('../../components/caveview/CaveViewPanel.tsx', () => ({
  default: (props: Record<string, unknown>) => {
    given = props;
    mounts++;
    return <div data-testid="viewer" />;
  },
}));

let narrow = false;
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => narrow }));

const { default: PublicTripPage } = await import('./PublicTripPage.tsx');

function participant(overrides: Partial<PublicTripParticipant> = {}): PublicTripParticipant {
  return {
    ordinal: 1,
    label: null,
    teamId: null,
    stationName: null,
    depthM: null,
    lastRecordedAt: null,
    in: false,
    out: false,
    ...overrides,
  };
}

function envelope(overrides: Partial<PublicTripEnvelope> = {}): PublicTripEnvelope {
  return {
    title: 'Peștera Demo Mare, exploration',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-14T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    model: null,
    teams: [
      { id: TEAM_A, title: 'Advance' },
      { id: TEAM_B, title: 'Survey' },
    ],
    participants: [
      participant({ ordinal: 1, label: 'Ana', teamId: TEAM_A, stationName: 'p.g.7', in: true, lastRecordedAt: '2026-09-14T09:00:00Z' }),
      participant({ ordinal: 2, teamId: TEAM_A, in: true, lastRecordedAt: '2026-09-14T08:40:00Z' }),
      participant({ ordinal: 3, teamId: TEAM_B, out: true, lastRecordedAt: '2026-09-14T08:00:00Z' }),
      participant({ ordinal: 4 }),
    ],
    ...overrides,
  };
}

function ready(overrides: Partial<PublicTripEnvelope> = {}) {
  answer = { data: envelope(overrides), isPending: false, error: null };
}

beforeEach(() => {
  answer = { data: undefined, isPending: true, error: null };
  askedFor = undefined;
  given = undefined;
  mounts = 0;
  narrow = false;
});

afterEach(cleanup);

describe('a trip followed by somebody with no account', () => {
  it('reads the trip by the token in the address, and nothing else', () => {
    ready();
    render(<PublicTripPage />);

    expect(askedFor).toBe('follow-token');
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent(
      'Peștera Demo Mare, exploration',
    );
  });

  it('offers no way into the application, because there is none for this reader', () => {
    // Not decoration. A visitor here holds no account and every other address refuses them, so a
    // link is a door that is locked and chrome is a promise of a workspace they are not in.
    ready();
    const { container } = render(<PublicTripPage />);

    expect(container.querySelectorAll('a')).toHaveLength(0);
    expect(screen.queryByRole('button', { name: /sign in/i })).toBeNull();
  });

  it('groups the party by team and keeps the teams in the order the envelope sent them', () => {
    ready();
    render(<PublicTripPage />);

    const party = screen.getByTestId('public-trip-party');
    const headings = within(party).getAllByRole('heading', { level: 2 });
    expect(headings.map((heading) => heading.textContent)).toEqual([
      'Advance',
      'Survey',
      'Not in a team',
    ]);
  });

  it('names each person as the administrator named them, and the rest by their place', () => {
    ready();
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('Ana');
    expect(screen.getByTestId('public-trip-caver-2')).toHaveTextContent('Caver 2');
    expect(screen.getByTestId('public-trip-caver-4')).toHaveTextContent('Caver 4');
    // The one thing that must never travel here.
    expect(screen.getByTestId('public-trip-party').innerHTML).not.toContain('caverId');
  });

  it('says who is out, and keeps "not reported yet" apart from it', () => {
    ready();
    render(<PublicTripPage />);

    expect(within(screen.getByTestId('public-trip-caver-3')).getByText('Out')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('public-trip-caver-4')).getByText('Not reported yet'),
    ).toBeInTheDocument();
    expect(screen.getByTestId('public-trip-count-underground')).toHaveTextContent('2');
    expect(screen.getByTestId('public-trip-count-out')).toHaveTextContent('1');
    expect(screen.getByTestId('public-trip-count-unheard')).toHaveTextContent('1');
  });

  it('says where each person was last reported', () => {
    ready();
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('p.g.7');
    expect(screen.getByTestId('public-trip-caver-2')).toHaveTextContent('No position reported');
  });

  it('marks a withheld position as withheld rather than as an absence', () => {
    // An absence read as "nobody knows where they are" is the failure this guards against; what is
    // true is that this reader is not being told.
    ready({
      positionsWithheld: true,
      participants: [
        participant({ ordinal: 1, in: true, lastRecordedAt: '2026-09-14T09:00:00Z' }),
      ],
    });
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-withheld')).toBeInTheDocument();
    expect(screen.getByTestId('public-trip-position-withheld')).toHaveTextContent(
      'Not shown, or not reported',
    );
  });

  it('says nothing about withholding on a trip where nothing was withheld', () => {
    ready();
    render(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-withheld')).toBeNull();
    expect(screen.queryByTestId('public-trip-position-withheld')).toBeNull();
  });

  it('says a closed watch is over, so a stopped page does not read as a stalled one', () => {
    ready({ state: 'closed', closedAt: '2026-09-14T13:00:00Z' });
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-closed')).toBeInTheDocument();
    expect(screen.getByTestId('public-trip-state-closed')).toHaveTextContent('Finished');
  });

  it('answers every unusable link with one page, and inspects nothing to choose it', () => {
    answer = { data: undefined, isPending: false, error: new Error('404') };
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-not-found')).toBeInTheDocument();
    expect(screen.getByText('Nothing to show for this link')).toBeInTheDocument();
  });

  it('keeps the party on screen when a poll fails, and says the page has stopped refreshing', () => {
    // A phone in a tunnel. The refusal says nothing about the link, and the envelope already in
    // hand is still the last true word about where everybody was — so replacing a party a family
    // is watching with "this link may never have existed" would be both alarming and untrue.
    ready();
    const view = render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('Ana');

    answer = { data: envelope(), isPending: false, error: new Error('Failed to fetch') };
    view.rerender(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-not-found')).toBeNull();
    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('Ana');
    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('p.g.7');
    expect(screen.getByTestId('public-trip-stale')).toHaveTextContent(
      'This page has stopped refreshing',
    );
  });

  it('says nothing about refreshing while the reads are going through', () => {
    ready();
    render(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
  });
});

describe('the drawing on a followed page', () => {
  const model = {
    format: 'survex3d' as const,
    modelUrl: '/api/v1/files/abc/content?token=first',
    meshUrl: null,
    anchorLongitude: null,
    anchorLatitude: null,
    anchorHeightM: null,
    sourceEpsg: 31700,
    proj4: '+proj=sterea +lat_0=46',
  };

  it('invents a file name whose extension selects the parser, since the envelope carries none', () => {
    ready({ model });
    render(<PublicTripPage />);

    expect(given?.fileName).toBe('trip.3d');
  });

  it('resolves the coordinate system out of the envelope, never over the network', async () => {
    // The route that answers coordinate definitions is not on the anonymous list, so a viewer left
    // to fetch one would 401 and draw the survey unreferenced.
    ready({ model });
    render(<PublicTripPage />);

    await expect(given?.crsLookup?.('31700')).resolves.toBe(model.proj4);
    await expect(given?.crsLookup?.('4326')).resolves.toBeNull();
  });

  it('keeps the delivery URL it first loaded, however many times the envelope is re-read', () => {
    ready({ model });
    const view = render(<PublicTripPage />);
    expect(given?.fileUrl).toBe('/api/v1/files/abc/content?token=first');

    // A minute later the page re-reads the envelope and is handed a freshly signed address for the
    // same bytes. Passing it through would re-download and re-parse the model and throw the camera
    // back to its opening view — every minute, for as long as somebody watches.
    ready({ model: { ...model, modelUrl: '/api/v1/files/abc/content?token=second' } });
    view.rerender(<PublicTripPage />);

    expect(given?.fileUrl).toBe('/api/v1/files/abc/content?token=first');
  });

  it('draws the party on the model, keyed by their place in it', () => {
    ready({ model });
    render(<PublicTripPage />);

    expect(given?.trackedCavers?.map((caver) => caver.caverId)).toEqual(['1', '2', '3', '4']);
    expect(given?.trackedCavers?.[0].name).toBe('Ana');
  });

  it('never hands the viewer the whole screen, whatever the screen is', () => {
    // The viewer takes every gesture that begins inside it, so a reader whose thumb has nowhere
    // else to land cannot scroll past it.
    ready({ model });
    render(<PublicTripPage />);
    expect(given?.height).toBe('min(440px, 60dvh)');

    cleanup();
    narrow = true;
    render(<PublicTripPage />);
    expect(given?.height).toBe('min(300px, 60dvh)');
  });

  it('shows no viewer at all for a published trip with no drawing', () => {
    ready({ model: null });
    render(<PublicTripPage />);

    expect(screen.queryByTestId('viewer')).toBeNull();
    expect(mounts).toBe(0);
  });
});
