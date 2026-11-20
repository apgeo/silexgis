// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import { readAllPages } from './mapLayers.ts';

/** A server holding this many rows, answering pages of this size the way a paged route does. */
function server(total: number, pageSize: number) {
  const asked: number[] = [];
  const rows = Array.from({ length: total }, (_, i) => ({ id: `row-${i}` }));
  return {
    asked,
    read: (page: number) => {
      asked.push(page);
      return Promise.resolve({ items: rows.slice((page - 1) * pageSize, page * pageSize), pageSize, totalItems: total });
    },
  };
}

describe('readAllPages', () => {
  it('reads every row past the first page, each once and in order', async () => {
    // More than the hundred the map used to read, across several pages of the size it asks for.
    const { read, asked } = server(1130, 500);
    const rows = await readAllPages(read);
    expect(rows).toHaveLength(1130);
    expect(new Set(rows.map((r) => r.id)).size).toBe(1130);
    expect(rows[1129]).toEqual({ id: 'row-1129' });
    expect(asked).toEqual([1, 2, 3]);
  });

  it('stops after a full last page without asking for an empty one', async () => {
    const { read, asked } = server(1000, 500);
    expect(await readAllPages(read)).toHaveLength(1000);
    expect(asked).toEqual([1, 2]);
  });

  it('stops on an empty page even when the total promised more', async () => {
    // Rows deleted while the pages were being turned: the total read first is no longer true.
    const asked: number[] = [];
    const rows = await readAllPages((page) => {
      asked.push(page);
      return Promise.resolve({ items: page === 1 ? [{ id: 'a' }] : [], pageSize: 1, totalItems: 5 });
    });
    expect(rows).toEqual([{ id: 'a' }]);
    expect(asked).toEqual([1, 2]);
  });

  it('answers an empty list with one request', async () => {
    const { read, asked } = server(0, 500);
    expect(await readAllPages(read)).toEqual([]);
    expect(asked).toEqual([1]);
  });

  it('fails as a whole when a later page fails, rather than answering with a short list', async () => {
    const read = vi.fn((page: number) =>
      page === 1
        ? Promise.resolve({ items: Array.from({ length: 500 }, () => 'x'), pageSize: 500, totalItems: 600 })
        : Promise.reject(new TypeError('Failed to fetch')),
    );
    await expect(readAllPages(read)).rejects.toThrow('Failed to fetch');
  });

  it('stops at its page ceiling when the total keeps promising more', async () => {
    // A catalogue growing faster than it is read must not hold the map waiting for ever.
    const read = vi.fn((page: number) =>
      Promise.resolve({ items: [`row-${page}`], pageSize: 1, totalItems: Number.MAX_SAFE_INTEGER }),
    );
    const rows = await readAllPages(read);
    expect(rows).toHaveLength(20);
    expect(read).toHaveBeenCalledTimes(20);
  });
});
