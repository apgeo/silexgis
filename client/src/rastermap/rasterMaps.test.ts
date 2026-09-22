// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';
import { rasterMapsFromLinks } from './rasterMaps.ts';

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

let linkCounter = 0;

function link(
  members: ResLinkMember[],
  relationCode: string | null,
  overrides: Partial<{ id: string; createdAt: string }> = {},
): ResLink {
  linkCounter += 1;
  return {
    id: overrides.id ?? `link-${linkCounter}`,
    shortCode: 'ABCD1234',
    relationType:
      relationCode === null
        ? null
        : { id: 1, code: relationCode, name: relationCode, directed: true, inverseName: 'x' },
    description: null,
    createdAt: overrides.createdAt ?? '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    mayEdit: true,
    members,
  } as unknown as ResLink;
}

const documentMember = (id = 'doc-1', title: string | null = 'Sheet A') =>
  member({
    id: `docmember-${id}`,
    targetId: id,
    display:
      title === null
        ? null
        : {
            title,
            subtitle: null,
            route: null,
            thumbnailUrl: `http://files.local/${id}/thumb?token=abc`,
            mediaType: 'image/png',
          },
  });

const modelMember = (anchorKind = 'whole', targetId = MODEL) =>
  member({
    id: `modelmember-${targetId}-${anchorKind}`,
    targetType: 'surveyModel',
    targetId,
    anchorKind: anchorKind as ResLinkMember['anchorKind'],
    anchor:
      anchorKind === 'whole'
        ? null
        : ({ survey: 'p.north' } as unknown as ResLinkMember['anchor']),
  });

describe('rasterMapsFromLinks', () => {
  it('reads a declared map out of a map-of link between a document and this model', () => {
    const maps = rasterMapsFromLinks(
      [link([documentMember(), modelMember()], 'map-plan-of')],
      MODEL,
    );

    expect(maps).toHaveLength(1);
    expect(maps[0]).toMatchObject({
      documentId: 'doc-1',
      viewKind: 'plan',
      title: 'Sheet A',
    });
  });

  it('carries the view kind of each code, which is the whole meaning the code has', () => {
    const maps = rasterMapsFromLinks(
      [
        link([documentMember('doc-1'), modelMember()], 'map-plan-of'),
        link([documentMember('doc-2'), modelMember()], 'map-profile-of'),
        link([documentMember('doc-3'), modelMember()], 'map-other-of'),
      ],
      MODEL,
    );

    expect(maps.map((m) => m.viewKind)).toEqual(['plan', 'profile', 'other']);
  });

  it('requires the code: the same structure under another relation is an annotation, not a map', () => {
    // Positive twin first: this exact structure IS a map under the map code…
    expect(
      rasterMapsFromLinks([link([documentMember(), modelMember()], 'map-plan-of')], MODEL),
    ).toHaveLength(1);

    // …and the identical structure under a generic relation, a custom map-flavoured code,
    // the pin code, or no relation at all, folds to nothing: structure alone must not
    // promote a casual document↔model link into a declared map.
    for (const code of ['documents', 'map-custom-of', 'map-station-point', null]) {
      expect(
        rasterMapsFromLinks([link([documentMember(), modelMember()], code)], MODEL),
      ).toHaveLength(0);
    }
  });

  it('requires the structure: the code alone must not turn a mislabeled link into a map', () => {
    // Positive twin: code with the right structure is a map.
    expect(
      rasterMapsFromLinks([link([documentMember(), modelMember()], 'map-plan-of')], MODEL),
    ).toHaveLength(1);

    // The code without a whole-document member — a region of the document instead — is not.
    expect(
      rasterMapsFromLinks(
        [
          link(
            [
              member({ anchorKind: 'imageRegion', anchor: { shape: 'point', x: 0.5, y: 0.5 } as never }),
              modelMember(),
            ],
            'map-plan-of',
          ),
        ],
        MODEL,
      ),
    ).toHaveLength(0);

    // The code without any model member — a map-of between two documents — is not either.
    expect(
      rasterMapsFromLinks(
        [link([documentMember('doc-1'), documentMember('doc-2')], 'map-plan-of')],
        MODEL,
      ),
    ).toHaveLength(0);
  });

  it('folds only the open model: a map of a different model is that model\'s map', () => {
    const links = [link([documentMember(), modelMember('whole', 'model-2')], 'map-plan-of')];

    expect(rasterMapsFromLinks(links, 'model-2')).toHaveLength(1);
    expect(rasterMapsFromLinks(links, MODEL)).toHaveLength(0);
  });

  it('accepts a coverage anchor on the model member, but not a single-point one', () => {
    // Partial coverage — "this sheet is the northern branch" — is a legal declaration…
    for (const anchorKind of ['whole', 'modelSurvey', 'modelStationRange']) {
      expect(
        rasterMapsFromLinks([link([documentMember(), modelMember(anchorKind)], 'map-plan-of')], MODEL),
      ).toHaveLength(1);
    }

    // …a station-anchored model member is not how coverage is declared, and a link shaped
    // that way is not a declaration.
    expect(
      rasterMapsFromLinks(
        [
          link(
            [
              documentMember(),
              member({
                id: 'modelmember-station',
                targetType: 'surveyModel',
                targetId: MODEL,
                anchorKind: 'modelStation',
                anchor: { station: 'p.g.7' } as unknown as ResLinkMember['anchor'],
              }),
            ],
            'map-plan-of',
          ),
        ],
        MODEL,
      ),
    ).toHaveLength(0);
  });

  it('keeps a withheld document title as null rather than inventing one', () => {
    const maps = rasterMapsFromLinks(
      [link([documentMember('doc-1', null), modelMember()], 'map-plan-of')],
      MODEL,
    );

    expect(maps).toHaveLength(1);
    expect(maps[0].title).toBeNull();
  });

  it('orders tabs deterministically: view kind, then title, then age', () => {
    const maps = rasterMapsFromLinks(
      [
        link([documentMember('doc-z', 'Zed sheet'), modelMember()], 'map-plan-of', {
          createdAt: '2026-09-03T10:00:00Z',
        }),
        link([documentMember('doc-p', 'Profile'), modelMember()], 'map-profile-of', {
          createdAt: '2026-09-01T10:00:00Z',
        }),
        link([documentMember('doc-a', 'Alpha sheet'), modelMember()], 'map-plan-of', {
          createdAt: '2026-09-02T10:00:00Z',
        }),
      ],
      MODEL,
    );

    expect(maps.map((m) => m.title)).toEqual(['Alpha sheet', 'Zed sheet', 'Profile']);
  });
});
