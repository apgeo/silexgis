// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PublicPastTrack } from '../../api/hooks.ts';
import PublicPastBar from './PublicPastBar.tsx';
import type { PastTripPlayback } from './usePastTripPlayback.ts';

/** Whether the strip is being driven by a finger, which is the one thing its sizes are chosen on. */
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

/**
 * The strip itself, driven directly.
 *
 * <b>What is proved here is what the two pages around it cannot prove cheaply.</b> Both of the
 * surfaces that draw this strip reach it through a whole page and a fetch; the two statements below
 * are about the strip's own arithmetic over a trip of a size no page test would build — a record so
 * long the response could not carry it, and one whose reports are too many to mark.
 */

const at = (iso: string) => Date.parse(iso);

function track(overrides: Partial<PublicPastTrack> = {}): PublicPastTrack {
  return {
    tripLogId: '0195f4a2-6c3e-7b10-9f21-ab44de77c001',
    expedition: null,
    title: 'Peștera Demo Mare, the 2019 push',
    tripDate: '2019-07-06',
    tripDateEnd: null,
    armedAt: '2019-07-06T08:00:00Z',
    closedAt: '2019-07-06T18:00:00Z',
    positionsWithheld: false,
    trackTruncated: false,
    model: null,
    teams: [],
    participants: [],
    ...overrides,
  };
}

/** A trip with `count` reports, one a minute from the armed instant. */
function everyMinute(count: number): number[] {
  const first = at('2019-07-06T08:00:00Z');
  return Array.from({ length: count }, (_, index) => first + index * 60_000);
}

function playback(moments: readonly number[], overrides: Partial<PastTripPlayback> = {}) {
  const span = { from: moments[0], to: moments[moments.length - 1] };
  return {
    tripLogId: 'trip-1',
    engaged: true,
    track: track(),
    loading: false,
    failed: false,
    span,
    moments,
    at: span.from,
    setAt: () => {},
    envelope: null,
    follow: null,
    setFollow: () => {},
    open: () => {},
    backToNow: () => {},
    // A clock standing still: nothing here is about time passing, and the clock's own arithmetic
    // is proved where it lives.
    transport: {
      playing: false,
      speed: 60,
      setSpeed: () => {},
      toggle: () => {},
      play: () => {},
      scrubTo: () => {},
      step: 1,
    },
    markerMoveMs: 0,
    ...overrides,
  } satisfies PastTripPlayback;
}

const marks = () => document.querySelectorAll('.public-past-mark').length;

afterEach(() => {
  cleanup();
  coarse = false;
});

describe('the rail a past trip is scrubbed on', () => {
  it('marks every report while the marks are still marks', () => {
    render(<PublicPastBar playback={playback(everyMinute(20))} liveState="closed" cavers={[]} />);

    expect(marks()).toBe(20);
  });

  it('draws none at all for a record with too many to tell apart', () => {
    // The dot is 6px and a response carries up to two thousand reports: drawn, they are a solid
    // bar that says less than a bare rail does, at the cost of one absolutely-positioned element
    // each, re-laid-out on every one of the five renders a second the clock produces. Stepping
    // from report to report is what the two arrows beside the handle are for, and they still do.
    render(<PublicPastBar playback={playback(everyMinute(600))} liveState="closed" cavers={[]} />);

    expect(marks()).toBe(0);
    expect(screen.getByTestId('public-past-report-next')).toBeEnabled();
  });
});

describe('a record the response could not carry whole', () => {
  it('says so, where a reader cannot miss it', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(3), { track: track({ trackTruncated: true }) })}
        liveState="closed"
        cavers={[]}
      />,
    );

    // In the banner rather than beside the transport: reaching the end of the rail must not read
    // as reaching the end of the trip.
    expect(screen.getByTestId('public-past-banner')).toContainElement(
      screen.getByTestId('public-past-truncated'),
    );
  });

  it('says nothing of the kind about a record that arrived whole', () => {
    render(<PublicPastBar playback={playback(everyMinute(3))} liveState="closed" cavers={[]} />);

    expect(screen.queryByTestId('public-past-truncated')).toBeNull();
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
  });
});

