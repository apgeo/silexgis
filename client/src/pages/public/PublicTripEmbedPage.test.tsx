// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PublicTripEnvelope, PublicTripParticipant } from '../../api/hooks.ts';
import type { CaveViewFocusRequest } from '../../components/caveview/CaveViewPanel.tsx';
import { EMBED_CHANNEL, EMBED_PROTOCOL } from './publicTripEmbed.ts';

const HOST = 'https://club.example.org';

let answer: { data?: PublicTripEnvelope; isPending: boolean; error: unknown };

/**
 * Every read this document made that a stranger on somebody else's website could not perform.
 * Stubbed rather than omitted so that reaching for one fails with the rule rather than with a
 * module error about a missing export — the page next door carries the whole of the reasoning.
 */
let reachedBeyondTheEnvelope: string[] = [];
const authenticatedOnly = (name: string) => () => {
  reachedBeyondTheEnvelope.push(name);
  return { data: undefined, isPending: false, error: null };
};

vi.mock('../../api/hooks.ts', () => ({
  usePublicTrip: () => answer,
  useResLinksForTarget: authenticatedOnly('useResLinksForTarget'),
  useSurveyModel: authenticatedOnly('useSurveyModel'),
}));
vi.mock('react-router-dom', () => ({ useParams: () => ({ token: 'follow-token' }) }));

let given: Record<string, unknown> | undefined;
vi.mock('../../components/caveview/CaveViewPanel.tsx', () => ({
  default: (props: Record<string, unknown>) => {
    given = props;
    return <div data-testid="viewer" />;
  },
}));

const { default: PublicTripEmbedPage } = await import('./PublicTripEmbedPage.tsx');

function participant(overrides: Partial<PublicTripParticipant> = {}): PublicTripParticipant {
  return {
    ordinal: 1,
    label: null,
    teamId: null,
    stationName: null,
    depthM: null,
    lastRecordedAt: null,
    positionRecordedAt: null,
    in: false,
    out: false,
    ...overrides,
  };
}

const model = {
  format: 'survex3d' as const,
  modelUrl: '/api/v1/files/abc/content?token=first',
  meshUrl: null,
  anchorLongitude: null,
  anchorLatitude: null,
  anchorHeightM: null,
  sourceEpsg: 31700,
  proj4: '+proj=sterea',
};

function envelope(overrides: Partial<PublicTripEnvelope> = {}): PublicTripEnvelope {
  return {
    title: 'Peștera Demo Mare',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-14T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    model,
    teams: [],
    participants: [
      participant({ ordinal: 1, label: 'Ana', stationName: 'p.g.7', in: true }),
      participant({ ordinal: 2, in: true }),
    ],
    ...overrides,
  };
}

/**
 * A stand-in for the document that framed this page.
 *
 * `window.parent` is the same object as `window` in a test environment, so the conversation this
 * page is written to have — with its framer, and with nobody else — cannot be driven without one.
 * Everything it records is what the page tried to send and where it tried to send it, which is the
 * half of the contract a wrong implementation gets wrong silently.
 */
function fakeParent() {
  const sent: { message: Record<string, unknown>; origin: string }[] = [];
  const parent = {
    postMessage: (message: Record<string, unknown>, origin: string) => {
      sent.push({ message, origin });
    },
  } as unknown as Window;
  Object.defineProperty(window, 'parent', { value: parent, configurable: true });
  return { parent, sent };
}

/** Delivers a message as the browser would, with a source and an origin on it. */
function deliver(source: Window, origin: string, data: unknown) {
  const event = new MessageEvent('message', { data, origin });
  Object.defineProperty(event, 'source', { value: source });
  act(() => {
    window.dispatchEvent(event);
  });
}

const hello = { silexgis: EMBED_CHANNEL, v: EMBED_PROTOCOL, type: 'hello' };
const focus = (kind: string, ref: string) => ({
  silexgis: EMBED_CHANNEL,
  v: EMBED_PROTOCOL,
  type: 'focus',
  target: { kind, ref },
});

