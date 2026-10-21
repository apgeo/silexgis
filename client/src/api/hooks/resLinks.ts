// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import { unwrap, unwrapVoid } from './shared.ts';
import type {
  ResLink,
  ResLinkCreate,
  ResLinkMemberAdd,
  ResLinkMemberUpdate,
  ResLinkPageParams,
  ResLinkRelationTypeWrite,
  ResLinkUpdate,
} from './types.ts';
import { queryKeys } from './queryKeys.ts';

/**
 * Links incident to one entity — the per-entity panel query. `totalItems` is the badge
 * count and is legitimately 0 with an empty page: a link reachable only through a member
 * whose location is protected from this caller is withheld whole, so two callers can
 * honestly see different counts for the same entity. Never reconcile it against another
 * source.
 */
export function useResLinksForTarget(
  targetType: string,
  targetId: string,
  params: ResLinkPageParams = {},
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.resLinksForTarget(targetType, targetId, params),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/reslinks/for-target', {
          params: { query: { type: targetType, id: targetId, ...params } },
        }),
      ),
    enabled: enabled && Boolean(targetId),
    // Deliberately no placeholder from the previous key: this query is keyed by the entity,
    // and a section that showed the last entity's links under this one's heading would not
    // just be stale — the rows elide the entity whose page they are on, so the previous
    // entity's own chip would appear as a sibling of one it is not linked to.
  });
}

/** The links of one target, read once rather than kept: for code that is not a component. */
export async function fetchResLinksForTarget(
  targetType: string,
  targetId: string,
  params: ResLinkPageParams = {},
): Promise<ResLink[]> {
  const page = await unwrap(
    api.GET('/api/v1/reslinks/for-target', {
      params: { query: { type: targetType, id: targetId, ...params } },
    }),
  );
  return page.items;
}

/** One link by id or by its 8-character short code — the endpoint tells them apart by shape. */
export function useResLink(idOrCode: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLink(idOrCode),
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/{idOrCode}', { params: { path: { idOrCode } } })),
    enabled: enabled && Boolean(idOrCode),
    retry: false,
  });
}

/** The picker feed. A blank query is answered with an empty list without touching the database. */
export function useResLinkTargets(targetType: string, q: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkTargets(targetType, q),
    queryFn: () =>
      unwrap(api.GET('/api/v1/reslinks/targets/search', { params: { query: { type: targetType, q } } })),
    enabled: enabled && q.trim().length > 0,
  });
}

/**
 * The audience a new GPS point would take for this caller if the form names none. The rule
 * reads the caller's caving-group roster, which nothing else publishes, so the audience is
 * asked for rather than guessed: a guess about who can see a cave position is worse than
 * no notice at all.
 */
export function useResLinkPointDefault(enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkPointDefault,
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/point-default')),
    enabled,
    staleTime: 5 * 60_000,
  });
}

export function useResLinkRelationTypes(enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkRelationTypes,
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/relation-types')),
    enabled,
    staleTime: 5 * 60_000,
  });
}

/**
 * Every link read hangs off one prefix, and a write to one link can change what any
 * entity's panel shows, so writes invalidate the lot. Write responses are deliberately
 * not seeded into the cache: the server answers an author with the membership they just
 * asserted, uncut, while the next ordinary read may withhold part of it — priming from
 * the echo would show the author a link that does not exist for them on refresh.
 */
export function useInvalidateResLinks() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['reslinks'] });
}

export function useCreateResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (body: ResLinkCreate) => unwrap(api.POST('/api/v1/reslinks', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ResLinkUpdate }) =>
      unwrap(api.PATCH('/api/v1/reslinks/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/reslinks/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

export function useAddResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ResLinkMemberAdd }) =>
      unwrap(api.POST('/api/v1/reslinks/{id}/members', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, memberId, body }: { id: string; memberId: string; body: ResLinkMemberUpdate }) =>
      unwrap(
        api.PATCH('/api/v1/reslinks/{id}/members/{memberId}', {
          params: { path: { id, memberId } },
          body,
        }),
      ),
    onSuccess: invalidate,
  });
}

export function useDeleteResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, memberId }: { id: string; memberId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/reslinks/{id}/members/{memberId}', { params: { path: { id, memberId } } }),
      ),
    onSuccess: invalidate,
  });
}

/**
 * Adds a relation to the installation's vocabulary. The whole link prefix is invalidated
 * rather than only the vocabulary list, because every link row carries its relation inline
 * and would otherwise keep rendering the wording that was current when it was fetched.
 */
export function useCreateResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (body: ResLinkRelationTypeWrite) =>
      unwrap(api.POST('/api/v1/reslinks/relation-types', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: ResLinkRelationTypeWrite }) =>
      unwrap(api.PATCH('/api/v1/reslinks/relation-types/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/reslinks/relation-types/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/**
 * How many of a trip's links are read when looking for pictures on its moments. The same bound the
 * links panel and the survey viewer's station strip use — a trip carries tens of links, and a
 * surface that paged them would show half a replay's photographs with nothing to say it had.
 */
export const RESLINK_PAGE_MAX = 200;
