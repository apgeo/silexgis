// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { pastPictures, pastPicturesAt, type PublishedMomentPicture } from './pastTrackPictures.ts';

const at = (clock: string) => Date.parse(`2019-07-06T${clock}:00Z`);

const picture = (
  clock: string,
  ordinal: number | null,
  file: string,
  caption: string | null = null,
): PublishedMomentPicture => ({
  at: `2019-07-06T${clock}:00Z`,
  ordinal,
  thumbnailUrl: `/api/v1/files/${file}/thumbnail?size=480&token=sig`,
  caption,
});

/** Two moments: the party and one person at 10:00, somebody else at 12:30. */
const TRACK = {
  pictures: [
    picture('10:00', null, 'all'),
    picture('10:00', 1, 'ana', 'At the pitch'),
    picture('12:30', 2, 'radu'),
  ],
};

const files = (pictures: readonly { thumbnailUrl: string }[]) =>
  pictures.map((entry) => entry.thumbnailUrl.split('/')[4]);

describe('the photographs a published replay came with', () => {
  it('shows nothing where the installation publishes none, and nothing from an older answer', () => {
    // What every installation sends until it turns the setting on: the list, empty.
    expect(pastPictures({ pictures: [] })).toEqual([]);
    expect(pastPicturesAt({ pictures: [] }, at('12:00'))).toEqual([]);
    // A server from before the list existed, and no answer at all.
    expect(pastPicturesAt({}, at('12:00'))).toEqual([]);
    expect(pastPicturesAt({ pictures: null }, at('12:00'))).toEqual([]);
    expect(pastPicturesAt(undefined, at('12:00'))).toEqual([]);
    expect(pastPicturesAt(null, at('12:00'))).toEqual([]);
  });

  it('places each on the clock with its number in the party, its address and its caption', () => {
    expect(pastPictures(TRACK)).toEqual([
      {
        at: at('10:00'),
        ordinal: null,
        thumbnailUrl: '/api/v1/files/all/thumbnail?size=480&token=sig',
        caption: null,
      },
      {
        at: at('10:00'),
        ordinal: 1,
        thumbnailUrl: '/api/v1/files/ana/thumbnail?size=480&token=sig',
        caption: 'At the pitch',
      },
      {
        at: at('12:30'),
        ordinal: 2,
        thumbnailUrl: '/api/v1/files/radu/thumbnail?size=480&token=sig',
        caption: null,
      },
    ]);
  });

  it('shows the photographs of the latest moment at or before the clock, and none before the first', () => {
    expect(pastPicturesAt(TRACK, at('09:59'))).toEqual([]);
    // On the instant itself, and for as long as no later moment carries any.
    expect(files(pastPicturesAt(TRACK, at('10:00')))).toEqual(['all', 'ana']);
    expect(files(pastPicturesAt(TRACK, at('12:29')))).toEqual(['all', 'ana']);
    // The next moment replaces them rather than joining them.
    expect(files(pastPicturesAt(TRACK, at('12:30')))).toEqual(['radu']);
    expect(files(pastPicturesAt(TRACK, at('18:00')))).toEqual(['radu']);
  });

  it('orders by moment whatever order they were sent in, keeping the order within a moment', () => {
    const shuffled = {
      pictures: [TRACK.pictures[2], TRACK.pictures[0], TRACK.pictures[1]],
    };
    expect(files(pastPictures(shuffled))).toEqual(['all', 'ana', 'radu']);
    expect(files(pastPicturesAt(shuffled, at('11:00')))).toEqual(['all', 'ana']);
  });

  it('leaves out a picture whose instant cannot be read rather than placing it at a guess', () => {
    const broken = {
      pictures: [{ ...picture('10:00', 1, 'lost'), at: 'not an instant' }, picture('11:00', 1, 'ana')],
    };
    expect(files(pastPictures(broken))).toEqual(['ana']);
    expect(pastPicturesAt(broken, at('10:30'))).toEqual([]);
  });
});