beforeEach(() => {
  answer = { data: envelope(), isPending: false, error: null };
  given = undefined;
  reachedBeyondTheEnvelope = [];
});

afterEach(() => {
  cleanup();
  Object.defineProperty(window, 'parent', { value: window, configurable: true });
});

describe('the viewer a website frames', () => {
  it('renders the viewer and nothing else — no title, no party list, no footer', () => {
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('viewer')).toBeInTheDocument();
    expect(screen.queryByText('Peștera Demo Mare')).toBeNull();
    expect(screen.queryByRole('heading')).toBeNull();
  });

  /**
   * The same rule as the page next door, asserted separately because this is a separate document
   * with its own viewer mount — and the one that would be forgotten, since it carries no chrome of
   * its own to make the omission visible to anybody reading it.
   */
  it('reaches for no station pictures, because a framed stranger cannot read the links', () => {
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('viewer')).toBeInTheDocument();
    expect(reachedBeyondTheEnvelope).toEqual([]);
    expect(given?.stationMedia).toBeUndefined();
  });

  it('fills the frame it was given, rather than a share of the screen', () => {
    render(<PublicTripEmbedPage />);

    expect(given?.height).toBe('100%');
  });

  it('keeps showing the trip when a later read fails, rather than emptying somebody’s page', () => {
    // A frame on a club's website that blanked itself every time a poll missed would read as a
    // broken embed. The envelope in hand is still the last true word.
    answer = { data: envelope(), isPending: false, error: new Error('offline') };
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('viewer')).toBeInTheDocument();
    expect(screen.queryByTestId('public-trip-embed-failure')).toBeNull();
  });

  it('says so in words when a published trip has no drawing to frame', () => {
    // An empty box on somebody else's website reads as a broken embed.
    answer = { data: envelope({ model: null }), isPending: false, error: null };
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-trip-embed-failure')).toHaveTextContent(
      'There is no survey drawing to show for this trip.',
    );
  });
});

