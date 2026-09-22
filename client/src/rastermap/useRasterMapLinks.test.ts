// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { rasterMapLinksKey } from './useRasterMapLinks.ts';

describe('rasterMapLinksKey', () => {
  it('is reached by the prefix invalidation every reslink mutation performs', () => {
    // The mutation hooks invalidate exactly this prefix and nothing else; a rastermap key
    // outside it would keep serving stale maps and pins after any link edit. This drives
    // the same call useInvalidateResLinks makes, against a real cache, so the assertion
    // is about matching behaviour rather than about the key's spelling.
    const queryClient = new QueryClient();
    const key = rasterMapLinksKey('model-1');
    queryClient.setQueryData(key, []);
    expect(queryClient.getQueryState(key)?.isInvalidated).toBe(false);

    void queryClient.invalidateQueries({ queryKey: ['reslinks'] });

    expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true);
  });

  it('keeps each model its own entry, untouched by prefixes that are not link writes', () => {
    // The positive twin's counterpart: sitting under the reslinks prefix must not make the
    // key promiscuous — an unrelated invalidation (say the survey model list) leaves it
    // alone, and two models never share a cache row.
    const queryClient = new QueryClient();
    queryClient.setQueryData(rasterMapLinksKey('model-1'), []);
    queryClient.setQueryData(rasterMapLinksKey('model-2'), []);

    void queryClient.invalidateQueries({ queryKey: ['survey-models'] });

    expect(queryClient.getQueryState(rasterMapLinksKey('model-1'))?.isInvalidated).toBe(false);
    expect(rasterMapLinksKey('model-1')).not.toEqual(rasterMapLinksKey('model-2'));
  });
});
