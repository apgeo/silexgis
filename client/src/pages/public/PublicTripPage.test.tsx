// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { PublicTripEnvelope, PublicTripParticipant } from '../../api/hooks.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';

const TEAM_A = '11111111-1111-1111-1111-111111111111';
const TEAM_B = '22222222-2222-2222-2222-222222222222';

let answer: {
  data?: PublicTripEnvelope;
  isPending: boolean;
  error: unknown;
  refetch?: () => unknown;
  /** When the last read that succeeded arrived, as the query reports it. */
  dataUpdatedAt?: number;
  /** The failure of an attempt that is still being retried, as the query reports it. */
  failureReason?: unknown;
  /** True while the browser holds the read back because it knows it has no connection. */
  isPaused?: boolean;
} = {
  data: undefined,
  isPending: true,
  error: null,
};
let askedFor: string | undefined;

/**
 * Every read this page made that an anonymous caller could not actually perform.
 *
 * <b>Stubbed and recorded rather than left out of the mock.</b> Leaving them out would also fail
 * the suite — as a module error naming a missing export, which says nothing about why this page may
 * not have it. A stub that answers like a real query and writes its name down instead fails the
 * test below with the rule itself: this page is read by somebody holding one token who is refused
 * every other address in the installation, so a request to one of these answers 401 where no
 * console is being watched, and the page then draws exactly what it would have drawn anyway.
 */
let reachedBeyondTheEnvelope: string[] = [];
const authenticatedOnly = (name: string) => () => {
  reachedBeyondTheEnvelope.push(name);
  return { data: undefined, isPending: false, error: null };
};

vi.mock('../../api/hooks.ts', () => ({
  usePublicTrip: (token: string | undefined) => {
    askedFor = token;
    return answer;
  },
  // The route a station's pictures are read from, and the one this page is likeliest to grow a
  // reach for: the signed-in surfaces draw pictures over this very model.
  useResLinksForTarget: authenticatedOnly('useResLinksForTarget'),
  useSurveyModel: authenticatedOnly('useSurveyModel'),
  // The archive, stubbed as never having answered: this file is about the live page, and whether
  // the archive is read at all — and when — is proved in the file that is about the archive.
  usePublicPastTrips: (_token: string | undefined, enabled: boolean) => ({
    data: undefined,
    isPending: enabled,
    isError: false,
  }),
  usePublicLiveTrips: (_token: string | undefined, enabled: boolean) => ({
    data: undefined,
    isPending: enabled,
    isError: false,
  }),
  usePublicPastTrack: (_token: string | undefined, tripLogId: string | undefined) => ({
    data: undefined,
    isPending: tripLogId !== undefined,
    isError: false,
  }),
}));

vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: 'follow-token' }),
  useSearchParams: () => [new URLSearchParams(), vi.fn()],
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
      /** Built from the envelope, and absent when it carried nothing; see the tests that say why. */
      stationMedia?: ReadonlyMap<string, readonly { url: string; thumbnailUrl?: string; caption?: string }[]>;
      /** How the viewer tells this page what the drawing could not place — see the test that drives it. */
      onUnplacedStationsChange?: (stations: ReadonlySet<string>) => void;
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

/**
 * The sheet pane is faked at its contract, exactly as the viewer above is: it pulls in the map
 * engine, and what this page owes it — which sheet, which party, whether its tab is the one on
 * screen — is the whole of what these tests read back. It is also lazy-loaded by the page, so
 * the tests that press its tab await its arrival the way a reader does.
 */
let sheetPane: { sheet?: { key: string; title: string | null }; active?: boolean; cavers?: readonly TrackedCaver[] } | undefined;
vi.mock('./PublicTripSheetPane.tsx', () => ({
  default: (props: NonNullable<typeof sheetPane>) => {
    sheetPane = props;
    return <div data-testid="sheet-pane" data-active={String(props.active)} />;
  },
}));

const { default: PublicTripPage } = await import('./PublicTripPage.tsx');

function participant(overrides: Partial<PublicTripParticipant> = {}): PublicTripParticipant {
  return {
    ordinal: 1,
    label: null,
    teamId: null,
    stationName: null,
    depthM: null,
    lastRecordedAt: null,
    positionRecordedAt: null,
    positionOnOtherModel: false,
    in: false,
    out: false,
    ...overrides,
  };
}