/**
 * The control that chooses whom the camera keeps up with.
 *
 * <b>It never shows an identifier.</b> The value the control holds is `team:<uuid>`, and the
 * library draws a value it has no option for verbatim — so a follow the options did not cover was
 * a UUID in a control on a phone, beside a banner naming the very team in words.
 */
describe('whom the replay can be asked to follow', () => {
  const TEAM_B = '22222222-2222-2222-2222-222222222222';
  const roster = () =>
    track({
      teams: [{ id: TEAM_B, title: 'Survey' }],
      participants: [
        {
          ordinal: 1,
          label: 'Ileana',
          track: [
            {
              recordedAt: '2019-07-06T10:00:00Z',
              teamId: TEAM_B,
              stationName: 'far.end.2',
              depthM: null,
              positionOnOtherModel: false,
              in: true,
              out: false,
            },
          ],
        },
        { ordinal: 2, label: null, track: [] },
      ],
    });
  // What the control draws for its value: the library's own box, read as text, because the
  // defect this guards against is exactly that box printing the raw value.
  const shown = () =>
    screen.getByTestId('public-past-follow').querySelector('.ant-select-content')?.textContent;

  it('names a followed team before any report has placed it, rather than printing its id', () => {
    // A link asking for the survey team opens at the trip's start, where the party on screen is
    // still nobody in any team.
    render(
      <PublicPastBar
        playback={playback(everyMinute(3), {
          track: roster(),
          follow: { kind: 'team', id: TEAM_B },
        })}
        liveState="closed"
        cavers={[]}
      />,
    );

    expect(shown()).toBe('Survey');
    expect(screen.getByTestId('public-past-follow').textContent).not.toContain(TEAM_B);
  });

  it('offers every team and person of the trip, whoever has been reported by now', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(3), { track: roster() })}
        liveState="closed"
        cavers={[]}
      />,
    );
    fireEvent.mouseDown(within(screen.getByTestId('public-past-follow')).getByRole('combobox'));

    // The drawn list, not the accessibility mirror: the mirror carries values, the list words.
    const offered = Array.from(document.querySelectorAll('.ant-select-item-option')).map(
      (option) => option.textContent,
    );
    expect(offered).toEqual(['Follow nobody', 'Survey', 'Ileana', 'Caver 2']);
  });

  it('says a followed party the trip never had is not in this trip, rather than printing the value', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(3), {
          track: roster(),
          follow: { kind: 'caver', id: '99' },
        })}
        liveState="closed"
        cavers={[]}
      />,
    );

    expect(shown()).toBe('Not in this trip');
  });
});

/**
 * The clock where there is only room for a time of day.
 *
 * <b>A camp trip is measured over two or three days.</b> Inside a frame and on a phone the clock
 * printed hh:mm and nothing else, so noon on the first day and noon on the second were the same
 * four characters — and the "reported 3 hours ago" beside every name was measured from that
 * ambiguous moment. The whole range in the banner ("6 – 7 Jul 2019") does not say which of the two
 * days the party is being shown on.
 */
describe('the clock, where there is only room for a time of day', () => {
  const noon = (day: string) => at(`2019-07-${day}T12:00:00Z`);
  const clock = () => screen.getByTestId('public-past-clock').textContent ?? '';

  it('names the day as well once the trip runs past a midnight', () => {
    render(
      <PublicPastBar
        playback={playback([noon('06'), noon('07')], {
          track: track({ tripDateEnd: '2019-07-07', closedAt: '2019-07-07T18:00:00Z' }),
        })}
        liveState="closed"
        cavers={[]}
        compact
      />,
    );

    expect(clock()).toMatch(/Jul/);
    expect(clock()).toContain(String(new Date(noon('06')).getDate()));
  });

  it('keeps to the time of day on a trip that fits inside one', () => {
    render(
      <PublicPastBar playback={playback(everyMinute(3))} liveState="closed" cavers={[]} compact />,
    );

    expect(clock()).not.toMatch(/Jul/);
    expect(clock()).toMatch(/\d{1,2}:\d{2}/);
  });
});

/**
 * A trip that was chosen and could not be read.
 *
 * <b>The strip says one thing about it, not two.</b> A link to a trip past this installation's
 * retention leaves the track unread for good, and a banner still saying the trip is being read
 * sat directly above a body saying it could not be — a strip that both is and is not reading it.
 */
