// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PublicPastTrack } from '../../api/hooks.ts';
import type { TrackedCaver } from '../../caveview/trackedCavers.ts';
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
    pictures: [],
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
    refused: false,
    span,
    moments,
    // Nobody followed, unless a case says who.
    followedMoments: [] as readonly number[],
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

describe('the photographs of the moment on the clock', () => {
  const hour = (clock: string) => at(`2019-07-06T${clock}:00Z`);
  const picture = (clock: string, ordinal: number | null, file: string, caption: string | null) => ({
    at: `2019-07-06T${clock}:00Z`,
    ordinal,
    thumbnailUrl: `/api/v1/files/${file}/thumbnail?size=480&token=sig`,
    caption,
  });
  /** The party at 10:00, and one person of it; somebody else at 12:00. */
  const withPictures = track({
    pictures: [
      picture('10:00', null, 'all', 'The whole party'),
      picture('10:00', 2, 'two', null),
      picture('12:00', 1, 'one', 'At the pitch'),
    ],
  });
  /** The page's own names for two places in the party; the answer carries only the numbers. */
  const party = [
    { caverId: '1', name: 'Ana' },
    { caverId: '2', name: 'Caver 2' },
  ] as TrackedCaver[];
  const span = [hour('08:00'), hour('18:00')];
  const addresses = () =>
    within(screen.getByTestId('public-past-pictures'))
      .getAllByRole('img')
      .map((image) => image.getAttribute('src'));

  it('draws no strip, no heading and no empty row for a replay that came with none', () => {
    // What every installation that does not publish them sends: the list, empty.
    render(
      <PublicPastBar playback={playback(span, { at: hour('12:00') })} liveState="closed" cavers={party} />,
    );

    expect(screen.queryByTestId('public-past-pictures')).not.toBeInTheDocument();
    expect(screen.queryByTestId('public-past-picture')).not.toBeInTheDocument();
    // The strip it stands under is there: the absence is the pictures', not the whole bar's.
    expect(screen.getByTestId('public-past-scrub')).toBeInTheDocument();
  });

  it('draws nothing before the first moment that carries a photograph', () => {
    render(
      <PublicPastBar
        playback={playback(span, { track: withPictures, at: hour('09:59') })}
        liveState="closed"
        cavers={party}
      />,
    );

    expect(screen.queryByTestId('public-past-pictures')).not.toBeInTheDocument();
  });

  it('shows those of the latest moment at or before the clock, each at the width it is drawn at', () => {
    render(
      <PublicPastBar
        playback={playback(span, { track: withPictures, at: hour('11:30') })}
        liveState="closed"
        cavers={party}
      />,
    );

    // The 10:00 pair and not the 12:00 one, which the clock has not reached.
    expect(addresses()).toEqual([
      '/api/v1/files/all/thumbnail?size=160&token=sig',
      '/api/v1/files/two/thumbnail?size=160&token=sig',
    ]);
    expect(screen.getAllByTestId('public-past-picture')).toHaveLength(2);
  });

  it('names a person by the page’s own name for that place in the party, and the party by nobody', () => {
    render(
      <PublicPastBar
        playback={playback(span, { track: withPictures, at: hour('11:30') })}
        liveState="closed"
        cavers={party}
      />,
    );

    const [ofAll, ofTwo] = screen.getAllByTestId('public-past-picture');
    // A picture of the moment: the gallery's caption, and nobody's name.
    expect(within(ofAll).getByRole('img')).toHaveAccessibleName('Photograph of this moment');
    expect(within(ofAll).queryByTestId('public-past-picture-who')).not.toBeInTheDocument();
    expect(within(ofAll).getByTestId('public-past-picture-caption')).toHaveTextContent('The whole party');
    // A picture of place 2 in the party, who this page calls "Caver 2".
    expect(within(ofTwo).getByRole('img')).toHaveAccessibleName('Photograph of Caver 2');
    expect(within(ofTwo).getByTestId('public-past-picture-who')).toHaveTextContent('Caver 2');
    expect(within(ofTwo).queryByTestId('public-past-picture-caption')).not.toBeInTheDocument();
  });

  it('replaces them at the next moment that carries any, and says which moment they are of', () => {
    // On the photograph's own instant the line and the clock say the same time.
    const { unmount } = render(
      <PublicPastBar
        playback={playback(span, { track: withPictures, at: hour('12:00') })}
        liveState="closed"
        cavers={party}
      />,
    );
    const atNoon = screen.getByTestId('public-past-clock').textContent;
    const said = screen.getByTestId('public-past-pictures-when').textContent;
    expect(said).toBe(`Photographs from ${atNoon}`);
    unmount();

    render(
      <PublicPastBar
        playback={playback(span, { track: withPictures, at: hour('15:00') })}
        liveState="closed"
        cavers={party}
      />,
    );

    expect(addresses()).toEqual(['/api/v1/files/one/thumbnail?size=160&token=sig']);
    expect(screen.getByTestId('public-past-picture-who')).toHaveTextContent('Ana');
    // Three hours on the photograph is still the noon one, and the line still says noon — the
    // photograph's moment, not the clock's.
    expect(screen.getByTestId('public-past-clock').textContent).not.toBe(atNoon);
    expect(screen.getByTestId('public-past-pictures-when').textContent).toBe(said);
  });

  it('names a number the page has no name for by nobody, rather than printing the number', () => {
    render(
      <PublicPastBar
        playback={playback(span, { track: withPictures, at: hour('15:00') })}
        liveState="closed"
        cavers={[]}
      />,
    );

    expect(screen.getByRole('img', { name: 'Photograph of this moment' })).toBeInTheDocument();
    expect(screen.queryByTestId('public-past-picture-who')).not.toBeInTheDocument();
  });

  it('stands under the rail in the sheet a frame keeps its controls in, and not on the frame’s one line', () => {
    const framed = playback(span, { track: withPictures, at: hour('11:30') });
    const { unmount } = render(
      <PublicPastBar playback={framed} liveState="closed" cavers={party} layout="sheet" />,
    );
    expect(screen.getAllByTestId('public-past-picture')).toHaveLength(2);
    unmount();

    render(<PublicPastBar playback={framed} liveState="closed" cavers={party} layout="line" />);
    expect(screen.queryByTestId('public-past-pictures')).not.toBeInTheDocument();
  });

  it('is worded in Romanian with the same words in the same places', async () => {
    const i18n = (await import('../../i18n')).default;
    await i18n.changeLanguage('ro');
    try {
      render(
        <PublicPastBar
          playback={playback(span, { track: withPictures, at: hour('11:30') })}
          liveState="closed"
          cavers={party}
        />,
      );

      expect(screen.getByTestId('public-past-pictures-when')).toHaveTextContent(/^Fotografii de la /);
      expect(screen.getByRole('img', { name: 'Fotografie din acest moment' })).toBeInTheDocument();
      expect(screen.getByRole('img', { name: 'Fotografie cu Caver 2' })).toBeInTheDocument();
    } finally {
      await i18n.changeLanguage('en');
    }
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
 * The two arrows that step from one report to the next.
 *
 * <b>They step through the reports of whoever is followed, and say so.</b> A trip of several
 * teams reports far more often than any one of them does; with the camera on one team, an arrow
 * that stops at every report of the trip mostly stops where nothing the reader is watching moved.
 */
describe('the arrows that step between reports', () => {
  const TEAM = '22222222-2222-2222-2222-222222222222';
  const minutes = everyMinute(20);
  // The followed party reported at three of the trip's twenty moments.
  const theirs = [minutes[2], minutes[9], minutes[15]];
  const surveyed = (title: string) => track({ teams: [{ id: TEAM, title }] });
  const stepping = (overrides: Partial<PastTripPlayback> = {}, layout?: 'page' | 'sheet') => {
    const scrubTo = vi.fn();
    const base = playback(minutes);
    render(
      <PublicPastBar
        playback={{ ...base, transport: { ...base.transport, scrubTo }, ...overrides }}
        liveState="closed"
        cavers={[]}
        layout={layout}
      />,
    );
    return {
      scrubTo,
      previous: screen.getByTestId('public-past-report-previous'),
      next: screen.getByTestId('public-past-report-next'),
    };
  };

  it('step through everybody’s while nobody is followed', () => {
    const { scrubTo, previous, next } = stepping({ at: minutes[5] });

    fireEvent.click(next);
    expect(scrubTo).toHaveBeenLastCalledWith(minutes[6]);
    fireEvent.click(previous);
    expect(scrubTo).toHaveBeenLastCalledWith(minutes[4]);
    expect(previous).toHaveAccessibleName('Previous report');
    expect(next).toHaveAccessibleName('Next report');
  });

  it('step through the followed party’s own reports, passing over everybody else’s', () => {
    const { scrubTo, previous, next } = stepping({
      track: surveyed('Survey'),
      follow: { kind: 'team', id: TEAM },
      followedMoments: theirs,
      at: minutes[5],
    });

    fireEvent.click(next);
    expect(scrubTo).toHaveBeenLastCalledWith(minutes[9]);
    fireEvent.click(previous);
    expect(scrubTo).toHaveBeenLastCalledWith(minutes[2]);
    // Whose reports, in the name a screen reader says and in the hover text a mouse shows.
    expect(previous).toHaveAccessibleName('Previous report about Survey');
    expect(next).toHaveAccessibleName('Next report about Survey');
    expect(next).toHaveAttribute('title', 'Next report about Survey');
  });

  it('stop at the followed party’s last report, though the trip went on reporting', () => {
    const { scrubTo, previous, next } = stepping({
      track: surveyed('Survey'),
      follow: { kind: 'team', id: TEAM },
      followedMoments: theirs,
      at: minutes[15],
    });

    expect(next).toBeDisabled();
    expect(previous).toBeEnabled();
    fireEvent.click(next);
    expect(scrubTo).not.toHaveBeenCalled();
  });

  it('leave the rail’s marks everybody’s, whoever is followed', () => {
    stepping({
      track: surveyed('Survey'),
      follow: { kind: 'team', id: TEAM },
      followedMoments: theirs,
    });

    expect(marks()).toBe(20);
  });

  it('step through everybody’s for a followed party of whom the trip holds no report', () => {
    // A link naming a place in the party beyond the roster's end. Narrowed to that party's
    // reports — none — both arrows would be dead on a trip of twenty.
    const { scrubTo, previous, next } = stepping({
      follow: { kind: 'caver', id: '99' },
      followedMoments: [],
      at: minutes[5],
    });

    fireEvent.click(next);
    expect(scrubTo).toHaveBeenLastCalledWith(minutes[6]);
    expect(next).toHaveAccessibleName('Next report');
    expect(previous).toHaveAccessibleName('Previous report');
  });

  it('say "the party being followed" for a followed team the trip has no name for', () => {
    // A team saved with an empty title has reports and nothing to be called by: the strip's line
    // about whom it keeps up with is silent for it, and the arrows still must not claim to step
    // through every report.
    const { scrubTo, previous, next } = stepping({
      track: surveyed(''),
      follow: { kind: 'team', id: TEAM },
      followedMoments: theirs,
      at: minutes[5],
    });

    fireEvent.click(next);
    expect(scrubTo).toHaveBeenLastCalledWith(minutes[9]);
    expect(next).toHaveAccessibleName('Next report about the party being followed');
    expect(previous).toHaveAccessibleName('Previous report about the party being followed');
  });

  it('do the same from the sheet a frame keeps them in', () => {
    const { scrubTo, next } = stepping(
      {
        track: surveyed('Survey'),
        follow: { kind: 'team', id: TEAM },
        followedMoments: theirs,
        at: minutes[5],
      },
      'sheet',
    );

    fireEvent.click(next);
    expect(scrubTo).toHaveBeenLastCalledWith(minutes[9]);
    expect(next).toHaveAccessibleName('Next report about Survey');
  });

  it('are named in Romanian with the same words in the same places', async () => {
    const { default: i18n } = await import('../../i18n');
    await i18n.changeLanguage('ro');
    try {
      const { next, previous } = stepping({
        track: surveyed('Topo'),
        follow: { kind: 'team', id: TEAM },
        followedMoments: theirs,
        at: minutes[5],
      });
      expect(next).toHaveAccessibleName('Raportul următor despre Topo');
      expect(previous).toHaveAccessibleName('Raportul anterior despre Topo');
    } finally {
      await i18n.changeLanguage('en');
    }
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
        layout="line"
      />,
    );

    expect(clock()).toMatch(/Jul/);
    expect(clock()).toContain(String(new Date(noon('06')).getDate()));
  });

  it('keeps to the time of day on a trip that fits inside one', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(3))}
        liveState="closed"
        cavers={[]}
        layout="line"
      />,
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

    // Neither half of the frame's strip, even handed an address: the line has no room and the
    // sheet is not the page.
    for (const layout of ['line', 'sheet'] as const) {
      const drawn = render(
        <PublicPastBar
          playback={playback(everyMinute(20))}
          liveState="closed"
          cavers={[]}
          momentAddress={address}
          layout={layout}
        />,
      );
      expect(screen.queryByTestId('public-past-links')).toBeNull();
      expect(screen.queryByRole('status')).toBeNull();
      drawn.unmount();
    }
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

  it('is left off the one line a frame keeps, and said in the sheet behind it', () => {
    const line = render(
      <PublicPastBar
        playback={playback(everyMinute(20), { track: track({ expedition: camp }) })}
        liveState="closed"
        cavers={[]}
        layout="line"
      />,
    );
    expect(screen.getByTestId('public-past-banner-what')).toBeInTheDocument();
    expect(screen.queryByTestId('public-past-banner-camp')).toBeNull();
    line.unmount();

    render(
      <PublicPastBar
        playback={playback(everyMinute(20), { track: track({ expedition: camp }) })}
        liveState="closed"
        cavers={[]}
        layout="sheet"
      />,
    );
    expect(screen.getByTestId('public-past-banner-camp')).toHaveTextContent('Camp: Summer camp 2019');
  });
});

/**
 * The strip in the frame inside somebody's article: one line, and a sheet behind it.
 *
 * <b>Two halves of one strip, and no control in both.</b> The frame draws them in the same
 * document at the same time, so anything drawn twice is two play buttons a reader can press and
 * two clocks that have to agree — and anything drawn in neither is a control the frame lost.
 */
describe('the strip in a frame, as one line and the sheet behind it', () => {
  /** Every control of the transport, by the name a reader and a test find it by. */
  const TRANSPORT = [
    'public-past-play',
    'public-past-clock',
    'public-past-back',
    'public-past-speed',
    'public-past-report-previous',
    'public-past-report-next',
    'public-past-follow',
    'public-past-scrub',
  ];
  const ON_THE_LINE = ['public-past-play', 'public-past-clock', 'public-past-back'];

  const both = (overrides: Partial<PastTripPlayback> = {}, liveState: 'armed' | 'closed' = 'armed') =>
    render(
      <>
        <PublicPastBar
          playback={playback(everyMinute(20), overrides)}
          liveState={liveState}
          cavers={[]}
          layout="line"
          onMore={() => {}}
        />
        <PublicPastBar
          playback={playback(everyMinute(20), overrides)}
          liveState={liveState}
          cavers={[]}
          layout="sheet"
        />
      </>,
    );

  it('draws every control of the transport exactly once between them, the line keeping three', () => {
    both();

    for (const id of TRANSPORT) {
      expect(screen.getAllByTestId(id), id).toHaveLength(1);
    }
    const line = within(screen.getByTestId('public-past-bar'));
    const sheet = within(screen.getByTestId('public-past-sheet'));
    for (const id of TRANSPORT) {
      const onTheLine = ON_THE_LINE.includes(id);
      expect(line.queryAllByTestId(id), `${id} on the line`).toHaveLength(onTheLine ? 1 : 0);
      expect(sheet.queryAllByTestId(id), `${id} in the sheet`).toHaveLength(onTheLine ? 0 : 1);
    }
  });

  it('says on the line that this is the past and which trip, and in full in the sheet', () => {
    both();

    const said = screen.getByTestId('public-past-banner');
    expect(screen.getByTestId('public-past-bar')).toContainElement(said);
    expect(said).toHaveTextContent('Past trip');
    expect(said).toHaveTextContent('the 2019 push');
    // The name may be cut short by the line's width, so the whole of it is also its hover text.
    expect(screen.getByTestId('public-past-banner-what')).toHaveAttribute(
      'title',
      expect.stringContaining('Peștera Demo Mare, the 2019 push'),
    );
    expect(screen.getByTestId('public-past-statement-what')).toHaveTextContent(
      'Nobody is being followed here',
    );
  });

  it('words the way back in one word, under the name that says where it goes', () => {
    const underground = both({}, 'armed');
    expect(screen.getByTestId('public-past-back')).toHaveTextContent(/^Now$/);
    expect(screen.getByTestId('public-past-back')).toHaveAccessibleName('Back to the party now');
    // A party to go back to is what "now" means: said in the sheet only where there is none.
    expect(screen.queryByTestId('public-past-no-live')).toBeNull();
    underground.unmount();

    both({}, 'closed');
    expect(screen.getByTestId('public-past-back')).toHaveTextContent(/^Back$/);
    expect(screen.getByTestId('public-past-back')).toHaveAccessibleName("Back to this link's trip");
    expect(screen.getByTestId('public-past-sheet')).toContainElement(
      screen.getByTestId('public-past-no-live'),
    );
  });

  it('opens the sheet from the line, and offers no such button where there is no sheet', () => {
    const opened = vi.fn();
    const withSheet = render(
      <PublicPastBar
        playback={playback(everyMinute(20))}
        liveState="armed"
        cavers={[]}
        layout="line"
        onMore={opened}
      />,
    );
    const more = screen.getByTestId('public-past-controls-open');
    expect(more).toHaveAccessibleName('Replay controls and other trips');
    fireEvent.click(more);
    expect(opened).toHaveBeenCalledTimes(1);
    withSheet.unmount();

    render(
      <PublicPastBar playback={playback(everyMinute(20))} liveState="armed" cavers={[]} layout="line" />,
    );
    expect(screen.getByTestId('public-past-play')).toBeInTheDocument();
    expect(screen.queryByTestId('public-past-controls-open')).toBeNull();
  });

  it('says a trip could not be read on the line, once, with the reason in the sheet', () => {
    both({ track: undefined, failed: true, span: null, moments: [], at: null });

    expect(screen.getAllByTestId('public-past-track-failed')).toHaveLength(1);
    expect(screen.getByTestId('public-past-bar')).toHaveTextContent('This trip could not be read');
    expect(screen.getByTestId('public-past-bar')).not.toHaveTextContent(/Reading this trip/);
    expect(screen.getByTestId('public-past-track-failed-why')).toHaveTextContent(
      'may no longer be published',
    );
    // No transport for a trip there is nothing of — and still the way back, and the way to the
    // sheet, which is where another trip is picked.
    expect(screen.queryByTestId('public-past-play')).toBeNull();
    expect(screen.queryByTestId('public-past-scrub')).toBeNull();
    expect(screen.getByTestId('public-past-back')).toBeInTheDocument();
    expect(screen.getByTestId('public-past-controls-open')).toBeInTheDocument();
  });

  it('says on the line itself that the record was cut short, and in full in the sheet', () => {
    // A reader who presses play on the line watches the clock stop at the last report that
    // arrived. With the sheet shut, the line is all there is to say why.
    both({ track: track({ trackTruncated: true }) });

    const onTheLine = screen.getByTestId('public-past-truncated');
    expect(screen.getByTestId('public-past-bar')).toContainElement(onTheLine);
    expect(onTheLine).toHaveTextContent('The record ends before the trip did');
    // The sentence in full is the mark's hover text, and stands in the sheet under a name of its
    // own — the two are in one document while the sheet is open.
    expect(onTheLine).toHaveAttribute('title', expect.stringContaining('the party went on after'));
    expect(screen.getByTestId('public-past-sheet')).toContainElement(
      screen.getByTestId('public-past-statement-truncated'),
    );
    expect(screen.getByTestId('public-past-statement-truncated')).toHaveTextContent(
      'the party went on after that moment',
    );
  });

  it('says nothing of the kind on the line of a record that arrived whole', () => {
    both();

    expect(screen.queryByTestId('public-past-truncated')).toBeNull();
    expect(screen.queryByTestId('public-past-statement-truncated')).toBeNull();
  });

  it('keeps who is being followed, and the way to stop, in the sheet', () => {
    const TEAM = '22222222-2222-2222-2222-222222222222';
    both({
      track: track({ teams: [{ id: TEAM, title: 'Survey' }] }),
      follow: { kind: 'team', id: TEAM },
    });

    const sheet = screen.getByTestId('public-past-sheet');
    expect(sheet).toContainElement(screen.getByTestId('public-past-banner-following'));
    expect(screen.getByTestId('public-past-banner-following')).toHaveTextContent('Survey');
    expect(sheet).toContainElement(screen.getByTestId('public-past-unfollow'));
  });
});

describe('the strip in a frame with room for the whole of it', () => {
  const TRANSPORT = [
    'public-past-play',
    'public-past-clock',
    'public-past-back',
    'public-past-speed',
    'public-past-report-previous',
    'public-past-report-next',
    'public-past-follow',
    'public-past-scrub',
  ];

  it('draws the statement and every control of the transport at once, with nothing behind a press', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(20), { track: track({ trackTruncated: true }) })}
        liveState="armed"
        cavers={[]}
        layout="frame"
        onMore={() => {}}
      />,
    );

    const strip = within(screen.getByTestId('public-past-bar'));
    for (const id of TRANSPORT) {
      expect(strip.getAllByTestId(id), id).toHaveLength(1);
    }
    expect(screen.queryByTestId('public-past-controls-open')).toBeNull();
    expect(screen.getByTestId('public-past-banner')).toHaveTextContent(
      'You are looking at a past trip',
    );
    expect(screen.getByTestId('public-past-banner-what')).toHaveTextContent('the 2019 push');
    // The way back is worded in full, as on the page: there is room for it.
    expect(screen.getByTestId('public-past-back')).toHaveTextContent('Back to the party now');
    // And the statement that the record was cut short is the whole sentence, in the statement.
    expect(screen.getByTestId('public-past-banner')).toContainElement(
      screen.getByTestId('public-past-truncated'),
    );
    expect(screen.getByTestId('public-past-truncated')).toHaveTextContent(
      'the party went on after that moment',
    );
  });

  it('offers the cave’s lists beside the way back, and only where there is a sheet to open', () => {
    const opened = vi.fn();
    const withSheet = render(
      <PublicPastBar
        playback={playback(everyMinute(20))}
        liveState="armed"
        cavers={[]}
        layout="frame"
        onMore={opened}
      />,
    );
    const lists = screen.getByTestId('public-past-lists-open');
    expect(lists).toHaveAccessibleName('Past trips in this cave');
    fireEvent.click(lists);
    expect(opened).toHaveBeenCalledTimes(1);
    withSheet.unmount();

    render(
      <PublicPastBar playback={playback(everyMinute(20))} liveState="armed" cavers={[]} layout="frame" />,
    );
    expect(screen.queryByTestId('public-past-lists-open')).toBeNull();
    // Nor on the page, which has a section of its own for them.
    cleanup();
    render(
      <PublicPastBar playback={playback(everyMinute(20))} liveState="armed" cavers={[]} onMore={opened} />,
    );
    expect(screen.queryByTestId('public-past-lists-open')).toBeNull();
  });

  it('keeps the way to another trip beside a trip that could not be read', () => {
    render(
      <PublicPastBar
        playback={playback(everyMinute(3), {
          track: undefined,
          failed: true,
          span: null,
          moments: [],
          at: null,
        })}
        liveState="armed"
        cavers={[]}
        layout="frame"
        onMore={() => {}}
      />,
    );

    expect(screen.getByTestId('public-past-track-failed')).toBeInTheDocument();
    expect(screen.getByTestId('public-past-back')).toBeInTheDocument();
    expect(screen.getByTestId('public-past-lists-open')).toBeInTheDocument();
  });
});