function envelope(overrides: Partial<PublicTripEnvelope> = {}): PublicTripEnvelope {
  return {
    tripLogId: '0195f4a2-6c3e-7b10-9f21-ab44de77c001',
    expedition: null,
    title: 'Peștera Demo Mare, exploration',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-14T06:00:00Z',
    closedAt: null,
    expectedReturnAt: null,
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
  reachedBeyondTheEnvelope = [];
  sheetPane = undefined;
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

  it('offers the other language as a button, in that language, wherever the page ends up', () => {
    // The suite reads English, so the way out leads to Romanian — and is written in Romanian,
    // because whoever needs it is not reading the page around it.
    ready();
    const { container, unmount } = render(<PublicTripPage />);

    const button = screen.getByRole('button', { name: 'Afișează pagina în română' });
    expect(button).toHaveTextContent('Română');
    expect(button).toHaveAttribute('lang', 'ro');
    expect(container.querySelector('footer')).toContainElement(button);
    expect(container.querySelectorAll('a')).toHaveLength(0);
    unmount();

    // A link that opens nothing says so in one sentence, and that sentence is the page.
    answer = { data: undefined, isPending: false, error: new ApiError(404) };
    render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-not-found')).toContainElement(
      screen.getByTestId('public-trip-language'),
    );
    cleanup();

    // And so does a server that could not be reached.
    answer = { data: undefined, isPending: false, error: new ApiError(503) };
    render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-unreachable')).toContainElement(
      screen.getByTestId('public-trip-language'),
    );
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

  /**
   * The name is the envelope's; a place in the party is what this page draws where it carries none.
   *
   * Which of the two arrives is the server's decision and not this page's. It may be a name an
   * administrator typed for this trip, or the roster's own name where the installation publishes
   * names; it is absent where the installation does not publish names, and also where somebody has
   * deliberately been kept off the page as a place in the party. This page cannot tell those apart
   * and must not try — what it owes is to show a name it was given and to say "Caver 2" in the
   * reader's own language when it was given none. Both halves are asserted here, because a page
   * that numbered everybody would pass the second on its own.
   */
  it('shows the name the envelope carries, and a place in the party where it carries none', () => {
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

  /**
   * The sentence this page must never produce about somebody underground.
   *
   * A watch re-pointed at a corrected survey mid-trip leaves every earlier report naming the
   * survey it was measured in. The server drops the station and the depth from those rows — a name
   * from another survey put beside this drawing reads as a place on it — and raises a bit saying
   * which kind of absence it is. Read as an ordinary absence, the page tells the family of a
   * person underground that nobody has reported where they are, which is false: somebody has.
   */
  it('says a place was reported on another survey rather than saying none was reported', () => {
    ready({
      participants: [
        participant({
          ordinal: 1,
          label: 'Ana',
          in: true,
          lastRecordedAt: '2026-09-14T09:00:00Z',
          positionOnOtherModel: true,
        }),
        // The twin, so this cannot pass by the page having stopped saying "no position" at all:
        // somebody nobody has placed still reads as nobody having placed them.
        participant({ ordinal: 2, in: true, lastRecordedAt: '2026-09-14T08:40:00Z' }),
      ],
    });
    render(<PublicTripPage />);

    const card = screen.getByTestId('public-trip-caver-1');
    expect(within(card).getByTestId('public-trip-position-other-model')).toBeInTheDocument();
    expect(card).toHaveTextContent('Reported on another survey');
    expect(card).not.toHaveTextContent('No position reported');
    // And the page says once, in full sentences, what the tag beside a name is short for — in
    // words that are actually drawn, rather than on an attribute nobody reads on a phone.
    expect(screen.getByTestId('public-trip-other-model')).toHaveTextContent(
      'Some places are not shown on this drawing',
    );
    expect(screen.getByTestId('public-trip-other-model')).toHaveTextContent(
      /It is not that nobody knows where they are/,
    );

    expect(screen.getByTestId('public-trip-caver-2')).toHaveTextContent('No position reported');
  });

  it('says nothing about other surveys when every place is on the one being drawn', () => {
    ready();
    render(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-other-model')).toBeNull();
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
    // The server's answer to a malformed, unknown, revoked and lapsed token is one identical 404,
    // and this page draws one identical card for it — with nothing read off the refusal to say
    // which of those it was.
    answer = { data: undefined, isPending: false, error: new ApiError(404) };
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-not-found')).toBeInTheDocument();
    expect(screen.getByText('Nothing to show for this link')).toBeInTheDocument();
    expect(screen.queryByTestId('public-trip-unreachable')).toBeNull();
  });

  it('says the server could not be reached when a first read fails on the network', () => {
    // A phone with no signal, opened straight onto the link: the read failed and nothing is in
    // hand. That is not an answer about the link, and a card saying it "may never have existed"
    // would be this page stating something it has no evidence for. Said as a fault, with the way
    // to ask again.
    const refetch = vi.fn();
    answer = { data: undefined, isPending: false, error: new TypeError('Failed to fetch'), refetch };
    render(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-not-found')).toBeNull();
    expect(screen.getByTestId('public-trip-unreachable')).toHaveTextContent(
      'The trip could not be read just now',
    );
    fireEvent.click(screen.getByTestId('public-trip-retry'));
    expect(refetch).toHaveBeenCalledOnce();
  });

  it('says the same when a first read was never sent because the phone knows it is offline', () => {
    // Nothing failed — the browser held the read back — so the query still calls it pending, and
    // drawn as pending it is a spinner for as long as the car park has no signal.
    answer = { data: undefined, isPending: true, error: null, isPaused: true };
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-unreachable')).toHaveTextContent(
      'The trip could not be read just now',
    );
    expect(screen.queryByTestId('public-trip-not-found')).toBeNull();

    // The twin: a first read that is simply in flight is still a page that is loading.
    cleanup();
    answer = { data: undefined, isPending: true, error: null, isPaused: false };
    render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-unreachable')).toBeNull();
  });

  it('says the same while a first read waits out a pause the server asked for', () => {
    // Refused for being asked too often, with a time to come back: the read is held for that
    // long before it is made again, and the query calls all of it pending.
    const refetch = vi.fn();
    answer = {
      data: undefined,
      isPending: true,
      error: null,
      failureReason: new ApiError(429, undefined, undefined, undefined, 60_000),
      refetch,
    };
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-unreachable')).toHaveTextContent(
      'The trip could not be read just now',
    );
    expect(screen.queryByTestId('public-trip-not-found')).toBeNull();
    fireEvent.click(screen.getByTestId('public-trip-retry'));
    expect(refetch).toHaveBeenCalledOnce();

    // The twin: an attempt being retried on this page's own short pacing is a blip, and is
    // still a page that is loading.
    cleanup();
    answer = { data: undefined, isPending: true, error: null, failureReason: new ApiError(503) };
    render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-unreachable')).toBeNull();
  });

  it('treats a server that is coming back like a dropped connection, not like a refusal', () => {
    answer = { data: undefined, isPending: false, error: new ApiError(503) };
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-unreachable')).toBeInTheDocument();
    expect(screen.queryByTestId('public-trip-not-found')).toBeNull();
  });

  it('says the link has stopped answering once a poll is refused for good, and promises no refresh', () => {
    // The ordinary end of every published trip that carried sheets or pictures: the grace after
    // the watch closed runs out, the route answers 404, and the last envelope is still in hand.
    // The party stays on screen, but the notice must not be the one about a dropped connection —
    // that one promises the page will refresh by itself, and this page never will again.
    ready();
    const view = render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('Ana');

    answer = { data: envelope(), isPending: false, error: new ApiError(404) };
    view.rerender(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-not-found')).toBeNull();
    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('Ana');
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
    expect(screen.getByTestId('public-trip-ended')).toHaveTextContent('This link has stopped answering');
    expect(screen.getByTestId('public-trip-ended')).not.toHaveTextContent('by itself');
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

/**
 * <b>How old the place is, on the page a family reads.</b>
 *
 * The same defect the coordinator's screen had, in front of the reader least able to question it:
 * a station reported at nine and a radio check at five to noon gave one age, the newer one, drawn
 * against the station. Somebody at home reading "p.g.7, five minutes ago" concludes the party was
 * at p.g.7 five minutes ago. Nobody said that.
 */
describe('how old a followed position is', () => {
  /** Noon, so the station below is three hours old and the note five minutes. */
  const AT_NOON = Date.parse('2026-09-14T12:00:00Z');
  let clock: MockInstance<typeof Date.now>;

  beforeEach(() => {
    clock = vi.spyOn(Date, 'now').mockReturnValue(AT_NOON);
  });

  // Restored one by one rather than through a blanket restore, which would also reset the module
  // mocks this file is built on.
  afterEach(() => clock.mockRestore());

  /** Ana, placed at nine and heard from at five to noon. */
  const placedAtNine = () =>
    participant({
      ordinal: 1,
      label: 'Ana',
      teamId: TEAM_A,
      stationName: 'p.g.7',
      in: true,
      lastRecordedAt: '2026-09-14T11:55:00Z',
      positionRecordedAt: '2026-09-14T09:00:00Z',
    });

  it('dates the station from the report that placed her, not from the later word', () => {
    ready({ participants: [placedAtNine()] });
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-position-age-1')).toHaveTextContent(
      'Reported 3 hours ago',
    );
    // The assertion this page exists to make true: the note five minutes old did not re-date the
    // place. Both halves, so it cannot pass by the age having disappeared.
    expect(screen.getByTestId('public-trip-position-age-1')).not.toHaveTextContent(
      '5 minutes ago',
    );
  });

  /**
   * Two facts, two labels, and they are allowed to disagree. "Last heard" answers whether word is
   * getting out of the cave at all; the position's age answers whether anybody has said where the
   * party is. A family watching for the second one must not be shown the first in its place.
   */
  it('keeps the last word and the position as two ages under two labels', () => {
    ready({ participants: [placedAtNine()] });
    render(<PublicTripPage />);

    const card = screen.getByTestId('public-trip-caver-1');
    expect(within(card).getByText('Last reported at')).toBeInTheDocument();
    expect(within(card).getByText('Last heard')).toBeInTheDocument();
    expect(within(card).getByText('5 minutes ago')).toBeInTheDocument();
    expect(screen.getByTestId('public-trip-position-age-1')).toHaveTextContent('3 hours ago');
    // The exact moment stays for whoever wants a clock rather than a gap.
    expect(screen.getByTestId('public-trip-position-age-1')).toHaveAttribute(
      'title',
      expect.stringContaining('2026'),
    );
  });

  /**
   * <b>Silence keeps no age here either, and a withheld position least of all.</b> A position
   * nobody reported and one this page may not carry arrive identically — with no moment — and
   * filling that gap from the last word would tell a stranger a position was reported at a moment
   * nobody reported one.
   */
  it('gives a position the envelope did not date no age at all', () => {
    ready({
      positionsWithheld: true,
      participants: [
        placedAtNine(),
        // Reported, not placed, on a trip that withholds: "not shown, or not reported".
        participant({ ordinal: 2, in: true, lastRecordedAt: '2026-09-14T11:50:00Z' }),
        // Nobody has said a word about this one at all.
        participant({ ordinal: 3 }),
      ],
    });
    render(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-position-age-2')).toBeNull();
    expect(screen.queryByTestId('public-trip-position-age-3')).toBeNull();
    // The positive twins: the withholding is still said in words, the unreported position still
    // says so, and the one place that was reported still carries its age.
    expect(screen.getByTestId('public-trip-position-withheld')).toBeInTheDocument();
    expect(screen.getByTestId('public-trip-caver-3')).toHaveTextContent('No position reported');
    expect(screen.getByTestId('public-trip-position-age-1')).toHaveTextContent('3 hours ago');
  });

  /**
   * The same refusal where a moment arrives beside an absence.
   *
   * The envelope sends no moment for a position it will not disclose — but the age is drawn beside
   * the place rather than off the participant, and this pins that. A page that dated a withheld
   * position would tell a stranger that a position exists and when it was reported, which is half
   * of what was being kept back.
   */
  it('draws no age beside a withheld position even if the envelope carries one', () => {
    ready({
      positionsWithheld: true,
      participants: [
        participant({
          ordinal: 2,
          in: true,
          lastRecordedAt: '2026-09-14T11:50:00Z',
          positionRecordedAt: '2026-09-14T09:00:00Z',
        }),
        placedAtNine(),
      ],
    });
    render(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-position-age-2')).toBeNull();
    expect(screen.getByTestId('public-trip-position-withheld')).toBeInTheDocument();
    // The twin: an age is still drawn where a place was.
    expect(screen.getByTestId('public-trip-position-age-1')).toBeInTheDocument();
  });
});

/**
 * <b>The page goes on being right while nobody touches it.</b>
 *
 * These run on a clock the test moves, because the defect they close is one of time passing: a
 * figure worded at a render and never again. Every case advances the clock with no read landing
 * and no press — the page's own ticker is the only thing that can have redrawn it.
 */
describe('a followed page left open', () => {
  /** Noon where the reader is, so "today" does not depend on the machine's time zone. */
  const NOON = new Date(2026, 8, 14, 12, 0).getTime();
  const at = (minutesBeforeNoon: number) => new Date(NOON - minutesBeforeNoon * 60_000).toISOString();
  const pass = (minutes: number) =>
    act(() => {
      vi.advanceTimersByTime(minutes * 60_000);
    });

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOON);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  /** Ana, placed three hours before noon and heard from five minutes before it. */
  const ana = () =>
    participant({
      ordinal: 1,
      label: 'Ana',
      teamId: TEAM_A,
      stationName: 'p.g.7',
      in: true,
      lastRecordedAt: at(5),
      positionRecordedAt: at(180),
    });

  const followed = (overrides: Partial<PublicTripEnvelope> = {}) => {
    answer = {
      data: envelope({ armedAt: at(190), participants: [ana()], ...overrides }),
      isPending: false,
      error: null,
      dataUpdatedAt: NOON - 40_000,
    };
  };

  it('says the hour the party planned to be out by, under since when, and no more once it has gone by', () => {
    // Planned out an hour and a half after noon.
    followed({ expectedReturnAt: at(-90) });
    render(<PublicTripPage />);
    const line = screen.getByTestId('public-trip-expected');
    expect(line.textContent).toMatch(/^Planned out by .*1:30/);
    // Under the line that says since when, in the same block of the header.
    const since = screen.getByTestId('public-trip-since');
    expect(since.compareDocumentPosition(line) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(line.parentElement).toBe(since.parentElement);

    // Two hours on the hour is behind, and the page has been told nothing new.
    pass(120);

    const after = screen.getByTestId('public-trip-expected').textContent ?? '';
    expect(after).toMatch(/^The plan was to be out by .*1:30/);
    // It states the plan and the hour. It is not an alarm and must not read as one.
    expect(after).not.toMatch(/late|overdue|missing|alarm|alert|warning|still|hr|min/i);
    expect(screen.getByTestId('public-trip-expected')).not.toHaveClass('ant-typography-danger');
    expect(screen.getByTestId('public-trip-expected')).not.toHaveClass('ant-typography-warning');
  });

  it('says nothing of a plan where it was sent none, which is every installation that does not publish it', () => {
    followed({ expectedReturnAt: null });
    const view = render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-expected')).toBeNull();
    expect(screen.getByTestId('public-trip-since')).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/planned|plan was/i);

    // The twin: the same page, sent an hour.
    answer = { ...answer, data: envelope({ armedAt: at(190), participants: [ana()], expectedReturnAt: at(-90) }) };
    view.rerender(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-expected')).toBeInTheDocument();
  });

  it('says nothing of a plan once nobody is underground, or once the watch is closed', () => {
    const out = participant({ ordinal: 1, label: 'Ana', out: true, lastRecordedAt: at(5) });
    // Past the hour, and everybody is out: people who are out are not expected out.
    followed({ expectedReturnAt: at(60), participants: [out] });
    const view = render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-expected')).toBeNull();

    // Somebody still recorded as in, and the watch closed by a person: the trip is over.
    answer = {
      ...answer,
      data: envelope({
        state: 'closed',
        armedAt: at(190),
        closedAt: at(10),
        expectedReturnAt: at(60),
        participants: [ana()],
      }),
    };
    view.rerender(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-expected')).toBeNull();

    // The twin: that hour, that person, a watch that is running.
    answer = { ...answer, data: envelope({ armedAt: at(190), expectedReturnAt: at(60), participants: [ana()] }) };
    view.rerender(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-expected')).toHaveTextContent(/^The plan was to be out by/);
  });

  it('moves every gap on with no read landing', () => {
    followed();
    render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-last-heard-1')).toHaveTextContent('5 minutes ago');
    expect(screen.getByTestId('public-trip-position-age-1')).toHaveTextContent('Reported 3 hours ago');

    pass(60);

    expect(screen.getByTestId('public-trip-last-heard-1')).toHaveTextContent('1 hour ago');
    expect(screen.getByTestId('public-trip-position-age-1')).toHaveTextContent('Reported 4 hours ago');
  });

  it('says when it last heard from the server, and that figure moves too', () => {
    followed();
    render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Page updated 40 seconds ago');

    pass(2);

    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Page updated 2 minutes ago');
  });

  it('says it has stopped refreshing while a read is held back or told to wait, though none has failed', () => {
    followed();
    const view = render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();

    // A phone that knows it is offline: the read is not sent, so nothing fails.
    answer = { ...answer, isPaused: true };
    view.rerender(<PublicTripPage />);
    pass(20);
    const held = screen.getByTestId('public-trip-stale');
    expect(held).toHaveTextContent('This page has stopped refreshing');
    expect(held).toHaveTextContent('20 minutes ago');
    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('p.g.7');
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();

    // The connection is back and a read lands: the notice goes by itself.
    answer = { ...answer, isPaused: false, dataUpdatedAt: Date.now() };
    view.rerender(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();

    // The server asked to be left alone for a while: the read is waiting, not failed.
    answer = {
      ...answer,
      failureReason: new ApiError(429, undefined, undefined, undefined, 30_000),
    };
    view.rerender(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-stale')).toHaveTextContent(
      'This page has stopped refreshing',
    );

    // The twin: an ordinary retry, seconds long, is not worth a warning.
    answer = { ...answer, failureReason: new ApiError(503) };
    view.rerender(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
  });

  it('measures from the read that last succeeded once a later one fails, and says how stale it is', () => {
    followed();
    const view = render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();

    // The connection drops: the envelope and the moment it arrived both stand.
    answer = { ...answer, error: new Error('Failed to fetch') };
    view.rerender(<PublicTripPage />);
    pass(5 * 60);

    const stale = screen.getByTestId('public-trip-stale');
    expect(stale).toHaveTextContent('This page has stopped refreshing');
    expect(stale).toHaveTextContent('5 hours ago');
    // Stale is a statement about the page. The party is still there, and nothing says the trip
    // or the link is gone.
    expect(screen.getByTestId('public-trip-caver-1')).toHaveTextContent('p.g.7');
    expect(screen.queryByTestId('public-trip-not-found')).toBeNull();
    expect(screen.queryByTestId('public-trip-unreachable')).toBeNull();
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();

    // And it clears by itself when a read lands.
    answer = { ...answer, error: null, dataUpdatedAt: Date.now() };
    view.rerender(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Page updated now');
  });

  it('says a gap while the trip is followed and the hour once it is over', () => {
    followed();
    const view = render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-last-heard-1')).toHaveTextContent('5 minutes ago');

    answer = {
      ...answer,
      data: envelope({ state: 'closed', armedAt: at(190), closedAt: at(1), participants: [ana()] }),
    };
    view.rerender(<PublicTripPage />);

    const heard = screen.getByTestId('public-trip-last-heard-1').textContent ?? '';
    const placed = screen.getByTestId('public-trip-position-age-1').textContent ?? '';
    expect(heard).toMatch(/11:55/);
    expect(placed).toMatch(/Reported at .*9:00/);
    expect(`${heard} ${placed}`).not.toMatch(/ago/);
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent(/Last read at .*11:59/);

    // Six hours on, word for word what it said: nothing on a finished trip grows.
    pass(6 * 60);
    expect(screen.getByTestId('public-trip-last-heard-1').textContent).toBe(heard);
    expect(screen.getByTestId('public-trip-position-age-1').textContent).toBe(placed);
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent(/Last read at .*11:59/);
  });

  it('says the hour too once the link has stopped answering, and when the page was last given anything', () => {
    followed();
    const view = render(<PublicTripPage />);

    answer = { ...answer, error: new ApiError(404) };
    view.rerender(<PublicTripPage />);
    pass(3 * 60);

    expect(screen.getByTestId('public-trip-last-heard-1').textContent).toMatch(/11:55/);
    expect(screen.getByTestId('public-trip-last-heard-1')).not.toHaveTextContent('ago');
    expect(screen.getByTestId('public-trip-ended')).toHaveTextContent(/at .*11:59/);
    expect(screen.getByTestId('public-trip-ended')).not.toHaveTextContent('by itself');
  });

  it('gives a day-old moment its date on a finished trip, so last night is not read as tonight', () => {
    answer = {
      data: envelope({
        state: 'closed',
        armedAt: at(30 * 60),
        closedAt: at(20 * 60),
        participants: [participant({ ordinal: 1, out: true, lastRecordedAt: at(20 * 60) })],
      }),
      isPending: false,
      error: null,
      dataUpdatedAt: NOON,
    };
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-last-heard-1').textContent).toMatch(/Sep 13.*4:00/);
  });

  it('says since when the trip has been followed, and for how long, and that grows', () => {
    followed();
    render(<PublicTripPage />);
    const since = screen.getByTestId('public-trip-since');
    expect(since.textContent).toMatch(/^Followed since .*8:50.* · 3 hr 10 min$/);
    // What the moment is: when the watch was started. Never when anybody went underground.
    expect(since).not.toHaveTextContent(/underground/i);

    pass(50);

    expect(screen.getByTestId('public-trip-since').textContent).toMatch(/ · 4 hr$/);
  });

  it('says a finished trip’s span instead, and it stands still', () => {
    followed({ state: 'closed', closedAt: at(10) });
    render(<PublicTripPage />);
    const said = screen.getByTestId('public-trip-since').textContent ?? '';
    expect(said).toMatch(/^Followed .*8:50.*11:50/);
    expect(said).not.toMatch(/since| hr| min/);

    pass(4 * 60);

    expect(screen.getByTestId('public-trip-since').textContent).toBe(said);
  });

  it('says nothing about since when where the trip carries no such moment', () => {
    followed({ armedAt: null });
    const view = render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-since')).toBeNull();

    answer = { ...answer, data: envelope({ armedAt: 'not a moment' }) };
    view.rerender(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-since')).toBeNull();

    // The twin: the line is there for a trip that does say.
    answer = { ...answer, data: envelope({ armedAt: at(190) }) };
    view.rerender(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-since')).toBeInTheDocument();
  });

  it('claims no age for itself where it was never told when it was read', () => {
    answer = { data: envelope(), isPending: false, error: new Error('Failed to fetch') };
    render(<PublicTripPage />);

    expect(screen.queryByTestId('public-trip-updated')).toBeNull();
    expect(screen.getByTestId('public-trip-stale')).toHaveTextContent('This page has stopped refreshing');
  });

  it('leaves no timer behind when the page goes', () => {
    followed();
    const view = render(<PublicTripPage />);
    expect(vi.getTimerCount()).toBeGreaterThan(0);

    view.unmount();

    expect(vi.getTimerCount()).toBe(0);
  });
});

describe('what a place on a followed page is', () => {
  it('says standing that a place is a report and not a live position', () => {
    ready();
    render(<PublicTripPage />);

    const about = screen.getByTestId('public-trip-about');
    expect(about).toHaveTextContent('where somebody was last reported');
    expect(about).toHaveTextContent('It is not a live position');
  });

  it('keeps the fuller explanation one press away, and opens it in place', () => {
    ready();
    const { container } = render(<PublicTripPage />);
    const more = screen.getByTestId('public-trip-about-more');
    const body = screen.getByTestId('public-trip-about-body');
    expect(more).toHaveAttribute('aria-expanded', 'false');
    expect(body).not.toBeVisible();

    fireEvent.click(more);

    expect(more).toHaveAttribute('aria-expanded', 'true');
    expect(body).toBeVisible();
    expect(body).toHaveTextContent('Long gaps are normal');
    expect(body).toHaveTextContent('It does not mean anything has happened');
    expect(more.getAttribute('aria-controls')).toBe(body.id);
    // Opened in place: still a page with no door in it.
    expect(container.querySelectorAll('a')).toHaveLength(0);
  });

  it('announces the party’s standing when it changes, and not the page’s age', () => {
    answer = { data: envelope(), isPending: false, error: null, dataUpdatedAt: Date.now() };
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-counts')).toHaveAttribute('aria-live', 'polite');
    // A figure that changes twice a minute would be read out twice a minute.
    expect(screen.getByTestId('public-trip-updated')).not.toHaveAttribute('aria-live');
    expect(screen.getByTestId('public-trip-updated')).not.toHaveAttribute('role');
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
    pictures: [],
    rasterMaps: [],
  };

  /** One published photograph, as the envelope hands it over: a rendering URL and nothing else. */
  const picture = (stationName: string, token: string, caption: string | null = null) => ({
    stationName,
    thumbnailUrl: `/api/v1/files/${token}/thumbnail?size=480&token=${token}`,
    caption,
  });

  /**
   * The same photograph as a later read hands it over: one file, a signature that is new every
   * time. Which is what a re-read of this envelope actually looks like — the file a picture is
   * cannot change, and the string that opens it cannot stay the same.
   */
  const resigned = (stationName: string, file: string, signature: string) => ({
    stationName,
    thumbnailUrl: `/api/v1/files/${file}/thumbnail?size=480&token=${signature}`,
    caption: null,
  });

  it('invents a file name whose extension selects the parser, since the envelope carries none', () => {
    ready({ model });
    render(<PublicTripPage />);

    expect(given?.fileName).toBe('trip.3d');
  });

  /**
   * Station pictures come out of the envelope, and out of nothing else.
   *
   * <b>The failure being guarded against is a silent one.</b> The signed-in surfaces draw a strip
   * of photographs over the stations of this same model, read from the model's links — and that
   * route takes an account. A page that copied the signed-in wiring here would fire a request that
   * answers 401, bury it in a query nobody inspects, and render a model with no strips: identical,
   * pixel for pixel, to a cave whose stations genuinely have no photographs. Nothing would ever
   * report it. So the request is asserted as well as the picture.
   */
  it('hangs the envelope\u2019s pictures on their stations without reaching for any route', () => {
    ready({
      model: { ...model, pictures: [picture('p.g.7', 'tok-a', 'The pitch head'), picture('p.g.9', 'tok-b')] },
    });
    render(<PublicTripPage />);

    expect(screen.getByTestId('viewer')).toBeTruthy();
    expect(reachedBeyondTheEnvelope).toEqual([]);
    expect([...given!.stationMedia!.keys()]).toEqual(['p.g.7', 'p.g.9']);

    // Both widths come off the one signed URL the envelope carried \u2014 the token is spent, never
    // replaced, and nothing anywhere reaches for the upload the rendering was drawn from.
    const [entry] = given!.stationMedia!.get('p.g.7')!;
    expect(entry.url).toBe('/api/v1/files/tok-a/thumbnail?size=1200&token=tok-a');
    expect(entry.thumbnailUrl).toBe('/api/v1/files/tok-a/thumbnail?size=160&token=tok-a');
    expect(entry.caption).toBe('The pitch head');
    expect(JSON.stringify(given!.stationMedia)).not.toContain('/content');
  });

  /**
   * The positive test's twin, and the ordinary case rather than an edge one.
   *
   * A club that has curated no public gallery publishes no photographs, which the owner settled as
   * correct rather than as a gap. What matters is <em>how</em> the page says so: not an empty map,
   * which tells the viewer this surface shows pictures and turns on the station label a strip would
   * hang under, but the prop's absence \u2014 so a trip with nothing to show behaves exactly as it did
   * before pictures existed, and no station draws a thumbnail element with nothing behind it.
   */
  it('shows the viewer no picture surface at all when the envelope carried none', () => {
    ready({ model: { ...model, pictures: [] } });
    render(<PublicTripPage />);

    expect(screen.getByTestId('viewer')).toBeTruthy();
    expect(reachedBeyondTheEnvelope).toEqual([]);
    expect(given!.stationMedia).toBeUndefined();
  });

  it('says which places the drawing cannot show, once for the page and beside each name', () => {
    // The one thing on this page the server could not have warned about. Ana's station was
    // measured on the very survey drawn above, so nothing in the envelope is out of the ordinary —
    // and the drawing this browser parsed holds no station of that name, which only the viewer
    // that parsed it can say. Unmarked, a family reads a name, a station and an age, and then
    // searches a drawing for a dot that was never going to be there.
    ready({ model });
    render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-not-on-model')).toBeNull();

    act(() => {
      (given!.onUnplacedStationsChange as (stations: ReadonlySet<string>) => void)(
        // The station, as the viewer answers it: what it knows is which names are nodes of the
        // file it parsed, and that is true of whoever is reported there.
        new Set(['p.g.7']),
      );
    });

    const card = screen.getByTestId('public-trip-caver-1');
    expect(within(card).getByTestId('public-trip-position-not-on-model-1')).toBeInTheDocument();
    // The station stays: it is what somebody reported, and it is what would be read out over a
    // phone. What is added is that the drawing above cannot show it.
    expect(card).toHaveTextContent('p.g.7');
    expect(screen.getByTestId('public-trip-not-on-model')).toHaveTextContent(
      'The drawing does not hold some of these stations',
    );
    expect(screen.getByTestId('public-trip-not-on-model')).toHaveTextContent(
      /It is not that nobody knows where they are/,
    );

    // The twin, on the same render: a reader watching somebody the drawing can place is told
    // nothing new about them.
    expect(screen.getByTestId('public-trip-caver-3')).not.toHaveTextContent('Not on the drawing');
  });

  /**
   * The pictures are deliberately <em>not</em> pinned the way the model URL is \u2014 and just as
   * deliberately not rebuilt.
   *
   * <b>Not pinned</b>, because a signed picture URL expires in about ten minutes and is spent
   * lazily: a thumbnail is fetched when a finger lands on its station, which may be long after the
   * page opened. A pinned set would be thumbnails that had quietly stopped resolving.
   *
   * <b>Not rebuilt</b>, because the viewer is told about pictures by being handed a source, and
   * being handed a new one closes the strip and drops the hover listeners under it. Every re-read
   * re-signs every URL, so a page that simply re-derived would dismiss the photographs a reader was
   * looking at, up to a minute after they tapped the station and without them touching anything.
   * So the same map comes back, restamped \u2014 which is also why this asserts an identity rather than
   * only a string.
   */
  it('restamps the pictures it already handed over instead of handing over a new set', () => {
    ready({ model: { ...model, pictures: [resigned('p.g.7', 'photo-1', 'sig-first')] } });
    const view = render(<PublicTripPage />);
    const handedOver = given!.stationMedia!;
    expect(handedOver.get('p.g.7')![0].url).toContain('token=sig-first');

    ready({
      model: {
        ...model,
        modelUrl: '/api/v1/files/abc/content?token=second',
        pictures: [resigned('p.g.7', 'photo-1', 'sig-second')],
      },
    });
    view.rerender(<PublicTripPage />);

    expect(given?.fileUrl).toBe('/api/v1/files/abc/content?token=first');
    expect(given!.stationMedia).toBe(handedOver);
    expect(given!.stationMedia!.get('p.g.7')![0].url).toContain('token=sig-second');
  });

  /**
   * The twin of the test above, and what stops it from being read as "never change the source".
   *
   * A photograph published since the page opened is something the reader is owed, and the strip
   * closing once is what showing it costs.
   */
  it('hands over a new set when a re-read actually brought different photographs', () => {
    ready({ model: { ...model, pictures: [resigned('p.g.7', 'photo-1', 'sig-first')] } });
    const view = render(<PublicTripPage />);
    const handedOver = given!.stationMedia!;

    ready({
      model: {
        ...model,
        pictures: [
          resigned('p.g.7', 'photo-1', 'sig-second'),
          resigned('p.g.9', 'photo-2', 'sig-second'),
        ],
      },
    });
    view.rerender(<PublicTripPage />);

    expect(given!.stationMedia).not.toBe(handedOver);
    expect([...given!.stationMedia!.keys()]).toEqual(['p.g.7', 'p.g.9']);
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

  /**
   * The other half of the pin, and the half that was missing: what is held still is the survey,
   * not the address of a survey.
   *
   * A coordinator may re-point an armed watch at a corrected survey while the party is
   * underground. The server then publishes stations measured in the new survey, and a page still
   * drawing the old one places a marker at whatever node of the old geometry happens to carry that
   * name — a confident marker for a person underground, on geometry their report was never
   * measured against. So an address naming a different file replaces the pin, camera reset and all.
   */
  it('takes up the new survey when the trip is re-pointed at one, mid-follow', () => {
    ready({ model });
    const view = render(<PublicTripPage />);
    expect(given?.fileUrl).toBe('/api/v1/files/abc/content?token=first');

    ready({ model: { ...model, modelUrl: '/api/v1/files/def/content?token=third' } });
    view.rerender(<PublicTripPage />);

    expect(given?.fileUrl).toBe('/api/v1/files/def/content?token=third');
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

  /** One sheet as the envelope hands it over, points resolved to the very rendering named. */
  const rasterMap = (title = 'Plan sheet') => ({
    title,
    viewKind: 'plan' as const,
    imageUrl: '/api/v1/files/sheet-1/thumbnail?size=1200&token=sig',
    points: [{ station: 'p.g.7', x: 0.25, y: 0.75 }],
  });

  /**
   * The ordinary trip has no sheets, and its page must be exactly the page that existed before
   * sheets did: the drawing, no strip. The bar is hidden rather than the Tabs left out so the
   * viewer keeps its identity across the moment a poll first brings a declaration in — but
   * what a reader can see and press is the same nothing either way.
   */
  it('shows no tab strip for a trip with no sheets', () => {
    ready({ model });
    render(<PublicTripPage />);

    expect(screen.getByTestId('viewer')).toBeTruthy();
    expect(screen.queryByRole('tab')).toBeNull();
    expect(screen.queryByTestId('sheet-pane')).toBeNull();
  });

  /**
   * With sheets, the strip appears beside the 3D drawing — and the sheet's pane arrives only
   * when its tab is pressed: the pane carries the map engine, and no sheet is fetched for a
   * tab nobody opened.
   */
  it('offers one tab per sheet and mounts a sheet only when its tab is opened', async () => {
    ready({ model: { ...model, rasterMaps: [rasterMap()] } });
    render(<PublicTripPage />);

    // The strip names both drawings; the 3D pane is the one on screen and the sheet is not
    // mounted at all yet.
    expect(screen.getByRole('tab', { name: '3D' })).toBeInTheDocument();
    const mapTab = screen.getByRole('tab', { name: /Plan sheet/ });
    const viewerBefore = screen.getByTestId('viewer');
    expect(screen.queryByTestId('sheet-pane')).toBeNull();

    act(() => {
      mapTab.click();
    });

    // The pane arrives (it is lazy), is the active one, and is handed the same party the 3D
    // pane draws — one fold, two drawings.
    expect(await screen.findByTestId('sheet-pane')).toHaveAttribute('data-active', 'true');
    expect(sheetPane?.sheet?.title).toBe('Plan sheet');
    expect(sheetPane?.cavers?.map((caver) => caver.caverId)).toEqual(['1', '2', '3', '4']);
    // The 3D pane was hidden, never unmounted: the same DOM node is still in the tree, so
    // its parsed model and its drawing context survive the switch — the whole reason the
    // strip keeps inactive panes mounted.
    expect(screen.getByTestId('viewer')).toBe(viewerBefore);

    // And nothing about sheets reached beyond the envelope: no route answers a stranger.
    expect(reachedBeyondTheEnvelope).toEqual([]);
  });
});
