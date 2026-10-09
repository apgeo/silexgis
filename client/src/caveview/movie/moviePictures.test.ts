// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import {
  drawMoviePicture,
  drawMoviePictureAt,
  moviePictureAt,
  moviePictureBox,
  moviePictureFrames,
  moviePictureSchedule,
  moviePicturesOf,
  type MoviePicture,
} from './moviePictures.ts';
import { DEFAULT_MOVIE_SETTINGS, normaliseMovieSettings, type MovieSettings } from './movieSettings.ts';

/**
 * The photographs of a movie: when each is on screen, how strongly, and where.
 *
 * A stand-in timeline of one or two trips whose instant is the frame's number in minutes, so that
 * "the first frame that has reached 09:07" is frame 7 and can be read off the test.
 */
const MIN = 60_000;
const T0 = Date.parse('2026-09-12T09:00:00Z');

/** Shown in a corner unless a test says otherwise: left to the default, a movie shows none. */
const settings = (pictures: Partial<MovieSettings['pictures']> = {}, fps = 10): Pick<MovieSettings, 'fps' | 'pictures' | 'captions'> => ({
  fps,
  captions: DEFAULT_MOVIE_SETTINGS.captions,
  pictures: { ...DEFAULT_MOVIE_SETTINGS.pictures, mode: 'corner', ...pictures },
});

/** `count` frames; trip 0 runs a minute a frame from T0, trip 1 the same from an hour later. */
function movie(count: number) {
  return {
    timeline: { instants: (position: number) => [T0 + position * MIN, T0 + 60 * MIN + position * MIN] },
    frames: { count, frame: (index: number) => ({ position: index }) },
  };
}

const picture = (key: string, minutes: number, extra: Partial<MoviePicture> = {}): MoviePicture => ({
  key,
  tripLogId: 'trip-a',
  at: T0 + minutes * MIN,
  caverId: null,
  caption: null,
  ...extra,
});

const TRIPS = ['trip-a', 'trip-b'];

describe('moviePictureSchedule', () => {
  it('shows a photograph from the first frame that has reached its moment, for the seconds asked', () => {
    const { timeline, frames } = movie(100);
    // Two seconds at ten frames a second: twenty frames.
    const schedule = moviePictureSchedule([picture('p', 7.5)], TRIPS, timeline, frames, settings({ seconds: 2 }));

    expect(schedule).toEqual([{ picture: picture('p', 7.5), from: 8, to: 28 }]);
    expect(moviePictureFrames(settings({ seconds: 2 }))).toBe(20);
  });

  it('shows two that would overlap one after the other, in the order they were taken', () => {
    const { timeline, frames } = movie(100);
    const schedule = moviePictureSchedule(
      [picture('late', 12), picture('early', 10), picture('same', 10.2)],
      TRIPS,
      timeline,
      frames,
      settings({ seconds: 1 }),
    );

    expect(schedule.map((entry) => [entry.picture.key, entry.from, entry.to])).toEqual([
      ['early', 10, 20],
      // Reached a frame later, and kept waiting until the first has had its time.
      ['same', 20, 30],
      ['late', 30, 40],
    ]);
  });

  it('cuts the last one short at the end of the movie, and shows nothing that would start after it', () => {
    const { timeline, frames } = movie(30);
    const schedule = moviePictureSchedule(
      [picture('a', 5), picture('b', 6), picture('c', 7)],
      TRIPS,
      timeline,
      frames,
      settings({ seconds: 2 }),
    );

    expect(schedule.map((entry) => [entry.picture.key, entry.from, entry.to])).toEqual([
      ['a', 5, 25],
      ['b', 25, 30],
    ]);
  });

  it('reads each photograph on its own trip’s clock', () => {
    const { timeline, frames } = movie(100);
    // 10:20 is frame 80 of the first trip and frame 20 of the second, which starts an hour later.
    const schedule = moviePictureSchedule(
      [picture('first', 80), picture('second', 80, { tripLogId: 'trip-b' })],
      TRIPS,
      timeline,
      frames,
      settings({ seconds: 1 }),
    );

    expect(schedule.map((entry) => [entry.picture.key, entry.from])).toEqual([
      ['second', 20],
      ['first', 80],
    ]);
  });

  it('shows one hung before the movie begins on the first frame, and never one the movie does not reach', () => {
    const { timeline, frames } = movie(10);
    const schedule = moviePictureSchedule(
      [picture('before', -30), picture('after', 500)],
      TRIPS,
      timeline,
      frames,
      settings({ seconds: 0.5 }),
    );

    expect(schedule.map((entry) => [entry.picture.key, entry.from, entry.to])).toEqual([['before', 0, 5]]);
  });

  it('leaves out a photograph of somebody the movie leaves out, and keeps one about nobody in particular', () => {
    const { timeline, frames } = movie(100);
    const shows = vi.fn((_trip: string, caverId: string) => caverId !== 'bogdan');
    const schedule = moviePictureSchedule(
      [picture('of-ana', 1, { caverId: 'ana' }), picture('of-bogdan', 2, { caverId: 'bogdan' }), picture('party', 3)],
      TRIPS,
      timeline,
      frames,
      settings({ seconds: 0.5 }),
      shows,
    );

    expect(schedule.map((entry) => entry.picture.key)).toEqual(['of-ana', 'party']);
    expect(shows).toHaveBeenCalledWith('trip-a', 'bogdan');
  });

  it('shows nothing of a trip that is not in the movie, and nothing at all when switched off', () => {
    const { timeline, frames } = movie(100);
    const other = picture('elsewhere', 5, { tripLogId: 'trip-z' });

    expect(moviePictureSchedule([other], TRIPS, timeline, frames, settings())).toEqual([]);
    expect(moviePictureSchedule([picture('p', 5)], TRIPS, timeline, frames, settings({ mode: 'off' }))).toEqual([]);
  });
});

