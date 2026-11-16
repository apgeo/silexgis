// SPDX-License-Identifier: AGPL-3.0-or-later
import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ResLink, ResLinkMember } from '../api/hooks.ts';

// The links of the model as the server answers them; a test changes it to stand for a refetch.
let answer: { items: ResLink[] } | undefined;
const asked: { enabled: boolean; options: unknown }[] = [];

vi.mock('../api/hooks.ts', () => ({
  useResLinksForTarget: (
    _type: string,
    _id: string,
    _params: unknown,
    enabled: boolean,
    options?: unknown,
  ) => {
    asked.push({ enabled, options });
    return { data: answer };
  },
}));

const { useStationMedia, STATION_MEDIA_REFRESH_MILLISECONDS } = await import('./useStationMedia.ts');

const MODEL = 'model-1';

function linkWithPhotoAt(station: string, token: string): ResLink {
  return {
    id: 'link-1',
    shortCode: 'ABCD1234',
    relationType: null,
    description: null,
    mayEdit: true,
    members: [
      {
        id: `station-${station}`,
        targetType: 'surveyModel',
        targetId: MODEL,
        isMain: false,
        sortOrder: 0,
        note: null,
        anchorKind: 'modelStation',
        anchor: { station },
        anchorFileId: null,
        anchorState: 'exact',
        display: null,
      },
      {
        id: 'photo-1',
        targetType: 'document',
        targetId: 'photo-1',
        isMain: false,
        sortOrder: 0,
        note: null,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
        anchorState: 'exact',
        display: {
          title: 'Sala mare',
          subtitle: null,
          route: null,
          thumbnailUrl: `http://files.local/photo-1/thumb?token=${token}`,
          mediaType: 'image/jpeg',
        },
      },
    ] as unknown as ResLinkMember[],
  } as unknown as ResLink;
}

describe('useStationMedia', () => {
  it('keeps handing the viewer the same pictures object when a refetch only re-signs them', () => {
    // The viewer drops an open strip whenever it is handed a new source, so a refetch on window
    // focus that only renewed the tokens would close the photographs a reader was looking at.
    answer = { items: [linkWithPhotoAt('p.g.7', 'first')] };
    const { result, rerender } = renderHook(() => useStationMedia(MODEL, true));
    const held = result.current;

    answer = { items: [linkWithPhotoAt('p.g.7', 'second')] };
    rerender();

    expect(result.current).toBe(held);
    // And the strip drawn next uses the renewed signature, not the one that has run out.
    expect(result.current.get('p.g.7')![0].thumbnailUrl).toContain('token=second');
  });

  it('hands over a new set when the pictures themselves changed', () => {
    answer = { items: [linkWithPhotoAt('p.g.7', 'first')] };
    const { result, rerender } = renderHook(() => useStationMedia(MODEL, true));
    const held = result.current;

    answer = { items: [linkWithPhotoAt('p.g.9', 'first')] };
    rerender();

    expect(result.current).not.toBe(held);
    expect([...result.current.keys()]).toEqual(['p.g.9']);
  });

  it('reads the links again before their thumbnail signatures run out, while the viewer is open', () => {
    asked.length = 0;
    answer = undefined;
    renderHook(() => useStationMedia(MODEL, true));

    const options = asked.at(-1)!.options as { refetchInterval?: number } | undefined;
    // The signed addresses last ten minutes; a strip opened later than that would show nothing.
    expect(options?.refetchInterval).toBe(STATION_MEDIA_REFRESH_MILLISECONDS);
    expect(STATION_MEDIA_REFRESH_MILLISECONDS).toBeLessThan(10 * 60_000);
  });
});
