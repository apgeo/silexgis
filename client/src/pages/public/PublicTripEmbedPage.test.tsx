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
  usePublicPastTrips: (_token: string | undefined, enabled: boolean) => ({
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
vi.mock('react-router-dom', () => ({ useParams: () => ({ token: 'follow-token' }) }));

let given: Record<string, unknown> | undefined;
vi.mock('../../components/caveview/CaveViewPanel.tsx', () => ({
  default: (props: Record<string, unknown>) => {
    given = props;
    return <div data-testid="viewer" />;
  },
}));

/** The sheet pane, faked at its contract like the viewer: it carries the map engine. */
let sheetPane: Record<string, unknown> | undefined;
vi.mock('./PublicTripSheetPane.tsx', () => ({
  default: (props: Record<string, unknown>) => {
    sheetPane = props;
    return <div data-testid="sheet-pane" data-active={String(props.active)} />;
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
    positionOnOtherModel: false,
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
  pictures: [],
  rasterMaps: [],
};

/** One published photograph, as the envelope hands it over: a rendering URL and nothing else. */
const picture = (stationName: string, token: string, caption: string | null = null) => ({
  stationName,
  thumbnailUrl: `/api/v1/files/${token}/thumbnail?size=480&token=${token}`,
  caption,
});

/** The viewer's media source as this file reads it back off the mocked panel. */
type Strips = ReadonlyMap<string, readonly { url: string; thumbnailUrl?: string; caption?: string }[]>;

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

/**
 * The viewer answering what the drawing it parsed turned out not to hold.
 *
 * The one thing this frame learns for itself: no server can say whether a parsed file contains a
 * station of a given name, so it arrives from the panel and from nowhere else. Driven here the way
 * the real panel drives it — a set of station paths, answered again whenever it changes.
 */
const answerUnplaced = (stations: ReadonlySet<string>) => {
  const tell = given?.onUnplacedStationsChange as ((s: ReadonlySet<string>) => void) | undefined;
  if (tell === undefined) {
    throw new Error('the frame gave the viewer nowhere to answer that question');
  }
  act(() => tell(stations));
};
const focus = (kind: string, ref: string) => ({
  silexgis: EMBED_CHANNEL,
  v: EMBED_PROTOCOL,
  type: 'focus',
  target: { kind, ref },
});

beforeEach(() => {
  answer = { data: envelope(), isPending: false, error: null };
  given = undefined;
  sheetPane = undefined;
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
  it('hangs the envelope\u2019s pictures on their stations without reaching for any route', () => {
    answer = {
      data: envelope({
        model: { ...model, pictures: [picture('p.g.7', 'tok-a', 'The pitch head')] },
      }),
      isPending: false,
      error: null,
    };
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('viewer')).toBeInTheDocument();
    expect(reachedBeyondTheEnvelope).toEqual([]);
    const strips = given?.stationMedia as Strips;
    expect([...strips.keys()]).toEqual(['p.g.7']);
    expect(strips.get('p.g.7')![0].url).toBe('/api/v1/files/tok-a/thumbnail?size=1200&token=tok-a');
    // Nothing on this surface ever points at the upload a rendering was drawn from.
    expect(JSON.stringify(strips)).not.toContain('/content');
  });

  /**
   * A re-read re-signs every picture URL, and this document is the one where that matters most.
   *
   * An embed lives in an article about a trip that finished months ago, opened by a reader who
   * reaches the drawing when they reach it — so its picture URLs are the ones most likely to be
   * spent long after they were minted, and the page goes on re-reading for no other reason. What
   * the reader must not pay for that is the strip they are looking at: handing the viewer a new
   * source closes it. So the same source comes back, restamped.
   */
  it('restamps the pictures it already framed instead of handing over a new set', () => {
    const signed = (signature: string) => ({
      stationName: 'p.g.7',
      thumbnailUrl: `/api/v1/files/photo-1/thumbnail?size=480&token=${signature}`,
      caption: null,
    });
    answer = {
      data: envelope({ model: { ...model, pictures: [signed('sig-first')] } }),
      isPending: false,
      error: null,
    };
    const view = render(<PublicTripEmbedPage />);
    const framed = given?.stationMedia as Strips;

    answer = {
      data: envelope({ model: { ...model, pictures: [signed('sig-second')] } }),
      isPending: false,
      error: null,
    };
    view.rerender(<PublicTripEmbedPage />);

    expect(given?.stationMedia).toBe(framed);
    expect(framed.get('p.g.7')![0].url).toContain('token=sig-second');
  });

  /**
   * The twin, and the ordinary case: a club that has published no photographs frames a drawing
   * with no picture surface at all rather than an empty one — so no station gains a strip, a
   * hover, or a thumbnail element with nothing behind it.
   */
  it('shows the viewer no picture surface at all when the envelope carried none', () => {
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

  /**
   * The pin, and the one thing it must not survive.
   *
   * An address is re-signed on every poll and names the same file, so passing it through would
   * re-download and re-parse the survey inside somebody's article every minute. A survey that has
   * actually been replaced — a watch re-pointed at a corrected one while the party is underground
   * — is the opposite case: the drawing has to follow, or the frame shows new stations on old
   * geometry.
   */
  it('keeps the survey while it is re-signed, and takes up a survey that has been replaced', () => {
    const view = render(<PublicTripEmbedPage />);
    expect(given?.fileUrl).toBe('/api/v1/files/abc/content?token=first');

    answer = {
      data: envelope({ model: { ...model, modelUrl: '/api/v1/files/abc/content?token=second' } }),
      isPending: false,
      error: null,
    };
    view.rerender(<PublicTripEmbedPage />);
    expect(given?.fileUrl).toBe('/api/v1/files/abc/content?token=first');

    answer = {
      data: envelope({ model: { ...model, modelUrl: '/api/v1/files/def/content?token=third' } }),
      isPending: false,
      error: null,
    };
    view.rerender(<PublicTripEmbedPage />);
    expect(given?.fileUrl).toBe('/api/v1/files/def/content?token=third');
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

  /**
   * A station is absent for two unrelated reasons, and the article around this frame writes its
   * prose against what it is told. Handed one shape for both, a page that greys out a link where
   * there is no station says nobody knows where a person underground is — at the moment when
   * somebody does, and only the survey the place was measured in is in the way.
   */
  it('tells the framer which absences are places it cannot draw', () => {
    answer = {
      data: envelope({
        participants: [
          participant({ ordinal: 1, label: 'Ana', in: true, positionOnOtherModel: true }),
          participant({ ordinal: 2, in: true }),
          participant({ ordinal: 3, label: 'Dan', stationName: 'p.g.7', in: true }),
        ],
      }),
      isPending: false,
      error: null,
    };
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, hello);

    expect(sent[0].message).toMatchObject({
      type: 'ready',
      party: [
        // A place exists and is not on this drawing: no station is handed over, and the reason is.
        { ordinal: 1, name: 'Ana', station: null, onOtherSurvey: true, notOnDrawing: false },
        // The twins: nobody has placed this one, and this one is placed on the survey in frame.
        { ordinal: 2, name: 'Caver 2', station: null, onOtherSurvey: false, notOnDrawing: false },
        { ordinal: 3, name: 'Dan', station: 'p.g.7', onOtherSurvey: false, notOnDrawing: false },
      ],
    });
  });

  /**
   * And the third kind of nothing, which is the one this frame has to discover for itself.
   *
   * <b>The article writes its prose from this message, and that is what made the omission
   * expensive.</b> Ana's place was measured on the very survey in the frame — every identifier
   * agrees — and the drawing holds no node of that name, because the survey was re-exported with
   * its stations renamed. Told the station anyway, a club's page printed "Ana is at cave.deep.3"
   * and linked it, beside a drawing showing nobody; the honest answer arrived only after a reader
   * pressed the link. So the station is withheld exactly as a place on another survey is, and the
   * reason is handed over beside it.
   */
  it('withholds a station its own drawing cannot show, and says which kind of nothing that is', () => {
    answer = {
      data: envelope({
        participants: [
          participant({ ordinal: 1, label: 'Ana', stationName: 'cave.deep.3', in: true }),
          participant({ ordinal: 2, label: 'Dan', stationName: 'p.g.7', in: true }),
        ],
      }),
      isPending: false,
      error: null,
    };
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);

    deliver(parent, HOST, hello);
    // Nothing is claimed about a model that has not been parsed yet, which is the state the first
    // announcement always goes out in.
    expect(sent.at(-1)!.message).toMatchObject({
      party: [
        { ordinal: 1, station: 'cave.deep.3', notOnDrawing: false },
        { ordinal: 2, station: 'p.g.7', notOnDrawing: false },
      ],
    });

    answerUnplaced(new Set(['cave.deep.3']));

    expect(sent.at(-1)!.message).toMatchObject({
      type: 'ready',
      party: [
        { ordinal: 1, name: 'Ana', station: null, onOtherSurvey: false, notOnDrawing: true },
        // The twin, in the same message: a station this drawing does hold is handed over as one.
        { ordinal: 2, name: 'Dan', station: 'p.g.7', onOtherSurvey: false, notOnDrawing: false },
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

  it('answers a link to a place its drawing cannot show, rather than flying at it', () => {
    // The offer is withdrawn before it is taken up. Handed to the panel this rejects, and what a
    // reader gets — in a frame with no chrome, inside somebody else's article — is a camera that
    // did not move and a notice in a box they did not ask for. Answered as not found, the article
    // can grey the link out instead. The twin is the test four above: the same link, the same
    // person, a drawing that holds the station, and the flight is asked for.
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    answerUnplaced(new Set(['p.g.7']));

    deliver(parent, HOST, focus('caver', '1'));

    expect(given?.focusRequest).toBeUndefined();
    expect(sent.at(-1)?.message).toMatchObject({
      type: 'focused',
      found: false,
      target: { kind: 'caver', ref: '1' },
    });
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

  it('shows no tab strip while the trip carries no sheets — the frame is the viewer it always was', () => {
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('viewer')).toBeInTheDocument();
    expect(screen.queryByRole('tab')).toBeNull();
    expect(screen.queryByTestId('sheet-pane')).toBeNull();
  });

  it('offers the sheets as tabs inside the frame, mounted only when opened', async () => {
    answer = {
      data: envelope({
        model: {
          ...model,
          rasterMaps: [
            {
              title: 'Plan sheet',
              viewKind: 'plan',
              imageUrl: '/api/v1/files/sheet-1/thumbnail?size=1200&token=sig',
              points: [{ station: 'p.g.7', x: 0.25, y: 0.75 }],
            },
          ],
        },
      }),
      isPending: false,
      error: null,
    };
    render(<PublicTripEmbedPage />);

    expect(screen.getByRole('tab', { name: '3D' })).toBeInTheDocument();
    expect(screen.queryByTestId('sheet-pane')).toBeNull();

    act(() => {
      screen.getByRole('tab', { name: /Plan sheet/ }).click();
    });

    expect(await screen.findByTestId('sheet-pane')).toHaveAttribute('data-active', 'true');
    // The frame's whole box cascades down: the pane is given the height the snippet chose.
    expect(sheetPane?.height).toBe('100%');
    // The viewer was hidden, never unmounted.
    expect(screen.getByTestId('viewer')).toBeInTheDocument();
    expect(reachedBeyondTheEnvelope).toEqual([]);
  });

  it('returns the strip to the 3D drawing when the article asks for a place', async () => {
    // The camera being flown is the 3D pane's: an answer performed behind a sheet tab would
    // read, from the article, as a link that did nothing.
    answer = {
      data: envelope({
        model: {
          ...model,
          rasterMaps: [
            {
              title: 'Plan sheet',
              viewKind: 'plan',
              imageUrl: '/api/v1/files/sheet-1/thumbnail?size=1200&token=sig',
              points: [],
            },
          ],
        },
      }),
      isPending: false,
      error: null,
    };
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);

    act(() => {
      screen.getByRole('tab', { name: /Plan sheet/ }).click();
    });
    expect(await screen.findByTestId('sheet-pane')).toHaveAttribute('data-active', 'true');

    deliver(parent, HOST, focus('station', 'p.g.7'));

    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'p.g.7' });
    expect(screen.getByTestId('sheet-pane')).toHaveAttribute('data-active', 'false');
  });
});
