// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * What the cave's survey list answers, and how many times it has been asked.
 *
 * The count is the point of the second half of this file: a list that is asked once and cached for
 * five minutes and a list that keeps asking are indistinguishable from their first answer.
 */
let answers: { id: string; status: string; format: string }[] = [];
let asked = 0;

vi.mock('./client.ts', () => ({
  api: {
    GET: () => {
      asked += 1;
      return Promise.resolve({ data: answers, response: new Response(null, { status: 200 }) });
    },
  },
  ApiError: class ApiError extends Error {},
  lastReadETag: () => undefined,
}));

const { useSurveyModelsForCaves } = await import('./hooks.ts');

function harness() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { wrapper, client };
}

beforeEach(() => {
  asked = 0;
  answers = [];
});

afterEach(() => {
  vi.useRealTimers();
});

/**
 * The ordinary order of work on a trip that is about to go underground: the survey is uploaded,
 * and while the server is still reading it somebody opens the trip and sets the watch up.
 *
 * That surface narrows its chooser to the surveys somebody can actually be placed on, so a survey
 * still being read is not drawn as unfinished — it is absent. If this list is asked once and then
 * held, the chooser stays empty after the reading has finished, for as long as the answer stays
 * fresh, and the party kits up in front of a screen offering them nothing. The single-cave list
 * has kept asking for exactly this reason for as long as it has existed; this one had not.
 */
describe("the survey list behind a trip's chooser", () => {
  it('keeps asking while a survey is still being read, and sees it settle', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    answers = [{ id: 'm1', caveId: 'cave-1', status: 'processing', format: 'survex3d' } as never];
    const { wrapper } = harness();

    const { result } = renderHook(() => useSurveyModelsForCaves(['cave-1']), { wrapper });
    await waitFor(() => expect(result.current.data).toHaveLength(1));
    expect(result.current.data[0].status).toBe('processing');

    // The reading finishes on the server. Nothing in the browser did it and nothing in the browser
    // was told; the only way this list hears about it is by asking again.
    answers = [{ id: 'm1', caveId: 'cave-1', status: 'ready', format: 'survex3d' } as never];
    await vi.advanceTimersByTimeAsync(2500);

    await waitFor(() => expect(result.current.data[0].status).toBe('ready'));
  });

  /**
   * The twin, and the reason this is a rule rather than a blanket interval: a list holding nothing
   * but finished readings has nothing to watch for, and a page that asked every two seconds for
   * the whole of a trip would never go quiet.
   */
  it('stops asking once everything on it has settled', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    answers = [{ id: 'm1', caveId: 'cave-1', status: 'ready', format: 'survex3d' } as never];
    const { wrapper } = harness();

    const { result } = renderHook(() => useSurveyModelsForCaves(['cave-1']), { wrapper });
    await waitFor(() => expect(result.current.data).toHaveLength(1));
    const afterFirstAnswer = asked;

    await vi.advanceTimersByTimeAsync(10_000);

    expect(asked).toBe(afterFirstAnswer);
  });
});
