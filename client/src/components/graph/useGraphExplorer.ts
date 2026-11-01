// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ApiError } from '../../api/client.ts';
import { fetchNeighbourhood } from '../../api/hooks/graph.ts';
import { getExplorerModel, getModelEpoch, useExplorerStore, type ExplorerSeed } from '../../graph/explorerStore.ts';
import type { GraphNeighbourhood } from '../../api/hooks/types.ts';
import type { AnswerRequest, NodeKey } from '../../graph/types.ts';

export type ExplorerStatus = 'idle' | 'loading' | 'ready' | 'notFound' | 'error';

export interface ExplorerController {
  status: ExplorerStatus;
  /** Requests in flight after the first hop: expansions and refreshes. */
  pending: number;
  /** Set when an expansion or a refresh failed; the graph itself is unchanged. */
  actionFailed: boolean;
  /** Takes one more hop (or one category's next page) around a node. No-op when nothing can be asked. */
  expand: (key: NodeKey, category?: string) => Promise<void>;
  /** Asks every request that built the graph again and swaps the result in. */
  refresh: () => Promise<void>;
}

const requestId = (r: { type: string; id: string; category?: string; offset?: number }): string =>
  `${r.type}:${r.id}:${r.category ?? ''}:${r.offset ?? 0}`;

/**
 * Drives the explorer's model from the server: loads the seed's first hop when the seed changes and
 * takes further hops on request. The model lives in the explorer store; this hook only fetches.
 *
 * A generation counter ends every answer that arrives after the seed changed or the page left, so a
 * slow response for the previous seed can never be merged into the new graph.
 */
export function useGraphExplorer(seed: ExplorerSeed | null): ExplorerController {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<ExplorerStatus>('idle');
  const [pending, setPending] = useState(0);
  const [actionFailed, setActionFailed] = useState(false);
  const generation = useRef(0);
  const inFlight = useRef(new Map<string, Promise<unknown>>());
  const seedType = seed?.type;
  const seedId = seed?.id;

  useEffect(() => {
    const mine = ++generation.current;
    inFlight.current.clear();
    setPending(0);
    setActionFailed(false);
    if (seedType === undefined || seedId === undefined) {
      setStatus('idle');
      return () => {
        generation.current = mine + 1;
      };
    }
    useExplorerStore.getState().start({ type: seedType, id: seedId });
    setStatus('loading');
    fetchNeighbourhood(queryClient, { type: seedType, id: seedId }).then(
      (answer) => {
        if (mine === generation.current) {
          useExplorerStore.getState().merge(answer);
          setStatus('ready');
        }
      },
      (error: unknown) => {
        if (mine === generation.current) {
          setStatus(error instanceof ApiError && error.status === 404 ? 'notFound' : 'error');
        }
      },
    );
    return () => {
      generation.current = mine + 1;
    };
  }, [seedType, seedId, queryClient]);

  const expand = useCallback(
    async (key: NodeKey, category?: string) => {
      const request = getExplorerModel().requestFor(key, category);
      if (request === null) {
        return;
      }
      const id = requestId(request);
      if (inFlight.current.has(id)) {
        return;
      }
      const mine = generation.current;
      const epoch = getModelEpoch();
      const asked = fetchNeighbourhood(queryClient, request);
      inFlight.current.set(id, asked);
      setPending((n) => n + 1);
      setActionFailed(false);
      try {
        const answer = await asked;
        // A refresh or a collapse that happened while this was loading replaced the model: the answer
        // belongs to the graph before it and would bring back what was just collapsed.
        if (mine === generation.current && epoch === getModelEpoch()) {
          const answered: AnswerRequest = request.category === undefined ? {} : { category: request.category, offset: request.offset };
          useExplorerStore.getState().merge(answer, answered);
        }
      } catch {
        if (mine === generation.current) {
          setActionFailed(true);
        }
      } finally {
        if (mine === generation.current) {
          inFlight.current.delete(id);
          setPending((n) => n - 1);
        }
      }
    },
    [queryClient],
  );

  const refresh = useCallback(async () => {
    const mine = generation.current;
    const epoch = getModelEpoch();
    // Expansions still loading are part of the graph the user sees once they land: let them land
    // first, or the rebuild below would replace the model without them.
    await Promise.allSettled([...inFlight.current.values()]);
    if (mine !== generation.current || epoch !== getModelEpoch()) {
      return;
    }
    let requests = getExplorerModel().answeredRequests();
    if (requests.length === 0) {
      return;
    }
    setPending((n) => n + 1);
    setActionFailed(false);
    try {
      const fresh = new Map<string, GraphNeighbourhood>();
      // An expansion started during the fetches may land in the old model; ask for what it added too.
      for (let round = 0; round < 3 && requests.length > 0; round++) {
        const answers = await Promise.all(requests.map((r) => fetchNeighbourhood(queryClient, r, true)));
        requests.forEach((r, i) => fresh.set(requestId(r), answers[i]));
        requests = getExplorerModel()
          .answeredRequests()
          .filter((r) => !fresh.has(requestId(r)));
      }
      if (mine === generation.current && epoch === getModelEpoch()) {
        const all = getExplorerModel()
          .answeredRequests()
          .filter((r) => fresh.has(requestId(r)));
        useExplorerStore.getState().rebuild(
          all.map((r) => ({
            answer: fresh.get(requestId(r))!,
            request: r.category === undefined ? {} : { category: r.category, offset: r.offset },
          })),
        );
      }
    } catch {
      if (mine === generation.current) {
        setActionFailed(true);
      }
    } finally {
      if (mine === generation.current) {
        setPending((n) => n - 1);
      }
    }
  }, [queryClient]);

  return { status, pending, actionFailed, expand, refresh };
}
