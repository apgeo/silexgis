// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { chunkIds, SELECTION_IDS_PER_REQUEST } from './workingSet.ts';

const uuid = (i: number) => `01a16a67-8718-72e3-b214-${i.toString(16).padStart(12, '0')}`;

describe("the selection summary's requests", () => {
  it('asks about a large selection in groups, in order, losing nothing', () => {
    const ids = Array.from({ length: 450 }, (_, i) => uuid(i));
    const chunks = chunkIds(ids);

    expect(chunks.map((chunk) => chunk.length)).toEqual([100, 100, 100, 100, 50]);
    expect(chunks.flat()).toEqual(ids);
    expect(chunkIds([])).toEqual([]);
  });

  it('keeps every request line well under the 8 KB a server or proxy refuses', () => {
    // The request as the client sends it: every id as its own `ids=` pair.
    const ids = Array.from({ length: SELECTION_IDS_PER_REQUEST }, (_, i) => uuid(i));
    const line = `GET /api/v1/features?${ids.map((id) => `ids=${id}`).join('&')}&pageSize=${ids.length} HTTP/1.1`;

    expect(line.length).toBeLessThan(6 * 1024);
  });
});
