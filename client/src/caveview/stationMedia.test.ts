// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import type { PublicTripStationPicture } from '../api/hooks.ts';
import {
  mediaForStation,
  restampStationMedia,
  stationMediaFromEnvelope,
  stationMediaFromLinks,
} from './stationMedia.ts';

const MODEL = 'model-1';

function member(overrides: Partial<ResLinkMember>): ResLinkMember {
  return {
    id: 'member-1',
    targetType: 'document',
    targetId: 'doc-1',
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: null,
    ...overrides,
  } as ResLinkMember;
}

function link(members: ResLinkMember[]): ResLink {
  return {
    id: 'link-1',
    shortCode: 'ABCD1234',
    relationType: null,
    description: null,
    mayEdit: true,
    members,
  } as unknown as ResLink;
}

const stationMember = (station: string) =>
  member({
    id: `station-${station}`,
    targetType: 'surveyModel',
    targetId: MODEL,
    anchorKind: 'modelStation',
    anchor: { station } as unknown as ResLinkMember['anchor'],
  });

const photoMember = (id: string, title = 'Sala mare') =>
  member({
    id,
    targetId: id,
    display: {
      title,
      subtitle: null,
      route: null,
      thumbnailUrl: `http://files.local/${id}/thumb?token=abc`,
      mediaType: 'image/jpeg',
    },
  });