describe('a chosen trip that could not be read', () => {
  const refused = (): PastTripPlayback => ({
    ...playback(everyMinute(3)),
    track: undefined,
    loading: false,
    failed: true,
    span: null,
    moments: [],
    at: null,
  });

  it('does not say it is still reading the trip', () => {
    render(<PublicPastBar playback={refused()} liveState="closed" cavers={[]} />);

    expect(screen.getByTestId('public-past-track-failed')).toBeTruthy();
    expect(screen.getByTestId('public-past-banner-what')).toHaveTextContent('A past trip');
    expect(screen.getByTestId('public-past-bar')).not.toHaveTextContent(/Reading this trip/);
  });

  it('still says it is reading the trip while it is being read', () => {
    render(
      <PublicPastBar
        playback={{ ...refused(), failed: false, loading: true }}
        liveState="closed"
        cavers={[]}
      />,
    );

    expect(screen.getByTestId('public-past-banner-what')).toHaveTextContent('Reading this trip');
  });
});

/**
 * Sizes chosen on the pointer, for every control on the strip.
 *
 * <b>Including the one inside the banner.</b> "Stop following" sits in a line of prose and was
 * drawn at the mouse size whatever was pointing at it — a twenty-four-pixel target on a phone,
 * beside a play button grown for a finger.
 */
describe('the strip under a finger', () => {
  const following = () =>
    playback(everyMinute(3), {
      track: track({ participants: [{ ordinal: 1, label: 'Ileana', track: [] }] }),
      follow: { kind: 'caver', id: '1' },
    });

  it('grows the stop-following control with the rest of the strip', () => {
    coarse = true;
    render(<PublicPastBar playback={following()} liveState="closed" cavers={[]} />);

    expect(screen.getByTestId('public-past-play').className).toContain('ant-btn-lg');
    expect(screen.getByTestId('public-past-unfollow').className).toContain('ant-btn-lg');
  });

  it('keeps it at the mouse size under a mouse', () => {
    render(<PublicPastBar playback={following()} liveState="closed" cavers={[]} />);

    expect(screen.getByTestId('public-past-unfollow').className).toContain('ant-btn-sm');
  });
});

/**
 * The two buttons that copy a link to the moment on the clock.
 *
 * <b>The strip decides what is offered and says what happened; the address is the page's.</b> So
 * what is proved here is the offer — only where a page handed an address in, never in the frame —
 * that each button asks for the right one of the two links, that a reader is told which moment went
 * into it, and that a browser refusing the clipboard still leaves them holding the link.
 */
