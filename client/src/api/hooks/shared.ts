// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQueryClient } from '@tanstack/react-query';
import type { Query, QueryClient } from '@tanstack/react-query';
import { ApiError, retryAfterOf, retryQuery } from '../client.ts';
import { noteVersionOfReading } from '../readingVersion.ts';

export async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await call;
  if (error !== undefined || data === undefined) {
    const problem = error as { code?: string; detail?: string } | undefined;
    // The whole problem object travels, not only the two members every screen reads: a refusal
    // that carries a machine-readable fact of its own is otherwise recoverable only by matching
    // it out of the English detail sentence.
    throw new ApiError(
      response.status,
      problem?.code,
      problem?.detail,
      problem as Record<string, unknown> | undefined,
      retryAfterOf(response),
    );
  }
  return data;
}

/**
 * unwrap for a read a form may be filled from: the version the answer was given at is kept
 * against the values it carried, for `versionReadWith` to hand to the save.
 */
export async function unwrapVersioned<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const data = await unwrap(call);
  noteVersionOfReading(data, (await call).response);
  return data;
}

/**
 * A query's retry rule for an answer where a refusal is itself the answer: a settled refusal is not
 * asked again; a failure to reach the server, a failed server and a 429 are, up to three times (a
 * 429 after the wait it names). This is the installation-wide default rule, delegated rather than
 * restated, so a hook that names it explicitly cannot drift from the screens that rely on the
 * default — it once treated a 429 as settled, and a rate-limited read then stayed in its error
 * state instead of recovering after the named wait.
 */
export function retryUnlessRefused(failureCount: number, error: unknown): boolean {
  return retryQuery(failureCount, error);
}

/** unwrap for endpoints with no response body (DELETE / 204). */
export async function unwrapVoid(
  call: Promise<{ error?: unknown; response: Response }>,
): Promise<void> {
  const { error, response } = await call;
  if (error !== undefined) {
    const problem = error as { code?: string; detail?: string } | undefined;
    // The whole problem object here too: a refused delete is where a server most often has
    // something to hand back — what still holds the thing in place.
    throw new ApiError(
      response.status,
      problem?.code,
      problem?.detail,
      problem as Record<string, unknown> | undefined,
      retryAfterOf(response),
    );
  }
}

/**
 * What the cache does when a record has been deleted: everything under the record's kind is asked
 * again, except what belongs to the record itself.
 *
 * A delete used to invalidate its kind by prefix, and the prefix also matches the deleted
 * record's own readings — its detail, its summary, whatever hangs under it. The page showing the
 * record is still mounted at that moment, because leaving it happens after the delete has
 * answered, so every one of those readings was fetched once more and answered 404: wasted
 * requests, errors in the console, and a reading left failed on a page about to go.
 *
 * So the record's own readings are passed over rather than asked again. They are not simply
 * removed, which was tried first: a reading removed from under a page that is still showing it
 * is built afresh the next time that page renders, and asked for all over again — and some
 * deletes wait for their listings before they answer, which is plenty of time for a render.
 * What is on screen stays as it is until the page lets go of it, and is dropped at that moment,
 * so a later visit to the same address finds nothing cached to show before its own 404. What
 * nothing is showing is dropped at once.
 *
 * A record's readings are the ones whose key names its id; a listing never does.
 */
export function invalidateAfterDelete(queryClient: QueryClient, scope: readonly unknown[], id: string) {
  const ofTheRecord = (query: Query) => query.queryKey.includes(id);
  const cache = queryClient.getQueryCache();

  const onScreen = new Set<Query>();
  for (const query of cache.findAll({ queryKey: scope, predicate: ofTheRecord })) {
    if (query.getObserversCount() === 0) {
      cache.remove(query);
    } else {
      onScreen.add(query);
    }
  }
  if (onScreen.size > 0) {
    const stop = cache.subscribe((event) => {
      if (
        event.type === 'observerRemoved' &&
        onScreen.has(event.query) &&
        event.query.getObserversCount() === 0
      ) {
        onScreen.delete(event.query);
        cache.remove(event.query);
        if (onScreen.size === 0) {
          stop();
        }
      }
    });
  }

  return queryClient.invalidateQueries({
    queryKey: scope,
    predicate: (query) => !ofTheRecord(query),
  });
}

/**
 * Marks stale what a write on a dated record moves outside its own family's caches.
 *
 * Trips, camps and events are rows on the calendar, and trips and camps are shapes on the map;
 * neither cache is held under the record's own prefix, so invalidating the family alone left the
 * calendar on the old days and the map on the old shape until each was next asked again. Which
 * caches a dated write reaches is decided here, once, rather than in each family's hooks, where
 * the copies had already drifted apart. `mapLayer` is the family's map layer, for those that have
 * one. The re-reads are handed back for a caller that waits on them; most do not.
 */
export function invalidateDatedRows(
  queryClient: QueryClient,
  mapLayer?: 'trip-logs' | 'expeditions',
) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: ['calendar'] }),
    mapLayer ? queryClient.invalidateQueries({ queryKey: ['map', mapLayer] }) : undefined,
  ]);
}

export function useInvalidateAttachments() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['attachments'] });
    // Attachments are audited children of their target entity, so any attach/detach/metadata
    // change surfaces in that entity's timeline — refresh it here so every caller stays in sync.
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

export function useInvalidateHistory() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['history'] });
}
