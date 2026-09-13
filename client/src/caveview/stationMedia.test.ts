// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import { mediaForStation, stationMediaFromLinks } from './stationMedia.ts';

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
      },
    ]);
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
    // Asked before a tap flies the camera to a station, so that a tap at a station with nothing
    // to show does not answer by moving the view for nothing.
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