describe('moviePictureAt', () => {
  const { timeline, frames } = movie(100);
  const base = settings({ seconds: 2, fade: true });
  const schedule = moviePictureSchedule([picture('p', 10)], TRIPS, timeline, frames, base);

  it('is nothing before and after, and the photograph in between', () => {
    expect(moviePictureAt(schedule, 9, base)).toBeNull();
    expect(moviePictureAt(schedule, 10, base)?.picture.key).toBe('p');
    expect(moviePictureAt(schedule, 29, base)?.picture.key).toBe('p');
    expect(moviePictureAt(schedule, 30, base)).toBeNull();
  });

  it('comes up and goes down over two fifths of a second, and stands whole between', () => {
    // Four frames at ten a second.
    const alphas = [10, 11, 12, 13, 14, 20, 25, 26, 27, 28, 29].map((index) => moviePictureAt(schedule, index, base)!.alpha);

    expect(alphas).toEqual([0.2, 0.4, 0.6, 0.8, 1, 1, 1, 0.8, 0.6, 0.4, 0.2]);
  });

  it('is whole from its first frame to its last with fading off', () => {
    const plain = settings({ seconds: 2, fade: false });

    expect([10, 15, 29].map((index) => moviePictureAt(schedule, index, plain)!.alpha)).toEqual([1, 1, 1]);
  });

  it('does not fade out towards an end the movie cut off', () => {
    const short = movie(25);
    const cut = moviePictureSchedule([picture('p', 10)], TRIPS, short.timeline, short.frames, base);

    expect(cut[0]).toMatchObject({ from: 10, to: 25 });
    expect(moviePictureAt(cut, 24, base)!.alpha).toBe(1);
    expect(moviePictureAt(cut, 10, base)!.alpha).toBe(0.2);
  });

  it('never fades a showing too short to do it in', () => {
    const quick = settings({ seconds: 0.5, fade: true }, 4);
    const two = moviePictureSchedule([picture('p', 10)], TRIPS, timeline, frames, quick);

    expect(two[0]).toMatchObject({ from: 10, to: 12 });
    expect([10, 11].map((index) => moviePictureAt(two, index, quick)!.alpha)).toEqual([1, 1]);
  });
});