describe('the conversation with the page that framed it', () => {
  it('answers a greeting with the party, addressed to the framer’s own origin', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, hello);

    expect(sent).toHaveLength(1);
    expect(sent[0].origin).toBe(HOST);
    expect(sent[0].message).toMatchObject({
      silexgis: EMBED_CHANNEL,
      v: EMBED_PROTOCOL,
      type: 'ready',
      loaded: true,
      party: [
        { ordinal: 1, name: 'Ana', station: 'p.g.7' },
        { ordinal: 2, name: 'Caver 2', station: null },
      ],
    });
  });

  it('says the party again once it arrives, because the greeting outruns the envelope', () => {
    // The real ordering, not a convenient one: the host script greets the frame when it loads,
    // and the request this page made inside that frame comes back afterwards. A greeting answered
    // once would tell a website the trip has nobody on it, forever, with no way to tell that
    // apart from a party that really is empty.
    answer = { data: undefined, isPending: true, error: null };
    const { parent, sent } = fakeParent();
    const view = render(<PublicTripEmbedPage />);

    deliver(parent, HOST, hello);
    expect(sent).toHaveLength(1);
    expect(sent[0].message).toMatchObject({ type: 'ready', loaded: false, party: [] });

    answer = { data: envelope(), isPending: false, error: null };
    view.rerender(<PublicTripEmbedPage />);

    expect(sent).toHaveLength(2);
    expect(sent[1].origin).toBe(HOST);
    expect(sent[1].message).toMatchObject({
      type: 'ready',
      loaded: true,
      party: [
        { ordinal: 1, name: 'Ana', station: 'p.g.7' },
        { ordinal: 2, name: 'Caver 2', station: null },
      ],
    });
  });

  it('says the party again when a poll moves somebody', () => {
    const { parent, sent } = fakeParent();
    const view = render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);

    answer = {
      data: envelope({
        participants: [
          participant({ ordinal: 1, label: 'Ana', stationName: 'p.g.14', in: true }),
          participant({ ordinal: 2, in: true }),
        ],
      }),
      isPending: false,
      error: null,
    };
    view.rerender(<PublicTripEmbedPage />);

    expect(sent).toHaveLength(2);
    expect(sent[1].message).toMatchObject({
      type: 'ready',
      party: [{ ordinal: 1, name: 'Ana', station: 'p.g.14' }, { ordinal: 2 }],
    });
  });

  it('says nothing again when a poll found nothing new', () => {
    // A minute's poll hands this page a fresh object saying the same thing. A host page that
    // re-renders its prose on every announcement should hear only about changes.
    const { parent, sent } = fakeParent();
    const view = render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);

    answer = { data: envelope(), isPending: false, error: null };
    view.rerender(<PublicTripEmbedPage />);

    expect(sent).toHaveLength(1);
  });

  it('says nothing to a page that never greeted it', () => {
    // Nothing is volunteered: this page does not know a framer's origin until the framer speaks,
    // and posting to one it guessed would be handing the party to whoever was listening.
    const { sent } = fakeParent();
    const view = render(<PublicTripEmbedPage />);

    answer = { data: envelope({ participants: [participant({ ordinal: 9, in: true })] }), isPending: false, error: null };
    view.rerender(<PublicTripEmbedPage />);

    expect(sent).toHaveLength(0);
  });

  it('never answers a wildcard origin', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, hello);
    deliver(parent, HOST, focus('station', 'p.g.7'));

    expect(sent.length).toBeGreaterThan(0);
    for (const outbound of sent) {
      expect(outbound.origin).not.toBe('*');
    }
  });

  it('ignores a message from anything that is not its framer', () => {
    // A page nested deeper, an opener, another frame in the tree: none of them is the document
    // this page is having a conversation with, and the origin alone cannot tell them apart.
    const { sent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(window, HOST, hello);
    deliver({} as Window, HOST, focus('station', 'p.g.7'));

    expect(sent).toHaveLength(0);
    expect(given?.focusRequest).toBeUndefined();
  });

  it('ignores anything that is not this conversation', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, { hello: 'there' });
    deliver(parent, HOST, { ...hello, silexgis: 'other-app' });
    deliver(parent, HOST, { ...hello, v: 99 });

    expect(sent).toHaveLength(0);
  });

  it('asks the viewer for a station a link in the prose named', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, focus('station', 'p.g.7'));

    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'p.g.7' });
  });

  it('asks for a named part of the survey when the link named one', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, focus('survey', 'galeria-nord'));

    expect(given?.focusRequest).toMatchObject({ kind: 'survey', ref: 'galeria-nord' });
  });

  it('resolves a place in the party to wherever that person was last reported', () => {
    // The point of naming a caver rather than a station: the link in the article stays correct
    // while the party moves.
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, focus('caver', '1'));

    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'p.g.7' });
  });

  it('answers a place in the party that nobody has placed, rather than saying nothing', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, focus('caver', '2'));

    expect(given?.focusRequest).toBeUndefined();
    expect(sent[0].message).toMatchObject({
      type: 'focused',
      found: false,
      target: { kind: 'caver', ref: '2' },
    });
  });

  it('asks again when the same link is pressed twice', () => {
    // The panel watches the request by identity, so a second ask that compared equal would leave
    // a reader who scrolled away and pressed the link again looking at the model they left.
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, focus('station', 'p.g.7'));
    const first = given?.focusRequest as CaveViewFocusRequest;
    deliver(parent, HOST, focus('station', 'p.g.7'));
    const second = given?.focusRequest as CaveViewFocusRequest;

    expect(second).not.toBe(first);
  });

  it('tells the framer whether the model held what the prose named', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, focus('station', 'p.g.7'));
    const request = given?.focusRequest as CaveViewFocusRequest;
    act(() => request.onSettled?.(false));

    expect(sent.at(-1)?.message).toMatchObject({
      type: 'focused',
      found: false,
      target: { kind: 'station', ref: 'p.g.7' },
    });
    expect(sent.at(-1)?.origin).toBe(HOST);
  });
});