describe('stationMediaFromLinks', () => {
  it('hangs a linked photograph on the station it is linked to', () => {
    const media = stationMediaFromLinks([link([stationMember('p.g.7'), photoMember('photo-1')])], MODEL);

    expect([...media.keys()]).toEqual(['p.g.7']);
    // Both URLs are renderings derived from the published thumbnail URL, token and all: a
    // reader who may not be told where a photograph was taken never receives the stored bytes.
    expect(media.get('p.g.7')).toEqual([
      {
        url: 'http://files.local/photo-1/thumb?token=abc&size=1200',
        thumbnailUrl: 'http://files.local/photo-1/thumb?token=abc&size=160',
        caption: 'Sala mare',
        // Which photograph this is, carried so that the click the viewer reports back names a
        // document rather than a URL to be matched against the map that produced it.
        documentId: 'photo-1',
      },
    ]);
  });

  it('derives every URL from the published thumbnail, never from the stored bytes', () => {
    const media = stationMediaFromLinks([link([stationMember('p.g.7'), photoMember('photo-1')])], MODEL);

    // The protection rule this file exists for, asserted rather than described: a strip is built
    // only out of renderings of the published thumbnail URL, carrying the token that came with it.
    // A reader who may not have a photograph's stored bytes is handed no address that would serve
    // them — so an entry pointing at /content, or one that dropped the token on the way, is the
    // failure to catch here and not in a browser.
    for (const entry of media.get('p.g.7') ?? []) {
      for (const url of [entry.url, entry.thumbnailUrl ?? '']) {
        expect(url).toContain('/thumb');
        expect(url).not.toContain('/content');
        expect(url).toContain('token=abc');
      }
    }
  });

  it('puts every picture of a link on every station of it', () => {
    const media = stationMediaFromLinks(
      [link([stationMember('p.g.7'), stationMember('p.g.8'), photoMember('photo-1'), photoMember('photo-2')])],
      MODEL,
    );

    expect(media.get('p.g.7')).toHaveLength(2);
    expect(media.get('p.g.8')).toHaveLength(2);
  });

  it('shows one photograph once, however many links reach the station', () => {
    const media = stationMediaFromLinks(
      [
        link([stationMember('p.g.7'), photoMember('photo-1')]),
        link([stationMember('p.g.7'), photoMember('photo-1')]),
      ],
      MODEL,
    );

    expect(media.get('p.g.7')).toHaveLength(1);
  });

  it('hands one station no more pictures than a strip drawn over a model can hold', () => {
    // The strip is laid out inside the model surface and fetches a thumbnail for every entry it is
    // given, so an unbounded set is a block of pictures larger than the model it is drawn over and
    // a request for each of them every time a finger lands on the station. Measured on the width a
    // 360px phone leaves the tracking panel, what is readable there is two across and under three
    // rows down.
    const members = [stationMember('p.g.7')];
    for (let index = 0; index < 30; index++) {
      members.push(photoMember(`photo-${index}`));
    }

    const media = stationMediaFromLinks([link(members)], MODEL);

    expect(media.get('p.g.7')).toHaveLength(12);
    // The first of them in the order they were linked, not an arbitrary twelve of the thirty.
    expect(media.get('p.g.7')?.[0].url).toContain('photo-0');
    expect(media.get('p.g.7')?.[11].url).toContain('photo-11');
  });

  it('counts that bound over the station, not over one link at a time', () => {
    // Thirty photographs reaching one station through thirty links are the same block of pictures
    // as thirty on a single link, and a bound applied per link would be no bound at all.
    const links = [];
    for (let index = 0; index < 30; index++) {
      links.push(link([stationMember('p.g.7'), photoMember(`photo-${index}`)]));
    }

    expect(stationMediaFromLinks(links, MODEL).get('p.g.7')).toHaveLength(12);
  });

  it('ignores what it cannot show and what is not a point of this model', () => {
    // A document with no rendering offered, a document that is not a picture, a stretch of
    // passage rather than a station, and another model's station: none of them is a strip.
    const noThumbnail = photoMember('photo-1');
    noThumbnail.display = { ...noThumbnail.display!, thumbnailUrl: null };
    const notAPicture = photoMember('photo-2');
    notAPicture.display = { ...notAPicture.display!, mediaType: 'application/pdf' };

    expect(stationMediaFromLinks([link([stationMember('p.g.7'), noThumbnail])], MODEL).size).toBe(0);
    expect(stationMediaFromLinks([link([stationMember('p.g.7'), notAPicture])], MODEL).size).toBe(0);
    expect(
      stationMediaFromLinks(
        [
          link([
            member({
              targetType: 'surveyModel',
              targetId: MODEL,
              anchorKind: 'modelStationRange',
              anchor: { fromStation: 'p.g.6', toStation: 'p.g.7' } as unknown as ResLinkMember['anchor'],
            }),
            photoMember('photo-3'),
          ]),
        ],
        MODEL,
      ).size,
    ).toBe(0);
    expect(
      stationMediaFromLinks([link([stationMember('p.g.7'), photoMember('photo-4')])], 'model-2').size,
    ).toBe(0);
  });

  it('answers what a station holds, whichever kind of source it was given', () => {
    // Asked when a thumbnail is clicked, so that the picture viewer it opens holds the station's
    // whole set — including the pictures the strip had no room to draw.
    const entry = { url: 'http://files.local/photo-1?size=1200' };
    const station = { name: () => 'p.g.7' };

    expect(mediaForStation(new Map([['p.g.7', [entry]]]), 'p.g.7', station)).toEqual([entry]);
    expect(mediaForStation(new Map([['p.g.7', [entry]]]), 'p.g.9', station)).toEqual([]);

    // A function source is asked about the station itself, which is what the viewer would hand it.
    const ask = vi.fn(() => [entry]);
    expect(mediaForStation(ask, 'p.g.7', station)).toEqual([entry]);
    expect(ask).toHaveBeenCalledWith(station);
    expect(mediaForStation(() => null, 'p.g.7', station)).toEqual([]);
  });

  it('places nothing from an anchor it was not allowed to read', () => {
    // The payload of a member whose location is protected from this reader arrives as null. A
    // strip placed at a guessed station is exactly what must not happen.
    const withheld = stationMember('p.g.7');
    withheld.anchor = null;

    expect(stationMediaFromLinks([link([withheld, photoMember('photo-1')])], MODEL).size).toBe(0);
  });
});