describe('moviePictureBox', () => {
  const pictures = DEFAULT_MOVIE_SETTINGS.pictures;

  it('fits the whole photograph into the whole frame, wide or tall', () => {
    expect(moviePictureBox(1280, 720, { width: 4000, height: 3000 }, { ...pictures, mode: 'full' })).toEqual({
      x: 160,
      y: 0,
      width: 960,
      height: 720,
    });
    expect(moviePictureBox(1280, 720, { width: 3000, height: 1000 }, { ...pictures, mode: 'full' })).toEqual({
      x: 0,
      y: 147,
      width: 1280,
      height: 427,
    });
  });

  it('puts a corner photograph inside the frame, in the corner asked for', () => {
    const image = { width: 1200, height: 900 };
    const box = (corner: MovieSettings['pictures']['corner']) =>
      moviePictureBox(1280, 720, image, { ...pictures, mode: 'corner', corner });

    for (const corner of ['bottomRight', 'bottomLeft', 'topRight', 'topLeft'] as const) {
      const at = box(corner);
      expect(at.x).toBeGreaterThanOrEqual(0);
      expect(at.y).toBeGreaterThanOrEqual(0);
      expect(at.x + at.width).toBeLessThanOrEqual(1280);
      expect(at.y + at.height).toBeLessThanOrEqual(720);
      // Its own shape, a third of the frame across at most.
      expect(at.width / at.height).toBeCloseTo(4 / 3, 1);
      expect(at.width).toBeLessThanOrEqual(Math.round(1280 * 0.34));
    }
    expect(box('bottomRight').x).toBeGreaterThan(640);
    expect(box('bottomRight').y).toBeGreaterThan(360);
    expect(box('topLeft').x).toBeLessThan(640);
    expect(box('topLeft').y).toBeLessThan(360);
    expect(box('bottomLeft').x).toBe(box('topLeft').x);
    expect(box('topRight').y).toBe(box('topLeft').y);
  });

  it('keeps a tall photograph in a corner no taller than two fifths of the frame', () => {
    const at = moviePictureBox(1280, 720, { width: 900, height: 1600 }, { ...pictures, mode: 'corner' });

    expect(at.height).toBeLessThanOrEqual(Math.round(720 * 0.42));
    expect(at.width / at.height).toBeCloseTo(900 / 1600, 1);
  });
});

/** A 2D context that keeps what was drawn on it and how strongly. */
function recordingContext() {
  const drawn: { what: string; alpha: number; args: unknown[] }[] = [];
  const context = {
    globalAlpha: 1,
    fillStyle: '',
    font: '',
    textBaseline: 'alphabetic',
    textAlign: 'start',
    save: vi.fn(),
    restore: vi.fn(),
    fillRect(...args: unknown[]) {
      drawn.push({ what: 'fillRect', alpha: context.globalAlpha, args });
    },
    drawImage(...args: unknown[]) {
      drawn.push({ what: 'drawImage', alpha: context.globalAlpha, args });
    },
    fillText(...args: unknown[]) {
      drawn.push({ what: 'fillText', alpha: context.globalAlpha, args });
    },
    measureText: (text: string) => ({ width: text.length * 10 }),
  };
  return { context: context as unknown as CanvasRenderingContext2D, drawn };
}

describe('drawMoviePicture', () => {
  const image = { width: 400, height: 300 } as unknown as HTMLImageElement;

  it('darkens the frame under a photograph shown over all of it, and draws the photograph at its strength', () => {
    const { context, drawn } = recordingContext();

    drawMoviePicture(context, 640, 360, image, 0.5, { ...DEFAULT_MOVIE_SETTINGS.pictures, mode: 'full' }, null);

    expect(drawn.map((step) => step.what)).toEqual(['fillRect', 'drawImage']);
    expect(drawn[0].args).toEqual([0, 0, 640, 360]);
    expect(drawn[0].alpha).toBeCloseTo(0.44);
    expect(drawn[1].alpha).toBe(0.5);
    expect(drawn[1].args).toEqual([image, 80, 0, 480, 360]);
  });

  it('gives a corner photograph a light edge and leaves the rest of the frame alone', () => {
    const { context, drawn } = recordingContext();
    const pictures = { ...DEFAULT_MOVIE_SETTINGS.pictures, mode: 'corner' as const };

    drawMoviePicture(context, 640, 360, image, 1, pictures, null);

    const box = moviePictureBox(640, 360, image, pictures);
    expect(drawn.map((step) => step.what)).toEqual(['fillRect', 'drawImage']);
    const [x, y, width, height] = drawn[0].args as number[];
    expect([x, y]).toEqual([box.x - 1, box.y - 1]);
    expect([width, height]).toEqual([box.width + 2, box.height + 2]);
    expect(drawn[1].args).toEqual([image, box.x, box.y, box.width, box.height]);
  });

  it('writes the photograph’s caption along its bottom edge, cut to fit, only where asked', () => {
    const pictures = { ...DEFAULT_MOVIE_SETTINGS.pictures, mode: 'corner' as const };
    const long = 'La baza puțului de probă, după echipare, cu toată echipa';
    const withCaption = recordingContext();
    drawMoviePicture(withCaption.context, 640, 360, image, 1, pictures, long);
    const text = withCaption.drawn.find((step) => step.what === 'fillText')!;
    expect(String(text.args[0]).endsWith('…')).toBe(true);
    expect(String(text.args[0]).length).toBeLessThan(long.length);

    const without = recordingContext();
    drawMoviePicture(without.context, 640, 360, image, 1, { ...pictures, captions: false }, long);
    expect(without.drawn.some((step) => step.what === 'fillText')).toBe(false);

    const blank = recordingContext();
    drawMoviePicture(blank.context, 640, 360, image, 1, pictures, '   ');
    expect(blank.drawn.some((step) => step.what === 'fillText')).toBe(false);
  });

  it('draws nothing when switched off or at no strength', () => {
    const { context, drawn } = recordingContext();

    drawMoviePicture(context, 640, 360, image, 1, { ...DEFAULT_MOVIE_SETTINGS.pictures, mode: 'off' }, null);
    drawMoviePicture(context, 640, 360, image, 0, { ...DEFAULT_MOVIE_SETTINGS.pictures, mode: 'corner' }, null);

    expect(drawn).toEqual([]);
  });
});

