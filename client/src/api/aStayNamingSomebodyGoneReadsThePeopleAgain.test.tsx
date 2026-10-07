// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ExpeditionRosterEntryWrite } from './hooks.ts';

// The transport is stubbed rather than the network: what is under test is which questions a
// refused write makes the page ask again, not what the server answers them with.
const get = vi.fn();
const put = vi.fn();
const post = vi.fn();

vi.mock('./client.ts', () => ({
  api: {
    GET: (path: string) => get(path),
    PUT: (path: string) => put(path),
    POST: (path: string) => post(path),
  },
  // The real one in the two members a refusal is told apart by.
  ApiError: class ApiError extends Error {
    readonly status: number;
    readonly code?: string;

    constructor(status: number, code?: string) {
      super(code ?? String(status));
      this.status = status;
      this.code = code;
    }
  },
  lastReadETag: () => undefined,
}));

const {
  useCavers,
  useCreateExpeditionRosterEntry,
  useExpeditionRoster,
  useUpdateExpeditionRosterEntry,
} = await import('./hooks.ts');

/**
 * A stay names its person by their entry in the list of people, and that entry can be gone by
 * the time the stay is saved: removed, or joined into another entry for the same person, with
 * the stays it held moved to the one that was kept. The server refuses the write and says why.
 *
 * That refusal is a statement about what the page is holding. The list of people it offers
 * still has the entry in it, and the roster may still show stays under it — so until both are
 * read again, the next thing somebody chooses is the same dead entry and the same refusal. Any
 * other refusal is about the request, and says nothing about either.
 */
const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';
const PEOPLE = '/api/v1/cavers';
const ROSTER = '/api/v1/expeditions/{expeditionId}/roster';

const GONE = 'expedition_roster.caver_unknown';

const stay: ExpeditionRosterEntryWrite = {
  caverId: 'caver-gone',
  newCaverName: null,
  roleId: 4,
  fromDate: '2026-07-18',
  toDate: null,
  note: null,
};

function answers(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) });
}

function refuses(status: number, code: string) {
  return Promise.resolve({ error: { code }, response: new Response(null, { status }) });
}

/** How often each path has been read. */
const readsOf = (path: string) => get.mock.calls.filter(([asked]) => asked === path).length;

/**
 * The page as it stands while a stay is being written: the roster drawn behind the dialog, and
 * the list of people the dialog's own box has asked for.
 */
async function page() {
  // Nothing is stale on its own: the only thing that may ask the server again is the refusal.
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  const { result } = renderHook(
    () => ({
      roster: useExpeditionRoster(CAMP),
      people: useCavers('Ana'),
      record: useCreateExpeditionRosterEntry(CAMP),
      correct: useUpdateExpeditionRosterEntry(CAMP),
    }),
    { wrapper },
  );
  await waitFor(() => {
    expect(result.current.roster.isSuccess).toBe(true);
    expect(result.current.people.isSuccess).toBe(true);
  });
  expect(readsOf(ROSTER)).toBe(1);
  expect(readsOf(PEOPLE)).toBe(1);
  return result;
}

beforeEach(() => {
  get.mockReset();
  put.mockReset();
  post.mockReset();
  get.mockImplementation((path: string) =>
    answers(path === PEOPLE ? [] : { expeditionId: CAMP, entries: [], people: 0 }),
  );
});

describe('a stay refused because the entry it names is gone', () => {
  it('has the people and the roster read again when it was being corrected', async () => {
    put.mockImplementation(() => refuses(400, GONE));
    const result = await page();

    await act(async () => {
      await expect(
        result.current.correct.mutateAsync({ entryId: 7, body: stay }),
      ).rejects.toMatchObject({ code: GONE });
    });

    await waitFor(() => expect(readsOf(PEOPLE)).toBe(2));
    await waitFor(() => expect(readsOf(ROSTER)).toBe(2));
  });

  it('has them read again when it was being recorded', async () => {
    // Somebody chosen out of a list read a while ago is as gone as one a stay was loaded with.
    post.mockImplementation(() => refuses(400, GONE));
    const result = await page();

    await act(async () => {
      await expect(result.current.record.mutateAsync(stay)).rejects.toMatchObject({ code: GONE });
    });

    await waitFor(() => expect(readsOf(PEOPLE)).toBe(2));
    await waitFor(() => expect(readsOf(ROSTER)).toBe(2));
  });
});

describe('a stay refused for any other reason', () => {
  it.each([
    [400, 'expedition_roster.role_unknown'],
    [403, 'access.denied'],
    [404, 'expedition_roster.not_found'],
  ])('asks for nothing again (%i %s)', async (status, code) => {
    // The request was wrong, or was not this caller's to make. What is held is as good as it
    // was, and reading it again would only redraw the page under an open dialog.
    put.mockImplementation(() => refuses(status, code));
    post.mockImplementation(() => refuses(status, code));
    const result = await page();

    await act(async () => {
      await expect(
        result.current.correct.mutateAsync({ entryId: 7, body: stay }),
      ).rejects.toMatchObject({ code });
      await expect(result.current.record.mutateAsync(stay)).rejects.toMatchObject({ code });
    });
    // Long enough for a read to have been asked for, had one been coming: in the cases above
    // both are asked for before the refused write has finished being reported.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20));
    });

    expect(readsOf(PEOPLE)).toBe(1);
    expect(readsOf(ROSTER)).toBe(1);
  });
});