/**
 * The same strip, built for the one surface that cannot read links at all.
 *
 * A visitor holding a follow link is refused every address in this installation except the
 * published envelope, so the server decides which photographs may appear and this side only shapes
 * them. These assert the shaping — and, deliberately, that no shaping is also filtering.
 */
describe('a published trip\u2019s station pictures', () => {
  const picture = (
    stationName: string,
    token: string,
    caption: string | null = null,
  ): PublicTripStationPicture => ({
    stationName,
    thumbnailUrl: `/api/v1/files/${token}/thumbnail?size=480&token=sig-${token}`,
    caption,
  });

  it('draws both widths off the one signed URL it was handed', () => {
    // The token signs how far the holder may reach, not which width they may ask for, so asking
    // the same URL for a bigger rendering is the same permission being exercised. Minting a second
    // URL is not something this surface could do even if it wanted to: it has no route to ask.
    const media = stationMediaFromEnvelope([picture('p.g.7', 'a', 'The pitch head')])!;

    expect([...media.keys()]).toEqual(['p.g.7']);
    expect(media.get('p.g.7')).toEqual([
      {
        url: '/api/v1/files/a/thumbnail?size=1200&token=sig-a',
        thumbnailUrl: '/api/v1/files/a/thumbnail?size=160&token=sig-a',
        caption: 'The pitch head',
      },
    ]);
  });

  it('never points at the upload a rendering was drawn from', () => {
    // The whole reason the server mints a renderings-only token: a photograph's own bytes carry
    // the fix its camera wrote. A client that reached for the original \u201cbecause it is only
    // showing it\u201d would hand over precisely what was withheld.
    const media = stationMediaFromEnvelope([picture('p.g.7', 'a')])!;
    expect(JSON.stringify([...media.values()])).not.toContain('/content');
  });

  it('carries no caption at all rather than an empty one', () => {
    const [entry] = stationMediaFromEnvelope([picture('p.g.7', 'a', null)])!.get('p.g.7')!;
    expect('caption' in entry).toBe(false);
  });

  it('gathers a station\u2019s pictures together and keeps stations apart', () => {
    const media = stationMediaFromEnvelope([
      picture('p.g.7', 'a'),
      picture('p.g.9', 'b'),
      picture('p.g.7', 'c'),
    ])!;

    expect(media.get('p.g.7')).toHaveLength(2);
    expect(media.get('p.g.9')).toHaveLength(1);
  });

  it('shows one photograph once, however many times it was sent for a station', () => {
    // The same picture reaches one station through two links as often as not \u2014 it is linked to
    // the station and to the passage the station stands in.
    const media = stationMediaFromEnvelope([picture('p.g.7', 'a'), picture('p.g.7', 'a')])!;
    expect(media.get('p.g.7')).toHaveLength(1);
  });

  it('bounds one station at what a strip can show, and drops nothing before that', () => {
    // The viewer fetches a thumbnail per entry as the strip appears, on a phone, over a surface
    // two thumbnails wide. The bound is the same one the link derivation applies.
    const many = Array.from({ length: 20 }, (_, i) => picture('p.g.7', `t${i}`));
    expect(stationMediaFromEnvelope(many)!.get('p.g.7')).toHaveLength(12);

    const twelve = Array.from({ length: 12 }, (_, i) => picture('p.g.7', `t${i}`));
    expect(stationMediaFromEnvelope(twelve)!.get('p.g.7')).toHaveLength(12);
  });

  it('answers nothing at all \u2014 not an empty map \u2014 for an envelope carrying no pictures', () => {
    // The two are different instructions to the viewer: a map says this surface shows pictures and
    // turns on the label a strip hangs under. A club that has published none must look exactly as
    // it did before pictures existed.
    expect(stationMediaFromEnvelope([])).toBeUndefined();
  });

  it('places nothing from a row it cannot address or cannot draw', () => {
    // Neither can occur through the route that builds the envelope, and both are cheap to refuse:
    // a strip placed at a guessed station, or an <img> with no source, is worse than no strip.
    expect(stationMediaFromEnvelope([picture('', 'a')])).toBeUndefined();
    expect(
      stationMediaFromEnvelope([{ stationName: 'p.g.7', thumbnailUrl: '', caption: null }]),
    ).toBeUndefined();
  });
});