describe('copying a link to the moment on the clock', () => {
  const realClipboard = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
  const clipboard = (writeText: (text: string) => Promise<void>) =>
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });

  afterEach(() => {
    if (realClipboard === undefined) {
      Reflect.deleteProperty(navigator, 'clipboard');
    } else {
      Object.defineProperty(navigator, 'clipboard', realClipboard);
    }
  });

  const address = (playing: boolean) =>
    `https://club.example/shared/trips/t?past=trip-1${playing ? '&play=1' : ''}`;

  it('copies the moment, or the moment set playing, and says which moment it took', async () => {
    const written: string[] = [];
    clipboard((text) => {
      written.push(text);
      return Promise.resolve();
    });
    render(
      <PublicPastBar
        playback={playback(everyMinute(20))}
        liveState="closed"
        cavers={[]}
        momentAddress={address}
      />,
    );
    // Named by their own words, which is what a screen reader is given too.
    expect(screen.getByRole('button', { name: 'Copy link to this moment' })).toBe(
      screen.getByTestId('public-past-copy-moment'),
    );
    expect(screen.getByRole('button', { name: 'Copy link that plays from here' })).toBe(
      screen.getByTestId('public-past-copy-playing'),
    );
    // The line a confirmation is announced in is there before there is anything to announce.
    expect(screen.getByRole('status')).toBeEmptyDOMElement();

    fireEvent.click(screen.getByTestId('public-past-copy-moment'));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Link copied'));
    expect(written).toEqual([address(false)]);
    const clock = screen.getByTestId('public-past-clock').textContent;
    expect(clock).not.toBe('');
    expect(screen.getByRole('status')).toHaveTextContent(`It opens this trip at ${clock}.`);
    expect(screen.getByRole('status')).not.toHaveTextContent('starts playing');

    fireEvent.click(screen.getByTestId('public-past-copy-playing'));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('starts playing'));
    expect(written).toEqual([address(false), address(true)]);
    expect(screen.queryByTestId('public-past-copy-by-hand')).toBeNull();
  });

  it('hands the link over to be copied by hand when the browser refuses the clipboard', async () => {
    clipboard(() => Promise.reject(new DOMException('denied', 'NotAllowedError')));
    render(
      <PublicPastBar
        playback={playback(everyMinute(20))}
        liveState="closed"
        cavers={[]}
        momentAddress={address}
      />,
    );

    fireEvent.click(screen.getByTestId('public-past-copy-playing'));

    const byHand = await screen.findByTestId('public-past-copy-by-hand');
    expect(byHand).toHaveValue(address(true));
    expect(byHand).toHaveAccessibleName('Link to this moment');
    expect(screen.getByRole('status')).toHaveTextContent('did not allow copying');
    expect(screen.getByRole('status')).not.toHaveTextContent('Link copied');
  });

  it('hands the link over as well where the browser has no clipboard at all', async () => {
    // A page opened over plain HTTP has no such object: reaching for it throws, which must end
    // the same way a refusal does rather than as a press that did nothing.
    Reflect.deleteProperty(navigator, 'clipboard');
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined });
    render(
      <PublicPastBar
        playback={playback(everyMinute(20))}
        liveState="closed"
        cavers={[]}
        momentAddress={address}
      />,
    );

    fireEvent.click(screen.getByTestId('public-past-copy-moment'));

    expect(await screen.findByTestId('public-past-copy-by-hand')).toHaveValue(address(false));
  });

  it('offers neither button where no page handed it an address, nor inside a frame', () => {
    // The positive twin is the first case of this group: the same strip, with an address.
    const { unmount } = render(
      <PublicPastBar playback={playback(everyMinute(20))} liveState="closed" cavers={[]} />,
    );
    expect(screen.getByTestId('public-past-play')).toBeInTheDocument();
    expect(screen.queryByTestId('public-past-links')).toBeNull();
    unmount();

    render(
      <PublicPastBar
        playback={playback(everyMinute(20))}
        liveState="closed"
        cavers={[]}
        momentAddress={address}
        compact
      />,
    );
    expect(screen.getByTestId('public-past-play')).toBeInTheDocument();
    expect(screen.queryByTestId('public-past-links')).toBeNull();
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('draws them as buttons, never as links the page would be navigated by', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(20))}
        liveState="closed"
        cavers={[]}
        momentAddress={address}
      />,
    );

    expect(screen.getByTestId('public-past-links').querySelectorAll('a')).toHaveLength(0);
    expect(screen.getByTestId('public-past-links').querySelectorAll('button')).toHaveLength(2);
  });
});

describe('the camp a past trip was part of', () => {
  const camp = { id: 'cccccccc-0000-0000-0000-000000000001', name: 'Summer camp 2019' };

  it('is said under the trip\'s name where the server names one', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(20), { track: track({ expedition: camp }) })}
        liveState="closed"
        cavers={[]}
      />,
    );

    expect(screen.getByTestId('public-past-banner-camp')).toHaveTextContent('Camp: Summer camp 2019');
  });

  it('is not said at all where the server names none', () => {
    render(<PublicPastBar playback={playback(everyMinute(20))} liveState="closed" cavers={[]} />);

    expect(screen.getByTestId('public-past-banner-what')).toBeInTheDocument();
    expect(screen.queryByTestId('public-past-banner-camp')).toBeNull();
  });

  it('is left out of a frame, whose strip is as tall as the drawing it stands over', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(20), { track: track({ expedition: camp }) })}
        liveState="closed"
        cavers={[]}
        compact
      />,
    );

    expect(screen.getByTestId('public-past-banner-what')).toBeInTheDocument();
    expect(screen.queryByTestId('public-past-banner-camp')).toBeNull();
  });
});