describe('drawMoviePictureAt', () => {
  const { timeline, frames } = movie(100);
  const base = settings({ seconds: 1, fade: false });
  const schedule = moviePictureSchedule([picture('here', 10), picture('missing', 40)], TRIPS, timeline, frames, base);
  const image = { width: 400, height: 300 } as unknown as HTMLImageElement;
  const images = new Map([['here', image]]);

  it('draws the frame’s photograph on the frames it is on, and nothing on the others', () => {
    const on = recordingContext();
    drawMoviePictureAt(on.context, 640, 360, schedule, 12, base, images);
    expect(on.drawn.filter((step) => step.what === 'drawImage')).toHaveLength(1);

    const offFrame = recordingContext();
    drawMoviePictureAt(offFrame.context, 640, 360, schedule, 30, base, images);
    expect(offFrame.drawn).toEqual([]);
  });

  it('leaves a photograph whose image did not load out of its frames', () => {
    const { context, drawn } = recordingContext();

    drawMoviePictureAt(context, 640, 360, schedule, 42, base, images);

    expect(drawn).toEqual([]);
  });
});

describe('moviePicturesOf', () => {
  it('takes a trip’s moment photographs as the movie keeps them', () => {
    expect(
      moviePicturesOf('trip-a', [
        {
          at: T0,
          caverId: 'ana',
          documentId: 'doc-1',
          memberId: 'member-1',
          entry: { url: '/files/1/thumbnail?size=1200', thumbnailUrl: '/files/1/thumbnail?size=160', caption: 'La intrare' },
        },
        { at: T0 + MIN, caverId: null, documentId: 'doc-2', memberId: 'member-2', entry: { url: '/files/2/thumbnail?size=1200' } },
      ]),
    ).toEqual([
      { key: '/files/1/thumbnail?size=1200', tripLogId: 'trip-a', at: T0, caverId: 'ana', caption: 'La intrare' },
      { key: '/files/2/thumbnail?size=1200', tripLogId: 'trip-a', at: T0 + MIN, caverId: null, caption: null },
    ]);
  });
});

describe('the photograph settings', () => {
  it('start switched off — two seconds each, fading, captioned, bottom right once switched on', () => {
    expect(DEFAULT_MOVIE_SETTINGS.pictures).toEqual({
      mode: 'off',
      seconds: 2,
      fade: true,
      corner: 'bottomRight',
      captions: true,
    });
  });

  it('are given to settings remembered from before there were any', () => {
    const { pictures: _dropped, ...before } = DEFAULT_MOVIE_SETTINGS;

    expect(normaliseMovieSettings(before).pictures).toEqual(DEFAULT_MOVIE_SETTINGS.pictures);
  });

  it('are held to what the dialog offers', () => {
    const repaired = normaliseMovieSettings({
      ...DEFAULT_MOVIE_SETTINGS,
      pictures: { mode: 'sideways', seconds: 900, fade: 'yes', corner: 'middle', captions: 0 },
    }).pictures;

    expect(repaired).toEqual({ ...DEFAULT_MOVIE_SETTINGS.pictures, seconds: 15 });
    expect(normaliseMovieSettings({ pictures: { mode: 'full', seconds: 0.1, fade: false, corner: 'topLeft', captions: false } }).pictures).toEqual({
      mode: 'full',
      seconds: 0.5,
      fade: false,
      corner: 'topLeft',
      captions: false,
    });
  });
});