/**
 * Re-reading a published page must not cost the reader the strip they are looking at.
 *
 * Every read of the envelope re-signs every picture URL, so a derivation alone answers a new object
 * with new strings each time; the viewer treats a new source as a new set of pictures and drops the
 * open strip and its hover listeners when it is handed one. What these state is the compromise: the
 * same photographs stay the same objects and are restamped, and a genuinely different set is handed
 * over as one.
 */
describe('a published page re-reading its pictures', () => {
  const signed = (station: string, file: string, signature: string, caption?: string) =>
    stationMediaFromEnvelope([
      {
        stationName: station,
        thumbnailUrl: `/api/v1/files/${file}/thumbnail?size=480&token=${signature}`,
        caption: caption ?? null,
      },
    ])!;

  it('keeps the map the viewer holds when the same photographs come back re-signed', () => {
    const held = signed('p.g.7', 'photo-1', 'first');
    const kept = restampStationMedia(held, signed('p.g.7', 'photo-1', 'second'));

    // The identity is the assertion. A different object here is a torn-down strip, and the reader
    // whose thumb is on the station never finds out why the photographs went away.
    expect(kept).toBe(held);
    expect(kept!.get('p.g.7')).toBe(held.get('p.g.7'));
  });

  it('carries the fresh signature onto the entries the viewer already has', () => {
    // The whole point of re-reading: a URL minted ten minutes ago opens nothing, and the viewer
    // reads its source when the pointer arrives rather than when it was handed one.
    const held = signed('p.g.7', 'photo-1', 'first');
    const [entry] = held.get('p.g.7')!;
    restampStationMedia(held, signed('p.g.7', 'photo-1', 'second'));

    expect(entry.url).toBe('/api/v1/files/photo-1/thumbnail?size=1200&token=second');
    expect(entry.thumbnailUrl).toBe('/api/v1/files/photo-1/thumbnail?size=160&token=second');
  });

  it('hands over a new map when the photographs themselves changed', () => {
    // The twin of the first test, and what stops it being written as "never replace anything".
    // A photograph published since the last read, one taken back out of the gallery, one moved to
    // another station or re-captioned: each is something a reader is owed, and the strip closing
    // once is what it costs.
    const held = signed('p.g.7', 'photo-1', 'first');

    expect(restampStationMedia(held, signed('p.g.7', 'photo-2', 'first'))).not.toBe(held);
    expect(restampStationMedia(held, signed('p.g.9', 'photo-1', 'first'))).not.toBe(held);
    expect(restampStationMedia(held, signed('p.g.7', 'photo-1', 'first', 'A name'))).not.toBe(held);
    expect(
      restampStationMedia(
        held,
        stationMediaFromEnvelope([
          {
            stationName: 'p.g.7',
            thumbnailUrl: '/api/v1/files/photo-1/thumbnail?size=480&token=first',
            caption: null,
          },
          {
            stationName: 'p.g.7',
            thumbnailUrl: '/api/v1/files/photo-3/thumbnail?size=480&token=first',
            caption: null,
          },
        ]),
      ),
    ).not.toBe(held);
  });

  it('answers the fresh side whenever either side has no pictures at all', () => {
    // Nothing to keep, or nothing to keep it for: a club whose gallery has just been curated, and
    // one whose last public photograph has just been withdrawn.
    const held = signed('p.g.7', 'photo-1', 'first');

    expect(restampStationMedia(held, undefined)).toBeUndefined();
    expect(restampStationMedia(undefined, held)).toBe(held);
    expect(restampStationMedia(undefined, undefined)).toBeUndefined();
  });
});
