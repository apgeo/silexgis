// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { ResLink } from '../api/hooks.ts';
import { allLinksForTarget, type LinkPage } from './paging.ts';

const linkOf = (id: string) => ({ id }) as unknown as ResLink;

const page = (ids: string[], totalItems: number, pageSize = 2): LinkPage => ({
  items: ids.map(linkOf),
  totalItems,
  pageSize,
});

describe('allLinksForTarget', () => {
  it('walks every page and concatenates them in order', async () => {
    const fetchPage = vi.fn(async (n: number) =>
      page([`l${n * 2 - 1}`, `l${n * 2}`].slice(0, n === 3 ? 1 : 2), 5),
    );

    const all = await allLinksForTarget(fetchPage);

    // A well-pinned cave holds hundreds of links; the single-page habit next door would
    // have silently dropped l3..l5 and drawn a map that looks finished.
    expect(all.map((l) => l.id)).toEqual(['l1', 'l2', 'l3', 'l4', 'l5']);
    expect(fetchPage.mock.calls.map(([n]) => n)).toEqual([1, 2, 3]);
  });

  it('asks once when one page holds everything', async () => {
    const fetchPage = vi.fn(async () => page(['l1', 'l2'], 2));

    const all = await allLinksForTarget(fetchPage);

    expect(all).toHaveLength(2);
    expect(fetchPage).toHaveBeenCalledTimes(1);
  });

  it('answers empty for a target with no links at all', async () => {
    const all = await allLinksForTarget(async () => page([], 0));

    expect(all).toEqual([]);
  });

  it('stops early when the set shrank underneath the walk', async () => {
    const fetchPage = vi.fn(async (n: number) => (n === 1 ? page(['l1', 'l2'], 6) : page([], 6)));

    const all = await allLinksForTarget(fetchPage);

    // Page 2 came back empty, so page 3 is never asked for: looping on the stale total
    // would go on requesting pages that no longer exist.
    expect(all.map((l) => l.id)).toEqual(['l1', 'l2']);
    expect(fetchPage.mock.calls.map(([n]) => n)).toEqual([1, 2]);
  });

  it('survives a nonsensical page size without walking forever', async () => {
    const fetchPage = vi.fn(async () => page(['l1'], 100, 0));

    const all = await allLinksForTarget(fetchPage);

    // With no usable page size there is no page count; one page is the honest answer,
    // where a division by zero would have meant an endless walk.
    expect(all).toHaveLength(1);
    expect(fetchPage).toHaveBeenCalledTimes(1);
  });
});
