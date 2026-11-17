// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, it, vi } from 'vitest';
import { readEveryPage } from './mapLayers.ts';

it('reads past the first page, so a layer that sorts after it is still offered', async () => {
  const pages: Record<number, string[]> = {
    1: Array.from({ length: 500 }, (_, i) => `a${i}`),
    2: Array.from({ length: 500 }, (_, i) => `b${i}`),
    3: ['last'],
  };
  const readPage = vi.fn((page: number) => Promise.resolve({ items: pages[page] ?? [], totalItems: 1001 }));

  const every = await readEveryPage(readPage);

  expect(every.items).toHaveLength(1001);
  expect(every.items.at(-1)).toBe('last');
  expect(readPage).toHaveBeenCalledTimes(3);
});

it('asks once when everything fits on the first page', async () => {
  const readPage = vi.fn(() => Promise.resolve({ items: ['only'], totalItems: 1 }));
  await readEveryPage(readPage);
  expect(readPage).toHaveBeenCalledTimes(1);
});

it('fails as a whole when a later page fails, rather than answering with a short list', async () => {
  const readPage = vi.fn((page: number) =>
    page === 1
      ? Promise.resolve({ items: Array.from({ length: 500 }, () => 'x'), totalItems: 600 })
      : Promise.reject(new TypeError('Failed to fetch')),
  );
  await expect(readEveryPage(readPage)).rejects.toThrow('Failed to fetch');
});
